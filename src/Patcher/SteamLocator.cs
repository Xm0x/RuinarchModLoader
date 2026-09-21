using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Ruinarch.Modding.Patcher
{
	/// <summary>
	/// Finds a Ruinarch install by scanning Steam's library folders on Windows,
	/// Linux, and macOS. Ruinarch is Steam appid 909320, installed under
	/// steamapps/common/Ruinarch. Steam can spread games across several drives, so
	/// we read libraryfolders.vdf from every known Steam root and check each
	/// library for the game.
	/// </summary>
	public static class SteamLocator
	{
		private const string GameFolder = "Ruinarch";
		private const string GameExe = "Ruinarch.exe";

		/// <summary>Return the Ruinarch install root, or null if not found.</summary>
		public static string FindRuinarch()
		{
			foreach (string lib in LibraryFolders())
			{
				string candidate = Path.Combine(lib, "steamapps", "common", GameFolder);
				if (LooksLikeRuinarch(candidate))
				{
					return candidate;
				}
			}
			return null;
		}

		/// <summary>True if the folder contains a Ruinarch install we can patch.</summary>
		public static bool LooksLikeRuinarch(string dir)
		{
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
			{
				return false;
			}
			// The exe on Steam builds is Ruinarch.exe on every OS (Proton on Linux).
			if (File.Exists(Path.Combine(dir, GameExe)))
			{
				return true;
			}
			return Installer.ResolveManagedDir(dir) != null;
		}

		/// <summary>Every Steam library folder discovered across all Steam roots.</summary>
		public static IEnumerable<string> LibraryFolders()
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string root in SteamRoots())
			{
				if (!Directory.Exists(root) || !seen.Add(root))
				{
					continue;
				}
				yield return root; // the root is itself a library

				string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
				if (!File.Exists(vdf))
				{
					continue;
				}
				string text;
				try { text = File.ReadAllText(vdf); }
				catch { continue; }

				// Entries look like:  "path"   "D:\\SteamLibrary"
				foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
				{
					string path = m.Groups[1].Value.Replace("\\\\", "\\");
					if (seen.Add(path))
					{
						yield return path;
					}
				}
			}
		}

		/// <summary>Candidate Steam install roots for the current OS.</summary>
		private static IEnumerable<string> SteamRoots()
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

			if (OperatingSystem.IsWindows())
			{
				string reg = WindowsRegistrySteamPath();
				if (reg != null)
				{
					yield return reg;
				}
				foreach (string pf in new[]
				{
					Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
					Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				})
				{
					if (!string.IsNullOrEmpty(pf))
					{
						yield return Path.Combine(pf, "Steam");
					}
				}
			}
			else if (OperatingSystem.IsMacOS())
			{
				yield return Path.Combine(home, "Library", "Application Support", "Steam");
			}
			else // Linux and the rest
			{
				yield return Path.Combine(home, ".local", "share", "Steam");
				yield return Path.Combine(home, ".steam", "steam");
				yield return Path.Combine(home, ".steam", "root");
				// Flatpak Steam
				yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam",
					".local", "share", "Steam");
			}
		}

		private static string WindowsRegistrySteamPath()
		{
			if (!OperatingSystem.IsWindows())
			{
				return null;
			}
			try
			{
				// HKCU\Software\Valve\Steam : SteamPath
				using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
				return key?.GetValue("SteamPath") as string;
			}
			catch
			{
				return null;
			}
		}
	}
}
