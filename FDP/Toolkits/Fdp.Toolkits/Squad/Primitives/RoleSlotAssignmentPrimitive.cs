using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;

namespace Fdp.Toolkit.Squad.Primitives
{
    /// <summary>
    /// One role/slot candidate for the greedy assignment pass.
    /// The score for a given (member, candidate) pair is supplied externally in the
    /// <c>scoreMatrix</c> parameter.
    /// </summary>
    public struct RoleSlotCandidate
    {
        /// <summary>Role identifier assigned when this candidate wins.</summary>
        public byte RoleId;
#pragma warning disable CS0169
        private byte _pad;
#pragma warning restore CS0169
    }

    /// <summary>
    /// Assigns roles/slots to squad members using the same greedy matrix algorithm
    /// as <see cref="Fdp.Toolkit.Utility.ThreatMatrixAssignmentSystem"/> (design §2 primitive 3).
    /// Writes winning <see cref="RoleSlot.RoleId"/> values into
    /// <c>state.Roles</c>. Re-run on every phase change.
    /// </summary>
    public static unsafe class RoleSlotAssignmentPrimitive
    {
        /// <summary>
        /// Runs the greedy role assignment.
        /// </summary>
        /// <param name="state">Squad cognitive state to write roles into.</param>
        /// <param name="candidates">
        ///   Role candidates. Length must equal the number of columns in
        ///   <paramref name="scoreMatrix"/>.
        /// </param>
        /// <param name="scoreMatrix">
        ///   Caller-provided row-major score matrix of size
        ///   <c>memberCount * candidates.Length</c>.
        ///   The caller computes scores (e.g. from a <see cref="Fdp.Toolkit.Utility.UtilityDecisionDef"/>).
        /// </param>
        /// <param name="memberCount">
        ///   Number of members (rows) to assign. Must not exceed 16.
        /// </param>
        public static void AssignRoles(
            ref SquadCognitiveState state,
            ReadOnlySpan<RoleSlotCandidate> candidates,
            ReadOnlySpan<float> scoreMatrix,
            int memberCount)
        {
            if (candidates.IsEmpty || memberCount == 0)
                return;

            int* assignmentsBuf = stackalloc int[memberCount];
            var assignments = new Span<int>(assignmentsBuf, memberCount);

            GreedyMatrixAssigner.Assign(scoreMatrix, memberCount, candidates.Length, maxFocusFire: 1, assignments);

            var rolesSpan = RolesSpan(ref state);
            // Clear all slots first so unassigned members get RoleId=0, not a stale previous value.
            rolesSpan.Slice(0, memberCount).Clear();
            for (int i = 0; i < memberCount; i++)
            {
                if (assignments[i] >= 0)
                    rolesSpan[i].RoleId = candidates[assignments[i]].RoleId;
            }
        }

        private static Span<RoleSlot> RolesSpan(ref SquadCognitiveState state)
            => MemoryMarshal.CreateSpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16);
    }
}
