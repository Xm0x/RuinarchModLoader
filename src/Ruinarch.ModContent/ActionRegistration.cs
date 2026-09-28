using System;
using System.Collections.Generic;

namespace Ruinarch.ModContent
{
	/// <summary>
	/// One state of a registered action: what the villager is doing, for how long, and
	/// whether finishing it counts as success. The game calls the action class's
	/// <c>Pre&lt;State&gt;</c>, <c>PerTick&lt;State&gt;</c> and <c>After&lt;State&gt;</c> methods
	/// (state name without spaces, e.g. <c>AfterWriteSuccess</c>) if the class has them.
	/// </summary>
	public sealed class ActionState
	{
		/// <summary>State name, e.g. "Write Success". The action enters it with
		/// <c>SetState(name, node)</c> from its <c>Perform</c>.</summary>
		public string Name;

		/// <summary>Length in game ticks (20 ticks = 1 game hour).</summary>
		public int DurationTicks;

		/// <summary>True if finishing this state means the action succeeded.</summary>
		public bool Success;

		/// <summary>
		/// Optional. The text of the action's log, asked for when the state starts. The log
		/// involves the actor and the target, so it shows in both their Logs tabs and is kept
		/// when the state finishes (an interrupted action keeps nothing). Null: no log.
		/// </summary>
		public Func<ActualGoapNode, string> Describe;

		public ActionState(string name, int durationTicks, bool success, Func<ActualGoapNode, string> describe = null)
		{
			Name = name;
			DurationTicks = durationTicks;
			Success = success;
			Describe = describe;
		}
	}

	/// <summary>
	/// A brand-new villager action (a new <c>INTERACTION_TYPE</c>), registered with
	/// <see cref="ModContent.RegisterAction"/>.
	/// </summary>
	public sealed class ActionRegistration
	{
		/// <summary>Stable, unique id, e.g. "mymod.write". Saves store the action by the value
		/// derived from it, so never change it once released.</summary>
		public string Id;

		/// <summary>The type's name in enum style, e.g. "WRITE_RECORD". The game shows it as
		/// "Write Record".</summary>
		public string Name;

		/// <summary>Makes the one instance of the action class (a <c>GoapAction</c> subclass
		/// whose constructor passes <see cref="Type"/> to the base).</summary>
		public Func<GoapAction> Factory;

		/// <summary>The action's states; at least one.</summary>
		public List<ActionState> States = new List<ActionState>();

		/// <summary>Optional. The villager's thought bubble (under their name on the map, in
		/// their panel and tooltip) while they walk to the target, e.g. "Going to write.".
		/// Null: "Going to " and the action's name. The game's UI needs one for every action.</summary>
		public Func<ActualGoapNode, string> Going;

		/// <summary>Optional. The thought bubble while they do it, e.g. "Writing.". Null: the
		/// action's name.</summary>
		public Func<ActualGoapNode, string> Doing;

		/// <summary>Filled in by <see cref="ModContent.RegisterAction"/>.</summary>
		public INTERACTION_TYPE Type { get; internal set; }
	}
}
