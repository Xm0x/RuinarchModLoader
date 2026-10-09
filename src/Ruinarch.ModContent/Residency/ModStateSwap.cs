using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// Mods keep their per-world state through <see cref="ModSave"/>; residency swaps it with
	/// the world: <c>save()</c> when a world freezes, <c>load(null)</c> before the next world,
	/// <c>load(json)</c> when it returns.
	/// </summary>
	internal static class ModStateSwap
	{
		internal static Dictionary<string, string> Capture() => ModSave.CaptureAll();

		internal static void Restore(Dictionary<string, string> state) => ModSave.RestoreAll(state);

		internal static void Reset() => ModSave.ResetAll();

		/// <summary>
		/// After a warm return, every handler must report exactly what it reported when its
		/// world froze; anything else means the mod keeps world state outside its handler.
		/// </summary>
		internal static void CheckRoundTrip(Dictionary<string, string> before)
		{
			Dictionary<string, string> after = ModSave.CaptureAll();
			foreach (KeyValuePair<string, string> kv in before)
			{
				after.TryGetValue(kv.Key, out string now);
				if (!string.Equals(kv.Value, now, StringComparison.Ordinal))
				{
					Debug.LogError("[Residency] ModSave handler '" + kv.Key + "' changed while its world was frozen; its state is not world-owned.");
				}
			}
		}
	}
}
