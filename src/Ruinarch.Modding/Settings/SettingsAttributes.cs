using System;

namespace Ruinarch.Modding
{
	/// <summary>Marks a mod's settings class. The title is shown in the Settings window's Mods tab;
	/// without this attribute the manifest name is used.</summary>
	[AttributeUsage(AttributeTargets.Class)]
	public sealed class ModSettingsAttribute : Attribute
	{
		public string Title { get; }
		public ModSettingsAttribute(string title) { Title = title; }
	}

	/// <summary>Starts a section with this header. It covers this field and the ones after it, up to
	/// the next [Section]. Fields before the first section have no header.</summary>
	[AttributeUsage(AttributeTargets.Field)]
	public sealed class SectionAttribute : Attribute
	{
		public string Title { get; }
		public SectionAttribute(string title) { Title = title; }
	}

	/// <summary>Shows a field in the Mods tab with this label; the description shows while the mouse is
	/// over the row. Fields without it are saved but not shown.</summary>
	[AttributeUsage(AttributeTargets.Field)]
	public sealed class SettingAttribute : Attribute
	{
		public string Label { get; }
		public string Description { get; }
		public SettingAttribute(string label, string description = null) { Label = label; Description = description; }
	}

	/// <summary>A change is saved at once but only takes effect when the game next starts. Use it for
	/// a value the mod reads only while loading.</summary>
	[AttributeUsage(AttributeTargets.Field)]
	public sealed class RequiresRestartAttribute : Attribute
	{
	}
}
