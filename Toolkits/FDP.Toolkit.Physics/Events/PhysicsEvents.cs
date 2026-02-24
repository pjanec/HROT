using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Physics.Events
{
    // ── HitEvent ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Published by <see cref="Systems.HitResolutionSystem"/> when a bullet ray resolves to a hit.
    /// Consumed by the Combat toolkit (Phase 5) to apply damage.
    /// <para>
    /// <b>Ownership note:</b> This event is defined here in <c>FDP.Toolkit.Physics</c> because
    /// the Combat toolkit (<c>FDP.Toolkit.Combat</c>) does not yet exist as of Phase 4.
    /// When the Combat toolkit is introduced it should either reference this assembly for the
    /// event type or take ownership of the type and update this assembly's reference.
    /// </para>
    /// </summary>
    [EventId(PhysicsConstants.HitEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct HitEvent
    {
        /// <summary>The entity that was struck by the bullet.</summary>
        public Entity HitEntity;

        /// <summary>
        /// Index of the bullet entity that caused the hit.
        /// Extracted from the low 31 bits of <see cref="Components.RaycastHit.RayId"/>
        /// when <see cref="PhysicsConstants.IsBulletRay"/> is true.
        /// </summary>
        public int BulletIndex;

        /// <summary>Hit parameter ∈ [0,1] along the bullet's Start→End segment.</summary>
        public float HitT;
    }
}
