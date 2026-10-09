using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Pathfinding;
using UnityEngine;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// A* finds its pathfinder and graph modifiers with <c>FindObjectsOfType</c>, which also sees
	/// the frozen world's (disabled but loaded) objects. Restrict discovery to the scene being
	/// constructed, or to the active world.
	/// </summary>
	internal static class PathfinderScope
	{
		internal static int ConstructingScene;

		internal static void Install(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(AstarPath), "Awake"),
				prefix: new HarmonyMethod(typeof(PathfinderScope), nameof(Entering)),
				transpiler: new HarmonyMethod(typeof(PathfinderScope), nameof(ScopeDiscovery)));
			harmony.Patch(AccessTools.Method(typeof(GraphModifier), "FindAllModifiers"),
				transpiler: new HarmonyMethod(typeof(PathfinderScope), nameof(ScopeDiscovery)));
			harmony.Patch(AccessTools.Method(typeof(RelevantGraphSurface), "FindAllGraphSurfaces"),
				transpiler: new HarmonyMethod(typeof(PathfinderScope), nameof(ScopeDiscovery)));
		}

		private static void Entering(AstarPath __instance)
		{
			ConstructingScene = __instance.gameObject.scene.handle;
		}

		private static IEnumerable<CodeInstruction> ScopeDiscovery(IEnumerable<CodeInstruction> instructions)
		{
			var native = AccessTools.Method(typeof(UnityEngine.Object), nameof(UnityEngine.Object.FindObjectsOfType), new[] { typeof(Type) });
			int count = 0;
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.Calls(native))
				{
					instruction.opcode = OpCodes.Call;
					instruction.operand = AccessTools.Method(typeof(PathfinderScope), nameof(Find));
					count++;
				}
				yield return instruction;
			}
			if (count != 1)
			{
				throw new InvalidOperationException("Unexpected native pathfinder discovery boundary: " + count);
			}
		}

		private static UnityEngine.Object[] Find(Type type)
		{
			UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(type);
			int selected = ConstructingScene != 0 ? ConstructingScene : WorldScene.ActiveHandle;
			int count = 0;
			foreach (UnityEngine.Object item in all)
			{
				if (item is Component c && c.gameObject.scene.handle == selected)
				{
					count++;
				}
			}
			if (count == all.Length)
			{
				return all;
			}
			var result = (UnityEngine.Object[])Array.CreateInstance(all.GetType().GetElementType(), count);
			int index = 0;
			foreach (UnityEngine.Object item in all)
			{
				if (item is Component c && c.gameObject.scene.handle == selected)
				{
					result[index++] = item;
				}
			}
			return result;
		}
	}
}
