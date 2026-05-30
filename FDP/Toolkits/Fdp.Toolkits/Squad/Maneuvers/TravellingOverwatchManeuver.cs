using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// Configuration and role logic for the travelling-overwatch maneuver (§8.6b).
    ///
    /// Phases:
    ///   0 Moving   - Lead element advances; FarSideReached -> Arrived
    ///   1 Arrived  - Terminal: lead reached destination
    ///   2 Aborted  - Terminal
    ///
    /// Roles:
    ///   0 = Unassigned
    ///   1 = Lead      (advance to destination)
    ///   2 = Overwatch (hold position, eyes on threat)
    ///
    /// Elements:
    ///   0 = Lead element
    ///   1 = Overwatch element
    /// </summary>
    public static unsafe class TravellingOverwatchManeuver
    {
        public const ushort ManeuverKind = 6;

        public const ushort PhaseMoving  = 0;
        public const ushort PhaseArrived = 1;
        public const ushort PhaseAborted = 2;

        public const byte RoleUnassigned = 0;
        public const byte RoleLead       = 1;
        public const byte RoleOverwatch  = 2;

        public const byte ElementLead      = 0;
        public const byte ElementOverwatch = 1;

        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new PhaseTransitionEntry[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseMoving, EventKind = PhaseEventKind.FarSideReached, ToPhaseId = PhaseArrived },
                new PhaseTransitionEntry { FromPhaseId = PhaseMoving, EventKind = PhaseEventKind.Abort,          ToPhaseId = PhaseAborted },
            };

        // 4 candidates: 2 Lead + 2 Overwatch (supports 4-member squad with maxFocusFire=1)
        public static readonly RoleSlotCandidate[] StandardCandidates =
            new RoleSlotCandidate[]
            {
                new RoleSlotCandidate { RoleId = RoleLead      },
                new RoleSlotCandidate { RoleId = RoleLead      },
                new RoleSlotCandidate { RoleId = RoleOverwatch },
                new RoleSlotCandidate { RoleId = RoleOverwatch },
            };

        /// <summary>
        /// Computes element partition inputs: first half -> Lead, second half -> Overwatch.
        /// </summary>
        public static void ComputePartitionInputs(int memberCount, Span<MemberPartitionInput> inputs)
        {
            int half = Math.Max(1, memberCount / 2);
            for (int i = 0; i < memberCount; i++)
            {
                float leadScore      = i < half ? 1.0f : 0.1f;
                float overwatchScore = i < half ? 0.1f : 1.0f;
                inputs[i] = new MemberPartitionInput(leadScore, overwatchScore);
            }
        }

        /// <summary>Builds a 4-column score matrix. Lead element -> Lead role; Overwatch element -> Overwatch role.</summary>
        public static void BuildRoleScoreMatrix(
            ref SquadCognitiveState state, int memberCount, Span<float> scoreMatrix)
        {
            var membersSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(
                    ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

            for (int m = 0; m < memberCount; m++)
            {
                bool isLead = membersSpan[m] == ElementLead;
                scoreMatrix[m * 4 + 0] = isLead ? 1.0f : 0.1f;  // Lead0
                scoreMatrix[m * 4 + 1] = isLead ? 1.0f : 0.1f;  // Lead1
                scoreMatrix[m * 4 + 2] = isLead ? 0.1f : 1.0f;  // Overwatch0
                scoreMatrix[m * 4 + 3] = isLead ? 0.1f : 1.0f;  // Overwatch1
            }
        }
    }
}
