using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Travel between two native saves while keeping the world being left frozen in memory,
	/// so coming back to it does not reload it. At most one world is retained; travelling to a
	/// third save unloads it. See docs/specs/2026-10-09-residency-core-design.md.
	/// </summary>
	public static class Residency
	{
		internal const string HarmonyId = "ruinarch.modcontent.residency";

		private const double LoadTimeoutSeconds = 900;

		private sealed class World
		{
			internal string SavePath;
			internal WorldScene Scene;
			internal WorldSuspension Suspension;
			internal WorldState State;
			internal Dictionary<string, string> Mods;
		}

		private static bool _enabled;
		private static World _retained;

		internal static ResidencyHost Host;

		/// <summary>The save the frozen world was loaded from, or null when none is retained.</summary>
		public static string RetainedSavePath => _retained?.SavePath;

		public static bool IsTravelling { get; private set; }

		/// <summary>
		/// Idempotent. Installs the residency patches; call it from your mod's <c>OnLoad</c>,
		/// before the first game scene loads. <see cref="ModContent.Install"/> never installs
		/// them, so players whose mods do not call this get no residency patches.
		/// </summary>
		public static void Enable()
		{
			if (_enabled)
			{
				return;
			}
			_enabled = true;
			var harmony = new Harmony(HarmonyId);
			PathfinderScope.Install(harmony);
			RoutineOwnership.Install(harmony);
			WorldScene.Install(harmony);
			// A normal load (main menu, Load Game) unloads every scene, the frozen one included.
			WorldScene.Unloaded += scene =>
			{
				if (_retained != null && _retained.Scene.Scene.handle == scene.handle)
				{
					_retained = null;
				}
			};
			var go = new GameObject("Ruinarch.ModContent residency");
			UnityEngine.Object.DontDestroyOnLoad(go);
			Host = go.AddComponent<ResidencyHost>();
			go.AddComponent<ResidencyStepper>();
			Debug.Log("[Residency] enabled");
		}

		/// <summary>
		/// Travel to a native save. If <paramref name="savePath"/> is the retained world, switch
		/// back to it; otherwise freeze the current world and load <paramref name="savePath"/>
		/// beside it. Save the world you are leaving first: travel never saves.
		/// <paramref name="done"/> is always called, on the main thread.
		/// </summary>
		public static void Travel(string savePath, Action<TravelResult> done)
		{
			if (done == null)
			{
				throw new ArgumentNullException(nameof(done));
			}
			bool warm = _retained != null && SamePath(_retained.SavePath, savePath);
			string refusal = Refusal(savePath);
			if (refusal != null)
			{
				Debug.Log("[Residency] travel refused: " + refusal);
				done(new TravelResult(false, TravelStage.Validate, refusal, warm, 0));
				return;
			}
			IsTravelling = true;
			Host.StartCoroutine(Run(savePath, warm, done));
		}

		private static string Refusal(string savePath)
		{
			if (!_enabled)
			{
				return "Residency.Enable was not called";
			}
			if (IsTravelling)
			{
				return "a travel is already running";
			}
			if (GameManager.Instance == null || !GameManager.Instance.gameHasStarted)
			{
				return "no game is running";
			}
			WorldScene active = WorldScene.Active;
			if (active == null)
			{
				return "the active world was loaded before Residency.Enable";
			}
			if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath))
			{
				return "save not found: " + savePath;
			}
			SaveCurrentProgressManager manager = SaveManager.Instance.saveCurrentProgressManager;
			if (SamePath(manager.currentSaveDataPath, savePath))
			{
				return "that save is the active world";
			}
			if (manager.isSaving || manager.isWritingToDisk)
			{
				return "the game is saving";
			}
			if (RoutineOwnership.NamedStarts(active.Scene.handle) > 0)
			{
				return "a game script started a coroutine by name, which cannot be paused";
			}
			foreach (GameObject root in active.Scene.GetRootGameObjects())
			{
				foreach (MonoBehaviour b in root.GetComponentsInChildren<MonoBehaviour>(true))
				{
					if (b != null && b.IsInvoking())
					{
						return "a pending Invoke on " + b.GetType().FullName + " cannot be paused";
					}
				}
			}
			return null;
		}

		private static IEnumerator Run(string savePath, bool warm, Action<TravelResult> done)
		{
			var watch = Stopwatch.StartNew();
			World target = warm ? _retained : null;
			World source = null;
			int coldScene = 0;
			using (var guard = new TransitionGuard())
			{
				string failure = null;
				string detail = null;
				IEnumerator steps = Steps(savePath, warm, guard, s => source = s, h => coldScene = h);
				while (true)
				{
					object current;
					try
					{
						guard.KeepLogging();
						if (!steps.MoveNext())
						{
							break;
						}
						current = steps.Current;
						if (guard.Errors > 0)
						{
							throw new InvalidOperationException("error logged during travel: " + guard.FirstError);
						}
					}
					catch (Exception e)
					{
						failure = e.Message;
						detail = e.ToString();
						break;
					}
					yield return current;
				}
				if (failure == null && guard.Errors > 0)
				{
					failure = "error logged during travel: " + guard.FirstError;
				}
				if (failure == null)
				{
					_retained = source;
					double seconds = watch.Elapsed.TotalSeconds;
					Debug.Log("[Residency] " + (warm ? "warm" : "cold") + " travel ready in " + seconds.ToString("F3") + " s; retained " + source.SavePath);
					if (warm)
					{
						ModStateSwap.CheckRoundTrip(target.Mods);
					}
					IsTravelling = false;
					done(new TravelResult(true, TravelStage.None, null, warm, seconds));
					yield break;
				}
				TravelStage failed = guard.Stage;
				Debug.LogError("[Residency] travel failed at " + failed + ": " + (detail ?? failure));
				IEnumerator back = Rollback(source, target, coldScene);
				while (true)
				{
					bool next;
					try
					{
						guard.KeepLogging();
						next = back.MoveNext();
					}
					catch (Exception e)
					{
						Debug.LogError("[Residency] rollback failed: " + e);
						failed = TravelStage.Rollback;
						failure += "; rollback failed: " + e.Message;
						break;
					}
					if (!next)
					{
						break;
					}
					yield return back.Current;
				}
				IsTravelling = false;
				done(new TravelResult(false, failed, failure, warm, watch.Elapsed.TotalSeconds));
			}
		}

		private static IEnumerator Steps(string savePath, bool warm, TransitionGuard guard, Action<World> frozen, Action<int> loading)
		{
			guard.Stage = TravelStage.Freeze;
			WorldScene active = WorldScene.Active;
			var source = new World
			{
				SavePath = SaveManager.Instance.saveCurrentProgressManager.currentSaveDataPath,
				Scene = active,
				Mods = ModStateSwap.Capture()
			};
			GameObject[] roots = active.Scene.GetRootGameObjects();
			source.Suspension = new WorldSuspension(active, roots);
			frozen(source);
			source.State = WorldState.Capture(roots);
			if (!warm && _retained != null)
			{
				yield return Evict(source);
			}
			ModStateSwap.Reset();
			Stage(guard, "freeze");
			if (warm)
			{
				guard.Stage = TravelStage.Activate;
				_retained.State.Apply();
				_retained.Suspension.Resume();
				ModStateSwap.Restore(_retained.Mods);
				SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(_retained.SavePath);
				Stage(guard, "activate");
			}
			else
			{
				guard.Stage = TravelStage.Load;
				// Detaching clears the frozen world's scene-owned singletons (UIManager among them):
				// take everything the load needs first.
				OptionsMenu options = UIManager.Instance.optionsMenu;
				GameManager previous = source.State.Manager;
				source.State.DetachForColdLoad();
				WorldScene.ColdLoading = true;
				SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(savePath);
				options.LoadSave();
				var deadline = Stopwatch.StartNew();
				while (!(GameManager.Instance != null && GameManager.Instance != previous && GameManager.Instance.gameHasStarted
					&& !LevelLoaderManager.Instance.IsLoadingScreenActive()))
				{
					if (WorldScene.Active != null && WorldScene.Active != source.Scene)
					{
						loading(WorldScene.Active.Scene.handle);
					}
					if (deadline.Elapsed.TotalSeconds > LoadTimeoutSeconds)
					{
						throw new TimeoutException("native load did not finish in " + LoadTimeoutSeconds + " s");
					}
					yield return null;
				}
				if (WorldScene.Active == null || WorldScene.Active == source.Scene)
				{
					throw new InvalidOperationException("native load finished without a new resident world");
				}
				loading(WorldScene.Active.Scene.handle);
				Stage(guard, "load");
			}
			guard.Stage = TravelStage.Ready;
			source.Suspension.Verify();
			Pathfinding.GridGraph grid = AstarPath.active?.data.graphs.OfType<Pathfinding.GridGraph>().FirstOrDefault();
			if (grid != null && !ReferenceEquals(Pathfinding.GridNode.GetGridGraph(grid.graphIndex), grid))
			{
				throw new InvalidOperationException("A* grid registry points at another world's graph");
			}
		}

		private static void Stage(TransitionGuard guard, string name)
		{
			Debug.Log("[Residency] stage " + name + " done");
			if (guard.Errors > 0)
			{
				throw new InvalidOperationException("error logged during " + name + ": " + guard.FirstError);
			}
		}

		// The retained world is destroyed as the current world (its own pathfinder, graph registry
		// and singletons), while the source is already frozen, so its OnDestroy code cleans up its
		// own state and nothing else runs during the unload. Its captured mod state is discarded.
		private static IEnumerator Evict(World source)
		{
			World old = _retained;
			_retained = null;
			old.State.Apply();
			old.Suspension.ReleaseForUnload();
			AsyncOperation unload = SceneManager.UnloadSceneAsync(old.Scene.Scene);
			while (!unload.isDone)
			{
				yield return null;
			}
			source.State.Apply();
			Debug.Log("[Residency] evicted retained world " + old.SavePath);
		}

		// Put the source back as it was. A retained world that was being switched to is frozen again.
		private static IEnumerator Rollback(World source, World target, int coldScene)
		{
			WorldScene.ColdLoading = false;
			if (source == null)
			{
				yield break;
			}
			if (coldScene != 0 && coldScene != source.Scene.Scene.handle)
			{
				Scene partial = FindScene(coldScene);
				if (partial.IsValid() && partial.isLoaded)
				{
					AsyncOperation unload = SceneManager.UnloadSceneAsync(partial);
					while (!unload.isDone)
					{
						yield return null;
					}
				}
			}
			if (target != null && !target.Scene.Frozen)
			{
				// The warm target had already resumed: freeze it again (with its current mod state) so it stays retained.
				target.Mods = ModStateSwap.Capture();
				target.Suspension = new WorldSuspension(target.Scene, target.Scene.Scene.GetRootGameObjects());
				target.State = WorldState.Capture(target.Scene.Scene.GetRootGameObjects());
				ModStateSwap.Reset();
			}
			if (source.State != null)
			{
				source.State.Apply();
			}
			source.Suspension.Resume();
			ModStateSwap.Restore(source.Mods);
			SaveManager.Instance.saveCurrentProgressManager.SetCurrentSaveDataPath(source.SavePath);
			if (LevelLoaderManager.Instance != null)
			{
				LevelLoaderManager.Instance.SetLoadingState(false);
			}
			Debug.Log("[Residency] rolled back to " + source.SavePath);
		}

		// Unity 2020 has no public SceneManager.GetSceneByHandle.
		private static Scene FindScene(int handle)
		{
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene s = SceneManager.GetSceneAt(i);
				if (s.handle == handle)
				{
					return s;
				}
			}
			return default(Scene);
		}

		private static bool SamePath(string a, string b)
		{
			if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
			{
				return false;
			}
			return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
		}
	}

	// Runs travel coroutines; lives outside every world, so it is never frozen.
	internal sealed class ResidencyHost : MonoBehaviour
	{
	}
}
