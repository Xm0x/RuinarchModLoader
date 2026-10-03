using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ruinarch.Modding;
using Settings;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ruinarch.ModMenu
{
	// The game's Settings window is one object for the whole session (SettingsManager is
	// DontDestroyOnLoad); the main menu and the pause menu both open it. Build the Mods tab the
	// first time it opens, then refresh the list on every open.
	[HarmonyPatch(typeof(SettingsManager), nameof(SettingsManager.OpenSettings))]
	internal static class SettingsManager_OpenSettings_Patch
	{
		private static void Postfix(SettingsManager __instance)
		{
			try
			{
				SettingsTab.Ensure(__instance.settingsGO);
			}
			catch (Exception e)
			{
				ModMenuMod.Log?.Error("The Mods settings tab could not be built: " + e);
			}
		}
	}

	/// <summary>
	/// A fourth tab, "Mods", in the game's Settings window: the mods that registered settings on
	/// the left, the selected mod's settings on the right. Every control is a clone of the
	/// window's own (tab button, checkbox, slider, dropdown, section header, button) with the
	/// game's localization, hover tooltip and callbacks removed, so it looks native.
	/// </summary>
	internal static class SettingsTab
	{
		private const float ResetConfirmSeconds = 3f;
		private static GameObject _window, _panel, _templates, _left, _right, _bottom;
		private static Transform _list, _rows;
		private static ToggleGroup _listGroup;
		private static TextMeshProUGUI _description, _empty, _resetLabel;
		private static string _selectedId;
		private static float _resetArmedUntil;

		internal static void Ensure(GameObject window)
		{
			if (_panel == null || _window != window)
			{
				_window = window;
				Build(window.transform);
			}
			Refresh();
		}

		private static void Build(Transform window)
		{
			Transform tabs = window.Find("Tabs");
			RectTransform graphicsPanel = (RectTransform)window.Find("Graphics Options");

			_panel = new GameObject("Mods Options", typeof(RectTransform));
			var panelRect = (RectTransform)_panel.transform;
			panelRect.SetParent(window, false);
			panelRect.anchorMin = graphicsPanel.anchorMin;
			panelRect.anchorMax = graphicsPanel.anchorMax;
			panelRect.pivot = graphicsPanel.pivot;
			panelRect.anchoredPosition = graphicsPanel.anchoredPosition;
			panelRect.sizeDelta = graphicsPanel.sizeDelta;
			panelRect.SetSiblingIndex(graphicsPanel.GetSiblingIndex() + 1);
			_panel.AddComponent<ResetClock>();

			// Clones are made under this inactive object, so nothing in them wakes up before it is cleaned.
			_templates = new GameObject("Templates", typeof(RectTransform));
			_templates.transform.SetParent(_panel.transform, false);
			_templates.SetActive(false);

			GameObject tab = Clone(tabs.Find("Graphics Tab"), tabs, "Mods Tab");
			SetText(tab, "Mods");
			Toggle tabToggle = tab.GetComponent<Toggle>();
			tabToggle.SetIsOnWithoutNotify(false);
			// Off now, so joining the window's tab group does not switch the game's tabs.
			tabToggle.group = tabs.GetComponent<ToggleGroup>();
			tabToggle.onValueChanged.AddListener(on => Guard("Mods tab", () => { _panel.SetActive(on); if (on) Refresh(); }));
			_panel.SetActive(false);

			// Left: the list of mods, in its own toggle group.
			_left = Box("Mod List", _panel.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(30, 130), new Vector2(260, -20));
			Image leftBack = _left.AddComponent<Image>();
			leftBack.color = new Color(0f, 0f, 0f, 0.25f);
			var listLayout = _left.AddComponent<VerticalLayoutGroup>();
			listLayout.padding = new RectOffset(8, 8, 8, 8);
			listLayout.spacing = 6;
			listLayout.childControlWidth = true;
			listLayout.childControlHeight = false;
			listLayout.childForceExpandWidth = true;
			listLayout.childForceExpandHeight = false;
			_listGroup = _left.AddComponent<ToggleGroup>();
			_listGroup.allowSwitchOff = false;
			_list = _left.transform;

			// Right: the selected mod's settings, in a clone of the key-binding window's scroll view.
			_right = Clone(window.Find("Gameplay Options/KeyBindWindow/Scroll View"), _panel.transform, "Mod Settings",
				go => { foreach (Transform key in go.GetComponent<ScrollRect>().content.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(key.gameObject); });
			Place(_right, new Vector2(0, 0), new Vector2(1, 1), new Vector2(280, 130), new Vector2(-30, -20));
			ScrollRect scroll = _right.GetComponent<ScrollRect>();
			var content = (RectTransform)scroll.content;
			content.anchorMin = new Vector2(0, 1);
			content.anchorMax = new Vector2(1, 1);
			content.pivot = new Vector2(0, 1);
			content.anchoredPosition = Vector2.zero;
			content.sizeDelta = new Vector2(-24, 0);
			var rowsLayout = content.GetComponent<VerticalLayoutGroup>();
			rowsLayout.spacing = 6;
			rowsLayout.childControlWidth = true;
			rowsLayout.childControlHeight = true;
			rowsLayout.childForceExpandWidth = true;
			rowsLayout.childForceExpandHeight = false;
			var fitter = content.GetComponent<ContentSizeFitter>();
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			_rows = content;

			// Bottom: the description of the setting under the mouse, and Reset to defaults.
			_bottom = Box("Mods Bottom", _panel.transform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(30, 16), new Vector2(-30, 120));
			GameObject description = Clone(window.Find("Gameplay Options/Misc/Log Limit/Slider/Value"), _bottom.transform, "Mods Description");
			Place(description, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0), new Vector2(-240, 0));
			_description = description.GetComponent<TextMeshProUGUI>();
			_description.fontSize = 18;
			_description.enableWordWrapping = true;
			_description.alignment = TextAlignmentOptions.MidlineLeft;
			_description.text = "";

			GameObject reset = Clone(window.Find("Gameplay Options/Reset Tutorials Button"), _bottom.transform, "Mods Reset");
			var resetRect = (RectTransform)reset.transform;
			resetRect.anchorMin = resetRect.anchorMax = new Vector2(1, 0.5f);
			resetRect.pivot = new Vector2(1, 0.5f);
			resetRect.anchoredPosition = Vector2.zero;
			_resetLabel = reset.GetComponentInChildren<TextMeshProUGUI>(true);
			_resetLabel.text = "Reset to defaults";
			reset.GetComponent<Button>().onClick.AddListener(() => Guard("Reset to defaults", OnReset));

			GameObject empty = Clone(window.Find("Gameplay Options/Misc/Log Limit/Slider/Value"), _panel.transform, "Mods Empty");
			Place(empty, new Vector2(0, 0), new Vector2(1, 1), new Vector2(30, 130), new Vector2(-30, -20));
			_empty = empty.GetComponent<TextMeshProUGUI>();
			_empty.fontSize = 24;
			_empty.alignment = TextAlignmentOptions.Center;
			_empty.text = "No installed mod has settings.";

			ModMenuMod.Log?.Info("Mods tab added to the game's Settings window.");
		}

		private static void Refresh()
		{
			if (_panel == null) return;
			foreach (Transform child in _list.Cast<Transform>().ToArray()) UnityEngine.Object.Destroy(child.gameObject);
			List<RegisteredSettings> mods = RegisteredSettings.All.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
			bool any = mods.Count > 0;
			_left.SetActive(any);
			_right.SetActive(any);
			_bottom.SetActive(any);
			_empty.gameObject.SetActive(!any);
			if (!any) return;
			if (mods.All(m => m.ModId != _selectedId)) _selectedId = mods[0].ModId;
			Toggle selected = null;
			foreach (RegisteredSettings mod in mods)
			{
				GameObject entry = Clone(_window.transform.Find("Tabs/Graphics Tab"), _list, "Mod: " + mod.ModId);
				SetText(entry, mod.Title);
				entry.AddComponent<LayoutElement>().preferredHeight = 36;
				Toggle toggle = entry.GetComponent<Toggle>();
				toggle.SetIsOnWithoutNotify(false);
				toggle.group = _listGroup;
				string id = mod.ModId;
				toggle.onValueChanged.AddListener(on => Guard("Mods list", () => { if (on) { _selectedId = id; BuildRows(); } }));
				if (id == _selectedId) selected = toggle;
			}
			// Its listener builds the rows; if it is somehow already on, the listener will not fire.
			if (selected.isOn) BuildRows();
			else selected.isOn = true;
		}

		private static void BuildRows()
		{
			foreach (Transform child in _rows.Cast<Transform>().ToArray()) UnityEngine.Object.Destroy(child.gameObject);
			_description.text = "";
			_resetArmedUntil = 0f;
			_resetLabel.text = "Reset to defaults";
			RegisteredSettings mod = RegisteredSettings.All.FirstOrDefault(s => s.ModId == _selectedId);
			if (mod == null) return;
			Transform w = _window.transform;

			GameObject title = Row("Mod Title", 46, null);
			GameObject titleText = Clone(w.Find("Title"), title.transform, "Text");
			Place(titleText, Vector2.zero, Vector2.one, new Vector2(10, 0), Vector2.zero);
			var titleLabel = titleText.GetComponent<TextMeshProUGUI>();
			titleLabel.text = mod.Title;
			titleLabel.fontSize = 30;
			titleLabel.alignment = TextAlignmentOptions.MidlineLeft;

			string section = null;
			foreach (SettingField f in mod.Fields)
			{
				if (f.Section != null && f.Section != section) SectionRow(f.Section);
				section = f.Section;
				SettingRow(mod, f);
			}
		}

		private static void SectionRow(string header)
		{
			Transform w = _window.transform;
			GameObject row = Row("Section: " + header, 40, null);
			Clone(w.Find("Gameplay Options/Controls/Image"), row.transform, "Divider");
			GameObject cover = Clone(w.Find("Gameplay Options/Controls/TextCover"), row.transform, "TextCover");
			var text = cover.GetComponentInChildren<TextMeshProUGUI>(true);
			text.text = header;
			var rect = (RectTransform)cover.transform;
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
			rect.sizeDelta = new Vector2(text.GetPreferredValues(header).x + 40, rect.sizeDelta.y);
		}

		private static void SettingRow(RegisteredSettings mod, SettingField f)
		{
			Transform w = _window.transform;
			float note = f.RequiresRestart ? 16 : 0;
			GameObject row;
			switch (f.Kind)
			{
				case SettingKind.Toggle:
				{
					row = Row("Setting: " + f.Name, 40 + note, f.Description);
					GameObject box = Clone(w.Find("Gameplay Options/Controls/EdgePanning"), row.transform, "Control");
					Anchor(box, 10, note / 2);
					var label = box.GetComponentInChildren<TextMeshProUGUI>(true);
					Fit(label, f.Label, 360);
					Toggle toggle = box.GetComponent<Toggle>();
					toggle.SetIsOnWithoutNotify((bool)mod.Get(f));
					toggle.onValueChanged.AddListener(v => Guard(f.Name, () => { mod.Set(f, v); UpdateNote(row, mod, f); }));
					break;
				}
				case SettingKind.Dropdown:
				{
					row = Row("Setting: " + f.Name, 56 + note, f.Description);
					GameObject pick = Clone(w.Find("Gameplay Options/Language/Language"), row.transform, "Control");
					Anchor(pick, 0, note / 2);
					Fit(pick.transform.Find("Title").GetComponent<TextMeshProUGUI>(), f.Label, 150);
					TMP_Dropdown dropdown = pick.GetComponentInChildren<TMP_Dropdown>(true);
					dropdown.ClearOptions();
					dropdown.AddOptions(f.Options.ToList());
					dropdown.SetValueWithoutNotify(f.Options.ToList().IndexOf(mod.Get(f).ToString()));
					dropdown.onValueChanged.AddListener(i => Guard(f.Name, () => { mod.Set(f, f.Options[i]); UpdateNote(row, mod, f); }));
					break;
				}
				default:
				{
					row = Row("Setting: " + f.Name, 50 + note, f.Description);
					GameObject line = Clone(w.Find("Gameplay Options/Misc/Log Limit"), row.transform, "Control",
						go => UnityEngine.Object.DestroyImmediate(go.transform.Find("Log Limit Text Field").gameObject));
					Anchor(line, 0, note / 2);
					// The cloned label sits left of the slider and grows leftwards past the row's
					// edge; lay both out from the row's left edge instead.
					var lbl = line.transform.Find("Lbl").GetComponent<TextMeshProUGUI>();
					Slider slider = line.GetComponentInChildren<Slider>(true);
					Fit(lbl, f.Label, SliderLabelWidth);
					LeftAt(lbl.rectTransform, 0, SliderLabelWidth);
					var sliderRect = (RectTransform)slider.transform;
					LeftAt(sliderRect, SliderLabelWidth + 16, sliderRect.rect.width);
					var value = slider.transform.Find("Value").GetComponent<TextMeshProUGUI>();
					bool whole = f.Kind == SettingKind.IntSlider;
					slider.wholeNumbers = whole;
					slider.minValue = f.Min;
					slider.maxValue = f.Max;
					float current = Convert.ToSingle(mod.Get(f));
					slider.SetValueWithoutNotify(current);
					value.text = whole ? ((int)current).ToString() : current.ToString("0.00");
					slider.onValueChanged.AddListener(v => Guard(f.Name, () =>
					{
						value.text = whole ? ((int)v).ToString() : v.ToString("0.00");
						mod.Set(f, whole ? (object)Mathf.RoundToInt(v) : v);
						UpdateNote(row, mod, f);
					}));
					break;
				}
			}
			if (!f.RequiresRestart) return;
			GameObject restart = Clone(w.Find("Gameplay Options/Misc/Log Limit/Slider/Value"), row.transform, "Restart Note");
			var noteRect = (RectTransform)restart.transform;
			noteRect.anchorMin = noteRect.anchorMax = noteRect.pivot = new Vector2(0, 0);
			noteRect.anchoredPosition = new Vector2(f.Kind == SettingKind.Toggle ? 55 : 10, 0);
			noteRect.sizeDelta = new Vector2(300, 16);
			var noteText = restart.GetComponent<TextMeshProUGUI>();
			noteText.text = "Restart to apply";
			noteText.alignment = TextAlignmentOptions.BottomLeft;
			UpdateNote(row, mod, f);
		}

		private static void UpdateNote(GameObject row, RegisteredSettings mod, SettingField f)
		{
			Transform note = row.transform.Find("Restart Note");
			if (note != null) note.gameObject.SetActive(mod.RestartPending(f));
		}

		private static void OnReset()
		{
			if (Time.unscaledTime > _resetArmedUntil)
			{
				_resetArmedUntil = Time.unscaledTime + ResetConfirmSeconds;
				_resetLabel.text = "Click again to reset";
				return;
			}
			RegisteredSettings.All.FirstOrDefault(s => s.ModId == _selectedId)?.ResetToDefaults();
			BuildRows();
		}

		// Every UI callback runs through here: nothing in the settings system throws into the game.
		private static void Guard(string what, Action a)
		{
			try { a(); }
			catch (Exception e) { ModMenuMod.Log?.Error("Mods tab (" + what + "): " + e.Message); }
		}

		internal static void ShowDescription(string text)
		{
			if (_description != null) _description.text = text ?? "";
		}

		// A full-width row in the settings list. A clear Image makes the whole row catch the mouse,
		// so its description shows anywhere along it.
		private static GameObject Row(string name, float height, string description)
		{
			var row = new GameObject(name, typeof(RectTransform));
			row.transform.SetParent(_rows, false);
			row.AddComponent<LayoutElement>().preferredHeight = height;
			Image hit = row.AddComponent<Image>();
			hit.color = Color.clear;
			if (!string.IsNullOrEmpty(description)) row.AddComponent<RowHover>().Text = description;
			return row;
		}

		private static GameObject Box(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			Place(go, anchorMin, anchorMax, offsetMin, offsetMax);
			return go;
		}

		private static void Place(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
		{
			var rt = (RectTransform)go.transform;
			rt.anchorMin = anchorMin;
			rt.anchorMax = anchorMax;
			rt.offsetMin = offsetMin;
			rt.offsetMax = offsetMax;
		}

		// Left edge of the row, vertically centred, lifted by `up` (room for the restart note).
		private static void Anchor(GameObject go, float x, float up)
		{
			var rt = (RectTransform)go.transform;
			rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
			rt.pivot = new Vector2(0, 0.5f);
			rt.anchoredPosition = new Vector2(x, up);
		}

		private const float SliderLabelWidth = 170;

		// Pins a control to its parent's left edge, vertically centred, keeping its height.
		private static void LeftAt(RectTransform rt, float x, float width)
		{
			float height = rt.rect.height;
			rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0.5f);
			rt.anchoredPosition = new Vector2(x, 0);
			rt.sizeDelta = new Vector2(width > 0 ? width : 220, height);
		}

		private static void Fit(TextMeshProUGUI label, string text, float width)
		{
			label.text = text;
			label.enableAutoSizing = true;
			label.fontSizeMin = 14;
			label.fontSizeMax = 24;
			var rt = label.rectTransform;
			rt.sizeDelta = new Vector2(width - (rt.anchorMax.x - rt.anchorMin.x) * ((RectTransform)rt.parent).rect.width, rt.sizeDelta.y);
		}

		private static void SetText(GameObject go, string text)
		{
			TextMeshProUGUI label = go.GetComponentInChildren<TextMeshProUGUI>(true);
			if (label != null) label.text = text;
		}

		// Copies a part of the game's window without its localization, tooltip and callbacks.
		// `prepare` removes further parts while the copy is still asleep.
		private static GameObject Clone(Transform source, Transform parent, string name, Action<GameObject> prepare = null)
		{
			GameObject copy = UnityEngine.Object.Instantiate(source.gameObject, _templates.transform, false);
			copy.name = name;
			foreach (var c in copy.GetComponentsInChildren<UtilityScripts.CustomLocalizeStringEvent>(true)) UnityEngine.Object.DestroyImmediate(c);
			foreach (var h in copy.GetComponentsInChildren<HoverHandler>(true)) UnityEngine.Object.DestroyImmediate(h);
			foreach (var s in copy.GetComponentsInChildren<Selectable>(true)) Mute(s);
			// Leave the window's tab group before going live: joining it while active would switch the game's tabs.
			foreach (Toggle t in copy.GetComponentsInChildren<Toggle>(true)) { t.group = null; t.SetIsOnWithoutNotify(false); }
			prepare?.Invoke(copy);
			copy.transform.SetParent(parent, false);
			copy.SetActive(true);
			return copy;
		}

		private static void Mute(Selectable s)
		{
			UnityEventBase e = s is Toggle t ? t.onValueChanged : s is Slider sl ? sl.onValueChanged
				: s is Button b ? b.onClick : s is TMP_Dropdown d ? (UnityEventBase)d.onValueChanged : null;
			if (e == null) return;
			for (int i = 0; i < e.GetPersistentEventCount(); i++) e.SetPersistentListenerState(i, UnityEventCallState.Off);
		}

		private sealed class RowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
		{
			internal string Text;
			public void OnPointerEnter(PointerEventData eventData) => ShowDescription(Text);
			public void OnPointerExit(PointerEventData eventData) => ShowDescription(null);
		}

		// Puts the reset button's label back when the confirm click does not come in time.
		private sealed class ResetClock : MonoBehaviour
		{
			private void Update()
			{
				if (_resetArmedUntil > 0f && Time.unscaledTime > _resetArmedUntil)
				{
					_resetArmedUntil = 0f;
					_resetLabel.text = "Reset to defaults";
				}
			}
		}
	}
}
