using System.Collections.Generic;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Internal store of registered content + the save-stable virtual enum allocator.
	///
	/// Virtual enum values live far above any real game value (the game's real
	/// STRUCTURE_TYPE / PLAYER_SKILL_TYPE tops out in the low hundreds) and far above any
	/// plausible future game addition. Crucially, <c>Enum.GetValues()</c> never returns
	/// them - so world-gen and every other "iterate all enum values" loop ignores virtual
	/// content entirely. Virtual values only ever surface through the specific reflection
	/// factories the framework patches, which is exactly the control we want.
	/// </summary>
	internal static class ContentRegistry
	{
		internal const int VirtualBase = 100000;
		internal const int VirtualRange = 900000; // [100000, 1000000)

		internal static readonly Dictionary<int, StructureRegistration> StructuresByType = new Dictionary<int, StructureRegistration>();
		internal static readonly Dictionary<int, StructureRegistration> StructuresBySkill = new Dictionary<int, StructureRegistration>();
		internal static readonly List<StructureRegistration> Structures = new List<StructureRegistration>();

		internal static bool IsVirtual(int enumValue)
		{
			return enumValue >= VirtualBase && enumValue < VirtualBase + VirtualRange;
		}

		/// <summary>
		/// Deterministic FNV-1a hash of the id into the virtual range, linear-probing on
		/// collision. Same id -> same value every run, so saved content stays loadable.
		/// </summary>
		internal static int AllocateValue(string id, Dictionary<int, StructureRegistration> taken)
		{
			uint hash = 2166136261u;
			for (int i = 0; i < id.Length; i++)
			{
				hash ^= id[i];
				hash *= 16777619u;
			}
			int slot = VirtualBase + (int)(hash % (uint)VirtualRange);
			while (taken.ContainsKey(slot))
			{
				slot++;
				if (slot >= VirtualBase + VirtualRange)
				{
					slot = VirtualBase;
				}
			}
			return slot;
		}
	}
}
