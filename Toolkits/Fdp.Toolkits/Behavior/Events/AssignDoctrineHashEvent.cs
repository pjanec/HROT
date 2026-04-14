using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Events
{
    /// <summary>
    /// Unmanaged event published by <see cref="Systems.MissionDirectorSystem"/> when a phase
    /// transition requires a new doctrine to be activated by hash. This is a lightweight
    /// alternative to <see cref="AssignDoctrineEvent"/> for cases where the doctrine name is
    /// not known (only the integer hash from <see cref="Components.MissionPhase.DoctrineId"/>).
    ///
    /// <para>
    /// Consumed by <see cref="Systems.DoctrineIngressSystem"/>, which is the sole owner of
    /// <see cref="Components.DoctrineState"/> writes. This preserves single-ownership and
    /// eliminates the dual-write pattern present when <c>MissionDirectorSystem</c> mutated
    /// <c>DoctrineState</c> directly.
    /// </para>
    ///
    /// <para>
    /// <b>Note on BrainTier:</b> This event does NOT set <c>BrainTier</c>. The tier is
    /// established once when the entity is initialised via <see cref="AssignDoctrineEvent"/>.
    /// Phase transitions within an already-active brain keep the same tier.
    /// </para>
    /// </summary>
    [EventId(BehaviorConstants.EventId_AssignDoctrineHash)]
    public struct AssignDoctrineHashEvent
    {
        /// <summary>The entity whose doctrine should be updated.</summary>
        public Entity Entity;

        /// <summary>
        /// The integer doctrine hash (from <see cref="Components.MissionPhase.DoctrineId"/>)
        /// to assign to the entity.
        /// </summary>
        public int DoctrineHash;
    }
}
