using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>What loading a template pack did.</summary>
	public sealed class PackLoadReport
	{
		public string PackId;
		/// <summary>Switched off in the mod manager (modloader.config.json): nothing loaded.</summary>
		public bool Disabled;
		public int Loaded;
		/// <summary>One line per skipped file: <c>pack/file.json: reason</c>.</summary>
		public List<string> Problems = new List<string>();
	}

	/// <summary>
	/// Template packs: a folder in Mods/ with a <c>templates/</c> folder (a data-only pack, or
	/// a code mod shipping templates). Its id is mod.json's id, else the folder name; every
	/// template id in it starts with that id and a '/'.
	/// </summary>
	internal static class TemplatePacks
	{
		internal static IEnumerable<string> PackDirectories()
		{
			string root = FrameworkLog.ModsRoot;
			return Directory.Exists(root)
				? Directory.GetDirectories(root).Where(d => Directory.Exists(Path.Combine(d, "templates"))).OrderBy(d => d, StringComparer.Ordinal)
				: Enumerable.Empty<string>();
		}

		internal static string PackId(string dir)
		{
			string json = Path.Combine(dir, "mod.json");
			try
			{
				string id = File.Exists(json) ? (string)JObject.Parse(File.ReadAllText(json))["id"] : null;
				return string.IsNullOrEmpty(id) ? Path.GetFileName(dir) : id;
			}
			catch
			{
				return Path.GetFileName(dir);
			}
		}

		// The mod manager's switch (Mods/modloader.config.json, {"disabled":[ids]}); read
		// directly, the framework does not reference the loader.
		internal static bool IsDisabled(string id)
		{
			string config = Path.Combine(FrameworkLog.ModsRoot, "modloader.config.json");
			try
			{
				return File.Exists(config) && JObject.Parse(File.ReadAllText(config))["disabled"] is JArray list
					&& list.Any(x => string.Equals((string)x, id, StringComparison.OrdinalIgnoreCase));
			}
			catch
			{
				return false;
			}
		}

		internal static PackLoadReport Load(string dir)
		{
			PackLoadReport report = new PackLoadReport { PackId = PackId(dir) };
			if (IsDisabled(report.PackId))
			{
				report.Disabled = true;
				return report;
			}
			string templates = Path.Combine(dir, "templates");
			foreach (string file in Directory.Exists(templates) ? Directory.GetFiles(templates, "*.json").OrderBy(f => f, StringComparer.Ordinal) : Enumerable.Empty<string>())
			{
				string name = $"{report.PackId}/{Path.GetFileName(file)}";
				try
				{
					BuildingTemplate t = TemplateJson.FromJson(File.ReadAllText(file));
					if (t.id == null || !t.id.StartsWith(report.PackId + "/", StringComparison.Ordinal))
					{
						throw new TemplateException($"id {t.id} must start with {report.PackId}/");
					}
					TemplateRegistry.Register(t, dir);
					report.Loaded++;
				}
				catch (TemplateException e)
				{
					Skipped(report, $"{name}: {e.Message}");
				}
				catch (Exception e)
				{
					Skipped(report, $"{name}: {e.GetType().Name}: {e.Message}");
				}
			}
			return report;
		}

		private static void Skipped(PackLoadReport report, string line)
		{
			report.Problems.Add(line);
			FrameworkLog.Warning("Template " + line + "; skipped.");
		}
	}
}
