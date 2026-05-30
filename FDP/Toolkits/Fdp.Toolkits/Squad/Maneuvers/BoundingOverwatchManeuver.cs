using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// Configuration and role-swap logic for the 3-phase bounding-overwatch maneuver.
    ///
    /// Phases:
    ///   0 Element0Moving  - Element 0 bounds; Element 1 covers (BoundComplete)
    ///   1 Element1Moving  - Element 1 bounds; Element 0 covers (BoundComplete)
    ///   2 Aborted         - Terminal phase (Abort event)
    ///
    /// Roles:
    ///   RoleId 0 = Unassigned
    ///   RoleId 1 = Moving   (executing the bound)
    ///   RoleId 2 = Covering (fire/overwatch)
    ///
    /// Elements:
    ///   Element 0 = first-half members
    ///   Element 1 = second-half members
    /// </summary>
    public static class BoundingOverwatchManeuver
    {
        // Maneuver kind ID.
        public const ushort ManeuverKind = 2;

        // Phase IDs.
        public const ushort PhaseElement0Moving = 0;
        public const ushort PhaseElement1Moving = 1;
        public const ushort PhaseAborted        = 2;

        // Role IDs.
        public const byte RoleUnassigned = 0;
        public const byte RoleMoving     = 1;
        public const byte RoleCovering   = 2;

        // Element indices.
        public const byte ElementAlpha = 0;
        public const byte ElementBravo = 1;

        // --- Transition table ---

        /// <summary>Builds the phase-transition table for <see cref="PhaseSequencer.Advance"/>.</summary>
        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new PhaseTransitionEntry[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseElement0Moving, EventKind = PhaseEventKind.BoundComplete, ToPhaseId = PhaseElement1Moving },
                new PhaseTransitionEntry { FromPhaseId = PhaseElement0Moving, EventKind = PhaseEventKind.Abort,         ToPhaseId = PhaseAborted        },
                new PhaseTransitionEntry { FromPhaseId = PhaseElement1Moving, EventKind = PhaseEventKind.BoundComplete, ToPhaseId = PhaseElement0Moving },
                new PhaseTransitionEntry { FromPhaseId = PhaseElement1Moving, EventKind = PhaseEventKind.Abort,         ToPhaseId = PhaseAborted        },
            };

        // --- Element partition ---

        /// <summary>
        /// Computes element partition inputs: first half → Element 0 (Alpha),
        /// second half → Element 1 (Bravo). Same heuristic as DangerAreaCrossingManeuver.
        /// </summary>
        public static void ComputePartitionInputs(int memberCount, Span<MemberPartitionInput> inputs)
        {
            int half = Math.Max(1, memberCount / 2);
            for (int i = 0; i < memberCount; i++)
            {
                float alphaScore = i < half ? 1.0f : 0.1f;
                float bravoScore = i < half ? 0.1f : 1.0f;
                inputs[i] = new MemberPartitionInput(alphaScore, bravoScore);
            }
        }

        // --- Role assignment ---

        /// <summary>
        /// 4 candidates: 2 Moving slots + 2 Covering slots.
        /// Using 4 candidates ensures all 4 members get a role with maxFocusFire=1.
        /// </summary>
        public static readonly RoleSlotCandidate[] StandardCandidates =
            new RoleSlotCandidate[]
            {
                new RoleSlotCandidate { RoleId = RoleMoving   },
                new RoleSlotCandidate { RoleId = RoleMoving   },
                new RoleSlotCandidate { RoleId = RoleCovering },
                new RoleSlotCandidate { RoleId = RoleCovering },
            };

        /// <summary>
        /// Builds a 4-column score matrix.
        /// Members of the moving element score high for the Moving candidates;
        /// members of the covering element score high for the Covering candidates.
        /// </summary>
        /// <param name="movingElement">
        /// Element index whose members should be assigned Moving (0 or 1).
        /// </param>
        public static void BuildRoleScoreMatrix(
            ref SquadCognitiveState state,
            int memberCount,
            byte movingElement,
            Span<float> scoreMatrix)
        {
            var membersSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(
                    ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

            // Columns: 0=Moving0, 1=Moving1, 2=Covering0, 3=Covering1
            for (int m = 0; m < memberCount; m++)
            {
                bool isMoving = membersSpan[m] == movingElement;
                scoreMatrix[m * 4 + 0] = isMoving ? 1.0f : 0.1f;  // Moving slot 0
                scoreMatrix[m * 4 + 1] = isMoving ? 1.0f : 0.1f;  // Moving slot 1
                scoreMatrix[m * 4 + 2] = isMoving ? 0.1f : 1.0f;  // Covering slot 0
                scoreMatrix[m * 4 + 3] = isMoving ? 0.1f : 1.0f;  // Covering slot 1
            }
        }

        /// <summary>
        /// Returns the moving element index for a given phase.
        /// Phase 0 → Element 0 moving; Phase 1 → Element 1 moving.
        /// </summary>
        public static byte GetMovingElement(ushort phaseId)
            => phaseId == PhaseElement1Moving ? ElementBravo : ElementAlpha;
    }
}
