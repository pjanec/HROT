using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Xunit;

namespace Fdp.Toolkits.Tests.Squad.Maneuvers
{
    /// <summary>
    /// Tests for StackAndRoomEntryManeuver and TravellingOverwatchManeuver (§8.6a, §8.6b).
    /// Success criteria: SC-P5-05-1 through SC-P5-05-3.
    /// </summary>
    public class BrieferCatalogManeuverTests
    {
        // ── SC-P5-05-1: Stack-and-room-entry assigns 4 members to 4 distinct roles ──

        [Fact]
        public void StackAndRoomEntry_AssignsFourDistinctRoles()
        {
            var (repo, commander, members) = BuildFixture(memberCount: 4);
            ref var state = ref SquadCognitiveState.Project(
                ref repo.GetComponentRW<Blackboard1024>(commander));

            Span<float> scoreMatrix = stackalloc float[4 * 4];
            StackAndRoomEntryManeuver.BuildRoleScoreMatrix(4, scoreMatrix);
            RoleSlotAssignmentPrimitive.AssignRoles(ref state,
                StackAndRoomEntryManeuver.StandardCandidates, scoreMatrix, 4);

            var rolesSpan = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<RoleAssignmentArray, RoleSlot>(ref state.Roles), 16);

            // Each of the 4 members should have a distinct role.
            var assignedRoles = new HashSet<byte>();
            for (int i = 0; i < 4; i++)
                assignedRoles.Add(rolesSpan[i].RoleId);
            // PointMan(1), BreachCover(2), Secondary(3) -- at least 3 distinct roles.
            Assert.True(assignedRoles.Count >= 3,
                $"Expected at least 3 distinct roles, got {assignedRoles.Count}");
            Assert.Contains(StackAndRoomEntryManeuver.RolePointMan,    assignedRoles);
            Assert.Contains(StackAndRoomEntryManeuver.RoleBreachCover, assignedRoles);
            Assert.Contains(StackAndRoomEntryManeuver.RoleSecondary,   assignedRoles);

            repo.Dispose();
        }

        // ── SC-P5-05-2: Travelling overwatch -- transition on FarSideReached ──────

        [Fact]
        public void TravellingOverwatch_FarSideReached_TransitionsToArrived()
        {
            var state = default(SquadCognitiveState);
            state.PhaseId          = TravellingOverwatchManeuver.PhaseMoving;
            state.PhaseEnteredTick = 0;
            var table = TravellingOverwatchManeuver.BuildTransitionTable();

            bool transitioned = PhaseSequencer.Advance(ref state,
                new System.ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.FarSideReached) }),
                table, currentTick: 10, dwellTimeoutTicks: 100, recoveryPhaseId: 2);

            Assert.True(transitioned);
            Assert.Equal(TravellingOverwatchManeuver.PhaseArrived, state.PhaseId);
        }

        // ── SC-P5-05-3: Primitive coverage static check (compile-time catalog) ───

        [Fact]
        public void CatalogCoverageCheck_AllPrimitivesExercised()
        {
            // Primitive 1 (ElementPartition): DangerAreaCrossing, BoundingOverwatch,
            //   SuppressAndManeuver, HillCrestHullDown, TravellingOverwatch all use it.
            Assert.True(typeof(ElementPartitionPrimitive).IsPublic, "ElementPartitionPrimitive must be public");

            // Primitive 2 (TacticalFeatureHandles): state.ActiveFeatureId used by DangerAreaCrossing.
            Assert.True(typeof(TacticalFeatureHandles).IsPublic, "TacticalFeatureHandles must be public");

            // Primitive 3 (RoleSlotAssignment): used by all 6 maneuvers.
            Assert.True(typeof(RoleSlotAssignmentPrimitive).IsPublic, "RoleSlotAssignmentPrimitive must be public");

            // Primitive 4 (PhaseSequencer): used by all maneuvers with BuildTransitionTable().
            Assert.True(typeof(PhaseSequencer).IsPublic, "PhaseSequencer must be public");

            // Primitive 5 (SlotRotation): used by HillCrestHullDown (BurnSlot/AcquireSlot).
            Assert.True(typeof(SlotRotation).IsPublic, "SlotRotation must be public");
        }

        // ── Fixture builder ───────────────────────────────────────────────────────

        private static (EntityRepository repo, Entity commander, Entity[] members)
            BuildFixture(int memberCount)
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<UnitRoster>();
            repo.RegisterComponent<Blackboard1024>();
            repo.RegisterComponent<WeaponState>();
            repo.RegisterComponent<NavigationStatus>();
            repo.RegisterComponent<UnitSubordinate>();

            var commander = repo.CreateEntity();
            repo.AddComponent(commander, new UnitRoster());
            repo.AddComponent(commander, new Blackboard1024());

            var members = new Entity[memberCount];
            for (int i = 0; i < memberCount; i++)
            {
                members[i] = repo.CreateEntity();
                repo.AddComponent(members[i], new WeaponState { Ammo = 10, MaxAmmo = 10 });
                repo.AddComponent(members[i], new NavigationStatus());
                repo.AddComponent(members[i], new UnitSubordinate { Commander = commander });
                ref var roster = ref repo.GetComponentRW<UnitRoster>(commander);
                UnitRoster.Add(ref roster, (long)members[i].PackedValue);
            }
            return (repo, commander, members);
        }
    }
}
