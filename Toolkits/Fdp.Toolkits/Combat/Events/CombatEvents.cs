using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Events
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
    // BATCH-10: HitEvent has been moved to Fdp.Core.HitEvent to break the circular
    // project dependency between FDP.Toolkit.Physics and FDP.Toolkit.Combat.
    // Import Fdp.Core (already referenced) to access HitEvent.
}
