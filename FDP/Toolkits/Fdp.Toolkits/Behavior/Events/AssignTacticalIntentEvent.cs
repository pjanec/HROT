using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Managed event that requests assignment of a tactical intent to an entity.
    /// Published by a Commander AI or <c>MissionAdapterSystem</c> and consumed by
    /// <see cref="Systems.TacticalIntentResolutionSystem"/> (Phase 2), which translates
    /// it into an <see cref="AssignBehaviorEvent"/> using the registered mapper registry
    /// or a direct pass-through fallback.
    ///
    /// Must be a class (not a struct) because it carries managed string fields,
    /// exactly like <see cref="AssignBehaviorEvent"/>.
    ///
    /// <para>
    /// <b>No IsRemote flag:</b> Authority-based gates in
    /// <c>TacticalIntentResolutionSystem</c> and the Phase 5 egress translator
    /// (both keyed on <c>HasAuthority&lt;BehaviorState&gt;</c>) are sufficient to prevent
    /// echo loops in a distributed topology. Adding a flag would be redundant and would
    /// re-introduce sender-side network knowledge.
    /// </para>
    /// </summary>
    public sealed class AssignTacticalIntentEvent
    {
        /// <summary>The entity to assign the tactical intent to.</summary>
        public Entity Entity;

        /// <summary>
        /// Generic tactical intent identifier, e.g. <c>"DefendArea"</c> or
        /// <c>"ConvoyEscort"</c>.  Resolved by a registered
        /// <c>ITacticalOrderMapper</c>, or treated as a direct behavior name
        /// when no mapper is found (pass-through fallback).
        /// </summary>
        public string IntentId = string.Empty;

        /// <summary>
        /// Serialised JSON parameter payload for the intent, e.g. lat/lon coordinates
        /// or network entity IDs.  Passed verbatim to the mapper or to
        /// <see cref="AssignBehaviorEvent.JsonParams"/> on the fallback path.
        /// Empty string is valid when the intent carries no configurable parameters.
        /// </summary>
        public string JsonParams = string.Empty;
        /// <summary>⭐ <c>CE-3034</c> — who asks; carried onto the behaviour the intent resolves to and over DDS.</summary>
        public Fdp.Toolkit.Behavior.Components.BehaviorOrigin Origin;

        /// <summary>⭐ <c>CE-3167</c> — assign (default) or clear. 📄 <c>DESIGN_Sensors_And_Doctrine.md</c> §7.7: <i>"the ONE wire
        /// path is the intent topic, which gains a <c>Kind</c> beside <c>Origin</c>"</i> — so releasing a unit travels exactly as
        /// an order does, from any node.</summary>
        public TacticalIntentKind Kind;

        /// <summary>⭐ <c>CE-3167</c> (Entity Commanding §2.5b) — before the behaviour starts, the owner runs every registered
        /// <see cref="TacticalOrderMapper.IIntentClearProvider"/> (today: the mission plan) in the same system call, so an
        /// immediate order cannot be undone by a plan resuming at its next phase.</summary>
        public bool ClearsPriorIntent;
    }

    /// <summary>⭐ <c>CE-3167</c> — what an <see cref="AssignTacticalIntentEvent"/> asks for (a byte on the wire).</summary>
    public enum TacticalIntentKind : byte
    {
        /// <summary>Start the behaviour <see cref="AssignTacticalIntentEvent.IntentId"/> names.</summary>
        Assign = 0,
        /// <summary>End the running behaviour (a <see cref="ClearBehaviorEvent"/> at the intent's origin) — the SOP picks next.</summary>
        Clear = 1,
    }
}
