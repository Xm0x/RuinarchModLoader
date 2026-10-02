using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Ruinarch.Modding
{
	/// <summary>
	/// A mod that has been successfully loaded.
	/// </summary>
	public sealed class LoadedMod
	{
		public ModInfo Info { get; internal set; }
		public Assembly Assembly { get; internal set; }
		public IRuinarchMod Instance { get; internal set; }
		public string Directory { get; internal set; }
	}


	/// <summary>
	/// The mod loader. <see cref="Initialize"/> is the single entry point; the
	/// patcher wires a call to it into the game's <c>Assembly-CSharp</c> module
	/// initializer, so it runs the instant Mono loads the game assembly, before
	/// any scene or game type. It scans the <c>Mods/</c> folder next to the
	/// executable, loads every mod assembly, and calls
	/// <see cref="IRuinarchMod.OnLoad"/> on each entry point.
	///
	/// Isolation: a mod that throws while loading is logged and skipped; it never
	/// takes down the game or the other mods. An <c>AssemblyResolve</c> hook lets
	/// mods drop their own dependencies (e.g. <c>0Harmony.dll</c>) anywhere under
	/// <c>Mods/</c> and have them resolve.
	/// </summary>
	public static class ModLoader
	{
		private static readonly List<LoadedMod> _loaded = new List<LoadedMod>();
		private static readonly List<KnownMod> _known = new List<KnownMod>();
		private static HashSet<string> _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static bool _initialized;
		private static bool _workshopScanned;
		private static readonly Dictionary<string, KnownMod> _packageIds = new Dictionary<string, KnownMod>(StringComparer.OrdinalIgnoreCase);
		private static readonly Dictionary<string, string> _assemblyPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private static readonly string[] Infrastructure = { "0Harmony", "Mono.Cecil", "Ruinarch.ModContent", "Ruinarch.ModMenu" };

		/// <summary>This loader's release version. In-game updates offer only newer versions.
		/// A property, not a const, so other assemblies read the installed value.</summary>
		public static string Version => "0.6.1";

		/// <summary>Absolute path to the <c>Mods/</c> root (set during init).</summary>
		public static string ModsRoot { get; private set; }

		/// <summary>Shared log file all mods append to.</summary>
		public static string LogFile { get; private set; }

		/// <summary>Absolute path to the loader config (<c>modloader.config.json</c>).</summary>
		public static string ConfigFile { get; private set; }

		/// <summary>Every mod that loaded successfully, in load order.</summary>
		public static IReadOnlyList<LoadedMod> Loaded => _loaded;

		/// <summary>
		/// Every mod discovered this session, activated or not (drives the mod
		/// manager UI). Disabled mods appear here with <see cref="KnownMod.Loaded"/>
		/// false.
		/// </summary>
		public static IReadOnlyList<KnownMod> Known => _known;

		/// <summary>
		/// Entry point. Called from the patched game assembly's module initializer.
		/// Idempotent: safe to call more than once.
		/// </summary>
		public static void Initialize()
		{
			if (_initialized)
			{
				return;
			}
			_initialized = true;

			try
			{
				ModsRoot = ResolveModsRoot();
				System.IO.Directory.CreateDirectory(ModsRoot);
				LogFile = Path.Combine(ModsRoot, "mods.log");
				ConfigFile = Path.Combine(ModsRoot, "modloader.config.json");
				TruncateLog();
				Debug.Log($"[ModLoader] RuinarchModLoader {Version}");

				_disabled = ReadDisabledSet();

				AppDomain.CurrentDomain.AssemblyResolve += ResolveFromMods;
				ScanLocalPackages();
				LoadMenu();
				StartContentFramework();
				PublishPackDirectories();
				Debug.Log($"[ModLoader] Local scan complete. {_loaded.Count} code mod(s) active.");
			}
			catch (Exception e)
			{
				Debug.LogError($"[ModLoader] Fatal error during init: {e}");
			}
		}

		// The content framework (Mods/Ruinarch.ModContent.dll) installs itself when a mod calls
		// it; start it here too, so template packs (data only, no DLL) load with no code mod
		// present. By reflection: the loader does not depend on the framework.
		private static void StartContentFramework()
		{
			try
			{
				string path = Path.Combine(ModsRoot, "Ruinarch.ModContent.dll");
				Assembly framework = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => SafeName(a) == "Ruinarch.ModContent")
					?? (File.Exists(path) ? Assembly.LoadFrom(path) : null);
				framework?.GetType("Ruinarch.ModContent.ModContent")?.GetMethod("Install")?.Invoke(null, null);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[ModLoader] Could not start the content framework: {e.Message}");
			}
		}


		private static string ResolveModsRoot()
		{
			// Application.dataPath == "<gameRoot>/Ruinarch_Data"; put Mods/ at the
			// game root, next to Ruinarch.exe, where players expect to find it.
			string gameRoot = System.IO.Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
			return Path.Combine(gameRoot, "Mods");
		}

		// Start a fresh mods.log. The previous launch's log is kept as
		// Mods/logs/mods-<date>_<time>.log (the time it was last written), newest 20 only.
		private static void TruncateLog()
		{
			try
			{
				KeepDatedCopy(LogFile, Path.Combine(ModsRoot, "logs"), "mods", 20);
			}
			catch
			{
			}
			try
			{
				File.WriteAllText(LogFile, $"# Ruinarch mod log - {DateTime.Now}{Environment.NewLine}");
			}
			catch
			{
			}
		}

		private static void KeepDatedCopy(string file, string dir, string name, int keep)
		{
			if (!File.Exists(file))
			{
				return;
			}
			System.IO.Directory.CreateDirectory(dir);
			string target = Path.Combine(dir, $"{name}-{File.GetLastWriteTime(file):yyyy-MM-dd_HH-mm-ss}.log");
			File.Copy(file, target, overwrite: true);
			foreach (string old in System.IO.Directory.GetFiles(dir, name + "-*.log").OrderByDescending(f => f, StringComparer.Ordinal).Skip(keep))
			{
				File.Delete(old);
			}
		}

		private static void ScanLocalPackages()
		{
			PackageInspector.ReserveGameAssemblies(Path.Combine(Application.dataPath, "Managed"));
			foreach (string dir in System.IO.Directory.GetDirectories(ModsRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
			{
				if (!File.Exists(Path.Combine(dir, "mod.json")) && !System.IO.Directory.Exists(Path.Combine(dir, "templates"))
					&& System.IO.Directory.GetFiles(dir, "*.dll").Length == 0 && System.IO.Directory.GetFiles(dir, "*.xml").Length == 0) continue;
				AddPackage(PackageInspector.Inspect(dir));
			}
			foreach (string dll in System.IO.Directory.GetFiles(ModsRoot, "*.dll").OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
				if (!Infrastructure.Contains(Path.GetFileNameWithoutExtension(dll), StringComparer.OrdinalIgnoreCase))
					_known.Add(new KnownMod { Directory = ModsRoot, DllPath = dll, Info = new ModInfo { id = Path.GetFileNameWithoutExtension(dll), name = Path.GetFileName(dll), version = "0.0.0" }, RejectionReason = "Loose user DLL. Put it in a package folder with an API 1 mod.json." });
			foreach (KnownMod mod in _known.Where(m => m.Compatible && m.Enabled).ToArray()) Activate(mod);
		}

		private static void AddPackage(KnownMod mod)
		{
			mod.Enabled = !_disabled.Contains(mod.Id);
			if (_packageIds.TryGetValue(mod.Id, out KnownMod owner)) mod.RejectionReason = DuplicateReason(mod.Id, owner);
			else _packageIds.Add(mod.Id, mod);
			if (mod.Compatible && mod.Enabled)
			{
				string collision = mod.Assemblies.Keys.FirstOrDefault(name => _assemblyPaths.ContainsKey(name));
				if (collision != null) mod.RejectionReason = "Assembly name already provided by another enabled package: " + collision;
				else foreach (var assembly in mod.Assemblies) _assemblyPaths.Add(assembly.Key, assembly.Value);
			}
			_known.Add(mod);
			if (!mod.Compatible) Debug.LogWarning($"[ModLoader] Not compatible with RuinarchModLoader: {mod.Id}: {mod.RejectionReason}");
			else if (!mod.Enabled) Debug.Log($"[ModLoader] Skipped disabled package '{mod.Id}' without loading code.");
		}

		/// <summary>Called once by the Steam bridge after native Steam initialization.
		/// Local IDs already reserve precedence. Updates and unsubscribes require restart.</summary>
		public static void LoadWorkshopPackages(IReadOnlyDictionary<ulong, string> installedFolders)
		{
			if (_workshopScanned) return;
			_workshopScanned = true;
			var added = new List<KnownMod>();
			foreach (var item in installedFolders.OrderBy(i => i.Key))
			{
				KnownMod mod = PackageInspector.Inspect(item.Value);
				mod.Origin = ModOrigin.SteamWorkshop; mod.WorkshopId = item.Key;
				AddPackage(mod); added.Add(mod);
			}
			foreach (KnownMod mod in added.Where(m => m.Compatible && m.Enabled)) Activate(mod);
			PublishPackDirectories();
			Debug.Log($"[ModLoader] Workshop scan complete: {added.Count} installed subscribed item(s).");
		}

		/// <summary>Lists subscribed Workshop items that finished installing after startup. They are
		/// checked like any package but never load in this session: code and templates load at startup.
		/// Returns the newly listed items; already listed ones are skipped.</summary>
		public static IReadOnlyList<KnownMod> AddLateWorkshopPackages(IReadOnlyDictionary<ulong, string> installedFolders)
		{
			var added = new List<KnownMod>();
			if (!_workshopScanned) return added;
			foreach (var item in installedFolders.OrderBy(i => i.Key))
			{
				if (_known.Any(k => k.Origin == ModOrigin.SteamWorkshop && k.WorkshopId == item.Key)) continue;
				KnownMod mod = PackageInspector.Inspect(item.Value);
				mod.Origin = ModOrigin.SteamWorkshop; mod.WorkshopId = item.Key;
				mod.Enabled = !_disabled.Contains(mod.Id);
				if (_packageIds.TryGetValue(mod.Id, out KnownMod owner)) mod.RejectionReason = DuplicateReason(mod.Id, owner);
				_known.Add(mod); added.Add(mod);
				Debug.Log($"[ModLoader] Workshop item {item.Key} ({mod.Id}) installed during play; it is checked again at the next launch.");
			}
			return added;
		}

		private static string DuplicateReason(string id, KnownMod owner)
		{
			string where = owner.Origin == ModOrigin.SteamWorkshop ? "Steam Workshop item " + owner.WorkshopId : "local folder " + Path.GetFileName(owner.Directory);
			return $"Duplicate package id {id}: the {where} already uses it (local copies win over Workshop items).";
		}

		private static void Activate(KnownMod mod)
		{
			if (mod.Info.type == "templates") { mod.Loaded = true; return; }
			try
			{
				Assembly assembly = Assembly.LoadFrom(mod.DllPath);
				Type entry = assembly.GetType(mod.Info.entryType, throwOnError: true);
				var logger = new ModLogger(mod.Id, LogFile);
				var instance = (IRuinarchMod)Activator.CreateInstance(entry);
				instance.OnLoad(new ModContext(mod.Info, mod.Directory, ModsRoot, logger));
				_loaded.Add(new LoadedMod { Info = mod.Info, Assembly = assembly, Instance = instance, Directory = mod.Directory });
				mod.Loaded = true;
				Debug.Log($"[ModLoader] Loaded {mod.Info} ({mod.Origin}).");
			}
			catch (Exception e)
			{
				mod.FailureReason = e.GetBaseException().Message;
				Debug.LogError($"[ModLoader] Package '{mod.Id}' failed during activation: {e}");
			}
		}

		private static void LoadMenu()
		{
			string dll = Path.Combine(ModsRoot, "Ruinarch.ModMenu.dll");
			if (!File.Exists(dll)) return;
			var menu = new KnownMod
			{
				Directory = ModsRoot, DllPath = dll, Enabled = true, Origin = ModOrigin.Infrastructure,
				Info = new ModInfo { id = "Ruinarch.ModMenu", name = "Mod manager", version = "1.0.0", author = "RuinarchModLoader", type = "code", entryType = "Ruinarch.ModMenu.ModMenuMod" }
			};
			_known.Add(menu); Activate(menu);
		}

		private static void PublishPackDirectories()
		{
			try
			{
				string[] directories = _known.Where(m => m.Compatible && m.Enabled && m.Loaded && m.Origin != ModOrigin.Infrastructure
					&& System.IO.Directory.Exists(Path.Combine(m.Directory, "templates"))).Select(m => m.Directory).ToArray();
				Assembly framework = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => SafeName(a) == "Ruinarch.ModContent");
				framework?.GetType("Ruinarch.ModContent.Templates.ModTemplates")?.GetMethod("SetPackDirectories")?.Invoke(null, new object[] { directories });
			}
			catch (Exception e) { Debug.LogWarning("[ModLoader] Template directory handoff failed: " + e.GetBaseException().Message); }
		}

		/// <summary>
		/// Enable or disable a mod by id. Rewrites <c>modloader.config.json</c> and
		/// updates the in-memory <see cref="Known"/> state so a UI reflects it
		/// immediately. The change to what actually loads takes effect on the next
		/// game launch, because mods are loaded once at startup.
		/// </summary>
		public static void SetModEnabled(string id, bool enabled)
		{
			if (string.IsNullOrEmpty(id))
			{
				return;
			}
			if (enabled)
			{
				_disabled.Remove(id);
			}
			else
			{
				_disabled.Add(id);
			}
			WriteDisabledSet();

			foreach (KnownMod km in _known.Where(k => k.Id == id && k.Compatible)) km.Enabled = enabled;
		}

		/// <summary>Whether a mod id is currently enabled (not in the disabled list).</summary>
		public static bool IsEnabled(string id)
		{
			return !string.IsNullOrEmpty(id) && !_disabled.Contains(id);
		}

		[Serializable]
		private class LoaderConfig
		{
			public string[] disabled = Array.Empty<string>();
		}

		private static HashSet<string> ReadDisabledSet()
		{
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			try
			{
				if (!string.IsNullOrEmpty(ConfigFile) && File.Exists(ConfigFile))
				{
					LoaderConfig cfg = JsonUtility.FromJson<LoaderConfig>(File.ReadAllText(ConfigFile));
					if (cfg?.disabled != null)
					{
						foreach (string id in cfg.disabled)
						{
							if (!string.IsNullOrEmpty(id))
							{
								set.Add(id);
							}
						}
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[ModLoader] Bad {Path.GetFileName(ConfigFile)}: {e.Message}");
			}
			return set;
		}

		private static void WriteDisabledSet()
		{
			try
			{
				if (string.IsNullOrEmpty(ConfigFile))
				{
					return;
				}
				var cfg = new LoaderConfig { disabled = _disabled.ToArray() };
				File.WriteAllText(ConfigFile, JsonUtility.ToJson(cfg, prettyPrint: true));
			}
			catch (Exception e)
			{
				Debug.LogError($"[ModLoader] Could not write {Path.GetFileName(ConfigFile)}: {e.Message}");
			}
		}


		// An assembly's simple name, or null. Runtime-generated (dynamic) assemblies are
		// skipped, and one whose name cannot be read is passed over: in a test run, Mono threw
		// CultureNotFoundException (culture name of garbage bytes) from GetName() on one loaded
		// assembly, which would fail every lookup that walked past it.
		internal static string SafeName(Assembly a)
		{
			if (a == null || a.IsDynamic)
			{
				return null;
			}
			try
			{
				return a.GetName().Name;
			}
			catch
			{
				return null;
			}
		}

		private static Assembly ResolveFromMods(object sender, ResolveEventArgs args)
		{
			try
			{
				string simpleName = new AssemblyName(args.Name).Name;

				// Return an already-loaded assembly with a matching simple name first.
				Assembly existing = AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(a => SafeName(a) == simpleName);
				if (existing != null)
				{
					return existing;
				}

				if (_assemblyPaths.TryGetValue(simpleName, out string packageDll)) return Assembly.LoadFrom(packageDll);
				if (ModsRoot != null && Infrastructure.Contains(simpleName, StringComparer.OrdinalIgnoreCase))
				{
					string path = Path.Combine(ModsRoot, simpleName + ".dll");
					if (File.Exists(path)) return Assembly.LoadFrom(path);
				}
				return null;
			}
			catch
			{
				return null;
			}
		}
	}
}
