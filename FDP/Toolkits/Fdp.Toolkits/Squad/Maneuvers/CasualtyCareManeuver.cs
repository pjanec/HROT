using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// ⭐ <c>CE-2152</c> C-c (R-2012 / R-2013, R-c; 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §7.5.3) — the casualty-care
    /// drill's phase table: the others SUPPRESS while the medic waits in cover; once every coverer has fired ("covering", ShotFired) the
    /// medic GOES and brings the man to a cover point (DefiladeReached ⇒ Done); fire picking up while he is still on the way sends him
    /// BACK (BoundComplete ⇒ Recall), and the squad decides again.
    /// <para>Elements: 0 = the coverers, 1 = the medic.</para>
    /// </summary>
    public static class CasualtyCareManeuver
    {
        public const ushort ManeuverKind = 11;

        public const ushort PhaseSuppress = 0;
        public const ushort PhaseGo       = 1;
        public const ushort PhaseDone     = 2;
        public const ushort PhaseRecall   = 3;
        public const ushort PhaseAborted  = 4;

        public const byte ElementCoverers = 0;
        public const byte ElementMedic    = 1;

        public const byte RoleCoverer = 1;
        public const byte RoleMedic   = 2;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new PhaseTransitionEntry[]
            {
                new() { FromPhaseId = PhaseSuppress, EventKind = PhaseEventKind.ShotFired,       ToPhaseId = PhaseGo      },
                new() { FromPhaseId = PhaseSuppress, EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted },
                new() { FromPhaseId = PhaseGo,       EventKind = PhaseEventKind.DefiladeReached, ToPhaseId = PhaseDone    },
                new() { FromPhaseId = PhaseGo,       EventKind = PhaseEventKind.BoundComplete,   ToPhaseId = PhaseRecall  },
                new() { FromPhaseId = PhaseGo,       EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted },
                new() { FromPhaseId = PhaseRecall,   EventKind = PhaseEventKind.BoundComplete,   ToPhaseId = PhaseAborted },
                new() { FromPhaseId = PhaseRecall,   EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted },
            };
    }
}
