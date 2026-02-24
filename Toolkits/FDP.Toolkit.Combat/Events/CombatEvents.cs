using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Combat.Events
{
    // ── FireRequestEvent ──────────────────────────────────────────────────────

    /// <summary>
    /// Published by <see cref="Executors.AimAndFireExecutor"/> when weapon conditions
    /// are met and a shot is being requested.
    /// Consumed by FireProcessingSystem (Phase 5+) to spawn a bullet entity and
    /// register it in the physics pipeline.
    /// </summary>
    [EventId(CombatConstants.FireRequestEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct FireRequestEvent
    {
        /// <summary>Entity that is firing.</summary>
        public Entity Shooter;

        /// <summary>Intended target entity.</summary>
        public Entity Target;

        /// <summary>World-space origin of the shot (shooter's SimTransform.Position).</summary>
        public Vector3 Origin;

        /// <summary>Normalised world-space direction from shooter toward target.</summary>
        public Vector3 Direction;
    }

    // ── HitEvent ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Published by <c>HitResolutionSystem</c> when a bullet ray resolves to a hit.
    /// Consumed by damage-application systems in the Combat toolkit.
    ///
    /// <b>Migration note:</b> This event was previously defined in
    /// <c>FDP.Toolkit.Physics/Events/PhysicsEvents.cs</c>.  It has been moved here
    /// in BATCH-09 (DEBT-023 partial resolution) now that the Combat toolkit exists.
    /// The numeric event ID (5001) is unchanged — see <see cref="CombatConstants.HitEventId"/>.
    /// </summary>
    [EventId(CombatConstants.HitEventId)]
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
