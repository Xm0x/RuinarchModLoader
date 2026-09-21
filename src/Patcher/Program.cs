using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Ruinarch.Modding.Patcher
{
	/// <summary>
	/// Installs the Ruinarch mod loader into a copy of the game you own.
	///
	/// It never ships or touches game code of its own: it edits the IL of your
	/// existing Assembly-CSharp.dll to add one call to ModLoader.Initialize() in
	/// the assembly's module initializer, then copies the loader + Harmony next to
	/// it. The original is backed up as Assembly-CSharp.dll.orig so --uninstall is
	/// a clean revert.
	/// </summary>
	internal static class Program
	{
		private const string GameAssembly = "Assembly-CSharp.dll";
		private const string LoaderAssembly = "Ruinarch.Modding.dll";
		private const string LoaderAsmName = "Ruinarch.Modding";
		private const string LoaderType = "Ruinarch.Modding.ModLoader";
		private const string LoaderMethod = "Initialize";
		private const string Harmony = "0Harmony.dll";

		private static int Main(string[] args)
		{
			try
			{
				string game = GetOpt(args, "--game");
				bool uninstall = args.Contains("--uninstall");
				bool force = args.Contains("--force");

				if (string.IsNullOrEmpty(game))
				{
					return Usage("missing --game <Ruinarch install dir>");
				}

				string managed = ResolveManagedDir(game);
				if (managed == null)
				{
					return Fail($"could not find a Managed/ folder under '{game}'. " +
						"Point --game at your Ruinarch install root (the folder with Ruinarch.exe).");
				}

				string gameDll = Path.Combine(managed, GameAssembly);
				if (!File.Exists(gameDll))
				{
					return Fail($"{GameAssembly} not found in {managed}");
				}
				string backup = gameDll + ".orig";

				if (uninstall)
				{
					return Uninstall(managed, gameDll, backup);
				}

				return Install(managed, gameDll, backup, force);
			}
			catch (Exception e)
			{
				return Fail(e.ToString());
			}
		}

		private static int Install(string managed, string gameDll, string backup, bool force)
		{
			// The loader + Harmony must sit next to the patcher (release layout) or
			// be discoverable. Look next to this exe first, then CWD.
			string self = AppContext.BaseDirectory;
			string loaderSrc = FirstExisting(
				Path.Combine(self, LoaderAssembly),
				Path.Combine(Directory.GetCurrentDirectory(), LoaderAssembly));
			string harmonySrc = FirstExisting(
				Path.Combine(self, Harmony),
				Path.Combine(Directory.GetCurrentDirectory(), Harmony));

			if (loaderSrc == null)
			{
				return Fail($"{LoaderAssembly} not found next to the patcher. " +
					"Keep the release files together.");
			}
			if (harmonySrc == null)
			{
				return Fail($"{Harmony} not found next to the patcher. " +
					"Keep the release files together.");
			}

			// 1) Back up the untouched original once.
			if (!File.Exists(backup))
			{
				File.Copy(gameDll, backup);
				Info($"backed up original -> {Path.GetFileName(backup)}");
			}

			// 2) Put the loader in Managed/ (so the game assembly can reference it)
			//    and Harmony in Mods/ (resolved by the loader for mods).
			string loaderDst = Path.Combine(managed, LoaderAssembly);
			File.Copy(loaderSrc, loaderDst, overwrite: true);
			Info($"installed {LoaderAssembly} -> Managed/");

			string gameRoot = Directory.GetParent(managed)?.Parent?.FullName ?? managed;
			string modsDir = Path.Combine(gameRoot, "Mods");
			Directory.CreateDirectory(modsDir);
			File.Copy(harmonySrc, Path.Combine(modsDir, Harmony), overwrite: true);
			Info($"installed {Harmony} -> Mods/");

			// 3) Patch the game assembly. We always read from the pristine backup
			//    and write to the live dll, so re-running installs exactly one call
			//    and can never double-inject.
			var resolver = new DefaultAssemblyResolver();
			resolver.AddSearchDirectory(managed);

			using (var loaderMod = ModuleDefinition.ReadModule(loaderDst,
				new ReaderParameters { AssemblyResolver = resolver }))
			using (var game = ModuleDefinition.ReadModule(backup,
				new ReaderParameters { AssemblyResolver = resolver, InMemory = true }))
			{
				PatchModule(game, loaderMod);
				game.Write(gameDll);
			}

			return Ok("install complete. Drop mods into the Mods/ folder and launch the game.");
		}

		/// <summary>
		/// Add (or prepend to) the module initializer a call to
		/// Ruinarch.Modding.ModLoader.Initialize(). The module initializer is the
		/// static constructor on the special &lt;Module&gt; type; Mono runs it the
		/// instant the assembly is loaded, before any game type is touched.
		/// </summary>
		private static void PatchModule(ModuleDefinition game, ModuleDefinition loaderMod)
		{
			TypeDefinition loaderType = loaderMod.GetType(LoaderType)
				?? throw new Exception($"{LoaderType} not found in {LoaderAssembly}");
			MethodDefinition init = loaderType.Methods.FirstOrDefault(
				m => m.Name == LoaderMethod && m.IsStatic && m.Parameters.Count == 0)
				?? throw new Exception($"{LoaderType}.{LoaderMethod}() not found");

			MethodReference initRef = game.ImportReference(init);

			TypeDefinition moduleType = game.GetType("<Module>");
			if (moduleType == null)
			{
				throw new Exception("<Module> type not found in game assembly");
			}

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

			Info("injected ModLoader.Initialize() into the module initializer.");
		}

		private static int Uninstall(string managed, string gameDll, string backup)
		{
			if (!File.Exists(backup))
			{
				return Fail($"no backup ({Path.GetFileName(backup)}) found; nothing to restore.");
			}
			File.Copy(backup, gameDll, overwrite: true);
			File.Delete(backup);
			Info($"restored original {GameAssembly} and removed the backup.");

			string loaderDst = Path.Combine(managed, LoaderAssembly);
			if (File.Exists(loaderDst))
			{
				File.Delete(loaderDst);
				Info($"removed {LoaderAssembly} from Managed/.");
			}
			Info("left the Mods/ folder in place (delete it yourself if you want).");
			return Ok("uninstall complete.");
		}

		// --- helpers ---

		private static string ResolveManagedDir(string game)
		{
			if (Directory.Exists(game) &&
				File.Exists(Path.Combine(game, GameAssembly)))
			{
				return game; // pointed straight at Managed/
			}
			string candidate = Path.Combine(game, "Ruinarch_Data", "Managed");
			if (File.Exists(Path.Combine(candidate, GameAssembly)))
			{
				return candidate;
			}
			// Fall back to a search (handles odd layouts).
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

		private static string FirstExisting(params string[] paths)
			=> paths.FirstOrDefault(File.Exists);

		private static string GetOpt(string[] args, string name)
		{
			int i = Array.IndexOf(args, name);
			return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
		}

		private static void Info(string m) => Console.WriteLine($"[patcher] {m}");

		private static int Ok(string m)
		{
			Console.WriteLine($"[patcher] {m}");
			return 0;
		}

		private static int Fail(string m)
		{
			Console.Error.WriteLine($"[patcher] ERROR: {m}");
			return 1;
		}

		private static int Usage(string why)
		{
			Console.Error.WriteLine($"[patcher] {why}");
			Console.Error.WriteLine();
			Console.Error.WriteLine("Usage:");
			Console.Error.WriteLine("  RuinarchModLoader.Patcher --game <Ruinarch install dir> [--force]");
			Console.Error.WriteLine("  RuinarchModLoader.Patcher --game <Ruinarch install dir> --uninstall");
			Console.Error.WriteLine();
			Console.Error.WriteLine("Keep Ruinarch.Modding.dll and 0Harmony.dll next to the patcher.");
			return 2;
		}
	}
}
