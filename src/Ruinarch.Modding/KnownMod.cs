using System.Collections.Generic;

namespace Ruinarch.Modding
{
	public enum ModOrigin { Local, SteamWorkshop, Infrastructure }

	/// <summary>A discovered package, including disabled and rejected packages.</summary>
	public sealed class KnownMod
	{
		public ModInfo Info { get; internal set; }
		public string Directory { get; internal set; }
		public string DllPath { get; internal set; }
		public bool Enabled { get; internal set; }
		public bool Loaded { get; internal set; }
		public ModOrigin Origin { get; internal set; }
		public ulong WorkshopId { get; internal set; }
		public string RejectionReason { get; internal set; }
		public string FailureReason { get; internal set; }
		public bool Compatible => RejectionReason == null;
		public string Id => Info?.id;
		internal readonly Dictionary<string, string> Assemblies = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
	}
}
