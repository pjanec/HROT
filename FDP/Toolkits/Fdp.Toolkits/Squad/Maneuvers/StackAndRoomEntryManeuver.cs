using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// Configuration and role logic for the stack-and-room-entry maneuver (§8.6a).
    ///
    /// Phases:
    ///   0 Stacking  - Stack at door; BoundComplete -> Entering
    ///   1 Entering  - Enter in sequence; FarSideReached -> Cleared
    ///   2 Cleared   - Terminal: room secure
    ///   3 Aborted   - Terminal
    ///
    /// Roles:
    ///   0 = Unassigned
    ///   1 = PointMan    (enters first, front sector)
    ///   2 = BreachCover (covers door)
    ///   3 = Secondary   (enters after PointMan)
    /// </summary>
    public static class StackAndRoomEntryManeuver
    {
        public const ushort ManeuverKind = 5;

        public const ushort PhaseStacking  = 0;
        public const ushort PhaseEntering  = 1;
        public const ushort PhaseCleared   = 2;
        public const ushort PhaseAborted   = 3;

        public const byte RoleUnassigned  = 0;
        public const byte RolePointMan    = 1;
        public const byte RoleBreachCover = 2;
        public const byte RoleSecondary   = 3;

        /// <summary>Builds the phase-transition table.</summary>
        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new PhaseTransitionEntry[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseStacking, EventKind = PhaseEventKind.BoundComplete,  ToPhaseId = PhaseEntering },
                new PhaseTransitionEntry { FromPhaseId = PhaseStacking, EventKind = PhaseEventKind.Abort,          ToPhaseId = PhaseAborted  },
                new PhaseTransitionEntry { FromPhaseId = PhaseEntering, EventKind = PhaseEventKind.FarSideReached, ToPhaseId = PhaseCleared  },
                new PhaseTransitionEntry { FromPhaseId = PhaseEntering, EventKind = PhaseEventKind.Abort,          ToPhaseId = PhaseAborted  },
            };

        // 4 candidates: 1 PointMan + 1 BreachCover + 2 Secondary
        // (allows 4-member squad to get distinct roles with maxFocusFire=1)
        public static readonly RoleSlotCandidate[] StandardCandidates =
            new RoleSlotCandidate[]
            {
                new RoleSlotCandidate { RoleId = RolePointMan    },
                new RoleSlotCandidate { RoleId = RoleBreachCover },
                new RoleSlotCandidate { RoleId = RoleSecondary   },
                new RoleSlotCandidate { RoleId = RoleSecondary   },
            };

        /// <summary>
        /// Score matrix: member 0 (point man candidate) gets high PointMan score;
        /// member 1 gets high BreachCover; members 2+ get Secondary.
        /// </summary>
        public static void BuildRoleScoreMatrix(int memberCount, Span<float> scoreMatrix)
        {
            for (int m = 0; m < memberCount; m++)
            {
                // Columns: 0=PointMan, 1=BreachCover, 2=Secondary0, 3=Secondary1
                scoreMatrix[m * 4 + 0] = (m == 0) ? 1.0f : 0.1f;
                scoreMatrix[m * 4 + 1] = (m == 1) ? 1.0f : 0.1f;
                scoreMatrix[m * 4 + 2] = (m >= 2) ? 1.0f : 0.1f;
                scoreMatrix[m * 4 + 3] = (m >= 2) ? 1.0f : 0.1f;
            }
        }
    }
}
