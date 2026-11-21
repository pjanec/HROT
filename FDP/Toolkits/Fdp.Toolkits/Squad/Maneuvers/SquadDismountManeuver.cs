using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ <c>CE-1056</c> I-2d — the squad DISMOUNTS: every member aboard a vehicle leaves it (beside the hull). One phase; the play ends
    /// it when nobody is aboard — the squad's SOP then fights (react to contact). 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §5a.
    /// </summary>
    public static class SquadDismountManeuver
    {
        public const ushort ManeuverKind = 10;

        public const ushort PhaseOut = 0;
        public const ushort PhaseAborted = 1;

        public const byte ElementAll = 0;
        /// <summary>A member told to get out.</summary>
        public const byte RoleDismounting = 1;
        /// <summary>A member already out, told to hold where it stands.</summary>
        public const byte RoleHolding = 2;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseOut, EventKind = PhaseEventKind.Abort, ToPhaseId = PhaseAborted },
            };
    }
}
