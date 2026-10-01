using System;
using System.IO;
using UnityEngine;

namespace Ruinarch.ModContent.Templates
{
	// The framework has no loader reference; it writes mods.log lines in the loader's format
	// itself, so a pack author sees template problems where every mod's messages are.
	internal static class FrameworkLog
	{
		/// <summary>Mods/ next to the game executable (the loader's ModsRoot).</summary>
		internal static string ModsRoot => Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath, "Mods");

		internal static void Info(string message)
		{
			Debug.Log("[ModContent] " + message);
			Append("INFO", message);
		}

		internal static void Warning(string message)
		{
			Debug.LogWarning("[ModContent] " + message);
			Append("WARN", message);
		}

		private static void Append(string level, string message)
		{
			try
			{
				File.AppendAllText(Path.Combine(ModsRoot, "mods.log"), $"{DateTime.Now:HH:mm:ss} [{level}] [ModContent] {message}{Environment.NewLine}");
			}
			catch
			{
				// Logging must never take the game down.
			}
		}
	}
}
