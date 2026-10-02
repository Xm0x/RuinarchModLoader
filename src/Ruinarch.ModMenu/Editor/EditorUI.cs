using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ruinarch.ModMenu.Editor
{
	internal static class EditorUI
	{
		internal static readonly Color Background = new Color32(23, 29, 35, 255);
		internal static readonly Color Panel = new Color32(38, 45, 52, 255);
		internal static readonly Color Control = new Color32(59, 67, 74, 255);
		internal static readonly Color Accent = new Color32(172, 149, 91, 255);
		internal static TMP_FontAsset Font;
		internal static Material FontMaterial;

		internal static GameObject New(string name, Transform parent)
		{
			var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go;
		}
		internal static void Rect(GameObject go, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
		{
			var rt = go.GetComponent<RectTransform>(); rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = low; rt.offsetMax = high;
		}
		internal static void Stretch(GameObject go) { Rect(go, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); }
		internal static GameObject Box(string name, Transform parent, Color color)
		{
			var go = New(name, parent); go.AddComponent<Image>().color = color; return go;
		}
		internal static TextMeshProUGUI Label(Transform parent, string text, float size = 18)
		{
			var go = New("Label", parent); var label = go.AddComponent<TextMeshProUGUI>();
			label.font = Font; label.fontSharedMaterial = FontMaterial; label.fontSize = size; label.text = text;
			label.color = Color.white; label.enableWordWrapping = true; label.raycastTarget = false;
			label.alignment = TextAlignmentOptions.MidlineLeft; label.richText = false;
			var le = go.AddComponent<LayoutElement>(); le.minHeight = size + 12; le.preferredHeight = size + 12;
			return label;
		}
		internal static Button Button(Transform parent, string text, Action action, float width = 0)
		{
			var go = Box(text, parent, Control); var button = go.AddComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
			var le = go.AddComponent<LayoutElement>(); le.minHeight = 34; le.preferredHeight = 34;
			if (width > 0) { le.minWidth = width; le.preferredWidth = width; }
			else le.flexibleWidth = 1;
			var label = Label(go.transform, text, 16); Stretch(label.gameObject); label.alignment = TextAlignmentOptions.Center;
			if (action != null) button.onClick.AddListener(() => action()); return button;
		}
		internal static TMP_InputField Input(Transform parent, string value, Action<string> changed, string name = "Input")
		{
			var go = Box(name, parent, new Color32(19, 24, 29, 255)); var input = go.AddComponent<TMP_InputField>();
			var viewport = New("Text viewport", go.transform); Rect(viewport, Vector2.zero, Vector2.one, new Vector2(8, 3), new Vector2(-8, -3));
			viewport.AddComponent<RectMask2D>();
			var text = Label(viewport.transform, "", 16); Stretch(text.gameObject);
			input.textViewport = viewport.GetComponent<RectTransform>(); input.textComponent = text; input.targetGraphic = go.GetComponent<Image>();
			var placeholder = Label(viewport.transform, name, 15); Stretch(placeholder.gameObject); placeholder.color = new Color32(153, 164, 174, 255);
			input.placeholder = placeholder;
			input.text = value ?? ""; input.characterLimit = 512;
			var le = go.AddComponent<LayoutElement>(); le.minHeight = 34; le.preferredHeight = 34;
			if (changed != null) input.onEndEdit.AddListener(s => changed(s));
			return input;
		}
		internal static GameObject Column(string name, Transform parent, int padding = 12)
		{
			var go = Box(name, parent, Panel); var layout = go.AddComponent<VerticalLayoutGroup>();
			layout.padding = new RectOffset(padding, padding, padding, padding); layout.spacing = 6;
			layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
			return go;
		}
		internal static Transform Row(Transform parent)
		{
			var go = New("Row", parent); var layout = go.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 6;
			layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
			var le = go.AddComponent<LayoutElement>(); le.minHeight = 34; le.preferredHeight = 34; return go.transform;
		}
		internal static Transform Scroll(Transform parent, string name)
		{
			var go = Box(name, parent, new Color(0, 0, 0, .15f)); var scroll = go.AddComponent<ScrollRect>();
			var viewport = New("Viewport", go.transform); Stretch(viewport); viewport.AddComponent<RectMask2D>();
			var content = New("Content", viewport.transform); var rt = content.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0, 1); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(.5f, 1); rt.sizeDelta = Vector2.zero;
			var layout = content.AddComponent<VerticalLayoutGroup>(); layout.spacing = 4; layout.padding = new RectOffset(4, 4, 4, 4);
			layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
			content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = rt; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
			go.AddComponent<LayoutElement>().flexibleHeight = 1; return content.transform;
		}
		internal static void Clear(Transform parent)
		{
			for (int i = parent.childCount - 1; i >= 0; i--) { var go = parent.GetChild(i).gameObject; go.SetActive(false); UnityEngine.Object.Destroy(go); }
		}
		internal static GameObject Root(string name)
		{
			var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2500;
			var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
			var background = root.AddComponent<Image>(); background.color = Background; return root;
		}
	}
}
