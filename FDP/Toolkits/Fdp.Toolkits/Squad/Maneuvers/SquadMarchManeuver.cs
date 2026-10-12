using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ <c>CE-2123</c> S3 — the squad MARCHES: the lead walks to the objective, the others keep a File / Wedge / Line slot on it and
    /// fall back to the lead's own trail when a slot does not work. One phase; the play ends it (everyone there).
    /// 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §6.1.
    /// </summary>
    public static class SquadMarchManeuver
    {
        public const ushort ManeuverKind = 9;

        public const ushort PhaseMarching = 0;
        public const ushort PhaseAborted  = 1;

        public const byte ElementAll = 0;

        public const byte RoleLead     = 1;
        public const byte RoleFollower = 2;

        /// <summary>The authored shapes (the contract's <c>Shape</c>).</summary>
        public const byte ShapeFile  = 0;
        public const byte ShapeWedge = 1;
        public const byte ShapeLine  = 2;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseMarching, EventKind = PhaseEventKind.Abort, ToPhaseId = PhaseAborted },
            };
    }
}
