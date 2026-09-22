using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Ruinarch.Modding.Patcher
{
	/// <summary>
	/// The mod-loader install/uninstall logic, shared by the CLI (Program.cs) and
	/// the GUI installer. It never ships or touches game code of its own: it edits
	/// the IL of your existing Assembly-CSharp.dll to add one call to
	/// ModLoader.Initialize() in the module initializer, then copies the loader +
	/// Harmony next to it. The original is backed up as Assembly-CSharp.dll.orig so
	/// Uninstall is a clean revert.
	///
	/// All output goes through a <c>log</c> callback so the GUI can show it in a
	/// text box and the CLI can write it to the console.
	/// </summary>
	public static class Installer
	{
		public const string GameAssembly = "Assembly-CSharp.dll";
		public const string LoaderAssembly = "Ruinarch.Modding.dll";
		public const string LoaderType = "Ruinarch.Modding.ModLoader";
		public const string LoaderMethod = "Initialize";
		public const string Harmony = "0Harmony.dll";
		public const string ContentFramework = "Ruinarch.ModContent.dll";

		/// <summary>
		/// Resolve the Managed/ directory that holds Assembly-CSharp.dll from a
		/// game path that may be the install root, the *_Data folder, or Managed/
		/// itself. Returns null if it cannot be found.
		/// </summary>
		public static string ResolveManagedDir(string game)
		{
			if (string.IsNullOrEmpty(game))
			{
				return null;
			}
			if (Directory.Exists(game) && File.Exists(Path.Combine(game, GameAssembly)))
			{
				return game; // pointed straight at Managed/
			}
			string candidate = Path.Combine(game, "Ruinarch_Data", "Managed");
			if (File.Exists(Path.Combine(candidate, GameAssembly)))
			{
				return candidate;
			}
			try
			{
				string hit = Directory.GetFiles(game, GameAssembly, SearchOption.AllDirectories)
					.FirstOrDefault();
				return hit != null ? Path.GetDirectoryName(hit) : null;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>True if the loader is currently installed in the given game.</summary>
		public static bool IsInstalled(string game)
		{
			string managed = ResolveManagedDir(game);
			if (managed == null)
			{
				return false;
			}
			return File.Exists(Path.Combine(managed, LoaderAssembly))
				|| File.Exists(Path.Combine(managed, GameAssembly) + ".orig");
		}

		/// <summary>The Mods/ folder for a resolved Managed/ directory.</summary>
		public static string ModsDirFor(string managed)
		{
			string gameRoot = Directory.GetParent(managed)?.Parent?.FullName ?? managed;
			return Path.Combine(gameRoot, "Mods");
		}

		/// <summary>
		/// Install the loader into the game at <paramref name="game"/>. The loader
		/// (Ruinarch.Modding.dll) and 0Harmony.dll are looked up in
		/// <paramref name="loaderSrcDirs"/> in order (typically: next to the app,
		/// then the current directory). Returns true on success.
		/// </summary>
		public static bool Install(string game, bool force, Action<string> log, params string[] loaderSrcDirs)
		{
			try
			{
				string managed = ResolveManagedDir(game);
				if (managed == null)
				{
					log($"ERROR: could not find Assembly-CSharp.dll under '{game}'. " +
						"Point at your Ruinarch install (the folder with Ruinarch.exe).");
					return false;
				}
				string gameDll = Path.Combine(managed, GameAssembly);
				string backup = gameDll + ".orig";

				string loaderSrc = FindNextTo(LoaderAssembly, loaderSrcDirs);
				string harmonySrc = FindNextTo(Harmony, loaderSrcDirs);
				if (loaderSrc == null)
				{
					log($"ERROR: {LoaderAssembly} not found next to the installer. Keep the release files together.");
					return false;
				}
				if (harmonySrc == null)
				{
					log($"ERROR: {Harmony} not found next to the installer. Keep the release files together.");
					return false;
				}

				if (IsInstalled(game) && !force)
				{
					log("Loader already installed; re-patching from the pristine backup (safe).");
				}

				// 1) Back up the untouched original once.
				if (!File.Exists(backup))
				{
					File.Copy(gameDll, backup);
					log($"Backed up original -> {Path.GetFileName(backup)}");
				}

				// 2) Loader in Managed/ (referenced by the game assembly),
				//    Harmony in Mods/ (resolved by the loader for mods).
				string loaderDst = Path.Combine(managed, LoaderAssembly);
				File.Copy(loaderSrc, loaderDst, overwrite: true);
				log($"Installed {LoaderAssembly} -> Managed/");

				string modsDir = ModsDirFor(managed);
				Directory.CreateDirectory(modsDir);
				File.Copy(harmonySrc, Path.Combine(modsDir, Harmony), overwrite: true);
				log($"Installed {Harmony} -> Mods/");

				// The content-injection framework (new structures/skills) ships in
				// Mods/ like Harmony. It is optional: a minimal package may omit it,
				// and mods that never call the ModContent API do not need it.
				string contentSrc = FindNextTo(ContentFramework, loaderSrcDirs);
				if (contentSrc != null)
				{
					File.Copy(contentSrc, Path.Combine(modsDir, ContentFramework), overwrite: true);
					log($"Installed {ContentFramework} -> Mods/");
				}
				else
				{
					log($"Note: {ContentFramework} not bundled; content mods that add new " +
						"structures/skills need it dropped into Mods/.");
				}

				// 3) Always read from the pristine backup and write the live dll, so
				//    re-running installs exactly one call and can never double-inject.
				var resolver = new DefaultAssemblyResolver();
				resolver.AddSearchDirectory(managed);
				using (var loaderMod = ModuleDefinition.ReadModule(loaderDst,
					new ReaderParameters { AssemblyResolver = resolver }))
				using (var gameMod = ModuleDefinition.ReadModule(backup,
					new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
				{
					PatchModule(gameMod, loaderMod, log);
					gameMod.Write(gameDll);
				}

				log("Install complete. Drop mods into the Mods/ folder and launch the game.");
				return true;
			}
			catch (Exception e)
			{
				log("ERROR: " + e.Message);
				return false;
			}
		}

		/// <summary>Restore the original assembly and remove the loader.</summary>
		public static bool Uninstall(string game, Action<string> log)
		{
			try
			{
				string managed = ResolveManagedDir(game);
				if (managed == null)
				{
					log($"ERROR: could not find Assembly-CSharp.dll under '{game}'.");
					return false;
				}
				string gameDll = Path.Combine(managed, GameAssembly);
				string backup = gameDll + ".orig";
				if (!File.Exists(backup))
				{
					log($"No backup ({Path.GetFileName(backup)}) found; nothing to restore.");
					return false;
				}
				File.Copy(backup, gameDll, overwrite: true);
				File.Delete(backup);
				log($"Restored original {GameAssembly} and removed the backup.");

				string loaderDst = Path.Combine(managed, LoaderAssembly);
				if (File.Exists(loaderDst))
				{
					File.Delete(loaderDst);
					log($"Removed {LoaderAssembly} from Managed/.");
				}
				log("Left the Mods/ folder in place (delete it yourself if you want).");
				log("Uninstall complete.");
				return true;
			}
			catch (Exception e)
			{
				log("ERROR: " + e.Message);
				return false;
			}
		}

		/// <summary>
		/// Add (or prepend to) the module initializer a call to
		/// Ruinarch.Modding.ModLoader.Initialize(). The module initializer is the
		/// static constructor on the special &lt;Module&gt; type; Mono runs it the
		/// instant the assembly is loaded, before any game type is touched.
		/// </summary>
		private static void PatchModule(ModuleDefinition game, ModuleDefinition loaderMod, Action<string> log)
		{
			TypeDefinition loaderType = loaderMod.GetType(LoaderType)
				?? throw new Exception($"{LoaderType} not found in {LoaderAssembly}");
			MethodDefinition init = loaderType.Methods.FirstOrDefault(
				m => m.Name == LoaderMethod && m.IsStatic && m.Parameters.Count == 0)
				?? throw new Exception($"{LoaderType}.{LoaderMethod}() not found");

			MethodReference initRef = game.ImportReference(init);

			TypeDefinition moduleType = game.GetType("<Module>")
				?? throw new Exception("<Module> type not found in game assembly");

			MethodDefinition cctor = moduleType.Methods.FirstOrDefault(m => m.Name == ".cctor");
			if (cctor == null)
			{
				cctor = new MethodDefinition(".cctor",
					MethodAttributes.Static | MethodAttributes.SpecialName |
					MethodAttributes.RTSpecialName | MethodAttributes.Private,
					game.TypeSystem.Void);
				moduleType.Methods.Add(cctor);
				ILProcessor il = cctor.Body.GetILProcessor();
				il.Append(il.Create(OpCodes.Call, initRef));
				il.Append(il.Create(OpCodes.Ret));
			}
			else
			{
				ILProcessor il = cctor.Body.GetILProcessor();
				Instruction first = cctor.Body.Instructions[0];
				il.InsertBefore(first, il.Create(OpCodes.Call, initRef));
			}

			log("Injected ModLoader.Initialize() into the module initializer.");
		}

		private static string FindNextTo(string file, string[] dirs)
		{
			foreach (string d in dirs)
			{
				if (string.IsNullOrEmpty(d))
				{
					continue;
				}
				string p = Path.Combine(d, file);
				if (File.Exists(p))
				{
					return p;
				}
			}
			return null;
		}
	}
}
