using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// Configuration and slot-management logic for the hill-crest hull-down rotation maneuver.
    ///
    /// Phases:
    ///   0 Deploying  - Wave advances to firing slots (FarSideReached -> Firing)
    ///   1 Firing     - Wave fires from hull-down (ShotFired -> Retiring)
    ///   2 Retiring   - Wave retires to defilade (DefiladeReached -> Deploying, next wave)
    ///   3 Aborted    - Terminal phase
    ///
    /// Roles:
    ///   RoleId 0 = Unassigned
    ///   RoleId 1 = Deploying   (current wave, advancing)
    ///   RoleId 2 = Covering    (reserve wave, at defilade)
    ///
    /// Elements:
    ///   Element 0 = current wave
    ///   Element 1 = reserve
    ///
    /// Slot management uses <see cref="SlotRotation"/> with AcquireSlot + BurnSlot.
    /// This matches the legacy HillAttackMutableState.WaveUsedSlotsMask + BurnedSlotsMask semantics.
    /// </summary>
    public static unsafe class HillCrestHullDownManeuver
    {
        // Maneuver kind ID.
        public const ushort ManeuverKind = 4;

        // Phase IDs.
        public const ushort PhaseDeploying = 0;
        public const ushort PhaseFiring    = 1;
        public const ushort PhaseRetiring  = 2;
        public const ushort PhaseAborted   = 3;

        // Role IDs.
        public const byte RoleUnassigned = 0;
        public const byte RoleDeploying  = 1;
        public const byte RoleCovering   = 2;

        // Element indices.
        public const byte ElementWave    = 0;
        public const byte ElementReserve = 1;

        // --- Transition table ---

        /// <summary>Builds the phase-transition table for <see cref="PhaseSequencer.Advance"/>.</summary>
        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new PhaseTransitionEntry[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseDeploying, EventKind = PhaseEventKind.FarSideReached,  ToPhaseId = PhaseFiring    },
                new PhaseTransitionEntry { FromPhaseId = PhaseDeploying, EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted   },
                new PhaseTransitionEntry { FromPhaseId = PhaseFiring,    EventKind = PhaseEventKind.ShotFired,       ToPhaseId = PhaseRetiring  },
                new PhaseTransitionEntry { FromPhaseId = PhaseFiring,    EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted   },
                new PhaseTransitionEntry { FromPhaseId = PhaseRetiring,  EventKind = PhaseEventKind.DefiladeReached, ToPhaseId = PhaseDeploying },
                new PhaseTransitionEntry { FromPhaseId = PhaseRetiring,  EventKind = PhaseEventKind.Abort,           ToPhaseId = PhaseAborted   },
            };

        // --- Element partition ---

        /// <summary>
        /// Computes element partition inputs for the current wave.
        /// Wave element members: first <paramref name="waveSize"/> members (by roster index).
        /// Remaining members: reserve element.
        /// </summary>
        public static void ComputePartitionInputs(
            int memberCount, int waveSize, Span<MemberPartitionInput> inputs)
        {
            for (int i = 0; i < memberCount; i++)
            {
                float waveScore    = i < waveSize ? 1.0f : 0.1f;
                float reserveScore = i < waveSize ? 0.1f : 1.0f;
                inputs[i] = new MemberPartitionInput(waveScore, reserveScore);
            }
        }

        // --- Role assignment ---

        /// <summary>
        /// 4 candidates: 2 Deploying slots + 2 Covering slots.
        /// Using 4 candidates supports up to 4 wave members with maxFocusFire=1.
        /// </summary>
        public static readonly RoleSlotCandidate[] StandardCandidates =
            new RoleSlotCandidate[]
            {
                new RoleSlotCandidate { RoleId = RoleDeploying },
                new RoleSlotCandidate { RoleId = RoleDeploying },
                new RoleSlotCandidate { RoleId = RoleCovering  },
                new RoleSlotCandidate { RoleId = RoleCovering  },
            };

        /// <summary>
        /// Builds a 4-column score matrix.
        /// Wave element members score high for Deploying; reserve members score high for Covering.
        /// </summary>
        public static void BuildRoleScoreMatrix(
            ref SquadCognitiveState state,
            int memberCount,
            Span<float> scoreMatrix)
        {
            var membersSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(
                    ref Unsafe.AsRef(in state.Elements.MemberElements)), 16);

            // Columns: 0=Deploying0, 1=Deploying1, 2=Covering0, 3=Covering1
            for (int m = 0; m < memberCount; m++)
            {
                bool isWave = membersSpan[m] == ElementWave;
                scoreMatrix[m * 4 + 0] = isWave ? 1.0f : 0.1f;
                scoreMatrix[m * 4 + 1] = isWave ? 1.0f : 0.1f;
                scoreMatrix[m * 4 + 2] = isWave ? 0.1f : 1.0f;
                scoreMatrix[m * 4 + 3] = isWave ? 0.1f : 1.0f;
            }
        }

        // --- Slot allocation helpers (parity with legacy HillAttackMutableState) ---

        /// <summary>
        /// Computes the total number of slots from a firing-line segment length and spacing.
        /// Matches the legacy: <c>Math.Max(1, (int)(segLen / spacing))</c> capped at 16.
        /// </summary>
        public static int ComputeTotalSlots(float segmentLength, float spacing)
        {
            if (spacing <= 0f) spacing = 30f;
            int count = Math.Max(1, (int)(segmentLength / spacing));
            return count > 16 ? 16 : count;
        }
    }
}
