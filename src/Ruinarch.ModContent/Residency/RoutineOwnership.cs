using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Coroutines started by "Game"-scene behaviours are wrapped so they make no progress while
	/// their world is frozen, including their <c>WaitForSeconds</c> timers. Coroutines started by
	/// name cannot be wrapped; they are counted and make their world refuse to travel.
	/// </summary>
	internal static class RoutineOwnership
	{
		private static readonly Dictionary<IEnumerator, Owned> Running = new Dictionary<IEnumerator, Owned>();
		private static readonly Dictionary<int, int> Named = new Dictionary<int, int>();
		private static readonly FieldInfo WaitSeconds = AccessTools.Field(typeof(WaitForSeconds), "m_Seconds");

		internal static void Install(Harmony harmony)
		{
			if (WaitSeconds == null)
			{
				throw new InvalidOperationException("Native WaitForSeconds duration field unavailable");
			}
			harmony.Patch(AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new[] { typeof(IEnumerator) }),
				prefix: new HarmonyMethod(typeof(RoutineOwnership), nameof(Start)));
			harmony.Patch(AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StopCoroutine), new[] { typeof(IEnumerator) }),
				prefix: new HarmonyMethod(typeof(RoutineOwnership), nameof(Stop)));
			harmony.Patch(AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.StartCoroutine), new[] { typeof(string), typeof(object) }),
				prefix: new HarmonyMethod(typeof(RoutineOwnership), nameof(CountNamed)));
		}

		internal static int NamedStarts(int sceneHandle) => Named.TryGetValue(sceneHandle, out int n) ? n : 0;

		internal static void ForgetScene(int handle)
		{
			Named.Remove(handle);
			var remove = new List<IEnumerator>();
			foreach (KeyValuePair<IEnumerator, Owned> pair in Running)
			{
				if (pair.Value.Scene == handle)
				{
					remove.Add(pair.Key);
				}
			}
			foreach (IEnumerator routine in remove)
			{
				Running.Remove(routine);
			}
		}

		private static bool Belongs(MonoBehaviour owner) => owner != null && owner.gameObject.scene.name == "Game";

		private static void Start(MonoBehaviour __instance, ref IEnumerator routine)
		{
			if (!Belongs(__instance) || routine is Owned || routine == null)
			{
				return;
			}
			var owned = new Owned(__instance, routine);
			Running[routine] = owned;
			routine = owned;
		}

		private static void Stop(ref IEnumerator routine)
		{
			if (routine != null && Running.TryGetValue(routine, out Owned owned))
			{
				routine = owned;
			}
		}

		private static void CountNamed(MonoBehaviour __instance)
		{
			if (!Belongs(__instance))
			{
				return;
			}
			int handle = __instance.gameObject.scene.handle;
			Named[handle] = NamedStarts(handle) + 1;
		}

		private sealed class Owned : IEnumerator, IDisposable
		{
			private readonly MonoBehaviour owner;
			private readonly IEnumerator inner;
			internal readonly int Scene;
			private object current;
			private float remaining;
			private bool waiting;
			private bool realtime;
			private CustomYieldInstruction custom;

			internal Owned(MonoBehaviour owner, IEnumerator inner)
			{
				this.owner = owner;
				this.inner = inner;
				Scene = owner.gameObject.scene.handle;
			}

			public object Current => current;

			public bool MoveNext()
			{
				if (owner == null)
				{
					Dispose();
					return false;
				}
				if (!WorldScene.IsActive(owner))
				{
					current = null;
					return true;
				}
				if (waiting)
				{
					remaining -= realtime ? Time.unscaledDeltaTime : Time.deltaTime;
					if (remaining > 0f)
					{
						current = null;
						return true;
					}
					waiting = false;
				}
				if (custom != null)
				{
					if (custom.keepWaiting)
					{
						current = null;
						return true;
					}
					custom = null;
				}
				if (!inner.MoveNext())
				{
					Dispose();
					return false;
				}
				object value = inner.Current;
				if (value is WaitForSeconds seconds)
				{
					remaining = (float)WaitSeconds.GetValue(seconds);
					realtime = false;
					waiting = true;
					current = null;
				}
				else if (value is WaitForSecondsRealtime real)
				{
					remaining = real.waitTime;
					realtime = true;
					waiting = true;
					current = null;
				}
				else if (value is CustomYieldInstruction condition)
				{
					custom = condition;
					current = null;
				}
				else if (value is IEnumerator child)
				{
					current = new Owned(owner, child);
				}
				else
				{
					current = value;
				}
				return true;
			}

			public void Reset() => throw new NotSupportedException();

			public void Dispose()
			{
				Running.Remove(inner);
				(inner as IDisposable)?.Dispose();
			}
		}
	}
}
