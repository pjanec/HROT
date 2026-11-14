using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ CE-1118 H-1 — the squad FAST-ROPES out of a hovering helicopter: one man per rope at a time, the next when the one below is 3 m
    /// down; each lander takes his spot in a 360° ring round the rope point (on a roof, inside its outline). One phase — the play paces the
    /// men itself. 📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.
    /// </summary>
    public static class SquadFastRopeManeuver
    {
        public const ushort ManeuverKind = 15;

        public const ushort PhaseRoping = 0;
        public const ushort PhaseAborted = 1;

        /// <summary>A member waiting aboard for his turn.</summary>
        public const byte RoleWaiting = 1;
        /// <summary>A member told to go down his rope.</summary>
        public const byte RoleRoping = 2;
        /// <summary>A member down, told to take his spot in the ring.</summary>
        public const byte RoleSecuring = 3;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[] { new PhaseTransitionEntry { FromPhaseId = PhaseRoping, EventKind = PhaseEventKind.Abort, ToPhaseId = PhaseAborted } };
    }

    /// <summary>
    /// ⭐ CE-1118 H-1 — the JUMPMASTER: the stick leaves the aircraft one per second along the Drop leg, each man at his release point with
    /// his landing point; then every man down takes his spot in the assembly ring. 📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.
    /// </summary>
    public static class SquadAirborneExitManeuver
    {
        public const ushort ManeuverKind = 16;

        public const ushort PhaseJumping = 0;
        public const ushort PhaseAborted = 1;

        /// <summary>A member waiting aboard for his turn at the door.</summary>
        public const byte RoleWaiting = 1;
        /// <summary>A member told to jump.</summary>
        public const byte RoleJumping = 2;
        /// <summary>A member under his canopy.</summary>
        public const byte RoleDescending = 3;
        /// <summary>A member down, told to take his spot in the assembly ring.</summary>
        public const byte RoleAssembling = 4;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[] { new PhaseTransitionEntry { FromPhaseId = PhaseJumping, EventKind = PhaseEventKind.Abort, ToPhaseId = PhaseAborted } };
    }

    /// <summary>
    /// ⭐ CE-1118 H-1 (D-22) — the squad ASSEMBLES: every member down takes his spot in a ring round the rally point; a man still coming
    /// down or aboard keeps what he does. 📄 docs/designs/airborne/DESIGN_Airborne_Insertion.md §6.
    /// </summary>
    public static class AssembleAfterDropManeuver
    {
        public const ushort ManeuverKind = 17;

        public const ushort PhaseAssembling = 0;
        public const ushort PhaseAborted = 1;

        /// <summary>A member still coming down (or aboard).</summary>
        public const byte RoleDescending = 1;
        /// <summary>A member down, told to take his spot.</summary>
        public const byte RoleAssembling = 2;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new[] { new PhaseTransitionEntry { FromPhaseId = PhaseAssembling, EventKind = PhaseEventKind.Abort, ToPhaseId = PhaseAborted } };
    }
}
