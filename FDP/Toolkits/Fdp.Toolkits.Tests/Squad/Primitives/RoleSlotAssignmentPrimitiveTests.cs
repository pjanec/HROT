using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Squad;
using Xunit;

namespace Fdp.Toolkit.Squad.Primitives.Tests
{
    /// <summary>
    /// P1-03: Tests for <see cref="RoleSlotAssignmentPrimitive"/>.
    /// Covers SC-P1-03-1 through SC-P1-03-3.
    /// </summary>
    public class RoleSlotAssignmentPrimitiveTests
    {
        private static ReadOnlySpan<RoleSlot> RolesReadOnly(ref SquadCognitiveState state)
            => MemoryMarshal.CreateReadOnlySpan<RoleSlot>(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16);

        [Fact]
        public void AssignRoles_GreedyAssignment_MatchesExpected()
        {
            // SC-P1-03-1
            // 4 members, 4 candidates: Pointman=0, Suppressor=1, Flanker=2, Sector=3.
            SquadCognitiveState state = default;
            var candidates = new RoleSlotCandidate[]
            {
                new RoleSlotCandidate { RoleId = 0 }, // Pointman
                new RoleSlotCandidate { RoleId = 1 }, // Suppressor
                new RoleSlotCandidate { RoleId = 2 }, // Flanker
                new RoleSlotCandidate { RoleId = 3 }, // Sector
            };
            // Row-major: member 0 -> Pointman, member 1 -> Suppressor,
            //            member 2 -> Flanker,  member 3 -> Sector.
            var scoreMatrix = new float[]
            {
                0.9f, 0.1f, 0.2f, 0.1f,  // member 0
                0.1f, 0.8f, 0.2f, 0.1f,  // member 1
                0.2f, 0.1f, 0.7f, 0.2f,  // member 2
                0.1f, 0.1f, 0.1f, 0.6f,  // member 3
            };

            RoleSlotAssignmentPrimitive.AssignRoles(ref state, candidates, scoreMatrix, memberCount: 4);

            var roles = RolesReadOnly(ref state);
            Assert.Equal(0, roles[0].RoleId); // Pointman
            Assert.Equal(1, roles[1].RoleId); // Suppressor
            Assert.Equal(2, roles[2].RoleId); // Flanker
            Assert.Equal(3, roles[3].RoleId); // Sector
        }

        [Fact]
        public void AssignRoles_PhaseChangeClearsAndReassigns()
        {
            // SC-P1-03-2
            SquadCognitiveState state = default;
            var candidates = new RoleSlotCandidate[]
            {
                new RoleSlotCandidate { RoleId = 10 },
                new RoleSlotCandidate { RoleId = 11 },
            };
            var scoreMatrix1 = new float[]
            {
                0.9f, 0.1f,  // member 0 -> role 10
                0.1f, 0.8f,  // member 1 -> role 11
            };
            RoleSlotAssignmentPrimitive.AssignRoles(ref state, candidates, scoreMatrix1, memberCount: 2);

            // Bump phase and re-run with a swapped matrix.
            state.PhaseId++;
            var scoreMatrix2 = new float[]
            {
                0.1f, 0.9f,  // member 0 -> role 11
                0.8f, 0.1f,  // member 1 -> role 10
            };
            RoleSlotAssignmentPrimitive.AssignRoles(ref state, candidates, scoreMatrix2, memberCount: 2);

            var roles = RolesReadOnly(ref state);
            Assert.Equal(11, roles[0].RoleId);
            Assert.Equal(10, roles[1].RoleId);
        }

        [Fact]
        public void AssignRoles_EmptyCandidates_IsNoOp()
        {
            // SC-P1-03-3
            SquadCognitiveState state = default;
            // Pre-fill roles[0].
            MemoryMarshal.CreateSpan<RoleSlot>(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16)[0].RoleId = 7;

            RoleSlotAssignmentPrimitive.AssignRoles(ref state, ReadOnlySpan<RoleSlotCandidate>.Empty,
                ReadOnlySpan<float>.Empty, memberCount: 2);

            var roles = RolesReadOnly(ref state);
            Assert.Equal(7, roles[0].RoleId);
        }
    }
}
