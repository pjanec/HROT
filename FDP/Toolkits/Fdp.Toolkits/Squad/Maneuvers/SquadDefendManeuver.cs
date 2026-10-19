using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ <c>CE-2275</c> D-1 — the squad DEFENDS a position: it moves there, then each man holds his post (the play picks them). Two
    /// phases; the play never ends it (it runs until the next order). 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8c.
    /// </summary>
    public static class SquadDefendManeuver
    {
        public const ushort ManeuverKind = 18;

        public const ushort PhaseMoving  = 0;
        public const ushort PhasePosting = 1;
        public const ushort PhaseAborted = 2;

        public const byte RoleMoving  = 1;
        public const byte RolePosted  = 2;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseMoving,  EventKind = PhaseEventKind.BoundComplete, ToPhaseId = PhasePosting },
                new PhaseTransitionEntry { FromPhaseId = PhaseMoving,  EventKind = PhaseEventKind.Abort,         ToPhaseId = PhaseAborted },
                new PhaseTransitionEntry { FromPhaseId = PhasePosting, EventKind = PhaseEventKind.Abort,         ToPhaseId = PhaseAborted },
            };
    }
}
