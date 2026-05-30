using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Components;
using Fdp.Toolkit.Squad.Systems;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Systems
{
    /// <summary>
    /// Tests for <see cref="SquadMovementModeBroadcastSystem"/>.
    /// Success criteria: SC-P4-04-1 through SC-P4-04-3.
    /// </summary>
    public class SquadMovementModeBroadcastSystemTests : IDisposable
    {
        private EntityRepository _repo;

        public SquadMovementModeBroadcastSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<SquadStateMarker>();
            _repo.RegisterComponent<MovementModeIntent>();
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

        private Entity AddMemberWithIntent(Entity commander)
        {
            var m = _repo.CreateEntity();
            _repo.AddComponent(m, new UnitSubordinate { Commander = commander });
            _repo.AddComponent(m, new MovementModeIntent());
            ref var roster = ref _repo.GetComponentRW<UnitRoster>(commander);
            UnitRoster.Add(ref roster, (long)m.PackedValue);
            return m;
        }

        // ── SC-P4-04-1: Bits 8-9 = 1 (Covered) broadcasts Covered to members ────

        [Fact]
        public void CoveredMode_BroadcastToAllMembers()
        {
            var cmd = CreateCommander();
            var m0 = AddMemberWithIntent(cmd);
            var m1 = AddMemberWithIntent(cmd);

            // Set bits 8-9 = 1 (Covered).
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));
            state.Flags = (state.Flags & ~0x0300u) | 0x0100u;

            SquadMovementModeBroadcastSystem.Run(_repo, cmd);

            Assert.Equal(MovementMode.Covered, _repo.GetComponentRO<MovementModeIntent>(m0).Mode);
            Assert.Equal(MovementMode.Covered, _repo.GetComponentRO<MovementModeIntent>(m1).Mode);
        }

        // ── SC-P4-04-2: Bits 8-9 = 0 (Default) broadcasts Default to members ────

        [Fact]
        public void DefaultMode_BroadcastToAllMembers()
        {
            var cmd = CreateCommander();
            var m0 = AddMemberWithIntent(cmd);
            var m1 = AddMemberWithIntent(cmd);

            // First set to Covered, then clear.
            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));
            state.Flags = state.Flags & ~0x0300u;

            SquadMovementModeBroadcastSystem.Run(_repo, cmd);

            Assert.Equal(MovementMode.Normal, _repo.GetComponentRO<MovementModeIntent>(m0).Mode);
            Assert.Equal(MovementMode.Normal, _repo.GetComponentRO<MovementModeIntent>(m1).Mode);
        }

        // ── SC-P4-04-3: Member without MovementModeIntent is unaffected ──────────

        [Fact]
        public void MemberWithoutMovementModeIntent_Unaffected()
        {
            var cmd = CreateCommander();

            // Member with intent.
            var m0 = AddMemberWithIntent(cmd);

            // Member without intent (no MovementModeIntent component).
            var m1 = _repo.CreateEntity();
            _repo.AddComponent(m1, new UnitSubordinate { Commander = cmd });
            ref var roster = ref _repo.GetComponentRW<UnitRoster>(cmd);
            UnitRoster.Add(ref roster, (long)m1.PackedValue);

            ref var state = ref SquadCognitiveState.Project(
                ref _repo.GetComponentRW<Blackboard1024>(cmd));
            state.Flags = (state.Flags & ~0x0300u) | 0x0200u; // Fast = 2

            // Should not throw even though m1 has no MovementModeIntent.
            SquadMovementModeBroadcastSystem.Run(_repo, cmd);

            Assert.Equal(MovementMode.Fast, _repo.GetComponentRO<MovementModeIntent>(m0).Mode);
        }
    }
}
