using System;
using System.Collections.Generic;
using System.Linq;
using Ruinarch.ModContent.Templates;

namespace Ruinarch.ModMenu.Editor
{
	internal sealed class EditorDocument
	{
		internal const string Keys = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#$%&*+-<=>?@^_~!";
		public BuildingTemplate Template { get; private set; }
		// Tilemap cells and furniture positions have different origins in stock prefabs.
		public float GridOffsetX, GridOffsetY;
		public TemplateThinWall WallBrush;
		public readonly HashSet<(int x, int y, int nx, int ny)> ThinWallEdges = new HashSet<(int, int, int, int)>();
		public readonly HashSet<(int x, int y)> ThinWallCells = new HashSet<(int, int)>();
		private readonly Stack<string> _undo = new Stack<string>();
		private readonly Stack<string> _redo = new Stack<string>();
		private string _before;
		private string _saved;
		public bool Dirty => ModTemplates.ToJson(Template) != _saved;
		public bool CanUndo => _undo.Count > 0;
		public bool CanRedo => _redo.Count > 0;

		public EditorDocument(BuildingTemplate template)
		{
			Template = ModTemplates.FromJson(ModTemplates.ToJson(template));
			_saved = ModTemplates.ToJson(Template);
		}
		public void MarkSaved() { _saved = ModTemplates.ToJson(Template); }
		public void BeginEdit() { if (_before == null) _before = ModTemplates.ToJson(Template); }
		public void EndEdit()
		{
			if (_before == null) return;
			string after = ModTemplates.ToJson(Template);
			if (after != _before) { _undo.Push(_before); _redo.Clear(); }
			_before = null;
		}
		public void Undo()
		{
			EndEdit();
			if (_undo.Count == 0) return;
			_redo.Push(ModTemplates.ToJson(Template));
			Template = ModTemplates.FromJson(_undo.Pop());
		}
		public void Redo()
		{
			EndEdit();
			if (_redo.Count == 0) return;
			_undo.Push(ModTemplates.ToJson(Template));
			Template = ModTemplates.FromJson(_redo.Pop());
		}
		public bool Contains(int x, int y)
		{
			int[] b = Template.bounds;
			return x >= b[0] && y >= b[1] && x < b[0] + b[2] && y < b[1] + b[3];
		}
		/// <summary>Entrances are the game's structure connectors: the points where a village
		/// attaches the building to its paths. Stock buildings put them just outside the walls,
		/// so they may be edited up to <see cref="EntranceMargin"/> cells outside the bounds.</summary>
		public const int EntranceMargin = 2;
		public bool CanEdit(string layer, int x, int y)
		{
			int[] b = Template.bounds; int m = layer == "entrances" ? EntranceMargin : 0;
			return x >= b[0] - m && y >= b[1] - m && x < b[0] + b[2] + m && y < b[1] + b[3] + m;
		}
		private List<string> Rows(string layer)
		{
			return layer == "floor" ? Template.floor : layer == "detail" ? Template.detail : Template.walls;
		}
		private bool At(float[] pos, int x, int y)
		{
			return pos != null && pos.Length >= 2 && (int)Math.Floor(pos[0] - GridOffsetX) == x && (int)Math.Floor(pos[1] - GridOffsetY) == y;
		}
		public string Get(string layer, int x, int y)
		{
			if (!CanEdit(layer, x, y)) return null;
			if (layer == "furniture") return Template.objects.FirstOrDefault(o => At(o.pos, x, y))?.type;
			if (layer == "entrances") return Template.entrances.Any(o => At(o.pos, x, y)) ? "entrance" : null;
			if (layer == "thin walls") return Template.thinWalls.FirstOrDefault(o => At(o.pos, x, y))?.sprites.FirstOrDefault();
			List<string> rows = Rows(layer);
			int row = Template.bounds[1] + Template.bounds[3] - 1 - y, col = x - Template.bounds[0];
			if (rows == null || row >= rows.Count || col >= rows[row].Length) return null;
			char key = rows[row][col];
			return key != '.' && Template.palette.TryGetValue(key.ToString(), out string reference) ? reference : null;
		}
		public void Paint(string layer, int x, int y, string value, string sprite, float rotation)
		{
			if (!CanEdit(layer, x, y)) return;
			float[] pos = { x + 0.5f + GridOffsetX, y + 0.5f + GridOffsetY, 0f };
			if (layer == "furniture")
			{
				Template.objects.RemoveAll(o => At(o.pos, x, y));
				if (value != null) Template.objects.Add(new TemplateObject { type = value, sprite = sprite, pos = pos, rot = rotation });
				return;
			}
			if (layer == "entrances")
			{
				Template.entrances.RemoveAll(o => At(o.pos, x, y));
				if (value != null) Template.entrances.Add(new TemplatePoint { pos = pos });
				return;
			}
			if (layer == "thin walls")
			{
				Template.thinWalls.RemoveAll(o => At(o.pos, x, y) && o.rot == rotation && (WallBrush == null || o.layout == WallBrush.layout));
				if (value != null) Template.thinWalls.Add(new TemplateThinWall
				{
					pos = pos, rot = rotation, layout = WallBrush?.layout,
					sprites = WallBrush == null ? new List<string> { value } : WallBrush.sprites.Select((s, i) => i == 0 ? value : s).ToList()
				});
				return;
			}
			List<string> rows = Rows(layer);
			while (rows.Count < Template.bounds[3]) rows.Add(new string('.', Template.bounds[2]));
			char key = '.';
			if (value != null)
			{
				string existing = Template.palette.FirstOrDefault(p => p.Value == value).Key;
				if (existing != null) key = existing[0];
				else
				{
					key = Keys.FirstOrDefault(k => !Template.palette.ContainsKey(k.ToString()));
					if (key == '\0') throw new TemplateException("The template palette is full. Remove unused tile references before adding another tile.");
					Template.palette[key.ToString()] = value;
				}
			}
			int row = Template.bounds[1] + Template.bounds[3] - 1 - y;
			char[] cells = rows[row].ToCharArray();
			cells[x - Template.bounds[0]] = key;
			rows[row] = new string(cells);
			if (layer == "floor") { Template.footprint = null; Template.rooms.Clear(); Template.clickBox = null; }
		}
		public void Fill(string layer, int x, int y, string value, string sprite, float rotation)
		{
			if (!Contains(x, y)) return;
			string old = Get(layer, x, y);
			if (old == value) return;
			var todo = new Queue<(int x, int y)>();
			var seen = new HashSet<(int, int)>();
			todo.Enqueue((x, y));
			while (todo.Count > 0)
			{
				var cell = todo.Dequeue();
				if (!seen.Add(cell) || !Contains(cell.x, cell.y) || Get(layer, cell.x, cell.y) != old) continue;
				Paint(layer, cell.x, cell.y, value, sprite, rotation);
				todo.Enqueue((cell.x - 1, cell.y)); todo.Enqueue((cell.x + 1, cell.y));
				todo.Enqueue((cell.x, cell.y - 1)); todo.Enqueue((cell.x, cell.y + 1));
			}
		}
		public void Rectangle(string layer, int x1, int y1, int x2, int y2, string value, string sprite, float rotation)
		{
			for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
				for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++) Paint(layer, x, y, value, sprite, rotation);
		}
		public void Resize(int width, int height)
		{
			if (width < 1 || height < 1 || width > 128 || height > 128) throw new TemplateException("Size must be between 1 and 128 cells on each axis.");
			foreach (string layer in new[] { "floor", "detail", "walls" })
			{
				var replacement = new List<string>();
				List<string> old = Rows(layer);
				// A layer the building does not use stays empty: its base building may have no tilemap for it.
				if (old == null || !old.Any(r => r.Any(c => c != '.'))) { old?.Clear(); continue; }
				for (int row = 0; row < height; row++)
				{
					int source = old.Count - height + row;
					string s = source >= 0 && source < old.Count ? old[source] : "";
					replacement.Add(s.Length >= width ? s.Substring(0, width) : s.PadRight(width, '.'));
				}
				if (layer == "floor") Template.floor = replacement;
				else if (layer == "detail") Template.detail = replacement;
				else Template.walls = replacement;
			}
			Template.bounds[2] = width; Template.bounds[3] = height;
			Template.size = new[] { width, height };
			Template.center = new[] { Template.bounds[0] + width / 2, Template.bounds[1] + height / 2, 0 };
			Template.objects.RemoveAll(o => !Inside(o.pos));
			Template.thinWalls.RemoveAll(o => !Inside(o.pos));
			Template.lightSpots.RemoveAll(o => !Inside(o.pos));
			Template.footprint = null; Template.rooms.Clear(); Template.clickBox = null;
		}
		private bool Inside(float[] p) { return p != null && p.Length >= 2 && Contains((int)Math.Floor(p[0] - GridOffsetX), (int)Math.Floor(p[1] - GridOffsetY)); }

		/// <param name="needsEntrance">Whether the base building has entrances. Buildings the world
		/// generator places directly (caves, mines) have none and need none.</param>
		public List<string> Warnings(Func<string, bool> knownTile, Func<string, bool> wallTile, bool needsEntrance)
		{
			var warnings = new List<string>();
			if (needsEntrance && Template.entrances.Count == 0) warnings.Add("No entrance. The game attaches this kind of building to a village's paths through its entrances, so it cannot place this one.");
			var floor = new HashSet<(int x, int y)>();
			var blocked = new HashSet<(int x, int y)>();
			blocked.UnionWith(ThinWallCells);
			int[] b = Template.bounds;
			for (int y = b[1]; y < b[1] + b[3]; y++) for (int x = b[0]; x < b[0] + b[2]; x++)
			{
				if (Get("floor", x, y) != null) floor.Add((x, y));
				string wall = Get("walls", x, y);
				if (wall != null) { blocked.Add((x, y)); if (!wallTile(wall)) warnings.Add($"Wall at {x},{y} is not a wall tile."); }
			}
			foreach (string tile in Template.palette.Values.Distinct()) if (!knownTile(tile)) warnings.Add("Unknown tile: " + tile);
			foreach (TemplateObject o in Template.objects)
				if (!floor.Contains(((int)Math.Floor(o.pos[0] - GridOffsetX), (int)Math.Floor(o.pos[1] - GridOffsetY)))) warnings.Add($"{o.type} at {o.pos[0]},{o.pos[1]} is off the floor.");
			// Villagers walk in from outside: start on the ring around the bounds and cross any
			// cell that is not a wall, through gaps between thin walls.
			int x0 = b[0] - 1, y0 = b[1] - 1, x1 = b[0] + b[2], y1 = b[1] + b[3];
			var reached = new HashSet<(int x, int y)>();
			var queue = new Queue<(int x, int y)>();
			for (int x = x0; x <= x1; x++) { queue.Enqueue((x, y0)); queue.Enqueue((x, y1)); }
			for (int y = y0; y <= y1; y++) { queue.Enqueue((x0, y)); queue.Enqueue((x1, y)); }
			while (queue.Count > 0)
			{
				var p = queue.Dequeue();
				if (p.x < x0 || p.y < y0 || p.x > x1 || p.y > y1 || blocked.Contains(p) || !reached.Add(p)) continue;
				Neighbors(queue, p.x, p.y);
			}
			int inaccessible = floor.Count(p => !blocked.Contains(p) && !reached.Contains(p));
			if (inaccessible > 0) warnings.Add($"{inaccessible} floor cells cannot be reached from outside the building: walls close them in (grid check; Test checks the game's own paths).");
			return warnings;
		}
		private void Neighbors(Queue<(int x, int y)> queue, int x, int y)
		{
			if (!ThinWallEdges.Contains((x, y, x - 1, y))) queue.Enqueue((x - 1, y));
			if (!ThinWallEdges.Contains((x, y, x + 1, y))) queue.Enqueue((x + 1, y));
			if (!ThinWallEdges.Contains((x, y, x, y - 1))) queue.Enqueue((x, y - 1));
			if (!ThinWallEdges.Contains((x, y, x, y + 1))) queue.Enqueue((x, y + 1));
		}
	}
}
