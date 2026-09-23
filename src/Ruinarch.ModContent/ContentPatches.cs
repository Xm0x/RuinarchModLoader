using System;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Locations.Settlements;
using UnityEngine;

namespace Ruinarch.ModContent
{
	// ============================================================================
	// The central patch layer. Every game "make new content" path routes through a
	// reflection factory keyed by an enum name:
	//   Type.GetType("<ns>." + enumValue.ToStringEnumNoSpace() + ", Assembly-CSharp")
	//   Activator.CreateInstance(type, args)
	// For a virtual (registered) enum value that lookup returns null and the game
	// throws. Each prefix below intercepts registered virtual values BEFORE the
	// reflection runs and returns the mod's instance, so the stock game never edits.
	// ============================================================================

	/// <summary>Build a fresh registered structure (player places it).</summary>
	[HarmonyPatch(typeof(LandmarkManager), nameof(LandmarkManager.CreateNewStructureAt))]
	internal static class Patch_CreateNewStructureAt
	{
		private static bool Prefix(Region location, STRUCTURE_TYPE structureType, BaseSettlement settlement, ref LocationStructure __result)
		{
			if (!ContentRegistry.StructuresByType.TryGetValue((int)structureType, out StructureRegistration reg))
			{
				return true; // not ours - run the stock reflection factory
			}
			LocationStructure s = reg.Factory(structureType, location);
			location.AddStructure(s);
			if (settlement != null)
			{
				settlement.AddStructure(s);
			}
			s.Initialize();
			DatabaseManager.Instance.structureDatabase.RegisterStructure(s);
			__result = s;
			return false;
		}
	}

	/// <summary>Rebuild a registered structure from a save.</summary>
	[HarmonyPatch(typeof(LandmarkManager), nameof(LandmarkManager.LoadNewStructureAt))]
	internal static class Patch_LoadNewStructureAt
	{
		private static bool Prefix(Region location, STRUCTURE_TYPE structureType, SaveDataLocationStructure saveDataLocationStructure, ref LocationStructure __result)
		{
			if (!ContentRegistry.StructuresByType.TryGetValue((int)structureType, out StructureRegistration reg))
			{
				return true;
			}
			LocationStructure s = reg.LoadFactory(structureType, location, saveDataLocationStructure);
			if (!s.hasBeenDestroyed)
			{
				s.InitializeFromSave();
			}
			DatabaseManager.Instance.structureDatabase.RegisterStructure(s);
			__result = s;
			return false;
		}
	}

	/// <summary>Reuse an existing structure's prefab/visual data (no new Unity asset).</summary>
	[HarmonyPatch(typeof(LandmarkManager), nameof(LandmarkManager.GetStructureData))]
	internal static class Patch_GetStructureData
	{
		private static bool Prefix(LandmarkManager __instance, STRUCTURE_TYPE p_structureType, ref StructureData __result)
		{
			if (!ContentRegistry.StructuresByType.TryGetValue((int)p_structureType, out StructureRegistration reg))
			{
				return true;
			}
			__result = __instance.GetStructureData(reg.PrefabSource);
			return false;
		}
	}

	/// <summary>Blueprint placement asks the structure data for prefabs keyed by the full
	/// StructureSetting (type + resource). A registered type has no entry of its own, so look
	/// up the PrefabSource's prefabs for the same resource instead.</summary>
	[HarmonyPatch(typeof(StructureData), nameof(StructureData.GetStructurePrefabs), new Type[] { typeof(FACTION_TYPE), typeof(StructureSetting) })]
	internal static class Patch_GetStructurePrefabs
	{
		private static void Prefix(ref StructureSetting p_structureSetting)
		{
			if (ContentRegistry.StructuresByType.TryGetValue((int)p_structureSetting.structureType, out StructureRegistration reg))
			{
				p_structureSetting = new StructureSetting(reg.PrefabSource, p_structureSetting.resource);
			}
		}
	}

	/// <summary>Inject registered build-skill data into the demonic-structure skill dict.
	/// Runs once, right after the game builds the dict from its fixed array - we never grow
	/// the fixed array (that drove the UnlockStructureUIController crash), we add straight to
	/// the dictionary that GetDemonicStructureSkillData + the build menu actually read.</summary>
	[HarmonyPatch(typeof(PlayerSkillManager), "ConstructAllDemonicStructureSkillsData")]
	internal static class Patch_ConstructDemonicSkills
	{
		private static void Postfix(PlayerSkillManager __instance)
		{
			for (int i = 0; i < ContentRegistry.Structures.Count; i++)
			{
				StructureRegistration reg = ContentRegistry.Structures[i];
				if (reg.Skill == null)
				{
					continue;
				}
				__instance.allDemonicStructureSkillsData[reg.SkillType] = reg.Skill;
			}
		}
	}

	/// <summary>Classification: make registered types answer the game's Extensions switches.
	/// (The game has no separate "demonic" switch: demonic structures are IsPlayerStructure.)</summary>
	[HarmonyPatch(typeof(Extensions), "IsPlayerStructure", new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class Patch_IsPlayerStructure
	{
		private static void Postfix(STRUCTURE_TYPE __0, ref bool __result)
		{
			if (ContentRegistry.StructuresByType.TryGetValue((int)__0, out StructureRegistration reg) && reg.IsPlayerStructure)
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(Extensions), "IsSpecialStructure", new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class Patch_IsSpecialStructure
	{
		private static void Postfix(STRUCTURE_TYPE __0, ref bool __result)
		{
			if (ContentRegistry.StructuresByType.TryGetValue((int)__0, out StructureRegistration reg) && reg.IsSpecialStructure)
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(Extensions), "IsVillageStructure", new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class Patch_IsVillageStructure
	{
		private static void Postfix(STRUCTURE_TYPE __0, ref bool __result)
		{
			if (ContentRegistry.StructuresByType.TryGetValue((int)__0, out StructureRegistration reg) && reg.IsVillageStructure)
			{
				__result = true;
			}
		}
	}

	// ---- Names -------------------------------------------------------------------------
	// The game names structure types through raw dictionary lookups
	// (StringEnumLookUp._structureTypeStrings[type]); a virtual type is not in them, so every
	// log line, job description or UI panel that names a registered structure would throw
	// KeyNotFoundException. Answer with the registration's DisplayName instead.

	internal static class StructureNames
	{
		internal static bool TryGet(STRUCTURE_TYPE type, out string displayName)
		{
			if (ContentRegistry.StructuresByType.TryGetValue((int)type, out StructureRegistration reg))
			{
				displayName = string.IsNullOrEmpty(reg.DisplayName) ? reg.Id : reg.DisplayName;
				return true;
			}
			displayName = null;
			return false;
		}
	}

	/// <summary>Enum-style key, e.g. "Mass Grave" -> "MASS_GRAVE".</summary>
	[HarmonyPatch(typeof(StringEnumLookUp), nameof(StringEnumLookUp.ToStringEnum), new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class Patch_StructureToStringEnum
	{
		private static bool Prefix(STRUCTURE_TYPE p_type, ref string __result)
		{
			if (!StructureNames.TryGet(p_type, out string name))
			{
				return true;
			}
			__result = name.Replace(' ', '_').ToUpperInvariant();
			return false;
		}
	}

	/// <summary>Display form, e.g. "Mass Grave". (The vanilla class lookup that strips the
	/// spaces from this resolves to nothing in Assembly-CSharp, which is the safe outcome;
	/// the factory prefixes above build registered types before that lookup runs.)</summary>
	[HarmonyPatch(typeof(StringEnumLookUp), nameof(StringEnumLookUp.ToStringEnumWithSpace), new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class Patch_StructureToStringEnumWithSpace
	{
		private static bool Prefix(STRUCTURE_TYPE p_type, ref string __result)
		{
			if (!StructureNames.TryGet(p_type, out string name))
			{
				return true;
			}
			__result = name;
			return false;
		}
	}

	/// <summary>Localized name: the game's table has no entry for a registered type.</summary>
	[HarmonyPatch(typeof(Extensions), nameof(Extensions.LocalizedStructureName), new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class Patch_LocalizedStructureName
	{
		private static bool Prefix(STRUCTURE_TYPE structureType, ref string __result)
		{
			if (!StructureNames.TryGet(structureType, out string name))
			{
				return true;
			}
			__result = name;
			return false;
		}
	}

	/// <summary>Unlock: when the player gains a registration's <c>UnlockWith</c> source skill,
	/// also grant the registered virtual skill so it appears in the dynamic build menu.</summary>
	[HarmonyPatch(typeof(PlayerSkillComponent), "AddAndCategorizePlayerSkill", new Type[] { typeof(SkillData), typeof(bool), typeof(bool) })]
	internal static class Patch_UnlockRegisteredSkill
	{
		private static void Postfix(PlayerSkillComponent __instance, SkillData p_skillData)
		{
			if (p_skillData == null)
			{
				return;
			}
			for (int i = 0; i < ContentRegistry.Structures.Count; i++)
			{
				StructureRegistration reg = ContentRegistry.Structures[i];
				if (reg.UnlockWith == PLAYER_SKILL_TYPE.NONE || p_skillData.type != reg.UnlockWith)
				{
					continue;
				}
				if (!__instance.demonicStructuresSkills.Contains(reg.SkillType))
				{
				__instance.demonicStructuresSkills.Add(reg.SkillType);
					BroadcastGained(reg.SkillType);
				}
			}
		}

		// Messenger is internal to Assembly-CSharp, so an external mod assembly cannot call
		// it directly - invoke the generic Broadcast<PLAYER_SKILL_TYPE> via reflection.
		private static void BroadcastGained(PLAYER_SKILL_TYPE skillType)
		{
			try
			{
				Type messenger = AccessTools.TypeByName("Messenger");
				System.Reflection.MethodInfo m = AccessTools.Method(messenger, "Broadcast",
					new Type[] { typeof(string), typeof(PLAYER_SKILL_TYPE) },
					new Type[] { typeof(PLAYER_SKILL_TYPE) });
				if (m != null)
				{
					m.Invoke(null, new object[] { PlayerSkillSignals.PLAYER_GAINED_DEMONIC_STRUCTURE, skillType });
				}
			}
			catch (Exception e)
			{
				Debug.LogError("[ModContent] gained-skill broadcast failed: " + e);
			}
		}
	}
}
