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
    /// Integration tests for <see cref="SuppressAndManeuverManeuver"/>.
    /// Success criteria: SC-P5-03-1 through SC-P5-03-4.
    /// </summary>
    public class SuppressAndManeuverManeuverTests
    {
        // ── SC-P5-03-1: FarSideReached -> AssaultComplete transition ─────────────

        [Fact]
        public void Suppressing_FarSideReached_TransitionsToAssaultComplete()
        {
            var state = default(SquadCognitiveState);
            state.PhaseId          = SuppressAndManeuverManeuver.PhaseSuppressing;
            state.PhaseEnteredTick = 0;
            var table = SuppressAndManeuverManeuver.BuildTransitionTable();

            bool transitioned = PhaseSequencer.Advance(ref state,
                new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.FarSideReached) }),
                table, currentTick: 5, dwellTimeoutTicks: 100, recoveryPhaseId: 2);

            Assert.True(transitioned);
            Assert.Equal(SuppressAndManeuverManeuver.PhaseAssaultComplete, state.PhaseId);
        }

        // ── SC-P5-03-2: Abort -> Aborted transition ───────────────────────────────

        [Fact]
        public void Suppressing_AbortEvent_TransitionsToAborted()
        {
            var state = default(SquadCognitiveState);
            state.PhaseId          = SuppressAndManeuverManeuver.PhaseSuppressing;
            state.PhaseEnteredTick = 0;
            var table = SuppressAndManeuverManeuver.BuildTransitionTable();

            bool transitioned = PhaseSequencer.Advance(ref state,
                new ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.Abort) }),
                table, currentTick: 3, dwellTimeoutTicks: 100, recoveryPhaseId: 2);

            Assert.True(transitioned);
            Assert.Equal(SuppressAndManeuverManeuver.PhaseAborted, state.PhaseId);
        }

        // ── SC-P5-03-3: Role assignment — BaseOfFire and Assault roles correctly split

        [Fact]
        public void RoleAssignment_SplitsBaseOfFireAndAssault_Correctly()
        {
            var (repo, commander, members) = BuildFixture(memberCount: 4);
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));

            Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
            SuppressAndManeuverManeuver.ComputePartitionInputs(4, inputs);
            ElementPartitionPrimitive.Partition(ref state, inputs, 2, 0f, out _);

            Span<float> scoreMatrix = stackalloc float[4 * 4];
            SuppressAndManeuverManeuver.BuildRoleScoreMatrix(ref state, 4, scoreMatrix);
            RoleSlotAssignmentPrimitive.AssignRoles(ref state,
                SuppressAndManeuverManeuver.StandardCandidates, scoreMatrix, 4);

            var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref Unsafe.AsRef(in state.Roles)), 16);

            // Members 0,1 -> BaseOfFire; members 2,3 -> Assault.
            Assert.Equal(SuppressAndManeuverManeuver.RoleBaseOfFire, rolesSpan[0].RoleId);
            Assert.Equal(SuppressAndManeuverManeuver.RoleBaseOfFire, rolesSpan[1].RoleId);
            Assert.Equal(SuppressAndManeuverManeuver.RoleAssault,    rolesSpan[2].RoleId);
            Assert.Equal(SuppressAndManeuverManeuver.RoleAssault,    rolesSpan[3].RoleId);

            repo.Dispose();
        }

        // ── SC-P5-03-4: Timer fallback (suppression dwell) transitions to recovery ─

        [Fact]
        public void PhaseSequencer_DwellTimeout_TransitionsToRecovery_WhileSuppressing()
        {
            var state = default(SquadCognitiveState);
            state.PhaseId          = SuppressAndManeuverManeuver.PhaseSuppressing;
            state.PhaseEnteredTick = 0;
            var table = SuppressAndManeuverManeuver.BuildTransitionTable();

            // No event, dwell elapsed.
            bool transitioned = PhaseSequencer.Advance(ref state,
                ReadOnlySpan<PhaseEvent>.Empty, table,
                currentTick: 200, dwellTimeoutTicks: 100, recoveryPhaseId: SuppressAndManeuverManeuver.PhaseAborted);

            Assert.True(transitioned);
            Assert.Equal(SuppressAndManeuverManeuver.PhaseAborted, state.PhaseId);
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
