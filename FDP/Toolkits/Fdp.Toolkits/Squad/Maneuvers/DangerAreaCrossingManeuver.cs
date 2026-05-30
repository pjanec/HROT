using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad.Primitives;

namespace Fdp.Toolkit.Squad.Maneuvers
{
    /// <summary>
    /// Configuration and orchestration logic for the 5-phase danger-area crossing maneuver.
    ///
    /// Phases:
    ///   0 SetSecurity     - security element occupies overwatch positions (DefiladeReached)
    ///   1 CrossElement    - first element crosses the danger area (FarSideReached)
    ///   2 FarSideCover    - first-across reassigned to covering role; signals ready (ShotFired)
    ///   3 CollapseSecurity- second element crosses, security follows (FarSideReached)
    ///   4 Reform          - terminal phase; all members on far side
    ///
    /// Roles:
    ///   RoleId 0 = Unassigned
    ///   RoleId 1 = Crossing  (assigned to crossing element)
    ///   RoleId 2 = Security  (assigned to overwatch element)
    ///
    /// Elements:
    ///   Element 0 = Crossing element
    ///   Element 1 = Security element
    /// </summary>
    public static class DangerAreaCrossingManeuver
    {
        // Maneuver kind ID stored in state.ManeuverKind.
        public const ushort ManeuverKind = 1;

        // Phase IDs.
        public const ushort PhaseSetSecurity      = 0;
        public const ushort PhaseCrossElement     = 1;
        public const ushort PhaseFarSideCover     = 2;
        public const ushort PhaseCollapseSecurity = 3;
        public const ushort PhaseReform           = 4;

        // Role IDs.
        public const byte RoleUnassigned = 0;
        public const byte RoleCrossing   = 1;
        public const byte RoleSecurity   = 2;

        // Element indices.
        public const byte ElementCrossing = 0;
        public const byte ElementSecurity = 1;

        // --- Transition table ---

        /// <summary>
        /// Builds the phase-transition table for <see cref="PhaseSequencer.Advance"/>.
        /// </summary>
        public static PhaseTransitionEntry[] BuildTransitionTable() =>
            new PhaseTransitionEntry[]
            {
                new PhaseTransitionEntry { FromPhaseId = PhaseSetSecurity,      EventKind = PhaseEventKind.DefiladeReached, ToPhaseId = PhaseCrossElement    },
                new PhaseTransitionEntry { FromPhaseId = PhaseCrossElement,     EventKind = PhaseEventKind.FarSideReached,  ToPhaseId = PhaseFarSideCover    },
                new PhaseTransitionEntry { FromPhaseId = PhaseFarSideCover,     EventKind = PhaseEventKind.ShotFired,       ToPhaseId = PhaseCollapseSecurity },
                new PhaseTransitionEntry { FromPhaseId = PhaseCollapseSecurity, EventKind = PhaseEventKind.FarSideReached,  ToPhaseId = PhaseReform          },
            };

        // --- Element partition ---

        /// <summary>
        /// Computes element partition inputs for the danger-area crossing scenario.
        ///
        /// Element 0 (Crossing): members in front half of the squad (lower member indices).
        /// Element 1 (Security): members in back half.
        ///
        /// This is a simple index-based heuristic for the starter pack; game-specific
        /// maneuvers should supply proper scoring from positional EQS data.
        /// </summary>
        public static void ComputePartitionInputs(
            int memberCount,
            Span<MemberPartitionInput> inputs)
        {
            int half = Math.Max(1, memberCount / 2);
            for (int i = 0; i < memberCount; i++)
            {
                // First half cross, second half provide security.
                float crossingScore = i < half ? 1.0f : 0.1f;
                float securityScore = i < half ? 0.1f : 1.0f;
                inputs[i] = new MemberPartitionInput(crossingScore, securityScore);
            }
        }

        // --- Role assignment ---

        /// <summary>
        /// Role candidates for <see cref="RoleSlotAssignmentPrimitive.AssignRoles"/>.
        /// Two Crossing slots + two Security slots so that all four members of a
        /// standard squad are assigned when <see cref="GreedyMatrixAssigner"/> runs
        /// with maxFocusFire=1 (one member per slot).
        /// Element 0 -> Crossing (RoleId 1); Element 1 -> Security (RoleId 2).
        /// </summary>
        public static readonly RoleSlotCandidate[] StandardCandidates = new RoleSlotCandidate[]
        {
            new RoleSlotCandidate { RoleId = RoleCrossing },  // Crossing slot A
            new RoleSlotCandidate { RoleId = RoleCrossing },  // Crossing slot B
            new RoleSlotCandidate { RoleId = RoleSecurity },  // Security slot A
            new RoleSlotCandidate { RoleId = RoleSecurity },  // Security slot B
        };

        /// <summary>
        /// Builds a score matrix (memberCount x 4) where crossing-element members score
        /// high for Crossing slots (columns 0-1) and security-element members score high
        /// for Security slots (columns 2-3).  Two slots per role ensure all members are
        /// assigned when <see cref="GreedyMatrixAssigner"/> uses maxFocusFire=1.
        /// </summary>
        public static void BuildRoleScoreMatrix(
            ref SquadCognitiveState state,
            int memberCount,
            Span<float> scoreMatrix)
        {
            var membersSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(ref state.Elements.MemberElements), 16);

            for (int m = 0; m < memberCount; m++)
            {
                byte elem = membersSpan[m];
                // Columns 0,1 = Crossing slots; columns 2,3 = Security slots.
                // Second slot in each pair scores slightly lower to guide greedy ordering.
                scoreMatrix[m * 4 + 0] = (elem == ElementCrossing) ? 1.0f : 0.1f;
                scoreMatrix[m * 4 + 1] = (elem == ElementCrossing) ? 0.9f : 0.0f;
                scoreMatrix[m * 4 + 2] = (elem == ElementSecurity) ? 1.0f : 0.1f;
                scoreMatrix[m * 4 + 3] = (elem == ElementSecurity) ? 0.9f : 0.0f;
            }
        }

        /// <summary>
        /// Reassigns the first-across member (slot 0 winner in crossing element)
        /// to the Covering role on entering <see cref="PhaseFarSideCover"/>.
        /// Re-runs role assignment with a flipped matrix that gives the slot-0
        /// crossing member a high Security score.
        /// </summary>
        /// <param name="state">State to mutate.</param>
        /// <param name="memberCount">Roster member count.</param>
        /// <param name="firstAcrossSlot">
        ///   Roster index of the first member who crossed (emitting FarSideReached).
        ///   If -1, method is a no-op.
        /// </param>
        public static unsafe void ReassignFirstAcrossToCovering(
            ref SquadCognitiveState state,
            int memberCount,
            int firstAcrossSlot)
        {
            if (firstAcrossSlot < 0 || firstAcrossSlot >= memberCount) return;

            // Force member at firstAcrossSlot to Security role directly.
            var rolesSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16);
            rolesSpan[firstAcrossSlot].RoleId = RoleSecurity;
        }
    }
}
