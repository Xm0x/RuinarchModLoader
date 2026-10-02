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
		public int Loaded;
		/// <summary>One line per skipped file: <c>pack/file.json: reason</c>.</summary>
		public List<string> Problems = new List<string>();
	}

	/// <summary>
	/// Template packs: a package folder with a <c>templates/</c> folder (a data-only pack, or
	/// a code mod shipping templates). Its id is mod.json's id; every template id in it starts
	/// with that id and a '/'. The loader decides which packages are compatible and enabled
	/// and hands over their directories; the framework never scans Mods/ or Workshop folders.
	/// </summary>
	internal static class TemplatePacks
	{
		private static string[] _accepted = new string[0];

		internal static IReadOnlyList<string> PackDirectories => _accepted;

		internal static void SetPackDirectories(string[] directories)
		{
			_accepted = (directories ?? new string[0]).Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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

		internal static PackLoadReport Load(string dir)
		{
			PackLoadReport report = new PackLoadReport { PackId = PackId(dir) };
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
