using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ <c>CE-2158</c> F-b (R-2013; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.6.1) — fire and movement's phase table,
    /// for a squad IN contact: one half is the base of fire, the other rushes to cover. A bound is two phases: in <c>Cover</c> the
    /// movers wait while the base of fire suppresses ("Covering!" ⇒ ShotFired); in <c>Rush</c> they go to their cover points
    /// ("Set!" ⇒ DefiladeReached), and the halves swap.
    /// <para>Elements: 0 and 1, the two halves.</para>
    /// </summary>
    public static class FireAndMovementManeuver
    {
        public const ushort ManeuverKind = 12;

        public const ushort PhaseCover0  = 0;
        public const ushort PhaseRush0   = 1;
        public const ushort PhaseCover1  = 2;
        public const ushort PhaseRush1   = 3;
        public const ushort PhaseAborted = 4;

        public const byte RoleMoving   = 1;
        public const byte RoleCovering = 2;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new PhaseTransitionEntry[]
            {
                new() { FromPhaseId = PhaseCover0, EventKind = PhaseEventKind.ShotFired,       ToPhaseId = PhaseRush0   },
                new() { FromPhaseId = PhaseRush0,  EventKind = PhaseEventKind.DefiladeReached, ToPhaseId = PhaseCover1  },
                new() { FromPhaseId = PhaseCover1, EventKind = PhaseEventKind.ShotFired,       ToPhaseId = PhaseRush1   },
                new() { FromPhaseId = PhaseRush1,  EventKind = PhaseEventKind.DefiladeReached, ToPhaseId = PhaseCover0  },
                new() { FromPhaseId = PhaseCover0, EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted },
                new() { FromPhaseId = PhaseRush0,  EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted },
                new() { FromPhaseId = PhaseCover1, EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted },
                new() { FromPhaseId = PhaseRush1,  EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted },
            };

        /// <summary>The half that moves in this bound (<c>Cover0</c>/<c>Rush0</c> ⇒ 0).</summary>
        public static byte MovingElement(ushort phase) => phase is PhaseCover1 or PhaseRush1 ? (byte)1 : (byte)0;

        /// <summary>The movers are on their way.</summary>
        public static bool IsRush(ushort phase) => phase is PhaseRush0 or PhaseRush1;
    }
}
