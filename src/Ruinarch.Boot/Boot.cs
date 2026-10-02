using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace Ruinarch.Boot
{
	/// <summary>
	/// First code the game runs (injected into its module initializer). Applies a staged
	/// loader update, then starts the loader by name, so the loader file is not yet open
	/// while it is replaced. Never throws into the game.
	/// </summary>
	public static class Boot
	{
		/// <summary>Boot step capabilities. A release whose manifest needs more requires the installer.</summary>
		public const int Version = 1;
		public const string Loader = "Ruinarch.Modding.dll";

		public static void Initialize()
		{
			try { ApplyStaged(); }
			catch (Exception e) { Result("failed " + e.Message); }
			try
			{
				Assembly.Load("Ruinarch.Modding").GetType("Ruinarch.Modding.ModLoader", true).GetMethod("Initialize").Invoke(null, null);
			}
			catch (Exception e) { UnityEngine.Debug.LogError("[Ruinarch.Boot] Loader failed to start: " + e); }
		}

		private static string Managed => Path.GetDirectoryName(typeof(Boot).Assembly.Location);
		private static string GameRoot => Path.GetDirectoryName(Path.GetDirectoryName(Managed));
		private static string UpdateDir => Path.Combine(GameRoot, "ModLoaderUpdate");

		private static void Result(string text)
		{
			try { Directory.CreateDirectory(UpdateDir); File.WriteAllText(Path.Combine(UpdateDir, "result.txt"), text); }
			catch { }
		}

		private static void ApplyStaged()
		{
			string staged = Path.Combine(UpdateDir, "staged"), list = Path.Combine(staged, "apply.txt");
			if (!File.Exists(list)) return;
			try
			{
				string[] lines = File.ReadAllLines(list);
				if (lines.Length < 2 || !lines[0].StartsWith("version ", StringComparison.Ordinal)) throw new IOException("bad apply.txt");
				string version = lines[0].Substring(8).Trim();
				var files = new List<(string source, string target)>();
				for (int i = 1; i < lines.Length; i++)
				{
					string[] parts = lines[i].Split(' ');
					if (parts.Length != 2 || !SafeName(parts[1]) || parts[1] == "Ruinarch.Boot.dll")
						throw new IOException("bad entry: " + lines[i]);
					string source = Path.Combine(staged, parts[1]);
					if (!string.Equals(Sha256(source), parts[0], StringComparison.OrdinalIgnoreCase)) throw new IOException("hash mismatch: " + parts[1]);
					string target = parts[1] == Loader ? Path.Combine(Managed, Loader) : Path.Combine(GameRoot, "Mods", parts[1]);
					files.Add((source, target));
				}
				Replace(files);
				Result("updated " + version);
			}
			finally
			{
				Directory.Delete(staged, true);
			}
		}

		private static void Replace(List<(string source, string target)> files)
		{
			string previous = Path.Combine(UpdateDir, "previous");
			if (Directory.Exists(previous)) Directory.Delete(previous, true);
			Directory.CreateDirectory(previous);
			var backups = new List<(string backup, string target)>();
			foreach (var f in files)
			{
				if (!File.Exists(f.target)) continue;
				string backup = Path.Combine(previous, Path.GetFileName(f.target));
				File.Copy(f.target, backup, true);
				backups.Add((backup, f.target));
			}
			try
			{
				foreach (var f in files) File.Copy(f.source, f.target, true);
			}
			catch
			{
				foreach (var b in backups) File.Copy(b.backup, b.target, true);
				throw;
			}
		}

		private static bool SafeName(string name)
		{
			return name.EndsWith(".dll", StringComparison.Ordinal) && name.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-');
		}

		private static string Sha256(string file)
		{
			using (var sha = SHA256.Create())
			using (var stream = File.OpenRead(file))
				return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
		}
	}
}
