using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Contracts
{
    /// <summary>
    /// Published by <c>HitResolutionSystem</c> when a bullet ray resolves to a hit.
    /// Consumed by damage-application systems in the Combat toolkit.
    ///
    /// <b>Placement note (DEBT-031):</b> Moved from <c>Fdp.Core</c> to this thin
    /// <c>FDP.Toolkit.Combat.Contracts</c> assembly to restore kernel purity.
    /// Both <c>FDP.Toolkit.Physics</c> and <c>FDP.Toolkit.Combat</c> reference
    /// <c>FDP.Toolkit.Combat.Contracts</c>; neither references the other directly,
    /// so no circular dependency exists.  The numeric event ID (5001) is unchanged —
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
