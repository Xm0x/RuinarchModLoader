using System;
using System.Collections;
using System.Collections.Generic;
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
		private static bool Prefix(Region location, ref STRUCTURE_TYPE structureType, BaseSettlement settlement, ref LocationStructure __result)
		{
			// A registered demonic building borrows another's prefab, and the game creates the
			// structure from the prefab's own type: while a registered build skill places its
			// building, the borrowed type stands for the registered one.
			StructureRegistration placing = Patch_BuildRegisteredDemonic.Placing;
			if (placing != null && structureType == placing.PrefabSource)
			{
				structureType = placing.StructureType;
				Patch_BuildRegisteredDemonic.Placing = null;
			}
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

	/// <summary>Marks which registration a build skill is placing (see Patch_CreateNewStructureAt).</summary>
	[HarmonyPatch(typeof(DemonicStructurePlayerSkill), "BuildDemonicStructure")]
	internal static class Patch_BuildRegisteredDemonic
	{
		internal static StructureRegistration Placing;

		private static void Prefix(DemonicStructurePlayerSkill __instance)
		{
			Placing = null;
			for (int i = 0; i < ContentRegistry.Structures.Count; i++)
			{
				if (ContentRegistry.Structures[i].Skill == __instance)
				{
					Placing = ContentRegistry.Structures[i];
					break;
				}
			}
		}

		private static Exception Finalizer(Exception __exception)
		{
			Placing = null;
			return __exception;
		}
	}

	/// <summary>The game finds a building's skill by parsing its enum name as a skill name
	/// (PlayerSkillManager.GetDemonicStructureSkillData(STRUCTURE_TYPE)), which throws for a
	/// registered type; it is used when a demonic building is destroyed.</summary>
	[HarmonyPatch(typeof(PlayerSkillManager), nameof(PlayerSkillManager.GetDemonicStructureSkillData), new Type[] { typeof(STRUCTURE_TYPE) })]
	internal static class Patch_SkillOfRegisteredStructure
	{
		private static bool Prefix(STRUCTURE_TYPE type, ref DemonicStructurePlayerSkill __result)
		{
			if (!ContentRegistry.StructuresByType.TryGetValue((int)type, out StructureRegistration reg))
			{
				return true;
			}
			__result = reg.Skill;
			return false;
		}
	}

	/// <summary>Rebuild a registered structure from a save.</summary>
	[HarmonyPatch(typeof(LandmarkManager), nameof(LandmarkManager.LoadNewStructureAt))]
	internal static class Patch_LoadNewStructureAt
	{
		private static bool Prefix(Region location, ref STRUCTURE_TYPE structureType, SaveDataLocationStructure saveDataLocationStructure, ref LocationStructure __result)
		{
			// Saves made before loader 0.7.0 hold a mod structure's type as null, read as 0
			// (see Patch_SaveVirtualEnums); find the registration again by the saved name.
			if ((int)structureType == 0 && StructureNames.FromSavedName(saveDataLocationStructure.name) is StructureRegistration lost)
			{
				structureType = lost.StructureType;
				saveDataLocationStructure.structureType = structureType;
				Templates.FrameworkLog.Info($"Recovered '{saveDataLocationStructure.name}' as {lost.Id} (saved by an older loader without its type).");
			}
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

	/// <summary>The game saves through FullSerializer, which writes an enum by name. A virtual
	/// value has no name, so it was written as null and loaded as 0: a saved world holding a
	/// mod building then failed to load. Write any nameless value as its number instead;
	/// the converter's own reader already accepts numbers.</summary>
	[HarmonyPatch(typeof(FullSerializer.Internal.fsEnumConverter), nameof(FullSerializer.Internal.fsEnumConverter.TrySerialize))]
	internal static class Patch_SaveVirtualEnums
	{
		private static void Postfix(object instance, ref FullSerializer.fsData serialized, Type storageType)
		{
			if (serialized.IsNull && instance != null && Enum.GetName(storageType, instance) == null)
			{
				serialized = new FullSerializer.fsData(Convert.ToInt64(instance));
			}
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
	/// up the PrefabSource's prefabs for the same resource instead. Template variants are
	/// appended (Templates/TemplateRegistry.WithVariants).</summary>
	[HarmonyPatch(typeof(StructureData), nameof(StructureData.GetStructurePrefabs), new Type[] { typeof(FACTION_TYPE), typeof(StructureSetting) })]
	internal static class Patch_GetStructurePrefabs
	{
		private static void Prefix(ref StructureSetting p_structureSetting, out StructureSetting __state)
		{
			__state = p_structureSetting;
			if (ContentRegistry.StructuresByType.TryGetValue((int)p_structureSetting.structureType, out StructureRegistration reg))
			{
				p_structureSetting = new StructureSetting(reg.PrefabSource, p_structureSetting.resource);
			}
		}

		// Template variants join the list for the kind as asked for (a registered kind's own,
		// not its PrefabSource's). Where the game has no list it throws; variants alone then
		// answer.
		private static Exception Finalizer(Exception __exception, FACTION_TYPE p_type, StructureSetting __state, ref List<GameObject> __result)
		{
			List<GameObject> withVariants = Templates.TemplateRegistry.WithVariants(p_type, __state, __exception == null ? __result : null);
			if (withVariants != null)
			{
				__result = withVariants;
				return null;
			}
			return __exception;
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
			IDictionary assets = AccessTools.Field(typeof(PlayerSkillManager), "_playerSkillDataDictionary").GetValue(__instance) as IDictionary;
			for (int i = 0; i < ContentRegistry.Structures.Count; i++)
			{
				StructureRegistration reg = ContentRegistry.Structures[i];
				if (reg.Skill == null && reg.CreateSkill != null)
				{
					try
					{
						reg.Skill = reg.CreateSkill(reg.StructureType, reg.SkillType);
					}
					catch (Exception e)
					{
						Templates.FrameworkLog.Warning($"{reg.Id}: making its build skill failed: {e}");
					}
				}
				if (reg.Skill == null)
				{
					continue;
				}
				__instance.allDemonicStructureSkillsData[reg.SkillType] = reg.Skill;
				// GetSkillData (used when granting, saving and drawing skills) reads this table.
				__instance.allPlayerSkillsData[reg.SkillType] = reg.Skill;
				// Granting a skill reads its PlayerSkillData asset (charges, costs, icon); the
				// game has none for a virtual skill, so give it a copy of a related one.
				PLAYER_SKILL_TYPE from = SkillDataSource(__instance, reg);
				PlayerSkillData source = from == PLAYER_SKILL_TYPE.NONE ? null : assets?[from] as PlayerSkillData;
				if (source == null)
				{
					Templates.FrameworkLog.Warning($"{reg.Id}: no skill settings to copy (set SkillDataFrom); the game cannot grant its build skill");
					continue;
				}
				PlayerSkillData copy = UnityEngine.Object.Instantiate(source);
				copy.name = reg.Id;
				copy.skill = reg.SkillType;
				reg.ConfigureSkillData?.Invoke(copy);
				assets[reg.SkillType] = copy;
			}
		}

		private static PLAYER_SKILL_TYPE SkillDataSource(PlayerSkillManager manager, StructureRegistration reg)
		{
			if (reg.SkillDataFrom != PLAYER_SKILL_TYPE.NONE) return reg.SkillDataFrom;
			if (reg.UnlockWith != PLAYER_SKILL_TYPE.NONE) return reg.UnlockWith;
			foreach (KeyValuePair<PLAYER_SKILL_TYPE, DemonicStructurePlayerSkill> pair in manager.allDemonicStructureSkillsData)
			{
				if (pair.Value != null && pair.Value.structureType == reg.PrefabSource && (int)pair.Key < ContentRegistry.VirtualBase) return pair.Key;
			}
			return PLAYER_SKILL_TYPE.NONE;
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

		/// <summary>The registration a saved structure name belongs to, or null if none or
		/// more than one fits. The game names a structure "noun + type name" (type name first
		/// in some languages), so the type name starts or ends it; the longest fitting name
		/// wins, so "Hall" never shadows "Town Hall".</summary>
		internal static StructureRegistration FromSavedName(string savedName)
		{
			if (string.IsNullOrEmpty(savedName))
			{
				return null;
			}
			StructureRegistration best = null;
			int bestLength = 0;
			bool tied = false;
			for (int i = 0; i < ContentRegistry.Structures.Count; i++)
			{
				StructureRegistration reg = ContentRegistry.Structures[i];
				TryGet(reg.StructureType, out string name);
				if (savedName != name && !savedName.EndsWith(" " + name, StringComparison.Ordinal)
					&& !savedName.StartsWith(name + " ", StringComparison.Ordinal))
				{
					continue;
				}
				if (name.Length > bestLength)
				{
					best = reg;
					bestLength = name.Length;
					tied = false;
				}
				else if (name.Length == bestLength)
				{
					tied = true;
				}
			}
			return tied ? null : best;
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
	/// also grant the registered virtual skill the game's own way, so it is marked in use,
	/// gets its charges and cost, and appears in the dynamic build menu.</summary>
	[HarmonyPatch(typeof(PlayerSkillComponent), "AddAndCategorizePlayerSkill", new Type[] { typeof(SkillData), typeof(bool), typeof(bool) })]
	internal static class Patch_UnlockRegisteredSkill
	{
		private static void Postfix(PlayerSkillComponent __instance, SkillData p_skillData, bool testScene, bool isDevMode)
		{
			if (p_skillData == null)
			{
				return;
			}
			for (int i = 0; i < ContentRegistry.Structures.Count; i++)
			{
				StructureRegistration reg = ContentRegistry.Structures[i];
				if (reg.UnlockWith == PLAYER_SKILL_TYPE.NONE || p_skillData.type != reg.UnlockWith
					|| __instance.demonicStructuresSkills.Contains(reg.SkillType))
				{
					continue;
				}
				SkillData skill = PlayerSkillManager.Instance.GetDemonicStructureSkillData(reg.SkillType);
				if (skill != null)
				{
					__instance.AddAndCategorizePlayerSkill(skill, testScene, isDevMode);
				}
			}
		}
	}
}
