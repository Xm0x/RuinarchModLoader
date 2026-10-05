using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// Builds a template into a prefab: a copy of a game building (which keeps every hidden
	/// setting the game expects: rooms behaviour, wall type and material, colliders) with its
	/// layers repainted and its furniture, thin walls, entrances, ward light spots, rooms,
	/// footprint and click area replaced by the template's. The copy lives under an inactive
	/// root, so it never wakes up itself; the copies the game's object pool makes from it do.
	/// </summary>
	internal static class TemplateBuilder
	{
		private static GameObject _hidden;

		internal static Transform Hidden
		{
			get
			{
				if (_hidden == null)
				{
					_hidden = new GameObject("ModContent.Templates");
					_hidden.SetActive(false);
					Object.DontDestroyOnLoad(_hidden);
				}
				return _hidden.transform;
			}
		}

		internal static GameObject Build(BuildingTemplate t, GameObject basePrefab, string poolName, string packDirectory)
		{
			GameObject root = Object.Instantiate(basePrefab, Hidden);
			try
			{
				// Saves record a building's root name and rebuild it from the pool of that name.
				root.name = poolName;
				LocationStructureObject lso = root.GetComponent<LocationStructureObject>();
				lso.structureType = TemplateRegistry.ResolveKind(t.kind) ?? throw new TemplateException("unknown kind " + t.kind);
				Paint(TemplateExporter.Field<Tilemap>(lso, "_groundTileMap"), t, t.floor, packDirectory, "floor", false);
				Paint(TemplateExporter.Field<Tilemap>(lso, "_detailTileMap"), t, t.detail, packDirectory, "detail", false);
				Paint(TemplateExporter.Field<Tilemap>(lso, "_blockWallsTilemap"), t, t.walls, packDirectory, "walls", true);
				ReplaceObjects(lso, t, packDirectory);
				ReplaceThinWalls(lso, t, packDirectory);
				AccessTools.Field(typeof(LocationStructureObject), "_connectors").SetValue(lso, ReplaceMarkers<StructureConnector>(lso, t.entrances, TemplatePalette.ConnectorPrototype).ToArray());
				AccessTools.Field(typeof(LocationStructureObject), "_wardLightSpots").SetValue(lso, ReplaceMarkers<WardLightSpot>(lso, t.lightSpots, null).ToArray());
				lso.roomTemplates = t.rooms.Select(r => new RoomTemplate { coordinatesInRoom = r.Select(Cell).ToArray() }).ToArray();
				AccessTools.Field(typeof(LocationStructureObject), "_size").SetValue(lso, new Vector2Int(t.size[0], t.size[1]));
				AccessTools.Field(typeof(LocationStructureObject), "_center").SetValue(lso, Cell(t.center));
				List<Vector3Int> occupied = t.footprint != null ? t.footprint.Select(Cell).ToList() : OccupiedFloor(lso, t);
				AccessTools.Field(typeof(LocationStructureObject), "_predeterminedOccupiedCoordinates").SetValue(lso, occupied);
				// The game's own rule: the 8 neighbours of occupied cells that are not occupied.
				lso.DetermineBorderCoordinates();
				SetClickBox(lso, t, occupied);
				return root;
			}
			catch (TemplateException)
			{
				Object.DestroyImmediate(root);
				throw;
			}
			catch (Exception e)
			{
				Object.DestroyImmediate(root);
				throw new TemplateException($"building it failed: {e.GetType().Name}: {e.Message}");
			}
		}

		/// <summary>The floor's painted cells (tilemap cells), top row first.</summary>
		internal static List<Vector3Int> FloorCells(BuildingTemplate t)
		{
			List<Vector3Int> cells = new List<Vector3Int>();
			for (int r = 0; r < t.floor.Count; r++)
			{
				for (int i = 0; i < t.floor[r].Length; i++)
				{
					if (t.floor[r][i] != '.')
					{
						cells.Add(new Vector3Int(t.bounds[0] + i, t.bounds[1] + t.bounds[3] - 1 - r, 0));
					}
				}
			}
			return cells;
		}

		// Floor rows use tilemap cells; native footprints use root-local cells plus center.
		// Templates use unit cells; dormant Tilemap coordinate APIs return zero even with a Grid.
		private static List<Vector3Int> OccupiedFloor(LocationStructureObject lso, BuildingTemplate t)
		{
			List<Vector3Int> cells = FloorCells(t);
			Tilemap ground = TemplateExporter.Field<Tilemap>(lso, "_groundTileMap");
			for (int i = 0; i < cells.Count; i++)
			{
				Vector3 cellCenter = new Vector3(cells[i].x + 0.5f, cells[i].y + 0.5f, 0f);
				Vector3 local = lso.transform.InverseTransformPoint(ground.transform.TransformPoint(cellCenter));
				cells[i] = Vector3Int.FloorToInt(local) + lso.center;
			}
			return cells;
		}

		private static Vector3Int Cell(int[] c)
		{
			return new Vector3Int(c[0], c[1], c.Length > 2 ? c[2] : 0);
		}

		private static void Paint(Tilemap tm, BuildingTemplate t, List<string> rows, string packDirectory, string layer, bool walls)
		{
			if (tm == null)
			{
				if (rows.Any(r => r.Any(c => c != '.')))
				{
					throw new TemplateException($"it paints {layer}, but {t.behavesLike ?? "the building it behaves like"} has no {layer} layer");
				}
				return;
			}
			// Vanilla classifies ground by the sprite's name. PNG floors borrow the base
			// building's ground semantics, independently of their art reference.
			string groundName = null;
			if (layer == "floor")
			{
				foreach (Vector3Int cell in tm.cellBounds.allPositionsWithin)
				{
					Sprite sprite = tm.GetSprite(cell);
					if (sprite != null)
					{
						groundName = sprite.name;
						break;
					}
				}
			}
			tm.ClearAllTiles();
			for (int r = 0; r < rows.Count; r++)
			{
				for (int i = 0; i < rows[r].Length; i++)
				{
					char key = rows[r][i];
					if (key == '.')
					{
						continue;
					}
					string reference = t.palette[key.ToString()];
					TileBase tile = TemplatePalette.Tile(reference, packDirectory, walls, groundName) ?? throw new TemplateException("unknown tile " + reference);
					tm.SetTile(new Vector3Int(t.bounds[0] + i, t.bounds[1] + t.bounds[3] - 1 - r, 0), tile);
				}
			}
			tm.CompressBounds();
		}

		private static void Place(LocationStructureObject lso, Transform child, float[] pos, float rot)
		{
			child.position = lso.transform.TransformPoint(new Vector3(pos[0], pos[1], pos.Length > 2 ? pos[2] : 0f));
			child.localEulerAngles = new Vector3(0f, 0f, rot);
		}

		// A detached copy of a prototype, so the base building's own children can be removed
		// before the template's are made from it.
		private static GameObject Detach(GameObject prototype)
		{
			return prototype == null ? null : Object.Instantiate(prototype, Hidden);
		}

		private static void ReplaceObjects(LocationStructureObject lso, BuildingTemplate t, string packDirectory)
		{
			StructureTemplateObjectData[] old = lso.GetComponentsInChildren<StructureTemplateObjectData>(true);
			Transform parent = lso.objectsParent != null ? lso.objectsParent : lso.transform;
			GameObject prototype = Detach(old.Length > 0 ? old[0].gameObject : TemplatePalette.ObjectPrototype);
			foreach (StructureTemplateObjectData o in old)
			{
				Object.DestroyImmediate(o.gameObject);
			}
			try
			{
				if (t.objects.Count > 0 && prototype == null)
				{
					throw new TemplateException("no game object to copy furniture from");
				}
				foreach (TemplateObject o in t.objects)
				{
					GameObject go = Object.Instantiate(prototype, parent);
					go.name = o.type;
					StructureTemplateObjectData data = go.GetComponent<StructureTemplateObjectData>();
					data.tileObjectType = (TILE_OBJECT_TYPE)Enum.Parse(typeof(TILE_OBJECT_TYPE), o.type);
					data.spriteRenderer.sprite = o.sprite == null ? null : TemplatePalette.Sprite(o.sprite, packDirectory) ?? throw new TemplateException("unknown sprite " + o.sprite);
					Place(lso, go.transform, o.pos, o.rot);
				}
			}
			finally
			{
				if (prototype != null)
				{
					Object.DestroyImmediate(prototype);
				}
			}
		}

		private static void ReplaceThinWalls(LocationStructureObject lso, BuildingTemplate t, string packDirectory)
		{
			ThinWallGameObject[] old = lso.GetComponentsInChildren<ThinWallGameObject>(true);
			Transform parent = old.Length > 0 ? old[0].transform.parent : lso.transform;
			// Pieces have different child layouts (plain wall, corner, decoration). Keep a
			// matching layout instead of cloning the first piece for every wall.
			SpriteRenderer[][] layouts = t.thinWalls.Any(w => w.layout == null) ? old.Select(w => w.GetComponentsInChildren<SpriteRenderer>(true)).ToArray() : new SpriteRenderer[0][];
			GameObject fallback = TemplatePalette.ThinWallPrototype;
			try
			{
				if (t.thinWalls.Count > 0 && old.Length == 0 && fallback == null)
				{
					throw new TemplateException("no game thin wall to copy from");
				}
				foreach (TemplateThinWall w in t.thinWalls)
				{
					int match = -1;
					for (int i = 0; w.layout == null && i < layouts.Length; i++)
					{
						if (layouts[i].Length != w.sprites.Count)
						{
							continue;
						}
						if (match < 0)
						{
							match = i;
						}
						bool same = true;
						for (int j = 0; j < layouts[i].Length; j++)
						{
							if (TemplatePalette.NameOf(layouts[i][j].sprite) != w.sprites[j])
							{
								same = false;
								break;
							}
						}
						if (same)
						{
							match = i;
							break;
						}
					}
					GameObject prototype;
					if (w.layout != null)
					{
						if (!TemplatePalette.ThinWallLayouts.TryGetValue(w.layout, out prototype))
							throw new TemplateException("unknown thin-wall layout " + w.layout);
					}
					else prototype = match >= 0 ? old[match].gameObject : fallback;
					GameObject go = Object.Instantiate(prototype, parent);
					if (w.layout != null)
					{
						var metadata = go.GetComponent<TemplateWallLayout>() ?? go.AddComponent<TemplateWallLayout>();
						metadata.Source = w.layout;
					}
					SpriteRenderer[] renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
					if (renderers.Length != w.sprites.Count)
					{
						throw new TemplateException("no thin-wall layout with " + w.sprites.Count + " sprite renderers");
					}
					for (int i = 0; i < renderers.Length; i++)
					{
						// Sprite names such as "horizontal" repeat across materials. An unchanged
						// exported layout already owns the exact stock sprite; retain that asset.
						if (w.layout != null && TemplatePalette.NameOf(renderers[i].sprite) == w.sprites[i]) continue;
						renderers[i].sprite = w.sprites[i] == null ? null : TemplatePalette.Sprite(w.sprites[i], packDirectory) ?? throw new TemplateException("unknown thin-wall sprite " + w.sprites[i]);
					}
					Place(lso, go.transform, w.pos, w.rot);
				}
			}
			finally
			{
				foreach (ThinWallGameObject wall in old)
				{
					Object.DestroyImmediate(wall.gameObject);
				}
			}
		}

		private static List<T> ReplaceMarkers<T>(LocationStructureObject lso, List<TemplatePoint> points, GameObject fallback) where T : Component
		{
			T[] old = lso.GetComponentsInChildren<T>(true);
			Transform parent = old.Length > 0 ? old[0].transform.parent : lso.transform;
			GameObject prototype = Detach(old.Length > 0 ? old[0].gameObject : fallback);
			foreach (T o in old)
			{
				Object.DestroyImmediate(o.gameObject);
			}
			List<T> made = new List<T>();
			foreach (TemplatePoint p in points)
			{
				GameObject go = prototype != null ? Object.Instantiate(prototype, parent) : new GameObject(typeof(T).Name);
				if (prototype == null)
				{
					go.transform.SetParent(parent, false);
				}
				T marker = go.GetComponent<T>() ?? go.AddComponent<T>();
				Place(lso, go.transform, p.pos, 0f);
				made.Add(marker);
			}
			if (prototype != null)
			{
				Object.DestroyImmediate(prototype);
			}
			return made;
		}

		// The template's click area, else a box over the footprint (a repainted building's
		// shape can differ from the base's).
		private static void SetClickBox(LocationStructureObject lso, BuildingTemplate t, List<Vector3Int> occupied)
		{
			BoxCollider2D box = TemplateExporter.ClickBox(lso);
			if (box == null)
			{
				return;
			}
			if (t.clickBox != null)
			{
				box.offset = new Vector2(t.clickBox.offset[0], t.clickBox.offset[1]);
				box.size = new Vector2(t.clickBox.size[0], t.clickBox.size[1]);
				return;
			}
			Vector3 low = box.transform.InverseTransformPoint(lso.transform.TransformPoint(
				new Vector3(occupied.Min(c => c.x) - lso.center.x - .5f, occupied.Min(c => c.y) - lso.center.y - .5f, 0)));
			Vector3 high = box.transform.InverseTransformPoint(lso.transform.TransformPoint(
				new Vector3(occupied.Max(c => c.x) - lso.center.x + .5f, occupied.Max(c => c.y) - lso.center.y + .5f, 0)));
			box.offset = (low + high) / 2f;
			box.size = new Vector2(Mathf.Abs(high.x - low.x), Mathf.Abs(high.y - low.y));
		}
	}
}
