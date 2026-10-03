using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ruinarch.Modding
{
	/// <summary>Reads JSON and PE metadata only. Never loads a package assembly.</summary>
	public static class PackageInspector
	{
		/// <summary>Manifest API versions this loader accepts. API 2 adds <c>context.Settings</c>.</summary>
		public static readonly int[] LoaderApis = { 1, 2 };
		private static readonly HashSet<string> Fields = new HashSet<string> { "id", "name", "version", "author", "description", "loader", "loaderApi", "type", "entryAssembly", "entryType", "dependencies" };
		private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ruinarch.Modding", "Ruinarch.ModContent", "Ruinarch.ModMenu", "0Harmony", "Mono.Cecil", "Assembly-CSharp", "Assembly-CSharp-firstpass", "mscorlib", "netstandard" };
		internal static void ReserveGameAssemblies(string managed)
		{
			foreach (string dll in System.IO.Directory.GetFiles(managed, "*.dll")) Reserved.Add(Path.GetFileNameWithoutExtension(dll));
		}
		public static KnownMod Inspect(string directory)
		{
			var mod = new KnownMod { Directory = Path.GetFullPath(directory), Info = new ModInfo { id = Path.GetFileName(directory), name = Path.GetFileName(directory), version = "0.0.0", author = "unknown", description = "" } };
			try
			{
				string file = Path.Combine(mod.Directory, "mod.json");
				if (!File.Exists(file)) throw new InvalidDataException("Missing mod.json. Vanilla XML items and loose DLLs are not loader packages.");
				CheckTree(mod.Directory);
				JObject json = Object(File.ReadAllText(file));
				if (json["id"]?.Type == JTokenType.String) mod.Info.id = (string)json["id"];
				if (json["name"]?.Type == JTokenType.String) mod.Info.name = (string)json["name"];
				foreach (JProperty p in json.Properties()) if (!Fields.Contains(p.Name)) throw new InvalidDataException("Unknown manifest field: " + p.Name);
				foreach (string field in new[] { "id", "name", "version", "loader", "type" })
					if (json[field]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)json[field])) throw new InvalidDataException("Missing or invalid manifest " + field + ".");
				if (!Regex.IsMatch((string)json["id"], "^[a-z0-9][a-z0-9._-]*$")) throw new InvalidDataException("Invalid package id; use lowercase letters, digits, dots, underscores or hyphens.");
				if (!Regex.IsMatch((string)json["version"], "^[0-9]+\\.[0-9]+\\.[0-9]+$") || !Version.TryParse((string)json["version"], out Version _)) throw new InvalidDataException("version must be major.minor.patch.");
				if ((string)json["loader"] != "RuinarchModLoader") throw new InvalidDataException("loader must be RuinarchModLoader.");
				if (json["loaderApi"]?.Type != JTokenType.Integer || !LoaderApis.Contains((int)(long)json["loaderApi"])) throw new InvalidDataException("Unsupported loaderApi; this loader supports API " + string.Join(" and ", LoaderApis) + ".");
				foreach (string field in new[] { "author", "description", "entryAssembly", "entryType" })
					if (json[field] != null && json[field].Type != JTokenType.String) throw new InvalidDataException(field + " must be a string.");
				if (json["dependencies"] != null && (!(json["dependencies"] is JArray deps) || deps.Any(d => d.Type != JTokenType.String))) throw new InvalidDataException("dependencies must be an array of relative DLL paths.");
				mod.Info = json.ToObject<ModInfo>(); mod.Info.author = mod.Info.author ?? "unknown"; mod.Info.description = mod.Info.description ?? "";
				string[] dlls = System.IO.Directory.GetFiles(mod.Directory, "*.dll", SearchOption.AllDirectories);
				if (mod.Info.type == "templates")
				{
					if (dlls.Length != 0 || json["entryAssembly"] != null || json["entryType"] != null || json["dependencies"] != null) throw new InvalidDataException("A templates package cannot contain DLLs or code entry fields.");
					if (!System.IO.Directory.Exists(Path.Combine(mod.Directory, "templates"))) throw new InvalidDataException("A templates package requires templates/.");
				}
				else if (mod.Info.type == "code") InspectCode(mod, dlls);
				else throw new InvalidDataException("type must be code or templates.");
				InspectTemplates(mod);
			}
			catch (Exception e) { mod.RejectionReason = e.Message; mod.Assemblies.Clear(); }
			return mod;
		}
		private static JObject Object(string text)
		{
			return JObject.Parse(text, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
		}
		private static string Inside(string root, string relative)
		{
			if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":")) throw new InvalidDataException("Package path must be relative: " + relative);
			string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string path = Path.GetFullPath(Path.Combine(prefix, relative.Replace('\\', Path.DirectorySeparatorChar)));
			if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escapes its directory: " + relative);
			return path;
		}
		private static void CheckTree(string directory)
		{
			if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Package paths cannot be symbolic links/reparse points.");
			foreach (string child in System.IO.Directory.GetFileSystemEntries(directory))
			{
				FileAttributes attributes = File.GetAttributes(child);
				if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Package contains a symbolic link/reparse point: " + Path.GetFileName(child));
				if ((attributes & FileAttributes.Directory) != 0) CheckTree(child);
			}
		}
		private static void InspectCode(KnownMod mod, string[] dlls)
		{
			if (string.IsNullOrWhiteSpace(mod.Info.entryAssembly) || string.IsNullOrWhiteSpace(mod.Info.entryType)) throw new InvalidDataException("Code packages require entryAssembly and entryType.");
			mod.DllPath = Inside(mod.Directory, mod.Info.entryAssembly);
			var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mod.DllPath };
			foreach (string dep in mod.Info.dependencies ?? new string[0]) if (!paths.Add(Inside(mod.Directory, dep))) throw new InvalidDataException("Duplicate dependency path: " + dep);
			if (dlls.Any(d => !paths.Contains(Path.GetFullPath(d)))) throw new InvalidDataException("Every package DLL must be the entry assembly or a declared dependency.");
			var modules = new Dictionary<string, ModuleDefinition>(StringComparer.OrdinalIgnoreCase);
			try
			{
				foreach (string path in paths)
				{
					if (!File.Exists(path) || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Missing managed DLL: " + Path.GetFileName(path));
					var module = ModuleDefinition.ReadModule(path, new ReaderParameters { ReadingMode = ReadingMode.Deferred, InMemory = true });
					string name = module.Assembly?.Name.Name;
					if (name == null || Reserved.Contains(name) || name.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase) || name.StartsWith("System", StringComparison.OrdinalIgnoreCase)) { module.Dispose(); throw new InvalidDataException("Cannot bundle or shadow infrastructure/game assembly: " + name); }
					if (modules.ContainsKey(name)) { module.Dispose(); throw new InvalidDataException("Duplicate assembly name: " + name); }
					modules.Add(name, module); mod.Assemblies.Add(name, path);
				}
				var entryModule = modules.Values.First(m => string.Equals(m.FileName, mod.DllPath, StringComparison.OrdinalIgnoreCase));
				TypeDefinition entry = Types(entryModule.Types).FirstOrDefault(t => t.FullName.Replace('/', '+') == mod.Info.entryType);
				if (entry == null || entry.IsAbstract || entry.IsInterface || entry.HasGenericParameters || !(entry.IsPublic || entry.IsNestedPublic)
					|| !entry.Methods.Any(m => m.IsConstructor && !m.IsStatic && m.IsPublic && m.Parameters.Count == 0)
					|| !IsMod(entry, modules, new HashSet<string>())) throw new InvalidDataException("entryType must be a public concrete IRuinarchMod with a public parameterless constructor: " + mod.Info.entryType);
			}
			finally { foreach (ModuleDefinition module in modules.Values) module.Dispose(); }
		}
		private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
		{
			foreach (TypeDefinition t in types) { yield return t; foreach (TypeDefinition nested in Types(t.NestedTypes)) yield return nested; }
		}
		private static bool IsMod(TypeDefinition type, Dictionary<string, ModuleDefinition> modules, HashSet<string> seen)
		{
			if (!seen.Add(type.Module.Name + ":" + type.FullName)) return false;
			foreach (InterfaceImplementation i in type.Interfaces)
				if (i.InterfaceType.FullName == "Ruinarch.Modding.IRuinarchMod" && i.InterfaceType.Scope is AssemblyNameReference scope && scope.Name == "Ruinarch.Modding") return true;
			TypeReference parent = type.BaseType;
			if (parent == null) return false;
			ModuleDefinition module = parent.Scope is AssemblyNameReference reference && modules.TryGetValue(reference.Name, out ModuleDefinition dependency) ? dependency : type.Module;
			TypeDefinition definition = Types(module.Types).FirstOrDefault(t => t.FullName == parent.FullName);
			return definition != null && IsMod(definition, modules, seen);
		}
		private static void InspectTemplates(KnownMod mod)
		{
			string dir = Path.Combine(mod.Directory, "templates");
			if (!System.IO.Directory.Exists(dir)) return;
			foreach (string file in System.IO.Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
			{
				JObject t = Object(File.ReadAllText(file));
				if (t["formatVersion"]?.Type != JTokenType.Integer || (long)t["formatVersion"] != 1 || t["id"]?.Type != JTokenType.String || !((string)t["id"]).StartsWith(mod.Id + "/", StringComparison.Ordinal)) throw new InvalidDataException("Template " + Path.GetFileName(file) + " requires formatVersion 1 and a package-scoped id.");
				foreach (JValue value in t.Descendants().OfType<JValue>().Where(v => v.Type == JTokenType.String))
				{
					string reference = (string)value;
					if (reference.StartsWith("art:", StringComparison.Ordinal))
					{
						string art = Inside(Path.Combine(mod.Directory, "art"), reference.Substring(4));
						if (!art.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || !File.Exists(art)) throw new InvalidDataException("Missing pack PNG: " + reference);
					}
				}
			}
		}
	}
}
