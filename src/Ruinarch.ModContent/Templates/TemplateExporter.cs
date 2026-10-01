using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>Reads a building prefab (LocationStructureObject on its root) into a template.</summary>
	internal static class TemplateExporter
	{
		internal static T Field<T>(LocationStructureObject lso, string name)
		{
			return (T)AccessTools.Field(typeof(LocationStructureObject), name).GetValue(lso);
		}

		internal static BoxCollider2D ClickBox(LocationStructureObject lso)
		{
			LocationStructureObjectClickCollider click = Field<LocationStructureObjectClickCollider>(lso, "_clickCollider");
			return click == null ? null : AccessTools.Field(typeof(LocationStructureObjectClickCollider), "clickCollider").GetValue(click) as BoxCollider2D;
		}

		internal static BuildingTemplate Export(GameObject prefab, string id, STRUCTURE_TYPE kind, FACTION_TYPE culture, RESOURCE material)
		{
			LocationStructureObject lso = prefab.GetComponent<LocationStructureObject>();
			if (lso == null)
			{
				throw new TemplateException(prefab.name + " has no LocationStructureObject on its root");
			}
			BuildingTemplate t = new BuildingTemplate
			{
				id = id,
				name = prefab.name,
				kind = ContentRegistry.StructuresByType.TryGetValue((int)kind, out StructureRegistration reg) ? reg.Id : kind.ToString(),
				cultures = new List<string> { culture.ToString() },
				material = material.ToString(),
				behavesLike = prefab.name,
			};
			Tilemap ground = Field<Tilemap>(lso, "_groundTileMap");
			Tilemap detail = Field<Tilemap>(lso, "_detailTileMap");
			Tilemap walls = Field<Tilemap>(lso, "_blockWallsTilemap");
			List<Vector3Int> painted = Cells(ground).Concat(Cells(detail)).Concat(Cells(walls)).ToList();
			if (painted.Count == 0)
			{
				throw new TemplateException(prefab.name + " has no painted cells");
			}
			int minX = painted.Min(c => c.x), minY = painted.Min(c => c.y), maxX = painted.Max(c => c.x), maxY = painted.Max(c => c.y);
			t.bounds = new[] { minX, minY, maxX - minX + 1, maxY - minY + 1 };
			Vector2Int size = Field<Vector2Int>(lso, "_size");
			Vector3Int center = Field<Vector3Int>(lso, "_center");
			t.size = new[] { size.x, size.y };
			t.center = new[] { center.x, center.y, center.z };
			// One palette for all layers; keys in the order cells are met, so the same building
			// always gets the same keys.
			Dictionary<string, char> keys = new Dictionary<string, char>();
			t.floor = Encode(ground, t, keys);
			t.detail = Encode(detail, t, keys);
			t.walls = Encode(walls, t, keys);
			foreach (ThinWallGameObject w in prefab.GetComponentsInChildren<ThinWallGameObject>(true))
			{
				t.thinWalls.Add(new TemplateThinWall
				{
					pos = Pos(lso, w.transform),
					rot = R(w.transform.localEulerAngles.z),
					sprites = w.GetComponentsInChildren<SpriteRenderer>(true).Select(s => TemplatePalette.NameOf(s.sprite)).ToList(),
				});
			}
			foreach (StructureTemplateObjectData o in prefab.GetComponentsInChildren<StructureTemplateObjectData>(true))
			{
				t.objects.Add(new TemplateObject
				{
					type = o.tileObjectType.ToString(),
					pos = Pos(lso, o.transform),
					rot = R(o.transform.localEulerAngles.z),
					sprite = TemplatePalette.NameOf(o.spriteRenderer != null ? o.spriteRenderer.sprite : null),
				});
			}
			foreach (StructureConnector c in Field<StructureConnector[]>(lso, "_connectors") ?? new StructureConnector[0])
			{
				if (c != null)
				{
					t.entrances.Add(new TemplatePoint { pos = Pos(lso, c.transform) });
				}
			}
			foreach (WardLightSpot s in Field<WardLightSpot[]>(lso, "_wardLightSpots") ?? new WardLightSpot[0])
			{
				if (s != null)
				{
					t.lightSpots.Add(new TemplatePoint { pos = Pos(lso, s.transform) });
				}
			}
			foreach (RoomTemplate r in lso.roomTemplates ?? new RoomTemplate[0])
			{
				t.rooms.Add((r.coordinatesInRoom ?? new Vector3Int[0]).Select(c => new[] { c.x, c.y, c.z }).ToList());
			}
			// A hand-made footprint (different from the floor cells) is kept as is.
			List<Vector3Int> occupied = Field<List<Vector3Int>>(lso, "_predeterminedOccupiedCoordinates");
			if (occupied != null && occupied.Count > 0 && !SameCells(occupied, Cells(ground)))
			{
				t.footprint = occupied.Select(c => new[] { c.x, c.y, c.z }).ToList();
			}
			BoxCollider2D box = ClickBox(lso);
			if (box != null)
			{
				t.clickBox = new TemplateBox { offset = new[] { R(box.offset.x), R(box.offset.y) }, size = new[] { R(box.size.x), R(box.size.y) } };
			}
			return t;
		}

		internal static List<Vector3Int> Cells(Tilemap tm)
		{
			List<Vector3Int> cells = new List<Vector3Int>();
			if (tm == null)
			{
				return cells;
			}
			foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
			{
				if (tm.GetTile(p) != null)
				{
					cells.Add(new Vector3Int(p.x, p.y, 0));
				}
			}
			return cells;
		}

		internal static bool SameCells(IEnumerable<Vector3Int> a, IEnumerable<Vector3Int> b)
		{
			return new HashSet<Vector2Int>(a.Select(c => new Vector2Int(c.x, c.y))).SetEquals(b.Select(c => new Vector2Int(c.x, c.y)));
		}

		private static List<string> Encode(Tilemap tm, BuildingTemplate t, Dictionary<string, char> keys)
		{
			List<string> rows = new List<string>();
			if (tm == null)
			{
				return rows;
			}
			int[] b = t.bounds;
			bool any = false;
			for (int y = b[1] + b[3] - 1; y >= b[1]; y--)
			{
				StringBuilder row = new StringBuilder(b[2]);
				for (int x = b[0]; x < b[0] + b[2]; x++)
				{
					TileBase tile = tm.GetTile(new Vector3Int(x, y, 0));
					if (tile == null)
					{
						row.Append('.');
						continue;
					}
					any = true;
					string reference = TemplatePalette.NameOf(tile);
					if (!keys.TryGetValue(reference, out char key))
					{
						if (keys.Count >= TemplateJson.Keys.Length)
						{
							throw new TemplateException($"more than {TemplateJson.Keys.Length} different tiles");
						}
						key = TemplateJson.Keys[keys.Count];
						keys[reference] = key;
						t.palette[key.ToString()] = reference;
					}
					row.Append(key);
				}
				rows.Add(row.ToString());
			}
			return any ? rows : new List<string>();
		}

		private static float[] Pos(LocationStructureObject lso, Transform child)
		{
			Vector3 p = lso.transform.InverseTransformPoint(child.position);
			return new[] { R(p.x), R(p.y), R(p.z) };
		}

		// Four decimals: positions are tile fractions; float noise must not make two exports differ.
		private static float R(float v)
		{
			return (float)Math.Round(v, 4);
		}
	}
}
