using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ruinarch.Modding
{
	/// <summary>Reads and writes one mod's <c>Mods/settings/&lt;mod id&gt;.json</c>: one property per
	/// public field, enums by name. A file that cannot be read is set aside, never overwritten.</summary>
	internal static class SettingsFile
	{
		private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings { Converters = { new StringEnumConverter() } });

		internal static void Load(RegisteredSettings s)
		{
			string name = Path.GetFileName(s.FilePath);
			if (File.Exists(s.FilePath))
			{
				JObject json = null;
				try
				{
					json = JObject.Parse(File.ReadAllText(s.FilePath));
				}
				catch (Exception e)
				{
					string bad = s.FilePath + ".bad";
					try
					{
						if (File.Exists(bad)) File.Delete(bad);
						File.Move(s.FilePath, bad);
					}
					catch (Exception m)
					{
						s.Log.Error($"{name} could not be read ({e.Message}) or set aside ({m.Message}); the mod runs on its defaults and the file is left as it is.");
						return;
					}
					s.Log.Warning($"{name} could not be read ({e.Message}); it was renamed to {Path.GetFileName(bad)} and the mod starts on its default settings.");
				}
				if (json != null)
				{
					foreach (FieldInfo f in s.Saved) Read(s, json, f);
				}
			}
			foreach (SettingField f in s.Fields) Clamp(s, f);
			Save(s);
		}

		private static void Read(RegisteredSettings s, JObject json, FieldInfo f)
		{
			JToken token = json[f.Name];
			if (token == null) return;
			try
			{
				object value = token.ToObject(f.FieldType, Serializer);
				if (f.FieldType.IsEnum && !Enum.IsDefined(f.FieldType, value)) throw new FormatException($"{f.FieldType.Name} has no value {token}");
				f.SetValue(s.Target, value);
			}
			catch (Exception e)
			{
				s.Log.Warning($"Setting {f.Name}: the saved value {token.ToString(Formatting.None)} could not be used ({e.Message}); keeping the default {f.GetValue(s.Target)}.");
			}
		}

		private static void Clamp(RegisteredSettings s, SettingField f)
		{
			object value = f.Field.GetValue(s.Target);
			object clamped = f.Kind == SettingKind.IntSlider ? Mathf.Clamp((int)value, (int)f.Min, (int)f.Max)
				: f.Kind == SettingKind.FloatSlider ? (object)Mathf.Clamp((float)value, f.Min, f.Max) : value;
			if (Equals(clamped, value)) return;
			f.Field.SetValue(s.Target, clamped);
			s.Log.Warning($"Setting {f.Name}: {value} is outside {f.Min}..{f.Max}; using {clamped}.");
		}

		internal static void Save(RegisteredSettings s)
		{
			try
			{
				var json = new JObject();
				foreach (FieldInfo f in s.Saved)
				{
					SettingField shown = s.Fields.FirstOrDefault(x => x.Field == f);
					object value = shown != null ? s.Get(shown) : f.GetValue(s.Target);
					json[f.Name] = value == null ? JValue.CreateNull() : JToken.FromObject(value, Serializer);
				}
				Directory.CreateDirectory(Path.GetDirectoryName(s.FilePath));
				File.WriteAllText(s.FilePath, json.ToString(Formatting.Indented));
			}
			catch (Exception e)
			{
				s.Log.Error($"Could not save {Path.GetFileName(s.FilePath)}: {e.Message}");
			}
		}
	}
}
