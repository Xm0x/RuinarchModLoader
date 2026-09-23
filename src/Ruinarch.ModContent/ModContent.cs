using System;
using HarmonyLib;
using UnityEngine;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Public entry point for mods that add genuinely NEW content to the stock game
	/// (new buildable structures today; more content kinds over time). A mod calls
	/// <see cref="RegisterStructure"/> in its <c>OnLoad</c>; the framework allocates virtual
	/// enum values and self-installs the Harmony patches that make the content behave like
	/// first-class game content - all against an unmodified <c>Assembly-CSharp</c>.
	///
	/// The framework is a plain dependency DLL that lives in <c>Mods/</c> (like 0Harmony).
	/// It has no coupling to the loader: it installs its own patches the first time any mod
	/// touches the API, which happens during mod load (module-init), before the game builds
	/// its skill/structure tables.
	/// </summary>
	public static class ModContent
	{
		private const string HarmonyId = "ruinarch.modcontent";
		private static Harmony _harmony;
		private static bool _installed;

		/// <summary>
		/// Idempotent. Applies the framework's Harmony patches. Auto-called by the
		/// register methods; a mod may also call it explicitly (order-independent).
		/// </summary>
		public static void Install()
		{
			if (_installed)
			{
				return;
			}
			_installed = true;
			_harmony = new Harmony(HarmonyId);
			// Patch class by class: with PatchAll, one unresolvable target aborts every patch
			// after it and leaves the framework half-installed. Isolated, a bad patch only
			// disables itself and is named in the log.
			int ok = 0;
			int failed = 0;
			foreach (Type t in AccessTools.GetTypesFromAssembly(typeof(ModContent).Assembly))
			{
				if (t.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0)
				{
					continue;
				}
				try
				{
					_harmony.CreateClassProcessor(t).Patch();
					ok++;
				}
				catch (Exception e)
				{
					failed++;
					Debug.LogError("[ModContent] Patch " + t.Name + " failed: " + e.Message);
				}
			}
			Debug.Log(string.Format("[ModContent] Content framework installed ({0} patch(es) applied, {1} failed).", ok, failed));
		}

		/// <summary>
		/// Register a brand-new buildable structure. Allocates a virtual STRUCTURE_TYPE +
		/// PLAYER_SKILL_TYPE (deterministic from <see cref="StructureRegistration.Id"/>),
		/// stores the registration, and returns it with those values filled in.
		/// </summary>
		public static StructureRegistration RegisterStructure(StructureRegistration reg)
		{
			if (reg == null)
			{
				throw new ArgumentNullException("reg");
			}
			if (string.IsNullOrEmpty(reg.Id))
			{
				throw new ArgumentException("StructureRegistration.Id is required");
			}
			if (reg.Factory == null || reg.LoadFactory == null)
			{
				throw new ArgumentException("StructureRegistration needs both Factory and LoadFactory");
			}
			Install();

			int structVal = ContentRegistry.AllocateValue(reg.Id, ContentRegistry.StructuresByType);
			int skillVal = ContentRegistry.AllocateValue(reg.Id + "#skill", ContentRegistry.StructuresBySkill);
			reg.StructureType = (STRUCTURE_TYPE)structVal;
			reg.SkillType = (PLAYER_SKILL_TYPE)skillVal;

			// The skill drives what gets placed; point its structureType at the allocated
			// virtual value. structureType has a protected setter, so set it via reflection.
			if (reg.Skill != null)
			{
				System.Reflection.MethodInfo setter = AccessTools.PropertySetter(reg.Skill.GetType(), "structureType");
				if (setter != null)
				{
					setter.Invoke(reg.Skill, new object[] { reg.StructureType });
				}
			}

			ContentRegistry.StructuresByType[structVal] = reg;
			ContentRegistry.StructuresBySkill[skillVal] = reg;
			ContentRegistry.Structures.Add(reg);

			Debug.Log(string.Format(
				"[ModContent] Registered structure '{0}' -> STRUCTURE_TYPE={1}, PLAYER_SKILL_TYPE={2}",
				reg.Id, structVal, skillVal));
			return reg;
		}

		/// <summary>The virtual STRUCTURE_TYPE allocated for a registered id (or default(0)).</summary>
		public static STRUCTURE_TYPE StructureTypeFor(string id)
		{
			for (int i = 0; i < ContentRegistry.Structures.Count; i++)
			{
				if (ContentRegistry.Structures[i].Id == id)
				{
					return ContentRegistry.Structures[i].StructureType;
				}
			}
			return (STRUCTURE_TYPE)0;
		}

		/// <summary>The virtual PLAYER_SKILL_TYPE allocated for a registered id's skill.
		/// A mod's skill class resolves its own <c>type</c> getter through this.</summary>
		public static PLAYER_SKILL_TYPE SkillTypeFor(string id)
		{
			for (int i = 0; i < ContentRegistry.Structures.Count; i++)
			{
				if (ContentRegistry.Structures[i].Id == id)
				{
					return ContentRegistry.Structures[i].SkillType;
				}
			}
			return PLAYER_SKILL_TYPE.NONE;
		}
	}
}
