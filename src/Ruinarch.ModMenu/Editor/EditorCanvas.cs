using System;
using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Ruinarch.ModContent.Templates;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace Ruinarch.ModMenu.Editor
{
	internal sealed class EditorCanvas : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
	{
		internal EditorDocument Document;
		internal string PackDirectory;
		internal GameObject BasePrefab;
		internal Action Changed;
		internal Action<string, int, int> Picked;
		internal Action<string> Error;
		internal string Layer = "floor", Tool = "Paint", Value, SpriteReference;
		internal float Rotation;
		internal readonly HashSet<string> Hidden = new HashSet<string>();
		private GameObject _renderRoot, _geometry;
		private Camera _camera;
		private RawImage _image;
		private RenderTexture _texture;
		private Material _material;
		private Vector2 _pan;
		private float _zoom = 8;
		private bool _painting, _panning;
		private Vector2Int _start, _last;
		private Vector2 _pointer;
		private readonly Dictionary<string, List<GameObject>> _layers = new Dictionary<string, List<GameObject>>();
		private readonly Dictionary<string, Tilemap> _maps = new Dictionary<string, Tilemap>();
		private readonly List<LineRenderer> _gridLines = new List<LineRenderer>();
		private Vector2 GridOffset => new Vector2(Document.GridOffsetX, Document.GridOffsetY);

		internal void Initialize()
		{
			_image = gameObject.AddComponent<RawImage>(); _image.color = Color.white;
			_renderRoot = new GameObject("Editor render scene"); _renderRoot.transform.position = new Vector3(10000, 10000, 0);
			var cameraGO = new GameObject("Editor camera"); cameraGO.transform.SetParent(_renderRoot.transform, false);
			_camera = cameraGO.AddComponent<Camera>(); _camera.orthographic = true; _camera.cullingMask = 1 << 31;
			_camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = new Color32(18, 23, 28, 255);
			_camera.nearClipPlane = .1f; _camera.farClipPlane = 100;
			ResizeTexture();
			_material = new Material(Shader.Find("Sprites/Default"));
			ConfigureGrid();
			Fit(); Redraw();
		}
		private void OnRectTransformDimensionsChange() { if (_camera != null) ResizeTexture(); }
		private void ResizeTexture()
		{
			Rect rect = ((RectTransform)transform).rect;
			int width = Mathf.Max(64, Mathf.RoundToInt(rect.width)), height = Mathf.Max(64, Mathf.RoundToInt(rect.height));
			if (_texture != null && _texture.width == width && _texture.height == height) return;
			if (_texture != null) { _texture.Release(); Destroy(_texture); }
			_texture = new RenderTexture(width, height, 24); _texture.Create(); _image.texture = _texture;
			_camera.targetTexture = _texture; _camera.aspect = width / (float)height; GridWidth();
		}
		internal void Fit()
		{
			int[] b = Document.Template.bounds; _pan = GridOffset + new Vector2(b[0] + b[2] * .5f, b[1] + b[3] * .5f);
			_zoom = Mathf.Max(3, Mathf.Max(b[2], b[3]) * .65f); CameraPosition();
		}
		private void CameraPosition()
		{
			if (_camera == null) return;
			_camera.transform.localPosition = new Vector3(_pan.x, _pan.y, -10); _camera.orthographicSize = _zoom; GridWidth();
		}
		// Grid lines stay one pixel wide at every zoom; thinner lines drop out of the render.
		private void GridWidth()
		{
			if (_texture == null) return;
			float width = _zoom * 2 / _texture.height;
			foreach (LineRenderer line in _gridLines) if (line != null) { line.startWidth = width; line.endWidth = width; }
		}
		/// <summary>Whether the base building has a tilemap for a tile layer. Other layers always exist.</summary>
		internal bool HasLayer(string layer)
		{
			string field = layer == "floor" ? "_groundTileMap" : layer == "detail" ? "_detailTileMap" : layer == "walls" ? "_blockWallsTilemap" : null;
			return field == null || AccessTools.Field(typeof(LocationStructureObject), field).GetValue(BasePrefab.GetComponent<LocationStructureObject>()) != null;
		}
		private void ConfigureGrid()
		{
			var lso = BasePrefab.GetComponent<LocationStructureObject>();
			var ground = AccessTools.Field(typeof(LocationStructureObject), "_groundTileMap").GetValue(lso) as Tilemap;
			Vector3 offset = ground == null ? Vector3.zero : BasePrefab.transform.InverseTransformPoint(ground.transform.position);
			Document.GridOffsetX = offset.x; Document.GridOffsetY = offset.y;
		}
		private void AddLayer(GameObject go, string layer)
		{
			go.layer = 31;
			if (!_layers.TryGetValue(layer, out List<GameObject> group)) _layers[layer] = group = new List<GameObject>();
			group.Add(go); go.SetActive(!Hidden.Contains(layer));
		}
		internal void SetVisible(string layer, bool visible)
		{
			if (visible) Hidden.Remove(layer); else Hidden.Add(layer);
			if (_layers.TryGetValue(layer, out List<GameObject> group)) foreach (GameObject go in group) go.SetActive(visible);
		}
		internal void Redraw()
		{
			if (_geometry != null) { _geometry.SetActive(false); Destroy(_geometry); }
			_geometry = new GameObject("Preview geometry"); _geometry.transform.SetParent(_renderRoot.transform, false); _layers.Clear(); _maps.Clear(); _gridLines.Clear(); ConfigureGrid();
			GameObject built = null;
			try
			{
				built = ModTemplates.BuildDetached(Document.Template, BasePrefab, PackDirectory);
				var lso = built.GetComponent<LocationStructureObject>();
				ReadWallBlocks(built);
				var grid = new GameObject("Grid", typeof(Grid)); grid.transform.SetParent(_geometry.transform, false);
				foreach (string layer in new[] { "floor", "detail", "walls" })
				{
					string field = layer == "floor" ? "_groundTileMap" : layer == "detail" ? "_detailTileMap" : "_blockWallsTilemap";
					var source = AccessTools.Field(typeof(LocationStructureObject), field).GetValue(lso) as Tilemap;
					if (source == null) continue;
					var go = new GameObject(layer, typeof(Tilemap), typeof(TilemapRenderer)); go.transform.SetParent(grid.transform, false);
					go.transform.localPosition = built.transform.InverseTransformPoint(source.transform.position);
					var map = go.GetComponent<Tilemap>(); map.tileAnchor = source.tileAnchor; map.color = source.color;
					_maps[layer] = map;
					foreach (Vector3Int p in source.cellBounds.allPositionsWithin)
					{
						TileBase tile = source.GetTile(p); if (tile == null) continue;
						map.SetTile(p, tile); map.SetTileFlags(p, TileFlags.None); map.SetTransformMatrix(p, source.GetTransformMatrix(p)); map.SetColor(p, source.GetColor(p));
					}
					var renderer = go.GetComponent<TilemapRenderer>(); renderer.sharedMaterial = _material; renderer.sortingOrder = layer == "floor" ? 0 : layer == "detail" ? 1 : 2;
					AddLayer(go, layer);
				}
				foreach (SpriteRenderer source in built.GetComponentsInChildren<SpriteRenderer>(true))
				{
					if (source.sprite == null) continue;
					string layer = HasAncestor<ThinWallGameObject>(source.transform) ? "thin walls"
						: HasAncestor<StructureTemplateObjectData>(source.transform) ? "furniture" : "detail";
					var go = new GameObject(source.name, typeof(SpriteRenderer)); go.transform.SetParent(_geometry.transform, false);
					go.transform.localPosition = built.transform.InverseTransformPoint(source.transform.position);
					go.transform.localRotation = Quaternion.Inverse(built.transform.rotation) * source.transform.rotation;
					go.transform.localScale = source.transform.lossyScale;
					var renderer = go.GetComponent<SpriteRenderer>(); renderer.sprite = source.sprite; renderer.color = source.color;
					renderer.flipX = source.flipX; renderer.flipY = source.flipY; renderer.sharedMaterial = _material; renderer.sortingOrder = 4;
					AddLayer(go, layer);
				}
			}
			catch (Exception e) { Error?.Invoke(e.Message); }
			finally { if (built != null) ModTemplates.DestroyDetached(built); }
			int[] b = Document.Template.bounds;
			for (int x = b[0]; x <= b[0] + b[2]; x++) _gridLines.Add(Line("grid", GridOffset + new Vector2(x, b[1]), GridOffset + new Vector2(x, b[1] + b[3]), new Color(1, 1, 1, .16f), .012f));
			for (int y = b[1]; y <= b[1] + b[3]; y++) _gridLines.Add(Line("grid", GridOffset + new Vector2(b[0], y), GridOffset + new Vector2(b[0] + b[2], y), new Color(1, 1, 1, .16f), .012f));
			GridWidth();
			foreach (TemplatePoint e in Document.Template.entrances)
			{
				var p = new Vector2(e.pos[0], e.pos[1]); Color c = new Color32(97, 196, 203, 255);
				Line("entrances", p - Vector2.right * .3f, p + Vector2.right * .3f, c, .07f);
				Line("entrances", p - Vector2.up * .3f, p + Vector2.up * .3f, c, .07f);
			}
		}
		private static bool HasAncestor<T>(Transform transform) where T : Component
		{
			for (; transform != null; transform = transform.parent) if (transform.GetComponent<T>() != null) return true;
			return false;
		}
		private void ReadWallBlocks(GameObject built)
		{
			Document.ThinWallEdges.Clear(); Document.ThinWallCells.Clear();
			foreach (ThinWallGameObject wall in built.GetComponentsInChildren<ThinWallGameObject>(true))
			{
				var colliders = AccessTools.Field(typeof(ThinWallGameObject), "_unpassableColliders").GetValue(wall) as BoxCollider2D[];
				if (colliders == null) continue;
				foreach (BoxCollider2D collider in colliders)
				{
					if (collider == null) continue;
					Vector3 center = built.transform.InverseTransformPoint(collider.transform.TransformPoint(collider.offset));
					Vector3 a = built.transform.InverseTransformPoint(collider.transform.TransformPoint(collider.offset - collider.size * .5f));
					Vector3 b = built.transform.InverseTransformPoint(collider.transform.TransformPoint(collider.offset + collider.size * .5f));
					center -= (Vector3)GridOffset; a -= (Vector3)GridOffset; b -= (Vector3)GridOffset;
					float width = Mathf.Abs(b.x - a.x), height = Mathf.Abs(b.y - a.y);
					if (width < height && width < .5f)
					{
						int x = Mathf.RoundToInt(center.x);
						for (int y = Mathf.FloorToInt(Mathf.Min(a.y, b.y) + .001f); y <= Mathf.FloorToInt(Mathf.Max(a.y, b.y) - .001f); y++) BlockEdge(x - 1, y, x, y);
					}
					else if (height < .5f)
					{
						int y = Mathf.RoundToInt(center.y);
						for (int x = Mathf.FloorToInt(Mathf.Min(a.x, b.x) + .001f); x <= Mathf.FloorToInt(Mathf.Max(a.x, b.x) - .001f); x++) BlockEdge(x, y - 1, x, y);
					}
					else Document.ThinWallCells.Add((Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y)));
				}
			}
		}
		private void BlockEdge(int x, int y, int nx, int ny)
		{
			Document.ThinWallEdges.Add((x, y, nx, ny)); Document.ThinWallEdges.Add((nx, ny, x, y));
		}
		private LineRenderer Line(string layer, Vector2 a, Vector2 b, Color color, float width)
		{
			var go = new GameObject(layer, typeof(LineRenderer)); go.transform.SetParent(_geometry.transform, false);
			var line = go.GetComponent<LineRenderer>(); line.useWorldSpace = false; line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b);
			line.sharedMaterial = _material; line.startColor = color; line.endColor = color; line.startWidth = width; line.endWidth = width; line.sortingOrder = 20; AddLayer(go, layer);
			return line;
		}
		private Vector2 Local(PointerEventData e)
		{
			var rt = (RectTransform)transform; RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out Vector2 p);
			return new Vector2(p.x / rt.rect.width, p.y / rt.rect.height);
		}
		private Vector2 Span(Vector2 local) { return new Vector2(local.x * _camera.aspect, local.y) * (_zoom * 2); }
		private Vector2 World(Vector2 local) { return _pan + Span(local); }
		private Vector2Int Cell(PointerEventData e) { Vector2 p = World(Local(e)) - GridOffset; return new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y)); }
		public void OnPointerDown(PointerEventData e)
		{
			if (e.button != PointerEventData.InputButton.Left) { _panning = true; _pointer = Local(e); return; }
			_start = _last = Cell(e);
			if (Tool == "Picker") { Picked?.Invoke(Layer, _start.x, _start.y); return; }
			if (!HasLayer(Layer)) { Error?.Invoke(MissingLayer(Layer)); return; }
			_painting = true; Document.BeginEdit();
			if (Tool == "Fill") { Document.Fill(Layer, _start.x, _start.y, Value, SpriteReference, Rotation); Commit(); }
			else if (Tool != "Rectangle") Paint(_start);
		}
		internal string MissingLayer(string layer)
		{
			string instead = layer == "walls" ? "Use the thin walls layer for its walls, or set" : "Set";
			return $"{BasePrefab.name} has no {layer} tiles. {instead} Behaves like to a building that has {layer} tiles.";
		}
		public void OnDrag(PointerEventData e)
		{
			if (_panning) { Vector2 current = Local(e); _pan -= Span(current - _pointer); _pointer = current; CameraPosition(); return; }
			if (!_painting || Tool == "Rectangle") return;
			var cell = Cell(e); if (cell == _last || !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, e.position, e.pressEventCamera)) return;
			// Interpolate the stroke so a fast drag cannot leave gaps.
			int count = Mathf.Max(Mathf.Abs(cell.x - _last.x), Mathf.Abs(cell.y - _last.y));
			for (int i = 1; i <= count; i++) Paint(new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(_last.x, cell.x, i / (float)count)), Mathf.RoundToInt(Mathf.Lerp(_last.y, cell.y, i / (float)count))));
			_last = cell;
		}
		private void Paint(Vector2Int cell)
		{
			string value = Tool == "Erase" ? null : Value;
			if (!Document.Contains(cell.x, cell.y) || !HasLayer(Layer)) return;
			if (_maps.TryGetValue(Layer, out Tilemap map))
			{
				if (Document.Get(Layer, cell.x, cell.y) == value) return;
				Document.Paint(Layer, cell.x, cell.y, value, SpriteReference, Rotation);
				map.SetTile(new Vector3Int(cell.x, cell.y, 0), TemplateAuthoring.Tile(value, PackDirectory, Layer == "walls"));
			}
			else { Document.Paint(Layer, cell.x, cell.y, value, SpriteReference, Rotation); Redraw(); }
		}
		public void OnPointerUp(PointerEventData e)
		{
			_panning = false; if (!_painting) return;
			if (Tool == "Rectangle" && HasLayer(Layer)) { var end = Cell(e); Document.Rectangle(Layer, _start.x, _start.y, end.x, end.y, Value, SpriteReference, Rotation); }
			Commit();
		}
		private void Commit() { _painting = false; Document.EndEdit(); if (Tool == "Rectangle" || Tool == "Fill") Redraw(); Changed?.Invoke(); }
		public void OnScroll(PointerEventData e)
		{
			Vector2 local = Local(e), before = World(local); _zoom = Mathf.Clamp(_zoom * Mathf.Pow(.85f, e.scrollDelta.y), .5f, 100);
			_pan += before - World(local); CameraPosition();
		}
		private void OnDestroy()
		{
			if (_renderRoot != null) Destroy(_renderRoot);
			if (_texture != null) { _texture.Release(); Destroy(_texture); }
			if (_material != null) Destroy(_material);
		}
	}
}
