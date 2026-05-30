using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;
using Fdp.Toolkit.Squad.Systems;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Systems
{
    /// <summary>
    /// Tests for <see cref="SquadVetoDetectionSystem"/>.
    /// Success criteria: SC-P4-02-1 through SC-P4-02-3.
    /// </summary>
    public class SquadVetoDetectionSystemTests : IDisposable
    {
        private EntityRepository _repo;

        public SquadVetoDetectionSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<SquadStateMarker>();
            _repo.RegisterComponent<BehaviorState>();
        }

        public void Dispose() => _repo.Dispose();

        // ── Helpers ──────────────────────────────────────────────────────────────

        private Entity CreateCommander()
        {
            var e = _repo.CreateEntity();
            _repo.AddComponent(e, new UnitRoster());
            _repo.AddComponent(e, new Blackboard1024());
            _repo.AddComponent(e, new SquadStateMarker());
            return e;
        }

        private Entity AddMemberWithRole(Entity commander, byte roleId, int behaviorHash)
        {
            var m = _repo.CreateEntity();
            _repo.AddComponent(m, new UnitSubordinate { Commander = commander });
            _repo.AddComponent(m, new BehaviorState { ActiveBehaviorHash = behaviorHash });
            ref var roster = ref _repo.GetComponentRW<UnitRoster>(commander);
            UnitRoster.Add(ref roster, (long)m.PackedValue);

            // Write role into SquadCognitiveState.
            int idx = roster.Count - 1;
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(commander));
            System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                ref System.Runtime.CompilerServices.Unsafe.As<RoleAssignmentArray, RoleSlot>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in state.Roles)), 16)[idx].RoleId = roleId;

            return m;
        }

        // ── SC-P4-02-1: Persistent divergence triggers veto after N ticks ────────

        [Fact]
        public void VetoDetected_After3ConsecutiveDivergenceTicks()
        {
            var cmd = CreateCommander();
            // Role 1 = Engage, expected hash = 42; member has hash 99 (Flee).
            var member = AddMemberWithRole(cmd, roleId: 1, behaviorHash: 99);

            var system = new SquadVetoDetectionSystem(vetoConfirmTicks: 3);
            // expectedHashByRole: index=RoleId, value=expected hash. RoleId=1 expects 42.
            int[] expectedHashByRole = { 0, 42 };
            var events = new List<(int memberSlot, PhaseEvent evt)>();

            // Tick 1: count=1 < 3, no veto.
            system.Run(_repo, cmd, expectedHashByRole, events);
            Assert.Empty(events);

            // Tick 2: count=2 < 3, no veto.
            system.Run(_repo, cmd, expectedHashByRole, events);
            Assert.Empty(events);

            // Tick 3: count=3 >= 3, veto emitted.
            system.Run(_repo, cmd, expectedHashByRole, events);
            Assert.Single(events);
            Assert.Equal(0, events[0].memberSlot);
            Assert.Equal(PhaseEventKind.VetoDetected, events[0].evt.Kind);
        }

        // ── SC-P4-02-2: Single-tick divergence followed by alignment — no veto ───

        [Fact]
        public void SingleTickDivergenceThenAlign_NoVeto()
        {
            var cmd = CreateCommander();
            var member = AddMemberWithRole(cmd, roleId: 1, behaviorHash: 99);

            var system = new SquadVetoDetectionSystem(vetoConfirmTicks: 3);
            int[] expectedHashByRole = { 0, 42 };
            var events = new List<(int memberSlot, PhaseEvent evt)>();

            // Tick 1: diverge (hash 99 != 42), count=1.
            system.Run(_repo, cmd, expectedHashByRole, events);
            Assert.Empty(events);

            // Tick 2: align (set hash to 42).
            _repo.GetComponentRW<BehaviorState>(member).ActiveBehaviorHash = 42;
            system.Run(_repo, cmd, expectedHashByRole, events);
            Assert.Empty(events);

            // Tick 3 and 4: still aligned, no veto.
            system.Run(_repo, cmd, expectedHashByRole, events);
            system.Run(_repo, cmd, expectedHashByRole, events);
            Assert.Empty(events);
        }

        // ── SC-P4-02-3: Member without BehaviorState — skip without veto ─────────

        [Fact]
        public void MemberWithoutBehaviorState_SkippedNoVeto()
        {
            var cmd = CreateCommander();

            // Add a member without BehaviorState — create manually so we can skip AddComponent.
            var m = _repo.CreateEntity();
            _repo.AddComponent(m, new UnitSubordinate { Commander = cmd });
            ref var roster = ref _repo.GetComponentRW<UnitRoster>(cmd);
            UnitRoster.Add(ref roster, (long)m.PackedValue);
            // Write roleId=1 into state.
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));
            System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                ref System.Runtime.CompilerServices.Unsafe.As<RoleAssignmentArray, RoleSlot>(
                    ref System.Runtime.CompilerServices.Unsafe.AsRef(in state.Roles)), 16)[0].RoleId = 1;

            var system = new SquadVetoDetectionSystem(vetoConfirmTicks: 3);
            int[] expectedHashByRole = { 0, 42 };
            var events = new List<(int memberSlot, PhaseEvent evt)>();

            // Run 3 ticks — member has no BehaviorState, so counter stays 0, no veto.
            system.Run(_repo, cmd, expectedHashByRole, events);
            system.Run(_repo, cmd, expectedHashByRole, events);
            system.Run(_repo, cmd, expectedHashByRole, events);
            Assert.Empty(events);
        }
    }
}
