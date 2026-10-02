using System.Collections.Generic;
using System.IO;
using Inner_Maps.Location_Structures;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// The tiles and sprites a template can name: the game's (gathered from every building
	/// prefab, by name) and a pack's own PNGs (<c>art/</c>), made into tiles at the game's
	/// pixels per unit. Also keeps one object, thin wall and entrance from the game's
	/// buildings to copy when a base building has none of its own.
	/// </summary>
	internal static class TemplatePalette
	{
		internal static readonly Dictionary<string, TileBase> GameTiles = new Dictionary<string, TileBase>();
		internal static readonly Dictionary<string, Sprite> GameSprites = new Dictionary<string, Sprite>();
		private static readonly Dictionary<string, Tile> ArtTiles = new Dictionary<string, Tile>();
		internal static GameObject ObjectPrototype;
		internal static GameObject ThinWallPrototype;
		internal static readonly Dictionary<string, GameObject> ThinWallLayouts = new Dictionary<string, GameObject>();
		internal static GameObject ConnectorPrototype;
		internal static float TilePixelsPerUnit = 64f;
		private static bool _gathered;

		// The game finds block walls by tile name (LocationStructureObject.RegisterWalls:
		// name contains "Wall"), so a pack PNG painted on the walls layer is named with it.
		private const string WallSuffix = "|Wall";

		internal static void Gather()
		{
			if (_gathered)
			{
				return;
			}
			bool ppuSet = false;
			foreach (GameLook look in TemplateCatalogue.Looks())
			{
				GameObject prefab = look.Prefab;
				ThinWallGameObject[] walls = prefab.GetComponentsInChildren<ThinWallGameObject>(true);
				for (int i = 0; i < walls.Length; i++) ThinWallLayouts[prefab.name + "#" + i] = walls[i].gameObject;
				foreach (Tilemap tm in prefab.GetComponentsInChildren<Tilemap>(true))
				{
					foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
					{
						TileBase t = tm.GetTile(p);
						if (t == null || GameTiles.ContainsKey(t.name))
						{
							continue;
						}
						GameTiles[t.name] = t;
						if (!ppuSet && t is Tile tile && tile.sprite != null)
						{
							TilePixelsPerUnit = tile.sprite.pixelsPerUnit;
							ppuSet = true;
						}
					}
				}
				foreach (SpriteRenderer sr in prefab.GetComponentsInChildren<SpriteRenderer>(true))
				{
					if (sr.sprite != null && !GameSprites.ContainsKey(sr.sprite.name))
					{
						GameSprites[sr.sprite.name] = sr.sprite;
					}
				}
				if (ObjectPrototype == null)
				{
					ObjectPrototype = prefab.GetComponentInChildren<StructureTemplateObjectData>(true)?.gameObject;
				}
				if (ThinWallPrototype == null)
				{
					ThinWallPrototype = prefab.GetComponentInChildren<ThinWallGameObject>(true)?.gameObject;
				}
				if (ConnectorPrototype == null)
				{
					ConnectorPrototype = prefab.GetComponentInChildren<StructureConnector>(true)?.gameObject;
				}
			}
			_gathered = GameTiles.Count > 0;
		}

		/// <summary>The tile a reference names (<c>game:name</c>, <c>art:file.png</c>), or null.</summary>
		internal static TileBase Tile(string reference, string packDirectory, bool forWalls, string groundName = null)
		{
			if (reference == null)
			{
				return null;
			}
			if (reference.StartsWith("game:"))
			{
				return GameTiles.TryGetValue(reference.Substring(5), out TileBase t) ? t : null;
			}
			if (!reference.StartsWith("art:") || packDirectory == null)
			{
				return null;
			}
			string path = Path.Combine(packDirectory, "art", reference.Substring(4));
			string key = path + (forWalls ? WallSuffix : "") + "|" + groundName;
			if (ArtTiles.TryGetValue(key, out Tile cached))
			{
				return cached;
			}
			Sprite sprite = ModArt.LoadSprite(path, TilePixelsPerUnit);
			if (sprite == null)
			{
				return null;
			}
			if (groundName != null)
			{
				sprite = Object.Instantiate(sprite);
				sprite.name = groundName;
			}
			Tile tile = ScriptableObject.CreateInstance<Tile>();
			tile.sprite = sprite;
			tile.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.None;
			tile.name = reference + (forWalls ? WallSuffix : "");
			ArtTiles[key] = tile;
			return tile;
		}

		/// <summary>The sprite a reference names, or null.</summary>
		internal static Sprite Sprite(string reference, string packDirectory)
		{
			if (reference == null)
			{
				return null;
			}
			if (reference.StartsWith("game:"))
			{
				return GameSprites.TryGetValue(reference.Substring(5), out Sprite s) ? s : null;
			}
			if (!reference.StartsWith("art:") || packDirectory == null)
			{
				return null;
			}
			Sprite art = ModArt.LoadSprite(Path.Combine(packDirectory, "art", reference.Substring(4)), TilePixelsPerUnit);
			if (art != null)
			{
				art.name = reference;
			}
			return art;
		}

		internal static string NameOf(TileBase tile)
		{
			if (tile == null)
			{
				return null;
			}
			string n = tile.name.EndsWith(WallSuffix) ? tile.name.Substring(0, tile.name.Length - WallSuffix.Length) : tile.name;
			return n.StartsWith("art:") ? n : "game:" + n;
		}

		internal static string NameOf(Sprite sprite)
		{
			if (sprite == null)
			{
				return null;
			}
			return sprite.name.StartsWith("art:") ? sprite.name : "game:" + sprite.name;
		}
	}
}
