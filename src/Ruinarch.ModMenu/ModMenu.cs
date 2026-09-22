using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using Ruinarch.Modding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ruinarch.ModMenu
{
	// Built-in mod that ships with RuinarchModLoader. It replaces the contents of
	// the game's main-menu "Mods" window (normally the Steam Workshop browser) with
	// a loader-native list of every mod the loader discovered, each with an
	// enable/disable toggle, a view of the shared mods.log, and an "open Mods
	// folder" button.
	//
	// It is built entirely in code (no Unity editor, no AssetBundle): it clones an
	// existing TextMeshPro label from the window to inherit the game's font, then
	// lays out its panel with uGUI layout groups. Toggling a mod writes the loader
	// config; because mods load once at startup, the change applies on next launch.
	public sealed class ModMenuMod : IRuinarchMod
	{
		internal static ModLogger Log;

		// The loader gives this mod a fallback id of the DLL name; hide it from its
		// own list so the manager does not manage itself.
		internal const string SelfId = "Ruinarch.ModMenu";

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
			bg.color = new Color(0.05f, 0.06f, 0.09f, 0.97f);

			var root = _panel.AddComponent<VerticalLayoutGroup>();
			root.padding = new RectOffset(28, 28, 22, 22);
			root.spacing = 8;
			root.childControlWidth = true;
			root.childControlHeight = true;
			root.childForceExpandWidth = true;
			root.childForceExpandHeight = false;

			Label(_panel.transform, "Installed mods", 34, TextAlignmentOptions.Left, FontStyles.Bold);
			Label(_panel.transform,
				"Managed by RuinarchModLoader. Toggle a mod to enable or disable it; " +
				"changes take effect the next time you launch the game.",
				18, TextAlignmentOptions.Left, FontStyles.Normal, new Color(0.75f, 0.78f, 0.85f));

			_noteLabel = Label(_panel.transform, "Change saved. Restart the game to apply.",
				18, TextAlignmentOptions.Left, FontStyles.Italic, new Color(1f, 0.85f, 0.4f));
			_noteLabel.gameObject.SetActive(false);

			// Mod list.
			_listContainer = NewUI("List", _panel.transform);
			var list = _listContainer.AddComponent<VerticalLayoutGroup>();
			list.spacing = 6;
			list.childControlWidth = true;
			list.childControlHeight = true;
			list.childForceExpandWidth = true;
			list.childForceExpandHeight = false;

			// Log heading + body.
			Label(_panel.transform, "mods.log", 22, TextAlignmentOptions.Left, FontStyles.Bold);
			var logHolder = NewUI("Log", _panel.transform);
			var logImg = logHolder.AddComponent<Image>();
			logImg.color = new Color(0f, 0f, 0f, 0.5f);
			var logLe = logHolder.AddComponent<LayoutElement>();
			logLe.minHeight = 200;
			logLe.flexibleHeight = 1f;
			var logPad = logHolder.AddComponent<VerticalLayoutGroup>();
			logPad.padding = new RectOffset(12, 12, 10, 10);
			logPad.childControlWidth = true;
			logPad.childControlHeight = true;
			logPad.childForceExpandWidth = true;
			_logLabel = Label(logHolder.transform, string.Empty, 15, TextAlignmentOptions.TopLeft,
				FontStyles.Normal, new Color(0.7f, 0.85f, 0.7f));

			// Footer buttons.
			var footer = NewUI("Footer", _panel.transform);
			var frow = footer.AddComponent<HorizontalLayoutGroup>();
			frow.spacing = 12;
			frow.childControlWidth = true;
			frow.childControlHeight = true;
			frow.childForceExpandWidth = true;
			var frowLe = footer.AddComponent<LayoutElement>();
			frowLe.minHeight = 46;

			Button(footer.transform, "Refresh", () => { RefreshList(); RefreshLog(); });
			Button(footer.transform, "Open Mods folder", OpenModsFolder);
			Button(footer.transform, "Close", () =>
			{
				if (_parent != null)
				{
					_parent.Hide();
				}
			});
		}

		private static void RefreshList()
		{
			if (_listContainer == null)
			{
				return;
			}
			for (int i = _listContainer.transform.childCount - 1; i >= 0; i--)
			{
				UnityEngine.Object.Destroy(_listContainer.transform.GetChild(i).gameObject);
			}

			var mods = ModLoader.Known.Where(m => m != null && m.Id != ModMenuMod.SelfId).ToList();
			if (mods.Count == 0)
			{
				Label(_listContainer.transform, "No mods found in the Mods folder.", 18,
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

			string status = mod.Loaded ? "running"
				: (mod.Enabled ? "enabled (restart to load)" : "disabled");
			var name = mod.Info != null ? mod.Info.name : mod.Id;
			var ver = mod.Info != null ? mod.Info.version : "0.0.0";
			var author = mod.Info != null ? mod.Info.author : "unknown";
			var desc = mod.Info != null ? mod.Info.description : string.Empty;

			var sb = new StringBuilder();
			sb.Append($"<b>{name}</b>  <size=80%>v{ver}  by {author}</size>\n");
			sb.Append($"<size=80%><color=#9aa0aa>{status}</color></size>");
			if (!string.IsNullOrEmpty(desc))
			{
				sb.Append($"\n<size=85%>{desc}</size>");
			}

			var info = Label(row.transform, sb.ToString(), 18, TextAlignmentOptions.TopLeft, FontStyles.Normal);
			var infoLe = info.gameObject.AddComponent<LayoutElement>();
			infoLe.flexibleWidth = 1f;

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
			TextAlignmentOptions align, FontStyles style, Color? color = null)
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
			return t;
		}

		private static Tuple<Button, Image, TextMeshProUGUI> Button(Transform parent, string label, Action onClick)
		{
			var go = NewUI("Button", parent);
			var img = go.AddComponent<Image>();
			img.color = new Color(0.18f, 0.20f, 0.26f, 1f);
			var btn = go.AddComponent<Button>();
			btn.targetGraphic = img;
			var le = go.AddComponent<LayoutElement>();
			le.minHeight = 42;
			le.minWidth = 120;

			var lbl = Label(go.transform, label, 20, TextAlignmentOptions.Center, FontStyles.Normal);
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
