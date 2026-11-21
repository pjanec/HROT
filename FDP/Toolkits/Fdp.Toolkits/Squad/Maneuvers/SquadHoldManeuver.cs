using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> S2 — the squad HOLDS: every member stays where it is and fights. One phase; the play ends it (a time limit,
    /// or no contact left). 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.
    /// </summary>
    public static class SquadHoldManeuver
    {
        public const ushort ManeuverKind = 7;

        public const ushort PhaseHolding = 0;
        public const ushort PhaseAborted = 1;

        public const byte ElementAll = 0;
        public const byte RoleHolding = 1;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseHolding, EventKind = PhaseEventKind.Abort, ToPhaseId = PhaseAborted },
            };
    }

    /// <summary>
    /// ⭐ <c>CE-2123</c> S2 — BREAK CONTACT: get out of the enemy's sight, element covering element. Its own kind on the squad (so
    /// it never reads as the bounding it may have paused), the bounding table's phases and roles (element 0 moves while element 1
    /// covers, then the other way). 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §4.5 S2-F.
    /// </summary>
    public static class BreakContactManeuver
    {
        public const ushort ManeuverKind = 8;

        public static PhaseTransitionEntry[] BuildTransitionTable() => BoundingOverwatchManeuver.BuildTransitionTable();
    }
}
