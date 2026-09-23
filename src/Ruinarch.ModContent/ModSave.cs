using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UtilityScripts;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Per-save data for mods, stored inside the player's own save file.
	///
	/// A Ruinarch save is a zip of the game's temp folder (<c>Utilities.tempZipPath</c>), and
	/// loading extracts that zip back into <c>Utilities.tempPath</c>. The framework writes each
	/// registered mod's data as <c>ModData/&lt;id&gt;.json</c> into that folder just before the
	/// game saves, and reads it back once a save has finished loading. The data therefore
	/// travels with the save (copying or deleting a save does the same to the mod data), and
	/// the game itself ignores the extra file, so a save made with mods still loads without
	/// them.
	///
	/// Lifecycle, for every registered mod:
	/// - a game starts (new or loaded): <c>load(null)</c>, so no state leaks between games;
	/// - the player (or autosave) saves: <c>save()</c> is written; returning null writes nothing;
	/// - a save finishes loading, with the whole world in place: <c>load(json)</c>, or
	///   <c>load(null)</c> if that save has no data for this mod (made before it was installed).
	/// </summary>
	public static class ModSave
	{
		internal const string Folder = "ModData";

		private sealed class Handler
		{
			public Func<string> Save;
			public Action<string> Load;
		}

		private static readonly Dictionary<string, Handler> Handlers = new Dictionary<string, Handler>();

		/// <summary>
		/// Register a mod's per-save data. <paramref name="id"/> names the file inside the save
		/// (use your mod id plus a feature name, e.g. <c>mymod.knowledge</c>); it must be a valid
		/// file name and stay the same across versions, or old saves lose their data.
		/// Registering the same id again replaces the handlers.
		/// </summary>
		public static void Register(string id, Func<string> save, Action<string> load)
		{
			if (string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				throw new ArgumentException("ModSave id must be a non-empty valid file name: " + id);
			}
			if (save == null || load == null)
			{
				throw new ArgumentNullException(save == null ? "save" : "load");
			}
			ModContent.Install();
			Handlers[id] = new Handler { Save = save, Load = load };
		}

		internal static void ResetAll()
		{
			foreach (KeyValuePair<string, Handler> kv in Handlers)
			{
				Call(kv.Key, "reset", () => kv.Value.Load(null));
			}
		}

		internal static void WriteAll(string saveFolder)
		{
			string dir = Path.Combine(saveFolder, Folder);
			foreach (KeyValuePair<string, Handler> kv in Handlers)
			{
				Call(kv.Key, "save", () =>
				{
					string path = Path.Combine(dir, kv.Key + ".json");
					string json = kv.Value.Save();
					if (json == null)
					{
						// A folder left from an earlier save this session must not leak into this one.
						if (File.Exists(path))
						{
							File.Delete(path);
						}
						return;
					}
					Directory.CreateDirectory(dir);
					File.WriteAllText(path, json);
				});
			}
		}

		internal static void ReadAll(string loadFolder)
		{
			string dir = Path.Combine(loadFolder, Folder);
			foreach (KeyValuePair<string, Handler> kv in Handlers)
			{
				Call(kv.Key, "load", () =>
				{
					string path = Path.Combine(dir, kv.Key + ".json");
					kv.Value.Load(File.Exists(path) ? File.ReadAllText(path) : null);
				});
			}
		}

		// One mod's broken data never stops the game saving or loading, or the other mods.
		private static void Call(string id, string what, Action a)
		{
			try
			{
				a();
			}
			catch (Exception e)
			{
				Debug.LogError("[ModContent] ModSave " + what + " failed for '" + id + "': " + e);
			}
		}
	}

	// Every game (new or loaded) starts here, right after the game wipes its temp folder.
	[HarmonyPatch(typeof(Initializer), nameof(Initializer.InitializeDataBeforeWorldCreationMainThread))]
	internal static class Patch_ModSaveReset
	{
		private static void Postfix()
		{
			ModSave.ResetAll();
		}
	}

	// Manual saves and autosaves both start here; the game zips the temp folder a few frames later.
	[HarmonyPatch(typeof(SaveCurrentProgressManager), nameof(SaveCurrentProgressManager.DoManualSave))]
	internal static class Patch_ModSaveWrite
	{
		private static void Prefix()
		{
			ModSave.WriteAll(Utilities.tempZipPath);
		}
	}

	// Called once a saved world has fully loaded (StartupManager.PerformStartUpLoadGame),
	// while the extracted save is still in the temp folder.
	[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteSaveFilesInTempDirectory))]
	internal static class Patch_ModSaveRead
	{
		private static void Postfix()
		{
			ModSave.ReadAll(Utilities.tempPath);
		}
	}
}
