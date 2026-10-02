using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>An authoring palette entry with native render assets and source context.</summary>
	public sealed class TemplateBrush
	{
		public string Reference;
		public string Source;
		public string Layer;
		public string ObjectType;
		public Sprite Sprite;
		public TileBase Tile;
		public float Rotation;
		public TemplateThinWall Wall;
		public string Name;
	}

	/// <summary>Main-menu authoring access. No world is generated while assets are indexed.</summary>
	public static class TemplateAuthoring
	{
		internal static bool Loading;
		private static List<TemplateBrush> _brushes;
		public static bool IsReady => TemplateCatalogue.HasLooks;

		/// <summary>Run as a Unity coroutine before opening the editor. Loads the Game scene
		/// additively once, holds native world startup, indexes referenced assets, then unloads it.</summary>
		public static IEnumerator Prepare()
		{
			if (IsReady) { TemplatePalette.Gather(); yield break; }
			if (Loading) { while (Loading) yield return null; yield break; }
			if (LandmarkManager.Instance != null) { TemplateCatalogue.Looks(); TemplatePalette.Gather(); yield break; }
			Loading = true;
			bool unloaded = false;
			try
			{
				yield return SceneManager.LoadSceneAsync("Game", LoadSceneMode.Additive);
				TemplateCatalogue.Looks();
				TemplatePalette.Gather();
				if (!IsReady) throw new TemplateException("The game scene did not provide a building catalogue.");
				yield return SceneManager.UnloadSceneAsync("Game");
				unloaded = true;
			}
			finally
			{
				if (!unloaded && SceneManager.GetSceneByName("Game").isLoaded) SceneManager.UnloadSceneAsync("Game");
				Loading = false;
			}
		}

		/// <summary>Stock looks plus the borrowed looks of registered non-player building kinds.</summary>
		public static IReadOnlyList<GameLook> Looks()
		{
			var result = ModTemplates.GameLooks().ToList();
			foreach (StructureRegistration reg in ContentRegistry.Structures.Where(r => !r.IsPlayerStructure))
				foreach (GameLook look in TemplateCatalogue.Looks().Where(l => l.Kind == reg.PrefabSource))
					result.Add(new GameLook { Kind = reg.StructureType, Culture = look.Culture, Material = look.Material, Prefab = look.Prefab });
			return result;
		}
		public static string KindId(STRUCTURE_TYPE kind)
		{
			return ContentRegistry.StructuresByType.TryGetValue((int)kind, out StructureRegistration reg) ? reg.Id : kind.ToString();
		}
		public static STRUCTURE_TYPE? Kind(string id) { return TemplateRegistry.ResolveKind(id); }
		public static TileBase Tile(string reference, string packDirectory, bool wall = false)
		{
			TemplatePalette.Gather(); return TemplatePalette.Tile(reference, packDirectory, wall);
		}
		public static Sprite Sprite(string reference, string packDirectory)
		{
			TemplatePalette.Gather(); return TemplatePalette.Sprite(reference, packDirectory);
		}
		public static IReadOnlyList<TemplateBrush> Brushes()
		{
			if (_brushes != null) return _brushes;
			var brushes = new List<TemplateBrush>();
			foreach (GameLook look in ModTemplates.GameLooks())
			{
				string source = look.Kind + " / " + look.Culture + " / " + look.Material + " / " + look.Prefab.name;
				var t = ModTemplates.Export(look.Prefab, "editor/source", look.Kind, look.Culture, look.Material);
				foreach (string layer in new[] { "floor", "detail", "walls" })
				{
					List<string> rows = layer == "floor" ? t.floor : layer == "detail" ? t.detail : t.walls;
					foreach (char key in string.Concat(rows).Where(c => c != '.').Distinct())
					{
						string reference = t.palette[key.ToString()]; var tile = Tile(reference, null, layer == "walls");
						brushes.Add(new TemplateBrush { Reference = reference, Source = source, Layer = layer, Tile = tile, Sprite = Preview(tile) });
					}
				}
				foreach (TemplateObject o in t.objects)
					if (!brushes.Any(b => b.Layer == "furniture" && b.Source == source && b.ObjectType == o.type && b.Reference == o.sprite))
						brushes.Add(new TemplateBrush { Reference = o.sprite, ObjectType = o.type, Source = source, Layer = "furniture", Sprite = Sprite(o.sprite, null), Rotation = o.rot });
				foreach (TemplateThinWall w in t.thinWalls)
				{
					string s = w.sprites.FirstOrDefault();
					var prototype = TemplatePalette.ThinWallLayouts[w.layout];
					if (!brushes.Any(b => b.Source == source && b.Wall?.layout == w.layout))
						brushes.Add(new TemplateBrush { Reference = s, Name = prototype.name, Wall = w, Source = source, Layer = "thin walls", Sprite = Sprite(s, null), Rotation = w.rot });
				}
			}
			_brushes = brushes; return _brushes;
		}
		private static Tilemap _scratch;
		/// <summary>The sprite a tile draws on its own. Random and rule tiles have no single sprite,
		/// so they are resolved through a hidden tilemap, or their first sprite.</summary>
		private static Sprite Preview(TileBase tile)
		{
			if (tile == null) return null;
			if (tile is UnityEngine.Tilemaps.Tile plain && plain.sprite != null) return plain.sprite;
			if (_scratch == null)
			{
				// No renderer: the tilemap only resolves tile data and never draws.
				var go = new GameObject("Template brush previews", typeof(Grid)); UnityEngine.Object.DontDestroyOnLoad(go);
				var map = new GameObject("Tiles", typeof(Tilemap)); map.transform.SetParent(go.transform, false); _scratch = map.GetComponent<Tilemap>();
			}
			_scratch.SetTile(Vector3Int.zero, tile);
			Sprite sprite = _scratch.GetSprite(Vector3Int.zero);
			_scratch.SetTile(Vector3Int.zero, null);
			// Rule and random tiles pick sprites from neighbours or chance; show their first one.
			return sprite != null ? sprite : FirstSprite(tile, 4);
		}
		private static Sprite FirstSprite(object value, int depth)
		{
			if (value == null || depth < 0) return null;
			if (value is Sprite s) return s;
			if (value is IEnumerable list && !(value is string))
			{
				foreach (object item in list) { Sprite found = FirstSprite(item, depth - 1); if (found != null) return found; }
				return null;
			}
			if (value is UnityEngine.Object && !(value is ScriptableObject)) return null;
			for (Type type = value.GetType(); type != null && type != typeof(object) && type != typeof(ScriptableObject); type = type.BaseType)
				foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
				{
					if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || field.FieldType == typeof(string)) continue;
					Sprite found = FirstSprite(field.GetValue(value), depth - 1);
					if (found != null) return found;
				}
			return null;
		}
	}

	[HarmonyPatch(typeof(StartupManager), "Start")]
	internal static class Patch_AuthoringSceneStartup
	{
		private static bool Prefix() { return !TemplateAuthoring.Loading; }
	}
}
