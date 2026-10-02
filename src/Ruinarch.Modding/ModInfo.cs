using System;

namespace Ruinarch.Modding
{
	/// <summary>API 1 package metadata from a validated root mod.json.</summary>
	[Serializable]
	public class ModInfo
	{
		public string id;
		public string name;
		public string version;
		public string author;
		public string description;
		public string loader;
		public int loaderApi;
		public string type;
		public string entryAssembly;
		public string entryType;
		public string[] dependencies;

		public override string ToString() { return $"{name} v{version} by {author} ({id})"; }
	}
}
