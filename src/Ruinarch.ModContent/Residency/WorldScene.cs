using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DG.Tweening;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// One loaded "Game" scene with its own 2D and 3D physics worlds. Only the active world
	/// steps physics and answers the game's physics queries; a frozen world keeps its colliders
	/// and bodies registered but untouched.
	/// </summary>
	internal sealed class WorldScene
	{
		private static readonly Dictionary<int, WorldScene> Worlds = new Dictionary<int, WorldScene>();

		internal static int ActiveHandle;

		// True while a frozen world disables its components (vision colliders must not react).
		internal static bool Suspending;

		// Set by Residency right before its own native load; consumed by the next "Game" load.
		internal static bool ColdLoading;

		internal static event Action<Scene> Unloaded;

		internal readonly Scene Scene;
		internal readonly PhysicsScene2D Physics2DScene;
		internal readonly PhysicsScene Physics3DScene;
		internal readonly HashSet<Tween> Tweens = new HashSet<Tween>();
		internal int Steps;
		internal bool Frozen;

		private WorldScene(Scene scene)
		{
			Scene = scene;
			Physics2DScene = scene.GetPhysicsScene2D();
			Physics3DScene = scene.GetPhysicsScene();
			if (!Physics2DScene.IsValid() || Physics2DScene.Equals(Physics2D.defaultPhysicsScene)
				|| !Physics3DScene.IsValid() || Physics3DScene.Equals(Physics.defaultPhysicsScene))
			{
				throw new InvalidOperationException("Game scene did not get its own physics worlds");
			}
		}

		internal static WorldScene Active => Worlds.TryGetValue(ActiveHandle, out WorldScene world) ? world : null;

		internal static bool AnyFrozen => Worlds.Values.Any(w => w.Frozen);

		internal static PhysicsScene2D Current2D() => Active?.Physics2DScene ?? Physics2D.defaultPhysicsScene;

		internal static PhysicsScene Current3D() => Active?.Physics3DScene ?? Physics.defaultPhysicsScene;

		// Behaviours outside any tracked world (menus, DontDestroyOnLoad) always run.
		internal static bool IsActive(MonoBehaviour owner)
		{
			return owner != null && (!Worlds.TryGetValue(owner.gameObject.scene.handle, out WorldScene world)
				|| (!world.Frozen && ActiveHandle == world.Scene.handle));
		}

		internal void Select()
		{
			Frozen = false;
			if (SceneManager.GetActiveScene().handle != Scene.handle && !SceneManager.SetActiveScene(Scene))
			{
				throw new InvalidOperationException("Cannot make the resident scene active (valid=" + Scene.IsValid() + ", loaded=" + Scene.isLoaded + ")");
			}
			ActiveHandle = Scene.handle;
		}

		internal static void Install(Harmony harmony)
		{
			harmony.Patch(AccessTools.Method(typeof(SceneManager), nameof(SceneManager.LoadSceneAsync), new[] { typeof(string), typeof(LoadSceneMode) }),
				prefix: new HarmonyMethod(typeof(WorldScene), nameof(LoadScene)));
			int routed = 0;
			foreach (Type type in new[] { typeof(Physics2D), typeof(Physics) })
			{
				MethodInfo getter = AccessTools.PropertyGetter(type, "defaultPhysicsScene");
				foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
				{
					if (method == getter || method.GetMethodBody() == null)
					{
						continue;
					}
					if (!PatchProcessor.GetOriginalInstructions(method).Any(i => i.Calls(getter)))
					{
						continue;
					}
					harmony.Patch(method, transpiler: new HarmonyMethod(typeof(WorldScene), nameof(RouteQuery)));
					routed++;
				}
			}
			if (routed == 0)
			{
				throw new InvalidOperationException("No native physics query was routed");
			}
			harmony.Patch(AccessTools.Method(typeof(CharacterMarkerVisionCollider), "OnDisable"),
				prefix: new HarmonyMethod(typeof(WorldScene), nameof(KeepVision)));
			harmony.Patch(AccessTools.Method(typeof(DOTween), nameof(DOTween.Clear)),
				prefix: new HarmonyMethod(typeof(WorldScene), nameof(KeepFrozenTweens)));
			harmony.Patch(AccessTools.Method(AccessTools.TypeByName("DG.Tweening.Core.TweenManager"), "AddActiveTween"),
				postfix: new HarmonyMethod(typeof(WorldScene), nameof(RecordTween)));
			SceneManager.sceneLoaded += OnLoaded;
			SceneManager.sceneUnloaded += OnUnloaded;
			Debug.Log("[Residency] routed " + routed + " native physics queries");
		}

		private static bool LoadScene(string __0, LoadSceneMode __1, ref AsyncOperation __result)
		{
			if (__0 != "Game")
			{
				return true;
			}
			LoadSceneMode mode = ColdLoading ? LoadSceneMode.Additive : __1;
			ColdLoading = false;
			__result = SceneManager.LoadSceneAsync(__0, new LoadSceneParameters(mode, LocalPhysicsMode.Physics2D | LocalPhysicsMode.Physics3D));
			return false;
		}

		private static IEnumerable<CodeInstruction> RouteQuery(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo getter2 = AccessTools.PropertyGetter(typeof(Physics2D), "defaultPhysicsScene");
			MethodInfo getter3 = AccessTools.PropertyGetter(typeof(Physics), "defaultPhysicsScene");
			int matches = 0;
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.Calls(getter2) || instruction.Calls(getter3))
				{
					instruction.operand = AccessTools.Method(typeof(WorldScene), instruction.Calls(getter2) ? nameof(Current2D) : nameof(Current3D));
					instruction.opcode = OpCodes.Call;
					matches++;
				}
				yield return instruction;
			}
			if (matches == 0)
			{
				throw new InvalidOperationException("Native physics query changed before routing");
			}
		}

		private static bool KeepVision() => !Suspending;

		private static bool KeepFrozenTweens() => !AnyFrozen;

		private static void RecordTween(Tween t)
		{
			Active?.Tweens.Add(t);
		}

		private static void OnLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name != "Game")
			{
				return;
			}
			var world = new WorldScene(scene);
			Worlds.Add(scene.handle, world);
			world.Select();
			PathfinderScope.ConstructingScene = 0;
		}

		private static void OnUnloaded(Scene scene)
		{
			if (!Worlds.Remove(scene.handle))
			{
				return;
			}
			if (ActiveHandle == scene.handle)
			{
				ActiveHandle = 0;
			}
			RoutineOwnership.ForgetScene(scene.handle);
			Unloaded?.Invoke(scene);
		}
	}

	// Steps only the active world's physics, after the game's own FixedUpdates.
	[DefaultExecutionOrder(10000)]
	internal sealed class ResidencyStepper : MonoBehaviour
	{
		private void FixedUpdate()
		{
			WorldScene world = WorldScene.Active;
			if (world == null || world.Frozen)
			{
				return;
			}
			if (!world.Physics2DScene.Simulate(Time.fixedDeltaTime))
			{
				throw new InvalidOperationException("Active world's 2D physics step failed");
			}
			world.Physics3DScene.Simulate(Time.fixedDeltaTime);
			world.Steps++;
		}
	}
}
