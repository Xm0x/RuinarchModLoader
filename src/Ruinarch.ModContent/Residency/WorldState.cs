using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UtilityScripts;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// The process-wide state one world owns: singleton statics, persistent managers, signal
	/// registries, Unity's random state, and three pieces of shared state proven to leak
	/// between resident worlds (A* graph registries, world settings, portrait usage).
	/// <see cref="Capture"/> records it, <see cref="Apply"/> puts it back.
	/// </summary>
	internal sealed class WorldState
	{
		private sealed class Slot
		{
			internal FieldInfo Field;
			internal object Owner;
			internal object Value;

			internal void Apply() => Field.SetValue(Owner, Value);
		}

		private readonly List<Slot> slots = new List<Slot>();
		private readonly List<Slot> signals = new List<Slot>();
		private readonly HashSet<GameObject> roots;
		private UnityEngine.Random.State random;

		internal readonly GameManager Manager;

		private WorldState(GameObject[] roots)
		{
			this.roots = new HashSet<GameObject>(roots);
			Manager = GameManager.Instance;
		}

		internal static WorldState Capture(GameObject[] roots)
		{
			var state = new WorldState(roots);
			state.random = UnityEngine.Random.state;
			state.CaptureSingletons();
			state.CaptureGraphRegistries();
			state.CapturePortraits();
			state.CaptureSignals();
			return state;
		}

		private void CaptureSingletons()
		{
			var types = new HashSet<Type>();
			foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(GameManager).Assembly))
			{
				if (!typeof(MonoBehaviour).IsAssignableFrom(type))
				{
					continue;
				}
				FieldInfo field = type.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
				if (field?.GetValue(null) is Component component && component != null)
				{
					types.Add(type);
				}
			}
			types.Add(typeof(BaseParticleEffect));
			types.Add(typeof(Utilities));
			types.Add(typeof(AstarPath));
			Type province = AccessTools.TypeByName("TruePlanet.ProvinceGame");
			if (province != null)
			{
				types.Add(province);
			}
			foreach (Type type in types)
			{
				CaptureStatic(type);
			}
			CaptureInstance(SaveManager.Instance, false);
			CaptureInstance(SaveManager.Instance.saveCurrentProgressManager, true);
			CaptureInstance(DatabaseManager.Instance, false);
			CaptureInstance(WorldSettings.Instance, false);
			CaptureInstance(WorldConfigManager.Instance, false);
			CaptureInstance(Ruinarch.InputManager.Instance, true);
			CaptureInstance(PlayerSkillManager.Instance, false);
			foreach (SkillData skill in PlayerSkillManager.Instance.allPlayerSkillsData.Values)
			{
				CaptureInstance(skill, true);
				CaptureInstance(skill.skillEventDispatcher, true);
			}
			foreach (object skill in PlayerSkillManager.Instance.passiveSkillsData.Values)
			{
				CaptureInstance(skill, true);
			}
			foreach (object loadout in PlayerSkillManager.Instance.allSkillLoadouts.Values)
			{
				CaptureInstance(loadout, true);
			}
		}

		// A* resolves grid nodes through process-wide arrays indexed by graph index and filled in
		// place by GridNode.SetGridGraph. Both worlds use index 0: without a copy per world, the
		// returning world's paths walk the other world's nodes and its path threads die.
		private void CaptureGraphRegistries()
		{
			foreach (Type type in new[] { typeof(Pathfinding.GridNode), typeof(Pathfinding.LevelGridNode), typeof(Pathfinding.TriangleMeshNode) })
			{
				foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
				{
					if (!field.IsLiteral && !field.IsInitOnly && field.FieldType.IsArray)
					{
						slots.Add(new Slot { Field = field, Value = CopyCollection(field.GetValue(null)) });
					}
				}
			}
		}

		// Portrait usage lives on collections owned by ExternalFileManager (DontDestroyOnLoad):
		// a load replaces each array and play edits it in place.
		private void CapturePortraits()
		{
			var portraits = new List<CharacterPortraitSpriteCollection>();
			CharacterManager.Instance.portraitCollection.PopulateAllSpriteCollection(portraits);
			FieldInfo availability = AccessTools.Field(typeof(CharacterPortraitSpriteCollection), "_portraitAvailability");
			foreach (CharacterPortraitSpriteCollection collection in portraits)
			{
				slots.Add(new Slot { Field = availability, Owner = collection, Value = CopyCollection(availability.GetValue(collection)) });
			}
		}

		// Native signal registries (SignalHandler<...>._handles) are found through their
		// sceneUnloaded cleanup subscriptions.
		private void CaptureSignals()
		{
			FieldInfo sceneEvent = AccessTools.Field(typeof(SceneManager), "sceneUnloaded");
			if (sceneEvent == null)
			{
				throw new InvalidOperationException("Native scene event storage unavailable");
			}
			var handlers = (Delegate)sceneEvent.GetValue(null);
			IEnumerable<Type> types = handlers.GetInvocationList().Select(d => d.Method.DeclaringType)
				.Where(t => t != null && t.Name.StartsWith("SignalHandler", StringComparison.Ordinal)).Distinct();
			foreach (Type type in types)
			{
				FieldInfo field = AccessTools.Field(type, "_handles");
				signals.Add(new Slot { Field = field, Value = CopyHandles((IDictionary)field.GetValue(null), d => true) });
			}
			if (signals.Count == 0)
			{
				throw new InvalidOperationException("No native signal registries found");
			}
		}

		/// <summary>
		/// Before a cold load beside this (frozen) world: drop its scene-owned singletons and
		/// listeners so the loading world registers its own.
		/// </summary>
		internal void DetachForColdLoad()
		{
			foreach (Slot slot in slots)
			{
				if (slot.Owner == null && slot.Value is Component component && component != null && Owns(component))
				{
					slot.Field.SetValue(null, null);
				}
			}
			AccessTools.Field(typeof(Ruinarch.InputManager), "m_onUpdateEvent").SetValue(null, null);
			IsolateWorldSettings();
			foreach (Slot slot in signals)
			{
				slot.Field.SetValue(null, CopyHandles((IDictionary)slot.Value, d => d.Target is Component c && c != null && !Owns(c)));
			}
		}

		// Initializer randomizes cultist thresholds on the current settings object before the
		// loaded save's settings replace it; the loading world must not write into ours.
		private void IsolateWorldSettings()
		{
			WorldSettings.Instance.SetWorldSettingsData(new WorldSettingsData());
		}

		/// <summary>Make this world's captured state the process-wide state again.</summary>
		internal void Apply()
		{
			foreach (Slot slot in slots)
			{
				slot.Apply();
			}
			foreach (Slot slot in signals)
			{
				slot.Field.SetValue(null, CopyHandles((IDictionary)slot.Value, d => true));
			}
			UnityEngine.Random.state = random;
			if (GameManager.Instance != Manager)
			{
				throw new InvalidOperationException("World state did not reactivate its GameManager");
			}
		}

		private bool Owns(Component component) => roots.Contains(component.transform.root.gameObject);

		private void CaptureStatic(Type type)
		{
			foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
			{
				if (!field.IsLiteral && !field.IsInitOnly && !field.IsDefined(typeof(ThreadStaticAttribute), false))
				{
					slots.Add(new Slot { Field = field, Value = field.GetValue(null) });
				}
			}
		}

		private void CaptureInstance(object owner, bool copyCollections)
		{
			if (owner == null)
			{
				return;
			}
			for (Type type = owner.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(UnityEngine.Object) && type != typeof(object); type = type.BaseType)
			{
				foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
				{
					object value = field.GetValue(owner);
					slots.Add(new Slot { Field = field, Owner = owner, Value = copyCollections ? CopyCollection(value) : value });
				}
			}
		}

		private static object CopyCollection(object value)
		{
			if (value is Array array)
			{
				return array.Clone();
			}
			if (value is IDictionary dictionary)
			{
				var copy = (IDictionary)Activator.CreateInstance(value.GetType());
				foreach (DictionaryEntry pair in dictionary)
				{
					copy.Add(pair.Key, pair.Value);
				}
				return copy;
			}
			if (value is IList list)
			{
				var copy = (IList)Activator.CreateInstance(value.GetType());
				foreach (object entry in list)
				{
					copy.Add(entry);
				}
				return copy;
			}
			return value;
		}

		private static object CopyHandles(IDictionary handles, Func<Delegate, bool> include)
		{
			var copy = (IDictionary)Activator.CreateInstance(handles.GetType());
			foreach (DictionaryEntry pair in handles)
			{
				var listeners = (IList)Activator.CreateInstance(pair.Value.GetType());
				foreach (Delegate listener in (IEnumerable)pair.Value)
				{
					if (include(listener))
					{
						listeners.Add(listener);
					}
				}
				copy.Add(pair.Key, listeners);
			}
			return copy;
		}
	}
}
