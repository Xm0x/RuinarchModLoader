using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ruinarch.ModContent.Templates;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ruinarch.ModMenu.Editor
{
	internal static partial class EditorView
	{
		private static GameObject _root, _body, _dialog;
		private static TextMeshProUGUI _status;
		internal static bool IsOpen => _root != null;
		internal static EditorDocument Document;
		internal static EditorPack Pack;
		private static EditorCanvas _canvas;
		private static void Shell(string title)
		{
			if (_root == null) _root = EditorUI.Root("Building template editor");
			EditorUI.Clear(_root.transform);
			var header = EditorUI.Box("Header", _root.transform, EditorUI.Panel);
			EditorUI.Rect(header, new Vector2(0, 1), Vector2.one, new Vector2(0, -64), Vector2.zero);
			var label = EditorUI.Label(header.transform, title, 25); EditorUI.Rect(label.gameObject, Vector2.zero, Vector2.one, new Vector2(24, 0), new Vector2(-140, 0));
			var close = EditorUI.Button(header.transform, "Back", Back); EditorUI.Rect(close.gameObject, new Vector2(1, 0), Vector2.one, new Vector2(-120, 14), new Vector2(-20, -14));
			_body = EditorUI.New("Editor body", _root.transform); EditorUI.Rect(_body, Vector2.zero, Vector2.one, new Vector2(20, 52), new Vector2(-20, -78));
			_status = EditorUI.Label(_root.transform, "", 16); EditorUI.Rect(_status.gameObject, Vector2.zero, new Vector2(1, 0), new Vector2(24, 10), new Vector2(-24, 42));
			_canvas = null;
		}
		internal static void Loading(string text) { Shell("Building templates"); _status.text = text; }
		internal static void LoadFailed(string reason) { _status.text = "Cannot load editor assets: " + reason; }
		internal static void Close()
		{
			if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); } _root = null;
		}
		private static void Guard(Action action)
		{
			try { action(); } catch (Exception e) { _status.text = e.Message; ModMenuMod.Log?.Warning("Editor: " + e.Message); }
		}
		private static void Back()
		{
			if (Document != null && Document.Dirty)
			{
				Dialog("Unsaved building changes", new[] { ("Save and close", (Action)(() => Guard(() => { Save(); ClearRetained(); Close(); }))), ("Discard changes", (Action)(() => { ClearRetained(); Close(); })), ("Keep editing", (Action)DismissDialog) });
			}
			else { ClearRetained(); Close(); }
		}
		private static void ClearRetained() { EditorHost.RetainedDocument = null; EditorHost.RetainedPack = null; Document = null; }
		private static void GoToPacks()
		{
			if (Document != null && Document.Dirty)
				Dialog("Unsaved building changes", new[] { ("Save and return to packs", (Action)(() => Guard(() => { Save(); ClearRetained(); StartScreen(); }))), ("Discard and return to packs", (Action)(() => { ClearRetained(); StartScreen(); })), ("Keep editing", (Action)DismissDialog) });
			else { ClearRetained(); StartScreen(); }
		}
		internal static void StartScreen()
		{
			Document = null; Pack = null; Shell("Building templates");
			var left = EditorUI.Column("Packs", _body.transform); EditorUI.Rect(left, Vector2.zero, new Vector2(.38f, 1), Vector2.zero, new Vector2(-8, 0));
			EditorUI.Label(left.transform, "Template packs", 23);
			EditorUI.Label(left.transform, "Local packs are editable. Workshop templates can be copied into a local pack.", 16).gameObject.GetComponent<LayoutElement>().preferredHeight = 56;
			var packs = EditorUI.Scroll(left.transform, "Pack list");
			var right = EditorUI.Column("Templates", _body.transform); EditorUI.Rect(right, new Vector2(.38f, 0), Vector2.one, new Vector2(8, 0), Vector2.zero);
			EditorUI.Label(right.transform, "Select a pack or create one", 23);
			Transform templates = EditorUI.Scroll(right.transform, "Template list");
			foreach (EditorPack p in EditorPacks.Sources())
				EditorUI.Button(packs, p.Name + " (" + p.Id + ")", () =>
				{
					if (p.ReadOnly) _status.text = "Steam Workshop packs are read-only. Select a local pack, then use New from existing to copy one of its templates.";
					else SelectPack(p, templates);
				});
			EditorUI.Label(left.transform, "Create a pack", 20);
			var name = EditorUI.Input(left.transform, "My buildings", null, "Pack name");
			var id = EditorUI.Input(left.transform, "my.buildings", null, "Pack id");
			EditorUI.Button(left.transform, "Create pack", () => Guard(() => { EditorPacks.Create(id.text, name.text); StartScreen(); }));
			EditorUI.Button(right.transform, "New from existing / New blank", () => { if (Pack == null) _status.text = "Select a pack first."; else Browser(Pack); });
			EditorUI.Button(right.transform, "Open pack folder", () => { if (Pack != null) Application.OpenURL("file://" + Pack.Directory); });
			_status.text = "Packs add building looks. Code mods add building kinds.";
		}
		private static void SelectPack(EditorPack pack, Transform list)
		{
			Pack = pack; EditorUI.Clear(list); _status.text = "Selected " + pack.Name;
			foreach (string file in EditorPacks.Files(pack))
			{
				string title;
				try { var t = ModTemplates.FromJson(File.ReadAllText(file)); title = (t.name ?? Path.GetFileNameWithoutExtension(file)) + " / " + t.kind; }
				catch (Exception e) { title = "! " + Path.GetFileName(file) + ": " + e.Message; }
				EditorUI.Button(list, title, () => Guard(() => { var t = ModTemplates.FromJson(File.ReadAllText(file)); EditorPacks.CheckShape(t); Edit(pack, new EditorDocument(t)); }));
			}
		}
		private static void Browser(EditorPack pack)
		{
			Shell("Choose a building to start from");
			var col = EditorUI.Column("Building browser", _body.transform); EditorUI.Stretch(col);
			EditorUI.Label(col.transform, "Search kind, culture, material or prefab name", 18);
			Transform filters = EditorUI.Row(col.transform);
			var search = EditorUI.Input(filters, "", null, "Building search");
			var kind = EditorUI.Input(filters, "", null, "Kind filter");
			var culture = EditorUI.Input(filters, "", null, "Culture filter");
			var material = EditorUI.Input(filters, "", null, "Material filter");
			Transform list = EditorUI.Scroll(col.transform, "Building looks");
			BuildingTemplate selected = null; EditorPack sourcePack = null;
			var selectedLabel = EditorUI.Label(col.transform, "Select a stock look or another pack's template.", 18);
			Action refresh = () => Guard(() =>
			{
				EditorUI.Clear(list);
				Func<string, string, bool> match = (s, q) => string.IsNullOrWhiteSpace(q) || (s ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
				foreach (GameLook look in TemplateAuthoring.Looks())
				{
					string k = TemplateAuthoring.KindId(look.Kind), c = look.Culture.ToString(), m = look.Material.ToString();
					string title = k + " / " + c + " / " + m + " / " + look.Prefab.name;
					if (!match(title, search.text) || !match(k, kind.text) || !match(c, culture.text) || !match(m, material.text)) continue;
					EditorUI.Button(list, title, () => Guard(() => { selected = ModTemplates.Export(look.Prefab, EditorPacks.NewId(pack, look.Prefab.name), look.Kind, look.Culture, look.Material); sourcePack = null; selectedLabel.text = title; }));
				}
				foreach (EditorPack p in EditorPacks.Sources()) foreach (string file in EditorPacks.Files(p))
				{
					BuildingTemplate t;
					try { t = ModTemplates.FromJson(File.ReadAllText(file)); EditorPacks.CheckShape(t); } catch { continue; }
					string title = p.Name + " / " + t.name + " / " + t.kind + " / " + string.Join(",", t.cultures) + " / " + t.material;
					if (!match(title, search.text) || !match(t.kind, kind.text) || !match(string.Join(",", t.cultures), culture.text) || !match(t.material, material.text)) continue;
					EditorUI.Button(list, title, () => { selected = ModTemplates.FromJson(ModTemplates.ToJson(t)); sourcePack = p; selectedLabel.text = title; });
				}
			});
			foreach (var input in new[] { search, kind, culture, material }) input.onValueChanged.AddListener(_ => refresh());
			var actions = EditorUI.Row(col.transform);
			var width = EditorUI.Input(actions, "12", null, "Blank width"); var height = EditorUI.Input(actions, "12", null, "Blank height");
			EditorUI.Button(actions, "New from selected", () => Guard(() =>
			{
				if (selected == null) throw new TemplateException("Select an existing building first.");
				var t = ModTemplates.FromJson(ModTemplates.ToJson(selected)); t.id = EditorPacks.NewId(pack, t.name);
				if (sourcePack != null && sourcePack.Directory != pack.Directory) EditorPacks.ImportArt(t, sourcePack, pack);
				Edit(pack, new EditorDocument(t)); Document.BeginEdit(); Document.Template.name += " variant"; Document.EndEdit(); RefreshDocument();
			}));
			EditorUI.Button(actions, "New blank", () => Guard(() =>
			{
				if (selected == null) throw new TemplateException("Select a building to behave like first.");
				if (!int.TryParse(width.text, out int w) || !int.TryParse(height.text, out int h)) throw new TemplateException("Blank size must be whole numbers.");
				var t = ModTemplates.FromJson(ModTemplates.ToJson(selected)); t.id = EditorPacks.NewId(pack, "blank-" + t.kind); t.name = "New " + t.kind;
				t.floor.Clear(); t.detail.Clear(); t.walls.Clear(); t.objects.Clear(); t.thinWalls.Clear(); t.entrances.Clear(); t.lightSpots.Clear(); t.palette.Clear();
				t.bounds = new[] { 0, 0, 1, 1 }; var doc = new EditorDocument(t); doc.BeginEdit(); doc.Resize(w, h); doc.EndEdit(); Edit(pack, doc);
			}));
			EditorUI.Button(actions, "Packs", StartScreen); refresh();
		}
		private static void Dialog(string title, IEnumerable<(string label, Action action)> options)
		{
			DismissDialog(); _dialog = EditorUI.Box("Editor dialog", _root.transform, new Color(0, 0, 0, .85f)); EditorUI.Stretch(_dialog);
			var col = EditorUI.Column("Choices", _dialog.transform, 24); EditorUI.Rect(col, new Vector2(.3f, .3f), new Vector2(.7f, .7f), Vector2.zero, Vector2.zero);
			EditorUI.Label(col.transform, title, 22);
			foreach (var choice in options) EditorUI.Button(col.transform, choice.label, () => { DismissDialog(); choice.action(); });
		}
		private static void DismissDialog() { if (_dialog != null) { _dialog.SetActive(false); UnityEngine.Object.Destroy(_dialog); } _dialog = null; }
	}
}
