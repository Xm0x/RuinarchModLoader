using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Ruinarch.ModContent
{
	// ============================================================================
	// Registered actions. The game keeps its actions in three dictionaries keyed by
	// INTERACTION_TYPE: the action name table (StringEnumLookUp), the states
	// (GoapActionStateDB.goapActionStates) and the action instances
	// (InteractionManager.goapActionData). A virtual value is simply one more
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

	/// <summary>After the game makes its own actions, add each registered one's states and
	/// make it the same way (one instance, filed under its effects so the planner can use it
	/// too). The states cannot go in at registration: GoapActionStateDB's static constructor
	/// needs GameManager, which does not exist yet while mods load (touching the class then
	/// breaks it for the whole session). By now the game's own actions have used it.</summary>
	[HarmonyPatch(typeof(InteractionManager), "ConstructGoapActionData")]
	internal static class Patch_ActionData
	{
		private static void Postfix(InteractionManager __instance)
		{
			foreach (ActionRegistration reg in ContentRegistry.ActionsByType.Values)
			{
				StateNameAndDuration[] states = new StateNameAndDuration[reg.States.Count];
				for (int i = 0; i < states.Length; i++)
				{
					states[i] = new StateNameAndDuration
					{
						name = reg.States[i].Name,
						status = reg.States[i].Success ? "Success" : "Fail",
						duration = reg.States[i].DurationTicks
					};
				}
				GoapActionStateDB.goapActionStates[reg.Type] = states;
			}
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
			string text = TextOf(reg, "Describe", state?.Describe, goapNode, null);
			if (string.IsNullOrEmpty(text))
			{
				return false;
			}
			__result = FixedLog(reg, goapNode, text);
			return false;
		}

		/// <summary>The text a registration's function gives for a node, or
		/// <paramref name="fallback"/> if it has none or throws.</summary>
		internal static string TextOf(ActionRegistration reg, string what, System.Func<ActualGoapNode, string> text, ActualGoapNode node, string fallback)
		{
			try
			{
				string s = text?.Invoke(node);
				return string.IsNullOrEmpty(s) ? fallback : s;
			}
			catch (System.Exception e)
			{
				Debug.LogError("[ModContent] Action '" + reg.Id + "' " + what + " failed: " + e.Message);
				return fallback;
			}
		}

		/// <summary>A log with fixed text and the action's usual fillers (actor, target,
		/// structure).</summary>
		internal static Log FixedLog(ActionRegistration reg, ActualGoapNode node, string text)
		{
			Log log = GameManager.CreateNewLogUsingNewLocalization(GameManager.Instance.Today(), "GoapAction", FixedTextFile, reg.Id, node.logTags);
			node.action.AddFillersToLog(log, node);
			log.SetLogText(text);
			return log;
		}
	}

	/// <summary>The game's UI reads a thought bubble for whatever a villager is doing (their
	/// nameplate, panel and tooltip) and throws when an action has none; the game makes them
	/// only from text-table keys a registered action does not have. Give every registered
	/// action both: its Going and Doing texts, or defaults from its name. Saves keep them.</summary>
	[HarmonyPatch(typeof(ActualGoapNode), "CreateThoughtBubbleLog")]
	internal static class Patch_ActionThoughtBubble
	{
		private static readonly System.Action<ActualGoapNode, Log> SetDoing =
			AccessTools.MethodDelegate<System.Action<ActualGoapNode, Log>>(AccessTools.PropertySetter(typeof(ActualGoapNode), nameof(ActualGoapNode.thoughtBubbleLog)));
		private static readonly System.Action<ActualGoapNode, Log> SetGoing =
			AccessTools.MethodDelegate<System.Action<ActualGoapNode, Log>>(AccessTools.PropertySetter(typeof(ActualGoapNode), nameof(ActualGoapNode.thoughtBubbleMovingLog)));

		private static void Postfix(ActualGoapNode __instance)
		{
			if (__instance?.action == null || !ContentRegistry.ActionsByType.TryGetValue((int)__instance.action.goapType, out ActionRegistration reg))
			{
				return;
			}
			string name = __instance.action.goapName;
			if (__instance.thoughtBubbleLog == null)
			{
				SetDoing(__instance, Patch_ActionDescription.FixedLog(reg, __instance, Patch_ActionDescription.TextOf(reg, "Doing", reg.Doing, __instance, name + ".")));
			}
			if (__instance.thoughtBubbleMovingLog == null)
			{
				SetGoing(__instance, Patch_ActionDescription.FixedLog(reg, __instance, Patch_ActionDescription.TextOf(reg, "Going", reg.Going, __instance, "Going to " + name + ".")));
			}
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
