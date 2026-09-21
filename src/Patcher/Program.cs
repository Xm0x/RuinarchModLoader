using System;
using System.IO;
using System.Linq;

namespace Ruinarch.Modding.Patcher
{
	/// <summary>
	/// Command-line front-end for the Ruinarch mod-loader installer. The actual
	/// work lives in <see cref="Installer"/>, shared with the GUI. With no --game,
	/// it tries to auto-detect the Steam install.
	/// </summary>
	internal static class Program
	{
		private static int Main(string[] args)
		{
			bool uninstall = args.Contains("--uninstall");
			bool force = args.Contains("--force");
			string game = GetOpt(args, "--game") ?? SteamLocator.FindRuinarch();

			if (args.Contains("--detect"))
			{
				if (string.IsNullOrEmpty(game))
				{
					Console.WriteLine("not found");
					return 1;
				}
				Console.WriteLine(game);
				Console.WriteLine(Installer.IsInstalled(game) ? "installed" : "not installed");
				return 0;
			}

			if (string.IsNullOrEmpty(game))
			{
				return Usage("no --game given and Ruinarch was not found in any Steam library.");
			}
			if (Installer.ResolveManagedDir(game) == null)
			{
				Console.Error.WriteLine($"[patcher] ERROR: no Ruinarch install at '{game}'.");
				return 1;
			}

			Action<string> log = m => Console.WriteLine("[patcher] " + m);
			string[] srcDirs = { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

			bool ok = uninstall
				? Installer.Uninstall(game, log)
				: Installer.Install(game, force, log, srcDirs);
			return ok ? 0 : 1;
		}

		private static string GetOpt(string[] args, string name)
		{
			int i = Array.IndexOf(args, name);
			return (i >= 0 && i + 1 < args.Length) ? args[i + 1] : null;
		}

		private static int Usage(string why)
		{
			Console.Error.WriteLine($"[patcher] {why}");
			Console.Error.WriteLine();
			Console.Error.WriteLine("Usage:");
			Console.Error.WriteLine("  RuinarchModLoader.Patcher [--game <Ruinarch install dir>] [--force]");
			Console.Error.WriteLine("  RuinarchModLoader.Patcher [--game <Ruinarch install dir>] --uninstall");
			Console.Error.WriteLine();
			Console.Error.WriteLine("With no --game, the Steam libraries are scanned automatically.");
			Console.Error.WriteLine("Keep Ruinarch.Modding.dll and 0Harmony.dll next to the patcher.");
			return 2;
		}
	}
}
