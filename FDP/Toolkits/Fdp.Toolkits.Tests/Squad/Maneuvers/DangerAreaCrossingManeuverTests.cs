using System.Collections.Generic;
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
    /// Integration tests for <see cref="DangerAreaCrossingManeuver"/>.
    /// Success criteria: SC-P5-01-1 through SC-P5-01-6.
    /// </summary>
    public class DangerAreaCrossingManeuverTests
    {
        // ── SC-P5-01-1: All phases enter in sequence ──────────────────────────────

        [Fact]
        public void ManeuverRunsAllFivePhases_InOrder()
        {
            // Arrange: 4-member squad in SetSecurity phase.
            var (repo, commander, _) = BuildFixture(memberCount: 4);
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));
            state.PhaseId          = DangerAreaCrossingManeuver.PhaseSetSecurity;
            state.PhaseEnteredTick = 0;

            var table = DangerAreaCrossingManeuver.BuildTransitionTable();
            var phasesEntered = new List<ushort> { DangerAreaCrossingManeuver.PhaseSetSecurity };

            // Phase 0 -> 1: DefiladeReached
            bool t01 = PhaseSequencer.Advance(ref state,
                new PhaseEvent[] { new PhaseEvent(PhaseEventKind.DefiladeReached) },
                table, currentTick: 1, dwellTimeoutTicks: 100, recoveryPhaseId: 0);
            Assert.True(t01);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseCrossElement, state.PhaseId);
            phasesEntered.Add(state.PhaseId);

            // Phase 1 -> 2: FarSideReached
            bool t12 = PhaseSequencer.Advance(ref state,
                new PhaseEvent[] { new PhaseEvent(PhaseEventKind.FarSideReached) },
                table, currentTick: 2, dwellTimeoutTicks: 100, recoveryPhaseId: 0);
            Assert.True(t12);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseFarSideCover, state.PhaseId);
            phasesEntered.Add(state.PhaseId);

            // Phase 2 -> 3: ShotFired
            bool t23 = PhaseSequencer.Advance(ref state,
                new PhaseEvent[] { new PhaseEvent(PhaseEventKind.ShotFired) },
                table, currentTick: 3, dwellTimeoutTicks: 100, recoveryPhaseId: 0);
            Assert.True(t23);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseCollapseSecurity, state.PhaseId);
            phasesEntered.Add(state.PhaseId);

            // Phase 3 -> 4: FarSideReached
            bool t34 = PhaseSequencer.Advance(ref state,
                new PhaseEvent[] { new PhaseEvent(PhaseEventKind.FarSideReached) },
                table, currentTick: 4, dwellTimeoutTicks: 100, recoveryPhaseId: 0);
            Assert.True(t34);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseReform, state.PhaseId);
            phasesEntered.Add(state.PhaseId);

            // Assert: all 5 phases entered in order.
            Assert.Equal(new ushort[] { 0, 1, 2, 3, 4 }, phasesEntered);

            repo.Dispose();
        }

        // ── SC-P5-01-2: Element partition splits squad correctly ──────────────────

        [Fact]
        public void ElementPartition_SplitsSquad_IntoTwoElements()
        {
            var (repo, commander, _) = BuildFixture(memberCount: 4);
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));

            // Compute partition inputs.
            Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
            DangerAreaCrossingManeuver.ComputePartitionInputs(4, inputs);

            // Run element partition.
            ElementPartitionPrimitive.Partition(ref state, inputs, elementCount: 2,
                                                decisiveGap: 0f, out int repartitions);

            // Read element assignments.
            var elemSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<MemberElementIndexArray, byte>(ref state.Elements.MemberElements), 16);

            // First 2 members = Element 0 (Crossing), last 2 = Element 1 (Security).
            Assert.Equal(DangerAreaCrossingManeuver.ElementCrossing, elemSpan[0]);
            Assert.Equal(DangerAreaCrossingManeuver.ElementCrossing, elemSpan[1]);
            Assert.Equal(DangerAreaCrossingManeuver.ElementSecurity, elemSpan[2]);
            Assert.Equal(DangerAreaCrossingManeuver.ElementSecurity, elemSpan[3]);
            Assert.True(repartitions > 0);

            repo.Dispose();
        }

        // ── SC-P5-01-3: Role assignment from element partition ────────────────────

        [Fact]
        public void RoleAssignment_AssignsCrossingAndSecurityRoles()
        {
            var (repo, commander, _) = BuildFixture(memberCount: 4);
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));

            // Partition first.
            Span<MemberPartitionInput> inputs = stackalloc MemberPartitionInput[4];
            DangerAreaCrossingManeuver.ComputePartitionInputs(4, inputs);
            ElementPartitionPrimitive.Partition(ref state, inputs, 2, 0f, out _);

            // Build score matrix (memberCount x 4 columns, two slots per role) and assign roles.
            Span<float> scoreMatrix = stackalloc float[4 * 4];
            DangerAreaCrossingManeuver.BuildRoleScoreMatrix(ref state, 4, scoreMatrix);
            RoleSlotAssignmentPrimitive.AssignRoles(ref state,
                DangerAreaCrossingManeuver.StandardCandidates, scoreMatrix, 4);

            // Read roles.
            var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16);

            // Members 0,1 -> Crossing; members 2,3 -> Security.
            Assert.Equal(DangerAreaCrossingManeuver.RoleCrossing, rolesSpan[0].RoleId);
            Assert.Equal(DangerAreaCrossingManeuver.RoleCrossing, rolesSpan[1].RoleId);
            Assert.Equal(DangerAreaCrossingManeuver.RoleSecurity, rolesSpan[2].RoleId);
            Assert.Equal(DangerAreaCrossingManeuver.RoleSecurity, rolesSpan[3].RoleId);

            repo.Dispose();
        }

        // ── SC-P5-01-4: First-across reassignment on Phase 2 entry ───────────────

        [Fact]
        public void ReassignFirstAcrossToCovering_ChangesRoleToSecurity()
        {
            var (repo, commander, _) = BuildFixture(memberCount: 4);
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));

            // Set member 0 to Crossing role initially.
            var rolesSpan = MemoryMarshal.CreateSpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16);
            rolesSpan[0].RoleId = DangerAreaCrossingManeuver.RoleCrossing;
            rolesSpan[1].RoleId = DangerAreaCrossingManeuver.RoleCrossing;

            // Reassign member 0 (first-across) to Security.
            DangerAreaCrossingManeuver.ReassignFirstAcrossToCovering(ref state, 4, firstAcrossSlot: 0);

            Assert.Equal(DangerAreaCrossingManeuver.RoleSecurity, rolesSpan[0].RoleId);
            Assert.Equal(DangerAreaCrossingManeuver.RoleCrossing, rolesSpan[1].RoleId);  // unchanged

            repo.Dispose();
        }

        // ── SC-P5-01-5: Slot rotation tracks crossing lanes ──────────────────────

        [Fact]
        public void SlotRotation_TwoCrossers_UseDifferentLanes()
        {
            // SlotRotation.AcquireSlot operates on a standalone SlotRotationState.
            var rotation = default(SlotRotationState);
            int lane0 = SlotRotation.AcquireSlot(ref rotation, totalSlots: 2);
            int lane1 = SlotRotation.AcquireSlot(ref rotation, totalSlots: 2);

            Assert.NotEqual(lane0, lane1);
            Assert.True(lane0 >= 0 && lane0 < 2);
            Assert.True(lane1 >= 0 && lane1 < 2);
        }

        // ── SC-P5-01-6: No phase transition before event (dwell guard) ────────────

        [Fact]
        public void PhaseSequencer_NoTransition_WhenNoEventAndDwellNotElapsed()
        {
            var state = default(SquadCognitiveState);
            state.PhaseId          = DangerAreaCrossingManeuver.PhaseSetSecurity;
            state.PhaseEnteredTick = 0;
            var table = DangerAreaCrossingManeuver.BuildTransitionTable();

            bool t = PhaseSequencer.Advance(ref state,
                ReadOnlySpan<PhaseEvent>.Empty, table,
                currentTick: 5, dwellTimeoutTicks: 100, recoveryPhaseId: 0);

            Assert.False(t);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseSetSecurity, state.PhaseId);
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
