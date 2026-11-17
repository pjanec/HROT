using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ <c>CE-4622</c> K-4 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-1) — the squad MANS A CHECKPOINT: the stopper at the stop
    /// line, two searchers at the search bay (both accepted exposed, CP-1a), the overwatch machine gun and the reaction men behind at the STAND-OFF,
    /// every one watching the approach. One phase; the play holds it (a checkpoint has no end of its own).
    /// </summary>
    public static class CheckpointManeuver
    {
        public const ushort ManeuverKind = 19;

        public const ushort PhaseManning = 0;
        public const ushort PhaseAborted = 1;

        public const byte ElementAll = 0;
        /// <summary>At the stop line, beside the lane: he signals and stops the vehicle.</summary>
        public const byte RoleStopper = 1;
        /// <summary>At the search bay: he searches the stopped vehicle and its people.</summary>
        public const byte RoleSearcher = 2;
        /// <summary>The machine gun covering the approach, at the stand-off.</summary>
        public const byte RoleOverwatch = 3;
        /// <summary>The rest, at the stand-off: the reaction.</summary>
        public const byte RoleReaction = 4;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseManning, EventKind = PhaseEventKind.Abort, ToPhaseId = PhaseAborted },
            };
    }
}
