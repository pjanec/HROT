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
    }
}
