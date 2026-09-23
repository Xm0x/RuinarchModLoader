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
	/// Every mod the loader discovered on disk this session, whether or not it was
	/// activated. Drives the in-game mod list / manager UI. A mod is "known" if it
	/// exposes an <see cref="IRuinarchMod"/> entry point or declares itself with a
	/// <c>mod.json</c>; plain dependency DLLs (0Harmony, resource assemblies) are
	/// not listed.
	/// </summary>
	public sealed class KnownMod
	{
		public ModInfo Info { get; internal set; }
		public string Directory { get; internal set; }
		public string DllPath { get; internal set; }

		/// <summary>Whether the mod is enabled in <c>modloader.config.json</c>.</summary>
		public bool Enabled { get; internal set; }

		/// <summary>Whether the mod's <see cref="IRuinarchMod.OnLoad"/> actually ran this session.</summary>
		public bool Loaded { get; internal set; }

		public string Id => Info != null ? Info.id : null;
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

				_disabled = ReadDisabledSet();

				// Let mods resolve their own dependencies from anywhere under Mods/.
				AppDomain.CurrentDomain.AssemblyResolve += ResolveFromMods;

				Debug.Log($"[ModLoader] Scanning for mods in: {ModsRoot}");
				List<string> dlls = DiscoverModDlls(ModsRoot);
				foreach (string dll in dlls)
				{
					TryLoad(dll);
				}
				Debug.Log($"[ModLoader] Done. {_loaded.Count} of {_known.Count} discovered mod(s) active.");
			}
			catch (Exception e)
			{
				Debug.LogError($"[ModLoader] Fatal error during init: {e}");
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

		/// <summary>
		/// Candidate DLLs: every <c>*.dll</c> directly in Mods/ plus one level of
		/// subfolders (<c>Mods/MyMod/MyMod.dll</c>). Non-mod DLLs (dependencies
		/// like 0Harmony) simply expose no <see cref="IRuinarchMod"/> and are
		/// skipped after inspection.
		/// </summary>
		private static List<string> DiscoverModDlls(string root)
		{
			var result = new List<string>();
			result.AddRange(System.IO.Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly));
			foreach (string dir in System.IO.Directory.GetDirectories(root))
			{
				result.AddRange(System.IO.Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly));
			}
			return result;
		}

		private static void TryLoad(string dllPath)
		{
			string fileName = Path.GetFileNameWithoutExtension(dllPath);
			string dir = Path.GetDirectoryName(dllPath);
			string jsonPath = Path.Combine(dir ?? ModsRoot, "mod.json");
			bool hasJson = File.Exists(jsonPath);

			// Fast path: a mod that ships a mod.json can be identified and skipped
			// WITHOUT loading its assembly at all when it is disabled.
			if (hasJson)
			{
				ModInfo declared = ModInfo.LoadOrDefault(jsonPath, fileName);
				if (_disabled.Contains(declared.id))
				{
					RecordKnown(declared, dir, dllPath, enabled: false, loaded: false);
					Debug.Log($"[ModLoader] Skipped disabled mod '{declared.id}' (not loaded).");
					return;
				}
			}

			Assembly assembly;
			try
			{
				assembly = Assembly.LoadFrom(dllPath);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[ModLoader] Could not load '{Path.GetFileName(dllPath)}': {e.Message}");
				return;
			}

			// Already processed this assembly (e.g. a shared dependency)?
			if (_loaded.Any(m => m.Assembly == assembly))
			{
				return;
			}

			Type[] types = SafeGetTypes(assembly);
			Type entryType = types.FirstOrDefault(t =>
				t != null && !t.IsAbstract && !t.IsInterface && typeof(IRuinarchMod).IsAssignableFrom(t));

			if (entryType == null)
			{
				// Not a mod (dependency DLL, resource assembly, etc.) - fine.
				return;
			}

			ModInfo info = ModInfo.LoadOrDefault(jsonPath, fileName);

			// A mod with no mod.json still honors the disabled list by its fallback id.
			if (_disabled.Contains(info.id))
			{
				RecordKnown(info, dir, dllPath, enabled: false, loaded: false);
				Debug.Log($"[ModLoader] Discovered disabled mod '{info.id}'; OnLoad skipped.");
				return;
			}

			try
			{
				var logger = new ModLogger(info.id, LogFile);
				var context = new ModContext(info, dir, ModsRoot, logger);

				var instance = (IRuinarchMod)Activator.CreateInstance(entryType);
				instance.OnLoad(context);

				_loaded.Add(new LoadedMod
				{
					Info = info,
					Assembly = assembly,
					Instance = instance,
					Directory = dir
				});
				RecordKnown(info, dir, dllPath, enabled: true, loaded: true);
				Debug.Log($"[ModLoader] Loaded {info}");
			}
			catch (Exception e)
			{
				RecordKnown(info, dir, dllPath, enabled: true, loaded: false);
				Debug.LogError($"[ModLoader] Mod '{fileName}' failed in OnLoad and was skipped: {e}");
			}
		}

		private static void RecordKnown(ModInfo info, string dir, string dllPath, bool enabled, bool loaded)
		{
			KnownMod existing = _known.FirstOrDefault(k => k.Id == info.id);
			if (existing != null)
			{
				existing.Loaded |= loaded;
				return;
			}
			_known.Add(new KnownMod
			{
				Info = info,
				Directory = dir,
				DllPath = dllPath,
				Enabled = enabled,
				Loaded = loaded
			});
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

			KnownMod km = _known.FirstOrDefault(k => k.Id == id);
			if (km != null)
			{
				km.Enabled = enabled;
			}
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

		private static Type[] SafeGetTypes(Assembly assembly)
		{
			try
			{
				return assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				return ex.Types.Where(t => t != null).ToArray();
			}
			catch
			{
				return Array.Empty<Type>();
			}
		}

		private static Assembly ResolveFromMods(object sender, ResolveEventArgs args)
		{
			try
			{
				string simpleName = new AssemblyName(args.Name).Name;

				// Return an already-loaded assembly with a matching simple name first.
				Assembly existing = AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(a => a.GetName().Name == simpleName);
				if (existing != null)
				{
					return existing;
				}

				if (string.IsNullOrEmpty(ModsRoot) || !System.IO.Directory.Exists(ModsRoot))
				{
					return null;
				}
				string match = System.IO.Directory
					.GetFiles(ModsRoot, simpleName + ".dll", SearchOption.AllDirectories)
					.FirstOrDefault();
				return match != null ? Assembly.LoadFrom(match) : null;
			}
			catch
			{
				return null;
			}
		}
	}
}
