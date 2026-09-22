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

	/// <summary>The game gates some paths on the fixed skill array via HasDemonicStructureSkill;
	/// report true for registered virtual skills.</summary>
	[HarmonyPatch(typeof(PlayerSkillManager), "HasDemonicStructureSkill")]
	internal static class Patch_HasDemonicStructureSkill
	{
		private static void Postfix(PLAYER_SKILL_TYPE __0, ref bool __result)
		{
			if (ContentRegistry.StructuresBySkill.ContainsKey((int)__0))
			{
				__result = true;
			}
		}
	}

	/// <summary>Classification: make registered types answer the game's Extensions switches.</summary>
	[HarmonyPatch(typeof(Extensions), "IsDemonicStructure", new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class Patch_IsDemonicStructure
	{
		private static void Postfix(STRUCTURE_TYPE __0, ref bool __result)
		{
			if (ContentRegistry.StructuresByType.TryGetValue((int)__0, out StructureRegistration reg) && reg.IsDemonic)
			{
				__result = true;
			}
		}
	}

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
