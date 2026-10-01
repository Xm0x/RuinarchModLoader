using Newtonsoft.Json;

namespace Ruinarch.ModContent.Templates
{
	internal static class TemplateJson
	{
		/// <summary>Palette keys, one character per tile ('.' is empty), in the order export assigns them.</summary>
		internal const string Keys = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#$%&*+-<=>?@^_~!";

		// An unknown field is an error: a misspelled one would otherwise be dropped silently.
		private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
		{
			Formatting = Formatting.Indented,
			NullValueHandling = NullValueHandling.Ignore,
			MissingMemberHandling = MissingMemberHandling.Error,
		};

		internal static string ToJson(BuildingTemplate t)
		{
			return JsonConvert.SerializeObject(t, Settings);
		}

		internal static BuildingTemplate FromJson(string json)
		{
			try
			{
				BuildingTemplate t = JsonConvert.DeserializeObject<BuildingTemplate>(json, Settings);
				if (t == null)
				{
					throw new TemplateException("the file is empty");
				}
				return t;
			}
			catch (JsonException e)
			{
				throw new TemplateException("not a valid template: " + e.Message);
			}
		}
	}
}
