using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ruinarch.ModContent.Templates;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ruinarch.ModMenu.Editor
{
	internal static partial class EditorView
	{
		private static TextMeshProUGUI _warnings, _brushLabel;
		private static Button _undoButton, _redoButton;
		private static Transform _palette;
		private static TMP_InputField _paletteSearch;
		private static readonly Dictionary<string, Button> _toolButtons = new Dictionary<string, Button>(), _layerButtons = new Dictionary<string, Button>();
		private static readonly Dictionary<TemplateBrush, Button> _brushButtons = new Dictionary<TemplateBrush, Button>();
		private static readonly Color Selected = new Color32(110, 96, 58, 255);
		private static readonly string[] Layers = { "floor", "detail", "walls", "thin walls", "furniture", "entrances" };
		internal static void Edit(EditorPack pack, EditorDocument document)
		{
			Pack = pack; Document = document; EditorHost.RetainedPack = pack; EditorHost.RetainedDocument = document;
			Shell("Building editor / " + pack.Name);
			var tools = EditorUI.Column("Tools and palette", _body.transform); EditorUI.Rect(tools, Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(280, 0));
			var props = EditorUI.Column("Properties", _body.transform); EditorUI.Rect(props, new Vector2(1, 0), Vector2.one, new Vector2(-300, 0), Vector2.zero);
			var center = EditorUI.New("Canvas area", _body.transform); EditorUI.Rect(center, Vector2.zero, Vector2.one, new Vector2(292, 0), new Vector2(-312, 0));
			var toolbar = EditorUI.New("Canvas toolbar", center.transform); EditorUI.Rect(toolbar, new Vector2(0, 1), Vector2.one, new Vector2(0, -40), Vector2.zero);
			var layout = toolbar.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 6; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
			EditorUI.Button(toolbar.transform, "Save", () => Guard(Save));
			EditorUI.Button(toolbar.transform, "Test", () => Guard(() => { Save(); EditorWorldTest.Begin(Pack, Document); }));
			_undoButton = EditorUI.Button(toolbar.transform, "Undo", () => { Document.Undo(); RefreshDocument(); });
			_redoButton = EditorUI.Button(toolbar.transform, "Redo", () => { Document.Redo(); RefreshDocument(); });
			EditorUI.Button(toolbar.transform, "Fit", () => _canvas.Fit());
			EditorUI.Button(toolbar.transform, "Packs", GoToPacks);
			var canvasGO = EditorUI.New("Building canvas", center.transform); EditorUI.Rect(canvasGO, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0, -48));
			_canvas = canvasGO.AddComponent<EditorCanvas>(); _canvas.Document = document; _canvas.PackDirectory = pack.Directory;
			_canvas.BasePrefab = FindBase(); _canvas.Changed = UpdateChecks; _canvas.Picked = Pick; _canvas.Error = message => _status.text = message;
			if (_root.GetComponent<EditorShortcuts>() == null) _root.AddComponent<EditorShortcuts>();
			_canvas.Initialize();
			EditorUI.Label(tools.transform, "Tools", 21);
			_toolButtons.Clear(); _layerButtons.Clear();
			for (int i = 0; i < 5; i += 3)
			{
				Transform row = EditorUI.Row(tools.transform);
				foreach (string tool in new[] { "Paint", "Erase", "Fill", "Rectangle", "Picker" }.Skip(i).Take(3))
					_toolButtons[tool] = EditorUI.Button(row, tool, () => { _canvas.Tool = tool; Highlight(_toolButtons, tool); });
			}
			Highlight(_toolButtons, _canvas.Tool);
			_rotate = EditorUI.Button(tools.transform, "", () => { _canvas.Rotation = (_canvas.Rotation + 90) % 360; RotationLabel(); });
			RotationLabel();
			EditorUI.Label(tools.transform, "Layers", 21);
			for (int i = 0; i < Layers.Length; i += 2)
			{
				Transform row = EditorUI.Row(tools.transform);
				// Only the layer buttons stretch; expanding the fixed toggles squeezed the names.
				row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
				foreach (string layer in Layers.Skip(i).Take(2))
				{
					var select = EditorUI.Button(row, layer, () => { _canvas.Layer = layer; _status.text = ""; RefreshPalette(); });
					select.GetComponentInChildren<TMP_Text>().fontSize = 13; _layerButtons[layer] = select;
					if (!_canvas.HasLayer(layer)) select.GetComponentInChildren<TMP_Text>().color = new Color32(140, 146, 152, 255);
					var visible = EditorUI.Button(row, _canvas.Hidden.Contains(layer) ? "Show" : "Hide", null, 48); visible.GetComponentInChildren<TMP_Text>().fontSize = 12;
					visible.onClick.AddListener(() => { bool show = _canvas.Hidden.Contains(layer); _canvas.SetVisible(layer, show); visible.GetComponentInChildren<TMP_Text>().text = show ? "Hide" : "Show"; });
				}
			}
			EditorUI.Button(tools.transform, "Toggle grid", () => _canvas.SetVisible("grid", _canvas.Hidden.Contains("grid")));
			_brushLabel = EditorUI.Label(tools.transform, "Choose a tile below", 15); _brushLabel.gameObject.GetComponent<LayoutElement>().preferredHeight = 38;
			_paletteSearch = EditorUI.Input(tools.transform, "", null, "Search tiles");
			_paletteSearch.onValueChanged.AddListener(_ => RefreshPalette());
			_palette = EditorUI.Scroll(tools.transform, "Palette");
			Properties(props.transform); RefreshPalette(); UpdateChecks();
			_status.text = "";
		}
		private static void Highlight<T>(Dictionary<T, Button> buttons, T active)
		{
			foreach (var pair in buttons) if (pair.Value != null) pair.Value.GetComponent<Image>().color = Equals(pair.Key, active) ? Selected : EditorUI.Control;
		}
		private static Button _rotate;
		private static void RotationLabel()
		{
			if (_rotate != null) _rotate.GetComponentInChildren<TMP_Text>().text = "Rotate 90 degrees (now " + _canvas.Rotation + ")";
		}
		private static GameObject FindBase()
		{
			var t = Document.Template;
			var looks = TemplateAuthoring.Looks();
			GameLook look = looks.FirstOrDefault(l => l.Prefab.name == t.behavesLike)
				?? looks.FirstOrDefault(l => TemplateAuthoring.KindId(l.Kind) == t.kind && l.Material.ToString() == t.material && t.cultures.Contains(l.Culture.ToString()))
				?? looks.FirstOrDefault(l => TemplateAuthoring.KindId(l.Kind) == t.kind);
			if (look == null) throw new TemplateException("No base prefab for " + t.kind + ". Enable its code mod or choose an existing building kind.");
			return look.Prefab;
		}
		private static void Properties(Transform parent)
		{
			EditorUI.Label(parent, "Building properties", 21);
			EditorUI.Label(parent, Document.Template.id, 14);
			EditorUI.Label(parent, "Name", 15); EditorUI.Input(parent, Document.Template.name, s => Change(() => Document.Template.name = s), "Building name");
			EditorUI.Label(parent, "Kind", 15);
			EditorUI.Button(parent, Document.Template.kind, () => Choose("Building kind", TemplateAuthoring.Looks().Select(l => TemplateAuthoring.KindId(l.Kind)).Distinct(), value => Change(() => Document.Template.kind = value)));
			EditorUI.Label(parent, "Cultures (comma separated)", 15);
			EditorUI.Input(parent, string.Join(",", Document.Template.cultures), s => Change(() =>
			{
				var values = s.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).Distinct().ToList();
				if (values.Count == 0 || values.Any(v => !Enum.TryParse(v, out FACTION_TYPE f) || !Enum.IsDefined(typeof(FACTION_TYPE), f))) throw new TemplateException("Use valid FACTION_TYPE names, for example None or Human_Empire.");
				Document.Template.cultures = values;
			}), "Cultures");
			EditorUI.Label(parent, "Material (which villages build it)", 15);
			EditorUI.Button(parent, MaterialShort(Document.Template.material), () => Choose("Material: which villages build this look", Materials(), value => Change(() => Document.Template.material = value), MaterialName));
			EditorUI.Label(parent, "Behaves like", 15);
			EditorUI.Button(parent, Document.Template.behavesLike ?? "Default", () => Choose("Base building", ModTemplates.GameLooks().Select(l => l.Prefab.name).Distinct(), value => Change(() => { Document.Template.behavesLike = value; _canvas.BasePrefab = FindBase(); })));
			EditorUI.Label(parent, "Size (width / height)", 15);
			Transform row = EditorUI.Row(parent);
			var width = EditorUI.Input(row, Document.Template.bounds[2].ToString(), null, "Width"); var height = EditorUI.Input(row, Document.Template.bounds[3].ToString(), null, "Height");
			EditorUI.Button(parent, "Resize", () => Guard(() =>
			{
				if (!int.TryParse(width.text, out int w) || !int.TryParse(height.text, out int h)) throw new TemplateException("Size must be whole numbers.");
				Document.BeginEdit(); Document.Resize(w, h); Document.EndEdit(); RefreshDocument(); _canvas.Fit();
			}));
			EditorUI.Label(parent, "Checks", 21);
			Transform warnings = EditorUI.Scroll(parent, "Live checks"); _warnings = EditorUI.Label(warnings, "", 15);
			_warnings.gameObject.GetComponent<LayoutElement>().preferredHeight = 250;
		}
		/// <summary>Villages build most kinds in wood or stone, depending on the resource they can reach;
		/// the game keeps one list of looks per material. Kinds without versions use NONE.</summary>
		private static IEnumerable<string> Materials()
		{
			var used = TemplateAuthoring.Looks().Where(l => TemplateAuthoring.KindId(l.Kind) == Document.Template.kind).Select(l => l.Material.ToString()).Distinct().ToList();
			return used.Count > 0 ? used : new List<string> { "WOOD", "STONE", "NONE" };
		}
		private static string MaterialShort(string material)
		{
			return material == "WOOD" ? "Wood" : material == "STONE" ? "Stone" : material == "NONE" ? "Any village" : material;
		}
		private static string MaterialName(string material)
		{
			return material == "WOOD" ? "Wood: villages that build in wood"
				: material == "STONE" ? "Stone: villages that build in stone"
				: material == "NONE" ? "Any village: this kind has no wood or stone versions"
				: material;
		}
		private static void Choose(string title, IEnumerable<string> values, Action<string> chosen, Func<string, string> name = null)
		{
			name = name ?? (v => v);
			DismissDialog(); _dialog = EditorUI.Box("Property picker", _root.transform, new Color(0, 0, 0, .85f)); EditorUI.Stretch(_dialog);
			var col = EditorUI.Column(title, _dialog.transform); EditorUI.Rect(col, new Vector2(.2f, .12f), new Vector2(.8f, .88f), Vector2.zero, Vector2.zero);
			EditorUI.Label(col.transform, title, 23); var search = EditorUI.Input(col.transform, "", null, "Search");
			Transform list = EditorUI.Scroll(col.transform, "Choices");
			Action refresh = () => { EditorUI.Clear(list); foreach (string value in values.OrderBy(v => name(v)).Where(v => name(v).IndexOf(search.text, StringComparison.OrdinalIgnoreCase) >= 0)) EditorUI.Button(list, name(value), () => { DismissDialog(); chosen(value); Edit(Pack, Document); }); };
			search.onValueChanged.AddListener(_ => refresh()); EditorUI.Button(col.transform, "Cancel", DismissDialog); refresh();
		}
		private static void Change(Action mutate)
		{
			Guard(() => { Document.BeginEdit(); try { mutate(); } finally { Document.EndEdit(); } RefreshDocument(); });
		}
		private static void RefreshDocument()
		{
			_canvas.BasePrefab = FindBase(); _canvas.Redraw(); UpdateChecks();
			var name = _root.GetComponentsInChildren<TMP_InputField>().FirstOrDefault(f => f.name == "Building name");
			if (name != null) name.SetTextWithoutNotify(Document.Template.name);
		}
		private static void RefreshPalette()
		{
			if (_palette == null || _canvas == null) return;
			EditorUI.Clear(_palette); _brushButtons.Clear(); Highlight(_layerButtons, _canvas.Layer);
			if (!_canvas.HasLayer(_canvas.Layer))
			{
				_canvas.Value = null; _brushLabel.text = "Nothing to paint on this layer";
				EditorUI.Label(_palette, _canvas.MissingLayer(_canvas.Layer), 14).GetComponent<LayoutElement>().preferredHeight = 110;
				return;
			}
			if (_canvas.Layer == "entrances")
			{
				EditorUI.Button(_palette, "Entrance marker", () => { _canvas.Value = "entrance"; _canvas.SpriteReference = null; _brushLabel.text = "Entrance marker"; });
				_canvas.Value = "entrance"; _brushLabel.text = "Entrance marker"; return;
			}
			// One brush per tile or furniture piece, preferring the base building's own copy.
			string own = " / " + _canvas.BasePrefab.name;
			var used = new HashSet<string>(Document.Template.palette.Values.Concat(Document.Template.objects.Select(o => o.type + "|" + o.sprite)).Concat(Document.Template.thinWalls.Select(w => w.layout)).Where(v => v != null));
			var brushes = TemplateAuthoring.Brushes().Where(b => b.Layer == _canvas.Layer)
				.GroupBy(b => (b.Reference, b.ObjectType, b.Name))
				.Select(g => g.FirstOrDefault(b => b.Source.EndsWith(own)) ?? g.First())
				.Select(b => (brush: b, group: b.Source.EndsWith(own) || used.Contains(b.Reference) || used.Contains(b.ObjectType + "|" + b.Reference) || (b.Wall != null && used.Contains(b.Wall.layout)) ? "In this building" : "All game buildings"))
				.ToList();
			string art = Path.Combine(Pack.Directory, "art");
			if (Directory.Exists(art)) foreach (string file in Directory.GetFiles(art, "*.png", SearchOption.AllDirectories))
			{
				string reference = "art:" + file.Substring(art.Length + 1).Replace('\\', '/');
				var sprite = TemplateAuthoring.Sprite(reference, Pack.Directory);
				if (_canvas.Layer == "furniture")
				{
					foreach (string type in brushes.Select(b => b.brush.ObjectType).Distinct().ToArray()) brushes.Add((new TemplateBrush { Reference = reference, ObjectType = type, Layer = "furniture", Source = "Pack PNGs", Sprite = sprite }, "Your pack's PNGs"));
				}
				else if (_canvas.Layer != "thin walls") brushes.Add((new TemplateBrush { Reference = reference, Layer = _canvas.Layer, Source = "Pack PNGs", Sprite = sprite }, "Your pack's PNGs"));
			}
			var order = new[] { "In this building", "Your pack's PNGs", "All game buildings" };
			var filtered = brushes.Where(b => BrushName(b.brush).IndexOf(_paletteSearch.text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			foreach (var group in filtered.GroupBy(b => b.group).OrderBy(g => Array.IndexOf(order, g.Key)))
			{
				var title = EditorUI.Label(_palette, group.Key + " (" + group.Count() + ")", 15); title.color = EditorUI.Accent; title.gameObject.GetComponent<LayoutElement>().preferredHeight = 36;
				foreach (TemplateBrush brush in group.Select(b => b.brush).OrderBy(BrushName))
				{
					var button = EditorUI.Button(_palette, BrushName(brush), () =>
					{
						SelectBrush(brush);
						if (_canvas.Tool == "Erase" || _canvas.Tool == "Picker") { _canvas.Tool = "Paint"; Highlight(_toolButtons, "Paint"); }
					});
					button.GetComponent<LayoutElement>().preferredHeight = 52;
					var text = button.GetComponentInChildren<TMP_Text>(); text.fontSize = 13; text.alignment = TextAlignmentOptions.MidlineLeft;
					EditorUI.Rect(text.gameObject, Vector2.zero, Vector2.one, new Vector2(54, 0), new Vector2(-4, 0));
					if (brush.Sprite != null)
					{
						var icon = EditorUI.New("Brush icon", button.transform); var image = icon.AddComponent<Image>(); image.sprite = brush.Sprite; image.preserveAspect = true; image.raycastTarget = false;
						EditorUI.Rect(icon, Vector2.zero, new Vector2(0, 1), new Vector2(4, 4), new Vector2(48, -4));
					}
					_brushButtons[brush] = button;
				}
			}
			TemplateBrush first = filtered.OrderBy(b => Array.IndexOf(order, b.group)).ThenBy(b => BrushName(b.brush)).Select(b => b.brush).FirstOrDefault();
			if (first != null) SelectBrush(first); else { _canvas.Value = null; _brushLabel.text = "Nothing matches the search."; }
		}
		/// <summary>A readable palette name: the tile, furniture or wall piece without reference prefixes.</summary>
		private static string BrushName(TemplateBrush brush)
		{
			string reference = brush.Reference == null ? "" : brush.Reference.StartsWith("game:") ? brush.Reference.Substring(5) : brush.Reference.StartsWith("art:") ? brush.Reference.Substring(4) : brush.Reference;
			string kind = brush.ObjectType != null ? Words(brush.ObjectType) : brush.Name;
			return kind == null ? reference : reference.Length == 0 ? kind : kind + " (" + reference + ")";
		}
		private static string Words(string id)
		{
			string s = id.Replace('_', ' ').ToLowerInvariant(); return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
		}
		private static void SelectBrush(TemplateBrush brush)
		{
			_canvas.Value = brush.ObjectType ?? brush.Reference; _canvas.SpriteReference = brush.ObjectType != null ? brush.Reference : null;
			if (brush.Wall != null || _canvas.Layer != "thin walls") Document.WallBrush = brush.Wall;
			_canvas.Rotation = brush.Rotation; RotationLabel(); _brushLabel.text = BrushName(brush);
			Highlight(_brushButtons, brush);
		}
		private static void Pick(string layer, int x, int y)
		{
			_canvas.Value = Document.Get(layer, x, y);
			if (layer == "furniture")
			{
				TemplateObject o = Document.Template.objects.FirstOrDefault(v => Mathf.FloorToInt(v.pos[0] - Document.GridOffsetX) == x && Mathf.FloorToInt(v.pos[1] - Document.GridOffsetY) == y);
				_canvas.SpriteReference = o?.sprite; _canvas.Rotation = o?.rot ?? 0;
			}
			else if (layer == "thin walls")
			{
				Document.WallBrush = Document.Template.thinWalls.FirstOrDefault(v => Mathf.FloorToInt(v.pos[0] - Document.GridOffsetX) == x && Mathf.FloorToInt(v.pos[1] - Document.GridOffsetY) == y);
				_canvas.Rotation = Document.WallBrush?.rot ?? 0;
			}
			_canvas.Tool = "Paint"; Highlight(_toolButtons, "Paint"); Highlight(_brushButtons, null); RotationLabel();
			_brushLabel.text = _canvas.Value == null ? "Empty cell (paints as eraser)" : _canvas.Value.StartsWith("game:") ? _canvas.Value.Substring(5) : _canvas.Value;
		}
		private static void UpdateChecks()
		{
			if (_warnings == null) return;
			bool needsEntrance = _canvas.BasePrefab.GetComponent<LocationStructureObject>().connectors?.Length > 0;
			List<string> warnings = Document.Warnings(r => TemplateAuthoring.Tile(r, Pack.Directory) != null, r => (TemplateAuthoring.Tile(r, Pack.Directory, true)?.name ?? "").Contains("Wall"), needsEntrance);
			_warnings.text = warnings.Count == 0 ? "No grid warnings. Test verifies native paths and furniture access." : string.Join("\n\n", warnings);
			_warnings.gameObject.GetComponent<LayoutElement>().preferredHeight = Mathf.Max(120, _warnings.GetPreferredValues(_warnings.text, 258, 10000).y + 20);
			_undoButton.interactable = Document.CanUndo; _redoButton.interactable = Document.CanRedo;
		}
		private static void Save()
		{
			string file = EditorPacks.Save(Pack, Document); _status.text = "Saved " + Path.GetFileName(file) + ". Warnings do not prevent saving."; UpdateChecks();
		}
		internal static void Shortcut(KeyCode key)
		{
			if (_canvas == null || _dialog != null) return;
			if (key == KeyCode.S) Guard(Save);
			else if (key == KeyCode.Z) { Document.Undo(); RefreshDocument(); }
			else if (key == KeyCode.Y) { Document.Redo(); RefreshDocument(); }
		}
	}
	internal sealed class EditorShortcuts : MonoBehaviour
	{
		private void Update()
		{
			if (EventSystem.current?.currentSelectedGameObject?.GetComponent<TMP_InputField>() != null) return;
			if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
			if (Input.GetKeyDown(KeyCode.S)) EditorView.Shortcut(KeyCode.S);
			if (Input.GetKeyDown(KeyCode.Z)) EditorView.Shortcut(KeyCode.Z);
			if (Input.GetKeyDown(KeyCode.Y)) EditorView.Shortcut(KeyCode.Y);
		}
	}
}
