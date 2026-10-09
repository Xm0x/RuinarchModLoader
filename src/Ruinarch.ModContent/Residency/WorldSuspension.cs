using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Pathfinding;
using UnityEngine;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Freezes one world without unloading it: its scripts, cameras, lights, animators,
	/// canvases and renderers are disabled, its particles, audio and tweens paused, and its
	/// pathfinder workers held. Colliders and rigidbodies are left registered (disabling them
	/// is what made earlier experiments stall for seconds). <see cref="Resume"/> restores
	/// exactly what was suspended.
	/// </summary>
	internal sealed class WorldSuspension
	{
		private readonly WorldScene world;
		private readonly Behaviour[] behaviours;
		private readonly Renderer[] renderers;
		private readonly ParticleSystem[] particles;
		private readonly AudioSource[] audio;
		private readonly Collider2D[] colliders;
		private readonly Rigidbody2D[] bodies;
		private readonly bool[] simulated;
		private readonly List<Tween> tweens = new List<Tween>();
		private readonly Dictionary<Animator, bool> animatorFlags = new Dictionary<Animator, bool>();
		private readonly int steps;
		private readonly PathProcessor.GraphUpdateLock pathLock;

		internal WorldSuspension(WorldScene world, GameObject[] roots)
		{
			this.world = world ?? throw new InvalidOperationException("No active world to freeze");
			Component[] all = roots.SelectMany(r => r.GetComponentsInChildren<Component>(true)).ToArray();
			behaviours = all.OfType<Behaviour>().Where(b => b.enabled && b.gameObject.activeInHierarchy
				&& (b is MonoBehaviour || b is Camera || b is Light || b is Animator || b is Canvas)).ToArray();
			renderers = all.OfType<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
			particles = all.OfType<ParticleSystem>().Where(p => p.isPlaying).ToArray();
			audio = all.OfType<AudioSource>().Where(a => a.isPlaying).ToArray();
			colliders = all.OfType<Collider2D>().Where(c => c.enabled && c.gameObject.activeInHierarchy).ToArray();
			bodies = all.OfType<Rigidbody2D>().ToArray();
			simulated = bodies.Select(b => b.simulated).ToArray();
			AstarPath.active.FlushGraphUpdates();
			AstarPath.active.FlushWorkItems();
			pathLock = AstarPath.active.PausePathfinding();
			steps = world.Steps;
			world.Frozen = true;
			WorldScene.ActiveHandle = 0;
			foreach (Tween tween in world.Tweens)
			{
				if (tween.IsActive() && tween.IsPlaying())
				{
					tweens.Add(tween);
					tween.Pause();
				}
			}
			foreach (ParticleSystem p in particles)
			{
				p.Pause(false);
			}
			foreach (AudioSource a in audio)
			{
				a.Pause();
			}
			WorldScene.Suspending = true;
			try
			{
				foreach (Animator animator in behaviours.OfType<Animator>())
				{
					animatorFlags.Add(animator, animator.keepAnimatorControllerStateOnDisable);
					animator.keepAnimatorControllerStateOnDisable = true;
				}
				foreach (Behaviour b in behaviours)
				{
					b.enabled = false;
				}
				foreach (Renderer r in renderers)
				{
					r.enabled = false;
				}
			}
			finally
			{
				WorldScene.Suspending = false;
			}
			Verify();
		}

		internal WorldScene World => world;

		/// <summary>Throws when the frozen world advanced or lost a registration.</summary>
		internal void Verify()
		{
			if (!world.Frozen || world.Steps != steps)
			{
				throw new InvalidOperationException("Frozen world's physics advanced");
			}
			if (!pathLock.Held)
			{
				throw new InvalidOperationException("Frozen world's pathfinder workers were released");
			}
			foreach (Collider2D collider in colliders)
			{
				if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
				{
					throw new InvalidOperationException("Freezing removed a collider");
				}
			}
			for (int i = 0; i < bodies.Length; i++)
			{
				if (bodies[i] == null || bodies[i].simulated != simulated[i])
				{
					throw new InvalidOperationException("Freezing changed a rigidbody registration");
				}
			}
		}

		/// <summary>
		/// Before unloading this frozen world: let its pathfinder shut its worker threads down.
		/// The world stays disabled; it must not be resumed afterwards.
		/// </summary>
		internal void ReleaseForUnload()
		{
			pathLock.Release();
		}

		internal void Resume()
		{
			Verify();
			world.Select();
			foreach (Behaviour b in behaviours)
			{
				if (b != null)
				{
					b.enabled = true;
				}
			}
			foreach (KeyValuePair<Animator, bool> entry in animatorFlags)
			{
				if (entry.Key != null)
				{
					entry.Key.keepAnimatorControllerStateOnDisable = entry.Value;
				}
			}
			foreach (Renderer r in renderers)
			{
				if (r != null)
				{
					r.enabled = true;
				}
			}
			foreach (ParticleSystem p in particles)
			{
				if (p != null)
				{
					p.Play(false);
				}
			}
			foreach (AudioSource a in audio)
			{
				if (a != null)
				{
					a.UnPause();
				}
			}
			foreach (Tween tween in tweens)
			{
				if (tween.IsActive())
				{
					tween.Play();
				}
			}
			pathLock.Release();
		}
	}
}
