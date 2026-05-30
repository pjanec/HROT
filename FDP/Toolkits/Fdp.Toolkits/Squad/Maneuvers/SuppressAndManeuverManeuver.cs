using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// Configuration and role logic for the suppress-and-maneuver (base-of-fire + assault).
    ///
    /// Phases:
    ///   0 Suppressing     - BaseOfFire suppresses; Assault flanks (FarSideReached)
    ///   1 AssaultComplete - Terminal: assault reached flank
    ///   2 Aborted         - Terminal (Abort event)
    ///
    /// Roles:
    ///   RoleId 0 = Unassigned
    ///   RoleId 1 = BaseOfFire  (hold, suppress)
    ///   RoleId 2 = Assault     (advance to flank)
    ///
    /// Elements:
    ///   Element 0 = Base-of-fire element (first half)
    ///   Element 1 = Assault element (second half)
    /// </summary>
    public static unsafe class SuppressAndManeuverManeuver
    {
        // Maneuver kind ID.
        public const ushort ManeuverKind = 3;

        // Phase IDs.
        public const ushort PhaseSuppressing     = 0;
        public const ushort PhaseAssaultComplete = 1;
        public const ushort PhaseAborted         = 2;

        // Role IDs.
        public const byte RoleUnassigned = 0;
        public const byte RoleBaseOfFire = 1;
        public const byte RoleAssault    = 2;

        // Element indices.
        public const byte ElementBaseOfFire = 0;
        public const byte ElementAssault    = 1;

        // --- Transition table ---

        /// <summary>Builds the phase-transition table for <see cref="PhaseSequencer.Advance"/>.</summary>
        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new PhaseTransitionEntry[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseSuppressing, EventKind = PhaseEventKind.FarSideReached, ToPhaseId = PhaseAssaultComplete },
                new PhaseTransitionEntry { FromPhaseId = PhaseSuppressing, EventKind = PhaseEventKind.Abort,          ToPhaseId = PhaseAborted         },
            };

        // --- Element partition ---

        /// <summary>
        /// Computes element partition inputs: first half → BaseOfFire element,
        /// second half → Assault element.
        /// </summary>
        public static void ComputePartitionInputs(int memberCount, Span<MemberPartitionInput> inputs)
        {
            int half = Math.Max(1, memberCount / 2);
            for (int i = 0; i < memberCount; i++)
            {
                float baseScore    = i < half ? 1.0f : 0.1f;
                float assaultScore = i < half ? 0.1f : 1.0f;
                inputs[i] = new MemberPartitionInput(baseScore, assaultScore);
            }
        }

        // --- Role assignment ---

        /// <summary>
        /// 4 candidates: 2 BaseOfFire slots + 2 Assault slots.
        /// Using 4 candidates ensures all 4 members get a role with maxFocusFire=1.
        /// </summary>
        public static readonly RoleSlotCandidate[] StandardCandidates =
            new RoleSlotCandidate[]
            {
                new RoleSlotCandidate { RoleId = RoleBaseOfFire },
                new RoleSlotCandidate { RoleId = RoleBaseOfFire },
                new RoleSlotCandidate { RoleId = RoleAssault    },
                new RoleSlotCandidate { RoleId = RoleAssault    },
            };

        /// <summary>
        /// Builds a 4-column score matrix.
        /// BaseOfFire element members score high for BaseOfFire candidates;
        /// Assault element members score high for Assault candidates.
        /// </summary>
        public static void BuildRoleScoreMatrix(
            ref SquadCognitiveState state,
            int memberCount,
            Span<float> scoreMatrix)
        {
            var membersSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(
                    ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

            // Columns: 0=BaseOfFire0, 1=BaseOfFire1, 2=Assault0, 3=Assault1
            for (int m = 0; m < memberCount; m++)
            {
                bool isBase = membersSpan[m] == ElementBaseOfFire;
                scoreMatrix[m * 4 + 0] = isBase ? 1.0f : 0.1f;  // BaseOfFire slot 0
                scoreMatrix[m * 4 + 1] = isBase ? 1.0f : 0.1f;  // BaseOfFire slot 1
                scoreMatrix[m * 4 + 2] = isBase ? 0.1f : 1.0f;  // Assault slot 0
                scoreMatrix[m * 4 + 3] = isBase ? 0.1f : 1.0f;  // Assault slot 1
            }
        }
    }
}
