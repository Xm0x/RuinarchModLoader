using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Ruinarch.ModContent.Templates;
using Ruinarch.Modding;

namespace Ruinarch.ModMenu.Editor
{
	internal sealed class EditorPack
	{
		internal string Directory, Id, Name;
		/// <summary>A Steam Workshop package: its templates can be copied, never edited in place.</summary>
		internal bool ReadOnly;
	}
	internal static class EditorPacks
	{
		/// <summary>Local, editable packs in Mods/.</summary>
		internal static List<EditorPack> List()
		{
			var packs = new List<EditorPack>();
			if (!System.IO.Directory.Exists(ModTemplates.ModsRoot)) return packs;
			foreach (string dir in System.IO.Directory.GetDirectories(ModTemplates.ModsRoot).OrderBy(d => d))
			{
				if (!System.IO.Directory.Exists(Path.Combine(dir, "templates"))) continue;
				try
				{
					var manifest = JObject.Parse(File.ReadAllText(Path.Combine(dir, "mod.json")));
					string id = (string)manifest["id"];
					if (!ValidId(id)) continue;
					// Editable so it can be repaired, but say that the game will not load it.
					KnownMod known = ModLoader.Known.FirstOrDefault(k => string.Equals(Path.GetFullPath(k.Directory ?? "."), Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase));
					string name = (string)manifest["name"] ?? id;
					if (known != null && !known.Compatible) name = "! " + name + " (not loaded)";
					packs.Add(new EditorPack { Directory = dir, Id = id, Name = name });
				}
				catch (Exception e) { ModMenuMod.Log?.Warning("Cannot open editor pack " + dir + ": " + e.Message); }
			}
			return packs;
		}
		/// <summary>Templates to start from: local packs plus compatible Workshop packages (read-only).</summary>
		internal static List<EditorPack> Sources()
		{
			var packs = List();
			foreach (KnownMod mod in ModLoader.Known.Where(m => m.Origin == ModOrigin.SteamWorkshop && m.Compatible
				&& System.IO.Directory.Exists(Path.Combine(m.Directory, "templates"))))
				packs.Add(new EditorPack { Directory = mod.Directory, Id = mod.Id, Name = mod.Info.name + " (Steam Workshop)", ReadOnly = true });
			return packs;
		}
		internal static bool ValidId(string id) { return id != null && Regex.IsMatch(id, "^[a-z0-9][a-z0-9._-]*$"); }
		internal static EditorPack Create(string id, string name)
		{
			if (!ValidId(id)) throw new TemplateException("Pack id must use lowercase letters, digits, dots, underscores or hyphens.");
			if (string.IsNullOrWhiteSpace(name)) throw new TemplateException("Enter a pack name.");
			if (List().Any(p => p.Id == id) || ModLoader.Known.Any(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase))) throw new TemplateException("A package with this id already exists.");
			string dir = Path.Combine(ModTemplates.ModsRoot, id);
			if (System.IO.Directory.Exists(dir)) throw new TemplateException("The destination folder already exists. Choose another pack id.");
			System.IO.Directory.CreateDirectory(Path.Combine(dir, "templates")); System.IO.Directory.CreateDirectory(Path.Combine(dir, "art"));
			File.WriteAllText(Path.Combine(dir, "mod.json"), new JObject { ["id"] = id, ["name"] = name, ["version"] = "1.0.0", ["author"] = "", ["description"] = "Building template pack", ["loader"] = "RuinarchModLoader", ["loaderApi"] = 1, ["type"] = "templates" }.ToString());
			return new EditorPack { Id = id, Name = name, Directory = dir };
		}
		internal static IEnumerable<string> Files(EditorPack pack)
		{
			return System.IO.Directory.GetFiles(Path.Combine(pack.Directory, "templates"), "*.json").OrderBy(f => f);
		}
		/// <summary>Deletes a local template pack folder. Packages that ship code are left alone:
		/// the editor only removes what it can create.</summary>
		internal static void DeletePack(EditorPack pack)
		{
			if (pack.ReadOnly) throw new TemplateException("Unsubscribe in Steam to remove a Workshop pack.");
			var manifest = JObject.Parse(File.ReadAllText(Path.Combine(pack.Directory, "mod.json")));
			if ((string)manifest["type"] != "templates" || System.IO.Directory.GetFiles(pack.Directory, "*.dll", SearchOption.AllDirectories).Length > 0)
				throw new TemplateException(pack.Name + " is a code mod. Remove it from the Mods folder yourself.");
			System.IO.Directory.Delete(pack.Directory, true);
		}
		internal static void DeleteTemplate(EditorPack pack, string file)
		{
			if (pack.ReadOnly) throw new TemplateException("Workshop packs are read-only.");
			File.Delete(file);
		}
		internal static string NewId(EditorPack pack, string name)
		{
			string slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9._-]+", "-").Trim('-'); if (slug.Length == 0) slug = "building";
			string id = slug; int suffix = 2;
			while (File.Exists(Path.Combine(pack.Directory, "templates", id + ".json"))) id = slug + "-" + suffix++;
			return pack.Id + "/" + id;
		}
		internal static void ImportArt(BuildingTemplate t, EditorPack source, EditorPack target)
		{
			var replacements = new Dictionary<string, string>();
			Func<string, string> import = reference =>
			{
				if (reference == null || !reference.StartsWith("art:")) return reference;
				if (replacements.TryGetValue(reference, out string existing)) return existing;
				string artRoot = Path.GetFullPath(Path.Combine(source.Directory, "art")) + Path.DirectorySeparatorChar;
				string path = Path.GetFullPath(Path.Combine(artRoot, reference.Substring(4)));
				if (!path.StartsWith(artRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
					throw new TemplateException("Cannot import pack art: " + reference);
				string filename = source.Id + "-" + Path.GetFileName(path);
				string destination = Path.Combine(target.Directory, "art", filename);
				if (File.Exists(destination)) { filename = Guid.NewGuid().ToString("N") + "-" + filename; destination = Path.Combine(target.Directory, "art", filename); }
				System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path, destination);
				string result = "art:" + filename; replacements[reference] = result; return result;
			};
			foreach (string key in t.palette.Keys.ToList()) t.palette[key] = import(t.palette[key]);
			foreach (TemplateObject o in t.objects) o.sprite = import(o.sprite);
			foreach (TemplateThinWall w in t.thinWalls) for (int i = 0; i < w.sprites.Count; i++) w.sprites[i] = import(w.sprites[i]);
		}
		internal static void CheckShape(BuildingTemplate t)
		{
			if (t.formatVersion != 1 || t.bounds == null || t.bounds.Length != 4 || t.bounds[2] < 1 || t.bounds[3] < 1 || t.bounds[2] > 128 || t.bounds[3] > 128)
				throw new TemplateException("Template bounds must have a width and height between 1 and 128.");
			if (t.size == null || t.size.Length != 2 || t.size[0] < 1 || t.size[1] < 1 || t.center == null || t.center.Length < 2)
				throw new TemplateException("Template size or center is invalid.");
			if (t.palette == null || t.floor == null || t.detail == null || t.walls == null || t.objects == null || t.thinWalls == null || t.entrances == null || t.lightSpots == null || t.rooms == null)
				throw new TemplateException("Template layers and object lists cannot be null.");
			foreach (List<string> rows in new[] { t.floor, t.detail, t.walls })
			{
				if (rows.Count == 0) continue;
				if (rows.Count != t.bounds[3] || rows.Any(r => r == null || r.Length != t.bounds[2])) throw new TemplateException("Layer rows must match template bounds.");
				if (rows.Any(r => r.Any(c => c != '.' && !t.palette.ContainsKey(c.ToString())))) throw new TemplateException("A layer contains a palette key that is not defined.");
			}
			foreach (var p in t.palette) if (p.Key.Length != 1 || p.Key == "." || p.Value == null) throw new TemplateException("Palette keys must be one non-dot character and reference an asset.");
			foreach (float[] p in t.objects.Select(o => o?.pos).Concat(t.thinWalls.Select(o => o?.pos)).Concat(t.entrances.Select(o => o?.pos)).Concat(t.lightSpots.Select(o => o?.pos)))
				if (p == null || p.Length != 3 || p.Any(v => float.IsNaN(v) || float.IsInfinity(v))) throw new TemplateException("Object positions must be three finite numbers.");
			if (t.thinWalls.Any(w => w.sprites == null)) throw new TemplateException("Thin-wall sprite lists cannot be null.");
		}
		internal static string Save(EditorPack pack, EditorDocument doc)
		{
			BuildingTemplate t = doc.Template; CheckShape(t);
			string[] parts = (t.id ?? "").Split('/');
			if (parts.Length != 2 || parts[0] != pack.Id || !ValidId(parts[1])) throw new TemplateException("Template id must be " + pack.Id + "/ followed by a lowercase filename.");
			string file = Path.Combine(pack.Directory, "templates", parts[1] + ".json");
			string temporary = file + ".tmp";
			try
			{
				File.WriteAllText(temporary, ModTemplates.ToJson(t));
				if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
			}
			finally { if (File.Exists(temporary)) File.Delete(temporary); }
			doc.MarkSaved(); return file;
		}
	}
}
