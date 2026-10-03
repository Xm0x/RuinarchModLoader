using System;
using Inner_Maps.Location_Structures;
using Locations.Settlements;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Describes a brand-new buildable structure a mod adds to the STOCK game. The
	/// framework allocates a virtual <c>STRUCTURE_TYPE</c> + <c>PLAYER_SKILL_TYPE</c> for
	/// it and makes the game's reflection factories, prefab lookup, classification
	/// switches and build menu treat it like a first-class structure - no source edits,
	/// no forked Assembly-CSharp.
	///
	/// A mod fills this in and passes it to <see cref="ModContent.RegisterStructure"/>.
	/// </summary>
	public sealed class StructureRegistration
	{
		/// <summary>
		/// Stable string id, e.g. <c>"ruinarch.plus.mass_grave"</c>. Drives the
		/// deterministic virtual-enum allocation, so the same id always maps to the same
		/// value across sessions/machines and saved structures keep loading.
		/// </summary>
		public string Id;

		/// <summary>Human display name (for menus/tooltips). Optional.</summary>
		public string DisplayName;

		/// <summary>
		/// Builds a fresh instance. Receives the allocated virtual type, which the mod's
		/// structure class must pass to its <c>base(STRUCTURE_TYPE, Region)</c> ctor.
		/// </summary>
		public Func<STRUCTURE_TYPE, Region, LocationStructure> Factory;

		/// <summary>Rebuilds an instance from a save. Receives the allocated virtual type.</summary>
		public Func<STRUCTURE_TYPE, Region, SaveDataLocationStructure, LocationStructure> LoadFactory;

		/// <summary>
		/// An existing structure whose <c>StructureData</c> (prefab/visual/footprint) this
		/// reuses, so no new Unity asset is required. e.g. <c>STRUCTURE_TYPE.CRYPT</c>.
		/// </summary>
		public STRUCTURE_TYPE PrefabSource;

		/// <summary>
		/// The build-skill instance (a <c>DemonicStructurePlayerSkill</c> subclass) that
		/// makes the structure appear in the demonic build menu. Its <c>type</c> getter
		/// should return <see cref="SkillType"/> (resolve it lazily via
		/// <see cref="ModContent.SkillTypeFor"/> so it reads correctly after registration).
		/// </summary>
		public DemonicStructurePlayerSkill Skill;

		/// <summary>
		/// Makes <see cref="Skill"/> when the game builds its skill tables (at the main menu),
		/// for skills that cannot be constructed in <c>OnLoad</c>: the game's skill classes
		/// read game state in their constructors. Receives the allocated structure and skill
		/// types. Used when <see cref="Skill"/> is null.
		/// </summary>
		public Func<STRUCTURE_TYPE, PLAYER_SKILL_TYPE, DemonicStructurePlayerSkill> CreateSkill;

		/// <summary>
		/// If set, the structure's build-skill is granted to the player whenever they gain
		/// this source skill (e.g. <c>PLAYER_SKILL_TYPE.CRYPT</c>), so it shows in the menu
		/// alongside a thematically-related structure. <c>NONE</c> = mod grants it itself.
		/// </summary>
		public PLAYER_SKILL_TYPE UnlockWith = PLAYER_SKILL_TYPE.NONE;

		/// <summary>
		/// Whose settings to copy for the build-skill: icon, mana cost, charges and cooldown by
		/// level. The game needs this <c>PlayerSkillData</c> asset for every skill it grants.
		/// <c>NONE</c> takes <see cref="UnlockWith"/>, else the skill that builds
		/// <see cref="PrefabSource"/>.
		/// </summary>
		public PLAYER_SKILL_TYPE SkillDataFrom = PLAYER_SKILL_TYPE.NONE;

		/// <summary>Optional: adjust the copied settings (for example the icon) before use.</summary>
		public Action<PlayerSkillData> ConfigureSkillData;

		/// <summary>Classification flags mirrored into the game's <c>Extensions</c> switches.
		/// A demonic (player-built) structure is <c>IsPlayerStructure</c>; the game has no
		/// separate demonic switch.</summary>
		public bool IsPlayerStructure = true;
		public bool IsSpecialStructure = false;
		public bool IsVillageStructure = false;

		// --- filled by the framework during RegisterStructure ---

		/// <summary>The virtual <c>STRUCTURE_TYPE</c> allocated for this content.</summary>
		public STRUCTURE_TYPE StructureType { get; internal set; }

		/// <summary>The virtual <c>PLAYER_SKILL_TYPE</c> allocated for this content's skill.</summary>
		public PLAYER_SKILL_TYPE SkillType { get; internal set; }
	}
}
