using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using UnityEngine;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// Every game building prefab, read straight from LandmarkManager's StructureData (not
	/// through GetStructurePrefabs, which the framework patches and which throws for a missing
	/// combination). Demonic buildings are left out: templates are not for them.
	/// </summary>
	internal static class TemplateCatalogue
	{
		private static List<GameLook> _looks;
		internal static bool HasLooks => _looks != null && _looks.Count > 0;

		internal static IReadOnlyList<GameLook> Looks()
		{
			if (_looks != null)
			{
				return _looks;
			}
			List<GameLook> looks = new List<GameLook>();
			IDictionary byType = AccessTools.Field(typeof(LandmarkManager), "structureData").GetValue(LandmarkManager.Instance) as IDictionary;
			foreach (DictionaryEntry e in byType ?? new Hashtable())
			{
				if (!(e.Value is StructureData data))
				{
					continue;
				}
				IDictionary byCulture = AccessTools.Field(typeof(StructureData), "structurePrefabs").GetValue(data) as IDictionary;
				foreach (DictionaryEntry f in byCulture ?? new Hashtable())
				{
					IDictionary choices = f.Value == null ? null : AccessTools.Field(f.Value.GetType(), "structureChoices").GetValue(f.Value) as IDictionary;
					foreach (DictionaryEntry c in choices ?? new Hashtable())
					{
						StructureSetting setting = (StructureSetting)c.Key;
						if (setting.structureType.IsPlayerStructure())
						{
							continue;
						}
						foreach (GameObject prefab in (c.Value as IList)?.OfType<GameObject>() ?? Enumerable.Empty<GameObject>())
						{
							if (prefab != null && prefab.GetComponent<LocationStructureObject>() != null)
							{
								looks.Add(new GameLook { Kind = setting.structureType, Culture = (FACTION_TYPE)f.Key, Material = setting.resource, Prefab = prefab });
							}
						}
					}
				}
			}
			// Prefabs are assets: the list stays valid for the session once it has anything.
			if (looks.Count > 0)
			{
				_looks = looks;
			}
			return looks;
		}

		/// <summary>The game's own looks for a kind, culture and material, the way the game
		/// picks them: a registered kind uses its PrefabSource's; no list for the culture
		/// falls back to FACTION_TYPE.None.</summary>
		internal static List<GameObject> Originals(STRUCTURE_TYPE kind, FACTION_TYPE culture, RESOURCE material)
		{
			STRUCTURE_TYPE lookup = Lookup(kind);
			FACTION_TYPE listed = HasOwnList(kind, culture, material) ? culture : FACTION_TYPE.None;
			return Looks().Where(l => l.Kind == lookup && l.Culture == listed && l.Material == material).Select(l => l.Prefab).ToList();
		}

		/// <summary>Whether the game has a list of its own for the culture; without one it uses
		/// the culture-neutral (FACTION_TYPE.None) list (StructureData.GetStructurePrefabs).
		/// Human and Elven buildings are all in the neutral lists.</summary>
		internal static bool HasOwnList(STRUCTURE_TYPE kind, FACTION_TYPE culture, RESOURCE material)
		{
			STRUCTURE_TYPE lookup = Lookup(kind);
			return Looks().Any(l => l.Kind == lookup && l.Culture == culture && l.Material == material);
		}

		// A registered kind has no prefabs of its own: it uses its PrefabSource's.
		private static STRUCTURE_TYPE Lookup(STRUCTURE_TYPE kind)
		{
			return ContentRegistry.StructuresByType.TryGetValue((int)kind, out StructureRegistration reg) ? reg.PrefabSource : kind;
		}

		internal static GameObject FindByName(string prefabName)
		{
			return Looks().FirstOrDefault(l => l.Prefab.name == prefabName)?.Prefab;
		}
	}
}
