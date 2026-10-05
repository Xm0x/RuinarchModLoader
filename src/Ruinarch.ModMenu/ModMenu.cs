using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Ruinarch.ModMenu.Editor;
using Ruinarch.Modding;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ruinarch.ModMenu
{
	// Built-in mod that ships with RuinarchModLoader. It replaces the contents of
	// the game's main-menu "Mods" window (normally the game's own Steam Workshop
	// browser) with a loader-native list of every package the loader discovered,
	// local and Steam Workshop, each with an enable/disable toggle or the reason it
	// is not compatible, a view of the shared mods.log, an "open Mods folder"
	// button, and Workshop browse/upload.
	//
	// It is built entirely in code (no Unity editor, no AssetBundle): it clones an
	// existing TextMeshPro label from the window to inherit the game's font, then
	// lays out its panel with uGUI layout groups. Toggling a mod writes the loader
	// config; because mods load once at startup, the change applies on next launch.
	public sealed class ModMenuMod : IRuinarchMod
	{
		internal static ModLogger Log;

		public void OnLoad(ModContext context)
		{
			Log = context.Logger;
			try
			{
				var harmony = new Harmony("ruinarch.modmenu");
				harmony.PatchAll(typeof(ModMenuMod).Assembly);
				Log.Info("Mod menu installed; the main-menu Mods window now lists loaded mods.");
			}
			catch (Exception e)
			{
				Log.Error("Mod menu failed to install: " + e);
			}
		}
	}

	// Rebuild our panel and re-hide the Workshop panels every time the window opens
	// (the game re-activates its own sub-UIs on each open).
	[HarmonyPatch(typeof(ModParentUI), "Show")]
	internal static class ModParentUI_Show_Patch
	{
		private static void Postfix(ModParentUI __instance)
		{
			try
			{
				ModMenuView.OnShow(__instance);
			}
			catch (Exception e)
			{
				Debug.LogError("[ModMenu] Failed to build panel: " + e);
			}
		}
	}

	internal static class ModMenuView
	{
		private static GameObject _panel;
		private static GameObject _listContainer;
		private static TextMeshProUGUI _logLabel;
		private static TextMeshProUGUI _noteLabel;
		private static TMP_Text _template;
		private static ModParentUI _parent;

		public static void OnShow(ModParentUI parent)
		{
			_parent = parent;
			GameObject windowGO = Field<GameObject>(parent, "_windowGO");
			if (windowGO == null)
			{
				Debug.LogWarning("[ModMenu] ModParentUI._windowGO not found; leaving window alone.");
				return;
			}

			HideWorkshopPanels(parent);

			// Rebuild if we have no panel yet, or the old one belongs to a window
			// instance that was destroyed (e.g. after a scene reload).
			if (_panel == null || _panel.transform.parent != windowGO.transform)
			{
				_template = FindFontTemplate(windowGO);
				Build(windowGO);
			}

			_panel.SetActive(true);
			_panel.transform.SetAsLastSibling();
			RefreshList();
			RefreshLog();
			if (_noteLabel != null)
			{
				_noteLabel.gameObject.SetActive(false);
			}
		}

		private static void HideWorkshopPanels(ModParentUI parent)
		{
			foreach (string field in new[] { "_subscribeModsUI", "_browseModUI", "_createModUI" })
			{
				try
				{
					var comp = Field<Component>(parent, field);
					if (comp == null)
					{
						continue;
					}
					var win = Field<GameObject>(comp, "_windowGO");
					if (win != null)
					{
						win.SetActive(false);
					}
					comp.gameObject.SetActive(false);
				}
				catch
				{
					// A missing/renamed field must never stop the rest of the UI.
				}
			}
		}

		private static void Build(GameObject windowGO)
		{
			_panel = NewUI("RuinarchModMenu", windowGO.transform);
			Stretch(_panel);
			var bg = _panel.AddComponent<Image>();
			bg.color = new Color(0.05f, 0.06f, 0.09f, 1f);

			var root = _panel.AddComponent<VerticalLayoutGroup>();
			root.padding = new RectOffset(28, 28, 22, 22);
			root.spacing = 8;
			root.childControlWidth = true;
			root.childControlHeight = true;
			root.childForceExpandWidth = true;
			root.childForceExpandHeight = false;

			Label(_panel.transform, "Installed mods", 34, TextAlignmentOptions.Left, FontStyles.Bold);
			Label(_panel.transform,
				"Managed by RuinarchModLoader: packages in the Mods folder and subscribed Steam Workshop items. " +
				"Toggle a package to enable or disable it; changes take effect the next time you launch the game. " +
				"Steam downloads and updates also need a restart.",
				18, TextAlignmentOptions.Left, FontStyles.Normal, new Color(0.75f, 0.78f, 0.85f));

			_noteLabel = Label(_panel.transform, "Change saved. Restart the game to apply.",
				18, TextAlignmentOptions.Left, FontStyles.Italic, new Color(1f, 0.85f, 0.4f));
			_noteLabel.gameObject.SetActive(false);

			// Package list: scrolls, because local and Workshop packages can be many.
			Transform content = EditorUI.Scroll(_panel.transform, "List");
			_listContainer = content.gameObject;
			var listLe = content.parent.parent.GetComponent<LayoutElement>();
			listLe.minHeight = 220;
			listLe.flexibleHeight = 2f;

			// Log heading + body.
			Label(_panel.transform, "mods.log", 22, TextAlignmentOptions.Left, FontStyles.Bold);
			var logHolder = NewUI("Log", _panel.transform);
			var logImg = logHolder.AddComponent<Image>();
			logImg.color = new Color(0f, 0f, 0f, 0.5f);
			var logLe = logHolder.AddComponent<LayoutElement>();
			logLe.minHeight = 150;
			logLe.flexibleHeight = 1f;
			// The newest lines sit at the bottom; older ones are clipped at the top.
			logHolder.AddComponent<RectMask2D>();
			_logLabel = Label(logHolder.transform, string.Empty, 15, TextAlignmentOptions.BottomLeft,
				FontStyles.Normal, new Color(0.7f, 0.85f, 0.7f), fit: false);
			var logRt = _logLabel.rectTransform;
			logRt.anchorMin = Vector2.zero;
			logRt.anchorMax = Vector2.one;
			logRt.offsetMin = new Vector2(12, 10);
			logRt.offsetMax = new Vector2(-12, -10);

			// Footer buttons.
			var footer = NewUI("Footer", _panel.transform);
			var frow = footer.AddComponent<HorizontalLayoutGroup>();
			frow.spacing = 12;
			frow.childControlWidth = true;
			frow.childControlHeight = true;
			frow.childForceExpandWidth = true;
			frow.childForceExpandHeight = false;
			var frowLe = footer.AddComponent<LayoutElement>();
			frowLe.minHeight = 38;
			frowLe.preferredHeight = 38;
			frowLe.flexibleHeight = 0f;

			Button(footer.transform, "Refresh", () => { RefreshList(); RefreshLog(); });
			Button(footer.transform, "Open Mods folder", OpenModsFolder);
			Button(footer.transform, "Browse Workshop", Workshop.Browse);
			Button(footer.transform, "Upload to Workshop", OpenUpload);
			Button(footer.transform, "Close", () =>
			{
				if (_parent != null)
				{
					_parent.Hide();
				}
			});
		}

		internal static void RefreshList()
		{
			if (_listContainer == null)
			{
				return;
			}
			try
			{
				Workshop.ScanLate();
			}
			catch (Exception e)
			{
				ModMenuMod.Log?.Warning("Workshop rescan failed: " + e.Message);
			}
			for (int i = _listContainer.transform.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.Destroy(_listContainer.transform.GetChild(i).gameObject);
			}

			var mods = ModLoader.Known.Where(m => m != null && m.Origin != ModOrigin.Infrastructure).ToList();
			if (mods.Count == 0)
			{
				Label(_listContainer.transform, "No packages found in the Mods folder or Steam Workshop subscriptions.", 18,
					TextAlignmentOptions.Left, FontStyles.Italic, new Color(0.7f, 0.7f, 0.7f));
				return;
			}

			foreach (KnownMod mod in mods)
			{
				BuildRow(mod);
			}
		}

		private static void BuildRow(KnownMod mod)
		{
			var row = NewUI("Row", _listContainer.transform);
			var rimg = row.AddComponent<Image>();
			rimg.color = new Color(1f, 1f, 1f, 0.05f);
			var rl = row.AddComponent<HorizontalLayoutGroup>();
			rl.padding = new RectOffset(12, 12, 10, 10);
			rl.spacing = 12;
			rl.childControlWidth = true;
			rl.childControlHeight = true;
			rl.childForceExpandWidth = false;
			rl.childForceExpandHeight = false;
			rl.childAlignment = TextAnchor.MiddleLeft;

			string status = !mod.Compatible ? "not loaded"
				: mod.FailureReason != null ? "failed to start: " + mod.FailureReason
				: mod.Loaded ? "running"
				: (mod.Enabled ? "enabled (restart to load)" : "disabled");
			string origin = mod.Origin == ModOrigin.SteamWorkshop ? "Steam Workshop " + mod.WorkshopId : "Local";
			var name = mod.Info != null ? mod.Info.name : mod.Id;
			var ver = mod.Info != null ? mod.Info.version : "0.0.0";
			var author = mod.Info != null ? mod.Info.author : "unknown";
			var desc = mod.Info != null ? mod.Info.description : string.Empty;

			var sb = new StringBuilder();
			if (!mod.Compatible) sb.Append("<color=#ff8a5c><b>!</b></color> ");
			sb.Append($"<b><noparse>{name}</noparse></b>  <size=80%>v{ver}  by <noparse>{author}</noparse></size>\n");
			sb.Append($"<size=80%><color=#9aa0aa>{origin}  |  <noparse>{status}</noparse></color></size>");
			if (!mod.Compatible)
			{
				sb.Append($"\n<size=85%><color=#ff8a5c>Not compatible with RuinarchModLoader: <noparse>{mod.RejectionReason}</noparse></color></size>");
			}
			if (!string.IsNullOrEmpty(desc))
			{
				sb.Append($"\n<size=85%><noparse>{desc}</noparse></size>");
			}

			var info = Label(row.transform, sb.ToString(), 18, TextAlignmentOptions.TopLeft, FontStyles.Normal);
			info.GetComponent<LayoutElement>().flexibleWidth = 1f;

			if (mod.Origin == ModOrigin.SteamWorkshop)
			{
				Button(row.transform, "Item page", () => Workshop.OpenItem(mod.WorkshopId));
			}
			// Rejected and duplicate packages have nothing to switch on.
			if (!mod.Compatible)
			{
				return;
			}

			var tuple = Button(row.transform, mod.Enabled ? "Enabled" : "Disabled", null);
			Button toggle = tuple.Item1;
			Image toggleImg = tuple.Item2;
			TextMeshProUGUI toggleLbl = tuple.Item3;
			ApplyToggleVisual(toggleImg, toggleLbl, mod.Enabled);
			var tle = toggle.gameObject.GetComponent<LayoutElement>();
			tle.minWidth = 140;

			toggle.onClick.AddListener(() =>
			{
				bool now = !mod.Enabled;
				ModLoader.SetModEnabled(mod.Id, now);
				ApplyToggleVisual(toggleImg, toggleLbl, now);
				if (_noteLabel != null)
				{
					_noteLabel.gameObject.SetActive(true);
				}
				ModMenuMod.Log?.Info($"{(now ? "Enabled" : "Disabled")} '{mod.Id}' (applies next launch).");
			});
		}

		private static GameObject _upload;
		private static readonly (string label, ERemoteStoragePublishedFileVisibility? value)[] Visibilities =
		{
			("Keep current visibility", null),
			("Private", ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate),
			("Friends only", ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly),
			("Public", ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic),
		};

		private static void OpenUpload()
		{
			if (_upload != null) UnityEngine.Object.Destroy(_upload);
			if (_template != null) { EditorUI.Font = _template.font; EditorUI.FontMaterial = _template.fontSharedMaterial; }
			_upload = EditorUI.Box("Workshop upload", _panel.transform, new Color(0, 0, 0, .94f));
			EditorUI.Stretch(_upload);
			_upload.AddComponent<LayoutElement>().ignoreLayout = true;
			var col = EditorUI.Column("Upload", _upload.transform, 20);
			EditorUI.Rect(col, new Vector2(.12f, .06f), new Vector2(.88f, .94f), Vector2.zero, Vector2.zero);
			EditorUI.Label(col.transform, "Upload a local package to Steam Workshop", 24);
			Transform list = EditorUI.Scroll(col.transform, "Packages");
			KnownMod chosen = null;
			TextMeshProUGUI chosenLabel = null, status = null;
			foreach (KnownMod mod in ModLoader.Known.Where(m => m.Origin == ModOrigin.Local && m.Compatible))
			{
				EditorUI.Button(list, $"{mod.Info.name} ({mod.Id}) v{mod.Info.version}", () => { chosen = mod; chosenLabel.text = "Package: " + mod.Info.name + "  (" + mod.Directory + ")"; });
			}
			chosenLabel = EditorUI.Label(col.transform, "Choose a package above.", 17);
			var preview = EditorUI.Input(col.transform, "", null, "Workshop picture: PNG/JPG/GIF path (blank uses package preview.png; under 1 MB)");
			var item = EditorUI.Input(col.transform, "", null, "Existing Workshop item id (leave blank to create a new item)");
			var note = EditorUI.Input(col.transform, "", null, "Change note (optional)");
			int visibility = 1;
			Button visButton = null;
			Action showVisibility = () => visButton.GetComponentInChildren<TextMeshProUGUI>().text = "Visibility: " + Visibilities[visibility].label
				+ (Visibilities[visibility].value == null ? " (new items: Private)" : "");
			Transform actions = EditorUI.Row(col.transform);
			visButton = EditorUI.Button(actions, "", () => { visibility = (visibility + 1) % Visibilities.Length; showVisibility(); });
			showVisibility();
			EditorUI.Button(actions, "Upload", () =>
			{
				try
				{
					if (chosen == null) throw new InvalidOperationException("Choose a package first.");
					ulong id = 0;
					if (!string.IsNullOrWhiteSpace(item.text) && !ulong.TryParse(item.text.Trim(), out id)) throw new InvalidOperationException("The item id must be a number.");
					WorkshopUpload.Start(chosen, id, Visibilities[visibility].value, note.text, preview.text, s => { if (status != null) status.text = s; });
				}
				catch (Exception e) { status.text = e.Message; }
			});
			EditorUI.Button(actions, "Close", () => { if (!WorkshopUpload.Busy) { UnityEngine.Object.Destroy(_upload); _upload = null; } else status.text = "Wait for the upload to finish."; });
			status = EditorUI.Label(col.transform, Workshop.Ready ? "" : "Steam is not available. Start the game through Steam to upload.", 17);
			status.gameObject.GetComponent<LayoutElement>().preferredHeight = 52;
			_upload.AddComponent<UploadProgress>().Status = status;
		}

		private sealed class UploadProgress : MonoBehaviour
		{
			internal TextMeshProUGUI Status;
			private void Update()
			{
				if (Status != null && WorkshopUpload.Progress(out ulong done, out ulong total, out EItemUpdateStatus stage) && stage != EItemUpdateStatus.k_EItemUpdateStatusInvalid)
				{
					string what = stage.ToString().Replace("k_EItemUpdateStatus", "");
					Status.text = total > 0 ? $"{what}: {done / 1024} of {total / 1024} KB" : what + "...";
				}
			}
		}

		private static void ApplyToggleVisual(Image img, TextMeshProUGUI lbl, bool enabled)
		{
			if (img != null)
			{
				img.color = enabled ? new Color(0.20f, 0.45f, 0.22f, 1f) : new Color(0.45f, 0.20f, 0.20f, 1f);
			}
			if (lbl != null)
			{
				lbl.text = enabled ? "Enabled" : "Disabled";
			}
		}

		private static void RefreshLog()
		{
			if (_logLabel == null)
			{
				return;
			}
			try
			{
				string path = ModLoader.LogFile;
				if (string.IsNullOrEmpty(path) || !File.Exists(path))
				{
					_logLabel.text = "(no log yet)";
					return;
				}
				string[] lines = File.ReadAllLines(path);
				int take = Math.Min(22, lines.Length);
				_logLabel.text = string.Join("\n", lines.Skip(lines.Length - take));
			}
			catch (Exception e)
			{
				_logLabel.text = "(could not read log: " + e.Message + ")";
			}
		}

		private static void OpenModsFolder()
		{
			try
			{
				string root = ModLoader.ModsRoot;
				if (!string.IsNullOrEmpty(root))
				{
					Application.OpenURL("file://" + root);
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning("[ModMenu] Could not open Mods folder: " + e.Message);
			}
		}

		// --- uGUI helpers -----------------------------------------------------

		private static TMP_Text FindFontTemplate(GameObject scope)
		{
			TMP_Text t = scope != null ? scope.GetComponentInChildren<TMP_Text>(true) : null;
			if (t == null)
			{
				t = UnityEngine.Object.FindObjectOfType<TMP_Text>();
			}
			return t;
		}

		private static GameObject NewUI(string name, Transform parent)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			return go;
		}

		private static void Stretch(GameObject go)
		{
			var rt = go.GetComponent<RectTransform>();
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
		}

		private static TextMeshProUGUI Label(Transform parent, string text, float size,
			TextAlignmentOptions align, FontStyles style, Color? color = null, bool fit = true)
		{
			var go = NewUI("Label", parent);
			var t = go.AddComponent<TextMeshProUGUI>();
			if (_template != null)
			{
				t.font = _template.font;
				t.fontSharedMaterial = _template.fontSharedMaterial;
			}
			t.text = text;
			t.fontSize = size;
			t.fontStyle = style;
			t.alignment = align;
			t.color = color ?? Color.white;
			t.enableWordWrapping = true;
			t.richText = true;
			if (fit)
			{
				go.AddComponent<LayoutElement>();
				go.AddComponent<FitTextHeight>();
			}
			return t;
		}

		// TMP caches its preferred height from the width it had when the text was set; inside
		// layout groups that width is often still zero, so wrapped labels got one line's
		// height and overlapped the next row. Measure again whenever width or text changes.
		private sealed class FitTextHeight : MonoBehaviour
		{
			private TextMeshProUGUI _text;
			private LayoutElement _layout;
			private float _width = -1;
			private string _measured;

			private void Awake()
			{
				_text = GetComponent<TextMeshProUGUI>();
				_layout = GetComponent<LayoutElement>();
			}

			private void LateUpdate()
			{
				float width = _text.rectTransform.rect.width;
				if (width <= 1 || (Mathf.Approximately(width, _width) && ReferenceEquals(_measured, _text.text)))
				{
					return;
				}
				_width = width;
				_measured = _text.text;
				float height = Mathf.Ceil(_text.GetPreferredValues(_text.text, width, 0).y);
				if (!Mathf.Approximately(_layout.preferredHeight, height))
				{
					_layout.minHeight = height;
					_layout.preferredHeight = height;
					LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform.parent);
				}
			}
		}

		private static Tuple<Button, Image, TextMeshProUGUI> Button(Transform parent, string label, Action onClick)
		{
			var go = NewUI("Button", parent);
			var img = go.AddComponent<Image>();
			img.color = new Color(0.18f, 0.20f, 0.26f, 1f);
			var btn = go.AddComponent<Button>();
			btn.targetGraphic = img;
			var le = go.AddComponent<LayoutElement>();
			le.minHeight = 34;
			le.preferredHeight = 34;
			le.flexibleHeight = 0f;
			le.minWidth = 100;

			var lbl = Label(go.transform, label, 16, TextAlignmentOptions.Center, FontStyles.Normal, fit: false);
			Stretch(lbl.gameObject);

			if (onClick != null)
			{
				btn.onClick.AddListener(() => onClick());
			}
			return Tuple.Create(btn, img, lbl);
		}

		private static T Field<T>(object target, string name) where T : class
		{
			if (target == null)
			{
				return null;
			}
			var f = AccessTools.Field(target.GetType(), name);
			return f?.GetValue(target) as T;
		}
	}
}
