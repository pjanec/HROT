using System.Runtime.InteropServices;

namespace Fdp.Kernel
{
    /// <summary>
    /// Published by <c>HitResolutionSystem</c> when a bullet ray resolves to a hit.
    /// Consumed by damage-application systems in the Combat toolkit.
    ///
    /// <b>Placement note (BATCH-10):</b> This event was previously defined in
    /// <c>FDP.Toolkit.Combat/Events/CombatEvents.cs</c>.  It has been moved here to
    /// <c>Fdp.Kernel</c> to break the circular project dependency that arises when
    /// <c>FDP.Toolkit.Combat</c> systems need <c>PhysicsCollider</c> and
    /// <c>RaycastBatchData</c> from <c>FDP.Toolkit.Physics</c> (which previously
    /// referenced Combat for HitEvent).  The numeric event ID (5001) is unchanged —
    /// see <c>CombatConstants.HitEventId</c> and <c>PhysicsConstants.HitEventId</c>.
    /// </summary>
    [EventId(5001)]   // = CombatConstants.HitEventId = PhysicsConstants.HitEventId; must not change.
    [StructLayout(LayoutKind.Sequential)]
    public struct HitEvent
    {
        /// <summary>The entity that was struck by the bullet.</summary>
        public Entity HitEntity;

        /// <summary>
        /// Index of the bullet entity that caused the hit.
        /// Extracted from the low 31 bits of <c>RaycastHit.RayId</c>
        /// when <c>PhysicsConstants.IsBulletRay</c> is true.
        /// </summary>
        public int BulletIndex;

        /// <summary>Hit parameter ∈ [0, 1] along the bullet's Start→End segment.</summary>
        public float HitT;
    }
}
