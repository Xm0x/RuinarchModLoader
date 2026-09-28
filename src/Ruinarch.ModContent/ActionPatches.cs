using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Ruinarch.ModContent
{
	// ============================================================================
	// Registered actions. The game keeps its actions in three dictionaries keyed by
	// INTERACTION_TYPE: the action name table (StringEnumLookUp), the states
	// (GoapActionStateDB.goapActionStates, filled at registration) and the action
	// instances (InteractionManager.goapActionData). A virtual value is simply one more
	// key in each. Everything else about an action (its Pre/PerTick/After callbacks) the
	// game finds by name on the action's own class.
	// ============================================================================

	/// <summary>The game rebuilds its action-name table at the main menu; add ours to it.
	/// The GoapAction constructor reads the name, so this must run before any factory.</summary>
	[HarmonyPatch(typeof(StringEnumLookUp), nameof(StringEnumLookUp.Initialize))]
	internal static class Patch_ActionNames
	{
		private static readonly AccessTools.FieldRef<Dictionary<INTERACTION_TYPE, string>> Names =
			AccessTools.StaticFieldRefAccess<Dictionary<INTERACTION_TYPE, string>>(AccessTools.Field(typeof(StringEnumLookUp), "_interactionTypeStrings"));

		private static void Postfix()
		{
			AddNames();
		}

		internal static void AddNames()
		{
			Dictionary<INTERACTION_TYPE, string> names = Names();
			if (names == null)
			{
				return;
			}
			foreach (ActionRegistration reg in ContentRegistry.ActionsByType.Values)
			{
				names[reg.Type] = reg.Name;
			}
		}
	}

	/// <summary>After the game makes its own actions, make each registered one the same way
	/// (one instance, filed under its effects so the planner can use it too).</summary>
	[HarmonyPatch(typeof(InteractionManager), "ConstructGoapActionData")]
	internal static class Patch_ActionData
	{
		private static void Postfix(InteractionManager __instance)
		{
			foreach (ActionRegistration reg in ContentRegistry.ActionsByType.Values)
			{
				GoapAction action;
				try
				{
					action = reg.Factory();
				}
				catch (System.Exception e)
				{
					Debug.LogError("[ModContent] Action '" + reg.Id + "' could not be created: " + e);
					continue;
				}
				if (action == null || action.goapType != reg.Type)
				{
					Debug.LogError("[ModContent] Action '" + reg.Id + "': the factory must return an action constructed with ModContent.ActionTypeFor(\"" + reg.Id + "\").");
					continue;
				}
				__instance.goapActionData[reg.Type] = action;
				__instance.goapActionList.Add(action);
				foreach (GoapEffectConditionTypeAndTargetType effect in action.possibleExpectedEffectsTypeAndTargetMatching)
				{
					if (!__instance.actionsCategorizedByEffectCondition.TryGetValue(effect.conditionType, out List<GoapAction> list))
					{
						list = new List<GoapAction>();
						__instance.actionsCategorizedByEffectCondition.Add(effect.conditionType, list);
					}
					list.Add(action);
				}
			}
		}
	}

	/// <summary>A registered action's log comes from its state's Describe, not from the
	/// game's text tables (which have no entry for it). The log gets the game's usual
	/// fillers (actor, target, structure), so it shows in their Logs tabs.</summary>
	[HarmonyPatch(typeof(GoapActionState), nameof(GoapActionState.CreateDescriptionLog))]
	internal static class Patch_ActionDescription
	{
		/// <summary>Marks a log whose text is fixed (not looked up in a text table).</summary>
		internal const string FixedTextFile = "ModContent";

		private static bool Prefix(GoapActionState __instance, ActualGoapNode goapNode, ref Log __result)
		{
			if (goapNode?.action == null || !ContentRegistry.ActionsByType.TryGetValue((int)goapNode.action.goapType, out ActionRegistration reg))
			{
				return true;
			}
			__result = null;
			ActionState state = reg.States.Find(s => s.Name == __instance.name);
			string text = null;
			try
			{
				text = state?.Describe?.Invoke(goapNode);
			}
			catch (System.Exception e)
			{
				Debug.LogError("[ModContent] Action '" + reg.Id + "' Describe failed: " + e.Message);
			}
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}
			Log log = GameManager.CreateNewLogUsingNewLocalization(GameManager.Instance.Today(), "GoapAction", FixedTextFile, reg.Id, goapNode.logTags);
			goapNode.action.AddFillersToLog(log, goapNode);
			log.SetLogText(text);
			__result = log;
			return false;
		}
	}

	/// <summary>A fixed-text log keeps its text when the game would look it up again (a
	/// language change, a rename): there is nothing to look up.</summary>
	[HarmonyPatch(typeof(Log), nameof(Log.ResetText))]
	internal static class Patch_FixedTextLog
	{
		private static bool Prefix(Log __instance)
		{
			return __instance.file != Patch_ActionDescription.FixedTextFile;
		}
	}
}
