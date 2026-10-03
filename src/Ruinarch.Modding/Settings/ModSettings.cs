using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Ruinarch.Modding
{
	/// <summary>The control a setting is shown with.</summary>
	public enum SettingKind { Toggle, IntSlider, FloatSlider, Dropdown }

	/// <summary>One setting shown in the Mods tab.</summary>
	public sealed class SettingField
	{
		internal FieldInfo Field;
		internal object Default;
		public string Name => Field.Name;
		public string Label { get; internal set; }
		public string Description { get; internal set; }
		/// <summary>Header of the section this setting is in; null before the first [Section].</summary>
		public string Section { get; internal set; }
		public SettingKind Kind { get; internal set; }
		public float Min { get; internal set; }
		public float Max { get; internal set; }
		public bool RequiresRestart { get; internal set; }
		/// <summary>The enum's names, in declaration order (dropdowns only).</summary>
		public IReadOnlyList<string> Options { get; internal set; }
	}

	/// <summary>
	/// A mod's settings (<c>context.Settings</c>). Call <see cref="Register{T}"/> once in OnLoad
	/// with a class whose public fields carry [Setting]; the loader shows them in the Settings
	/// window's Mods tab, saves them in <see cref="FilePath"/> and writes changes straight into the
	/// returned object.
	/// </summary>
	public sealed class SettingsHandle
	{
		private readonly ModInfo _info;
		private readonly ModLogger _log;
		internal RegisteredSettings Registered;

		/// <summary>Where this mod's settings are saved: <c>Mods/settings/&lt;mod id&gt;.json</c>.</summary>
		public string FilePath { get; }

		/// <summary>Raised on the main thread after a change is written and saved; the argument is
		/// the field name. Raised for restart-only fields too (the object keeps its startup value).</summary>
		public event Action<string> Changed;

		internal SettingsHandle(ModInfo info, string modsRoot, ModLogger log)
		{
			_info = info;
			_log = log;
			FilePath = Path.Combine(modsRoot ?? "", "settings", info.id + ".json");
		}

		/// <summary>Creates <typeparamref name="T"/>, fills it from the saved file and returns it.
		/// One class per mod: a second call logs an error and returns the object already registered.</summary>
		public T Register<T>() where T : class, new()
		{
			if (Registered != null)
			{
				_log.Error($"Settings are already registered ({Registered.Target.GetType().Name}); Register<{typeof(T).Name}>() returns that object.");
				return Registered.Target as T;
			}
			T target = null;
			try
			{
				target = new T();
				string title = typeof(T).GetCustomAttribute<ModSettingsAttribute>()?.Title ?? _info.name;
				Registered = RegisteredSettings.Create(target, _info.id, title, FilePath, _log, this);
				RegisteredSettings.Add(Registered);
			}
			catch (Exception e)
			{
				_log.Error($"Settings could not be registered; the mod runs on its defaults: {e}");
			}
			return target;
		}

		internal void Raise(string field)
		{
			Action<string> changed = Changed;
			if (changed == null) return;
			foreach (Action<string> handler in changed.GetInvocationList())
			{
				try { handler(field); }
				catch (Exception e) { _log.Error($"A settings change handler failed for {field}: {e}"); }
			}
		}
	}

	/// <summary>One mod's registered settings. The Mods tab reads and changes them through this.</summary>
	public sealed class RegisteredSettings
	{
		private static readonly List<RegisteredSettings> _all = new List<RegisteredSettings>();
		private readonly List<SettingField> _fields = new List<SettingField>();
		private readonly Dictionary<SettingField, object> _pending = new Dictionary<SettingField, object>();
		internal readonly List<FieldInfo> Saved = new List<FieldInfo>();
		internal object Target;
		internal string FilePath;
		internal ModLogger Log;
		internal SettingsHandle Handle;

		/// <summary>Every mod that registered settings, in load order.</summary>
		public static IReadOnlyList<RegisteredSettings> All => _all;
		public string ModId { get; private set; }
		public string Title { get; private set; }
		public IReadOnlyList<SettingField> Fields => _fields;

		private RegisteredSettings() { }

		internal static void Add(RegisteredSettings s) => _all.Add(s);

		/// <summary>Reads the class's fields, then loads and rewrites the file. Not added to <see cref="All"/>.</summary>
		internal static RegisteredSettings Create(object target, string modId, string title, string filePath, ModLogger log, SettingsHandle handle)
		{
			var s = new RegisteredSettings { Target = target, ModId = modId, Title = title, FilePath = filePath, Log = log, Handle = handle };
			s.Scan();
			SettingsFile.Load(s);
			return s;
		}

		/// <summary>The current value; for a restart-only setting, the value chosen for the next start.</summary>
		public object Get(SettingField f) => _pending.TryGetValue(f, out object v) ? v : f.Field.GetValue(Target);

		/// <summary>True while a restart-only setting's saved value differs from the one the game started with.</summary>
		public bool RestartPending(SettingField f) => _pending.ContainsKey(f);

		/// <summary>Changes a setting the way the Mods tab does: converts and clamps the value, writes it
		/// (restart-only: keeps it pending), saves the file and raises Changed. An unchanged value does nothing.</summary>
		public void Set(SettingField f, object value)
		{
			try
			{
				object v = Coerce(f, value);
				if (Equals(v, Get(f))) return;
				Apply(f, v);
				SettingsFile.Save(this);
				Handle?.Raise(f.Name);
			}
			catch (Exception e)
			{
				Log.Error($"Could not change setting {f.Name} to {value}: {e.Message}");
			}
		}

		/// <summary>Puts every shown setting back to the value written in the mod's code.</summary>
		public void ResetToDefaults()
		{
			try
			{
				List<SettingField> changed = _fields.Where(f => !Equals(Get(f), f.Default)).ToList();
				if (changed.Count == 0) return;
				foreach (SettingField f in changed) Apply(f, f.Default);
				SettingsFile.Save(this);
				foreach (SettingField f in changed) Handle?.Raise(f.Name);
			}
			catch (Exception e)
			{
				Log.Error($"Could not reset settings to their defaults: {e.Message}");
			}
		}

		private void Apply(SettingField f, object v)
		{
			if (!f.RequiresRestart) { f.Field.SetValue(Target, v); return; }
			if (Equals(v, f.Field.GetValue(Target))) _pending.Remove(f);
			else _pending[f] = v;
		}

		private static object Coerce(SettingField f, object value)
		{
			switch (f.Kind)
			{
				case SettingKind.Toggle: return Convert.ToBoolean(value);
				case SettingKind.IntSlider: return Mathf.Clamp(Convert.ToInt32(value), (int)f.Min, (int)f.Max);
				case SettingKind.FloatSlider: return Mathf.Clamp(Convert.ToSingle(value), f.Min, f.Max);
				default:
					Type t = f.Field.FieldType;
					object e = value is string name ? Enum.Parse(t, name) : Enum.ToObject(t, value);
					if (!Enum.IsDefined(t, e)) throw new ArgumentException($"{value} is not a {t.Name}");
					return e;
			}
		}

		// Public instance fields in declaration order; [Setting] ones are shown when declared correctly.
		private void Scan()
		{
			string section = null;
			foreach (FieldInfo fi in Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => f.MetadataToken))
			{
				if (fi.IsInitOnly || fi.IsDefined(typeof(NonSerializedAttribute), false)) continue;
				Saved.Add(fi);
				section = fi.GetCustomAttribute<SectionAttribute>()?.Title ?? section;
				SettingAttribute setting = fi.GetCustomAttribute<SettingAttribute>();
				if (setting == null) continue;
				RangeAttribute range = fi.GetCustomAttribute<RangeAttribute>();
				Type t = fi.FieldType;
				SettingKind kind;
				string mistake = null;
				if (t == typeof(bool)) { kind = SettingKind.Toggle; if (range != null) mistake = "[Range] does not apply to a bool"; }
				else if (t == typeof(int) || t == typeof(float))
				{
					kind = t == typeof(int) ? SettingKind.IntSlider : SettingKind.FloatSlider;
					if (range == null) mistake = "a number needs [Range(min, max)]";
					else if (range.min > range.max) mistake = "its [Range] minimum is above its maximum";
				}
				else if (t.IsEnum) { kind = SettingKind.Dropdown; if (range != null) mistake = "[Range] does not apply to an enum"; }
				else { kind = SettingKind.Toggle; mistake = $"type {t.Name} is not supported (use bool, int, float or an enum)"; }
				if (mistake != null)
				{
					Log.Warning($"Setting {fi.Name} is not shown: {mistake}. It is still saved.");
					continue;
				}
				_fields.Add(new SettingField
				{
					Field = fi, Default = fi.GetValue(Target), Label = setting.Label, Description = setting.Description, Section = section,
					Kind = kind, Min = range?.min ?? 0f, Max = range?.max ?? 0f,
					RequiresRestart = fi.IsDefined(typeof(RequiresRestartAttribute), false),
					Options = t.IsEnum ? Enum.GetNames(t) : null,
				});
			}
		}
	}
}
