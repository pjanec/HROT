using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Xunit;

namespace Fdp.Toolkits.Tests.Squad.Maneuvers
{
    /// <summary>
    /// Integration tests for <see cref="BoundingOverwatchManeuver"/>.
    /// Success criteria: SC-P5-02-1 through SC-P5-02-5.
    /// </summary>
    public class BoundingOverwatchManeuverTests
    {
        // ── SC-P5-02-1: At least 2 bound swaps (phase alternation) ────────────────

        [Fact]
        public void BoundingOverwatch_PhaseAlternates_OnBoundComplete()
        {
            var state = default(SquadCognitiveState);
            state.PhaseId          = BoundingOverwatchManeuver.PhaseElement0Moving;
            state.PhaseEnteredTick = 0;
            var table = BoundingOverwatchManeuver.BuildTransitionTable();
            var swapCount = 0;

            // 4 bounds: 0→1, 1→0, 0→1, 1→0
            var expected = new ushort[]
            {
                BoundingOverwatchManeuver.PhaseElement1Moving,
                BoundingOverwatchManeuver.PhaseElement0Moving,
                BoundingOverwatchManeuver.PhaseElement1Moving,
                BoundingOverwatchManeuver.PhaseElement0Moving,
            };

            for (int t = 1; t <= 4; t++)
            {
                bool transitioned = PhaseSequencer.Advance(ref state,
                    new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.BoundComplete) }),
                    table, currentTick: (uint)t, dwellTimeoutTicks: 100, recoveryPhaseId: 2);
                Assert.True(transitioned, $"Expected transition at tick {t}");
                Assert.Equal(expected[t - 1], state.PhaseId);
                swapCount++;
            }

            Assert.True(swapCount >= 2, "Expected at least 2 bound swaps");
        }

        // ── SC-P5-02-2: Abort transitions to terminal phase ───────────────────────

        [Fact]
        public void BoundingOverwatch_AbortEvent_TransitionsToAborted()
        {
            var state = default(SquadCognitiveState);
            state.PhaseId          = BoundingOverwatchManeuver.PhaseElement0Moving;
            state.PhaseEnteredTick = 0;
            var table = BoundingOverwatchManeuver.BuildTransitionTable();

            bool transitioned = PhaseSequencer.Advance(ref state,
                new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.Abort) }),
                table, currentTick: 5, dwellTimeoutTicks: 100, recoveryPhaseId: 2);

            Assert.True(transitioned);
            Assert.Equal(BoundingOverwatchManeuver.PhaseAborted, state.PhaseId);
        }

        // ── SC-P5-02-3: Role assignment — never >2 members in Moving role simultaneously

        [Fact]
        public void RoleAssignment_AtMost2Members_HaveMovingRole()
        {
            var (repo, commander, members) = BuildFixture(memberCount: 4);
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));

            // Partition the squad.
            Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
            BoundingOverwatchManeuver.ComputePartitionInputs(4, inputs);
            ElementPartitionPrimitive.Partition(ref state, inputs, 2, 0f, out _);

            // Assign roles with Element 0 moving.
            Span<float> scoreMatrix = stackalloc float[4 * 4];
            BoundingOverwatchManeuver.BuildRoleScoreMatrix(ref state, 4,
                BoundingOverwatchManeuver.ElementAlpha, scoreMatrix);
            RoleSlotAssignmentPrimitive.AssignRoles(ref state,
                BoundingOverwatchManeuver.StandardCandidates, scoreMatrix, 4);

            // Count Moving roles.
            var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), 16);
            int movingCount = 0;
            for (int i = 0; i < 4; i++)
                if (rolesSpan[i].RoleId == BoundingOverwatchManeuver.RoleMoving)
                    movingCount++;

            Assert.True(movingCount <= 2, $"Expected <= 2 moving, got {movingCount}");
            Assert.True(movingCount >= 1, "Expected at least 1 moving member");

            repo.Dispose();
        }

        // ── SC-P5-02-4: Role swap on phase transition (Element 0 cover after swap) ─

        [Fact]
        public void RoleAssignment_AfterSwap_Element0MembersGetCoveringRole()
        {
            var (repo, commander, members) = BuildFixture(memberCount: 4);
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));

            Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
            BoundingOverwatchManeuver.ComputePartitionInputs(4, inputs);
            ElementPartitionPrimitive.Partition(ref state, inputs, 2, 0f, out _);

            // After Phase 0→1 swap: Element 1 moves (Bravo).
            Span<float> scoreMatrix = stackalloc float[4 * 4];
            BoundingOverwatchManeuver.BuildRoleScoreMatrix(ref state, 4,
                BoundingOverwatchManeuver.ElementBravo, scoreMatrix);
            RoleSlotAssignmentPrimitive.AssignRoles(ref state,
                BoundingOverwatchManeuver.StandardCandidates, scoreMatrix, 4);

            var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), 16);

            // Element 0 members (0,1) should be Covering; Element 1 members (2,3) should be Moving.
            Assert.Equal(BoundingOverwatchManeuver.RoleCovering, rolesSpan[0].RoleId);
            Assert.Equal(BoundingOverwatchManeuver.RoleCovering, rolesSpan[1].RoleId);
            Assert.Equal(BoundingOverwatchManeuver.RoleMoving,   rolesSpan[2].RoleId);
            Assert.Equal(BoundingOverwatchManeuver.RoleMoving,   rolesSpan[3].RoleId);

            repo.Dispose();
        }

        // ── SC-P5-02-5: GetMovingElement returns correct element per phase ─────────

        [Fact]
        public void GetMovingElement_ReturnsCorrectElement_ForEachPhase()
        {
            Assert.Equal(BoundingOverwatchManeuver.ElementAlpha,
                BoundingOverwatchManeuver.GetMovingElement(BoundingOverwatchManeuver.PhaseElement0Moving));
            Assert.Equal(BoundingOverwatchManeuver.ElementBravo,
                BoundingOverwatchManeuver.GetMovingElement(BoundingOverwatchManeuver.PhaseElement1Moving));
        }

        // ── Fixture builder ───────────────────────────────────────────────────────

        private static (EntityRepository repo, Entity commander, Entity[] members)
            BuildFixture(int memberCount)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<UnitRoster>();
            repo.RegisterComponent<Blackboard1024>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<UnitSubordinate>();

            var commander = repo.CreateEntity();
            repo.AddComponent(commander, new UnitRoster());
            repo.AddComponent(commander, new Blackboard1024());

            var members = new Entity[memberCount];
            for (int i = 0; i < memberCount; i++)
            {
                members[i] = repo.CreateEntity();
                repo.AddComponent(members[i], new NavigationStatus());
                repo.AddComponent(members[i], new UnitSubordinate { Commander = commander });
                ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
                UnitRoster.Add(ref roster, (long)members[i].PackedValue);
            }
            return (repo, commander, members);
        }
    }
}
