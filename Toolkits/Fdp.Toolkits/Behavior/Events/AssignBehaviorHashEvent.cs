using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Unmanaged event published by <see cref="Systems.MissionDirectorSystem"/> when a phase
    /// transition requires a new behavior to be activated by hash. This is a lightweight
    /// alternative to <see cref="AssignBehaviorEvent"/> for cases where the behavior name is
    /// not known (only the integer hash from <see cref="Components.MissionPhase.BehaviorId"/>).
    ///
    /// <para>
    /// Consumed by <see cref="Systems.BehaviorIngressSystem"/>, which is the sole owner of
    /// <see cref="Components.BehaviorState"/> writes. This preserves single-ownership and
    /// eliminates the dual-write pattern present when <c>MissionDirectorSystem</c> mutated
    /// <c>BehaviorState</c> directly.
    /// </para>
    ///
    /// <para>
    /// <b>Note on BrainTier:</b> This event does NOT set <c>BrainTier</c>. The tier is
    /// established once when the entity is initialised via <see cref="AssignBehaviorEvent"/>.
    /// Phase transitions within an already-active brain keep the same tier.
    /// </para>
    /// </summary>
    [EventId(BehaviorConstants.EventId_AssignBehaviorHash)]
    public struct AssignBehaviorHashEvent
    {
        /// <summary>The entity whose behavior should be updated.</summary>
        public Entity Entity;

        /// <summary>
        /// The integer behavior hash (from <see cref="Components.MissionPhase.BehaviorId"/>)
        /// to assign to the entity.
        /// </summary>
        public int BehaviorHash;
    }
}
