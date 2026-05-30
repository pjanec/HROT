using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Primitives;
using Fdp.Toolkit.Squad.Systems;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Systems
{
    /// <summary>
    /// Tests for <see cref="SquadEventIngressSystem"/>.
    /// Success criteria: SC-P4-03-1 through SC-P4-03-3.
    /// </summary>
    public class SquadEventIngressSystemTests : IDisposable
    {
        private EntityRepository _repo;
        private SquadEventIngressSystem _system;
        private Entity _commander, _member0, _member1;
        private List<PhaseEvent> _events;

        public SquadEventIngressSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<WeaponState>();
            _repo.RegisterComponent<NavigationStatus>();
            _repo.RegisterComponent<UnitSubordinate>();

            _system = new SquadEventIngressSystem();
            _events = new List<PhaseEvent>();

            _commander = _repo.CreateEntity();
            _repo.AddComponent(_commander, new UnitRoster());
            _repo.AddComponent(_commander, new Blackboard1024());

            _member0 = _repo.CreateEntity();
            _repo.AddComponent(_member0, new WeaponState { Ammo = 10, MaxAmmo = 10 });
            _repo.AddComponent(_member0, new NavigationStatus());
            ref var roster = ref _repo.GetComponentRW<UnitRoster>(_commander);
            UnitRoster.Add(ref roster, (long)_member0.PackedValue);

            _member1 = _repo.CreateEntity();
            _repo.AddComponent(_member1, new WeaponState { Ammo = 10, MaxAmmo = 10 });
            _repo.AddComponent(_member1, new NavigationStatus());
            ref var roster2 = ref _repo.GetComponentRW<UnitRoster>(_commander);
            UnitRoster.Add(ref roster2, (long)_member1.PackedValue);
        }

        public void Dispose() => _repo.Dispose();

        // ── SC-P4-03-1: ShotFired detection ─────────────────────────────────────

        [Fact]
        public void Run_DetectsShotFired_WhenMemberAmmoDecreases()
        {
            // First call: snapshot prevAmmo = 10. No events.
            _system.Run(_repo, _commander, _events);
            Assert.Empty(_events);

            // Decrement ammo.
            _repo.GetComponentRW<WeaponState>(_member0).Ammo = 9;

            _events.Clear();
            _system.Run(_repo, _commander, _events);
            Assert.Single(_events);
            Assert.Equal(PhaseEventKind.ShotFired, _events[0].Kind);
        }

        [Fact]
        public void Run_NoShotFired_WhenAmmoUnchanged()
        {
            _system.Run(_repo, _commander, _events);  // snapshot
            _events.Clear();
            _system.Run(_repo, _commander, _events);  // same ammo
            Assert.Empty(_events);
        }

        // ── SC-P4-03-2: FarSideReached detection ────────────────────────────────

        [Fact]
        public void Run_DetectsFarSideReached_WhenIntentIdMatches()
        {
            _system.FarSideIntentId = 42u;
            _system.Run(_repo, _commander, _events);  // snapshot ammo
            _events.Clear();

            ref var ns = ref _repo.GetComponentRW<NavigationStatus>(_member0);
            ns.Result   = NavigationResult.Arrived;
            ns.IntentId = 42u;

            _system.Run(_repo, _commander, _events);
            Assert.Contains(_events, e => e.Kind == PhaseEventKind.FarSideReached);
        }

        [Fact]
        public void Run_NoFarSideReached_WhenIntentIdMismatch()
        {
            _system.FarSideIntentId = 42u;
            _system.Run(_repo, _commander, _events);  // snapshot
            _events.Clear();

            ref var ns = ref _repo.GetComponentRW<NavigationStatus>(_member0);
            ns.Result   = NavigationResult.Arrived;
            ns.IntentId = 99u;  // different

            _system.Run(_repo, _commander, _events);
            Assert.DoesNotContain(_events, e => e.Kind == PhaseEventKind.FarSideReached);
        }

        // ── SC-P4-03-3: TimerFallback via PhaseSequencer integration ────────────

        [Fact]
        public void PhaseSequencer_Advance_TriggersTimerFallback_WhenDwellElapsed()
        {
            // Fabricate a SquadCognitiveState with phase 1 entered at tick 0.
            var state = default(SquadCognitiveState);
            state.PhaseId          = 1;
            state.PhaseEnteredTick = 0;

            // No events.
            var events = ReadOnlySpan<PhaseEvent>.Empty;
            var table  = ReadOnlySpan<PhaseTransitionEntry>.Empty;

            // Dwell of 50 ticks; current tick = 100. Should transition to recovery (0).
            bool transitioned = PhaseSequencer.Advance(ref state, events, table,
                                                       currentTick: 100, dwellTimeoutTicks: 50,
                                                       recoveryPhaseId: 0);

            Assert.True(transitioned);
            Assert.Equal(0, state.PhaseId);
        }

        [Fact]
        public void PhaseSequencer_Advance_NoTimerFallback_BeforeDwell()
        {
            var state = default(SquadCognitiveState);
            state.PhaseId          = 1;
            state.PhaseEnteredTick = 80;

            var events = ReadOnlySpan<PhaseEvent>.Empty;
            var table  = ReadOnlySpan<PhaseTransitionEntry>.Empty;

            // Current tick = 100, entered at 80 = 20 ticks, dwell = 50. Not yet.
            bool transitioned = PhaseSequencer.Advance(ref state, events, table,
                                                       currentTick: 100, dwellTimeoutTicks: 50,
                                                       recoveryPhaseId: 0);

            Assert.False(transitioned);
            Assert.Equal(1, state.PhaseId);  // unchanged
        }

        // ── Exactly-one-event guard (SC-P4-03-1 parity) ─────────────────────────

        [Fact]
        public void Run_EmitsExactlyOneShotFired_PerFiringEvent()
        {
            // Two members, only one fires.
            _system.Run(_repo, _commander, _events);  // snapshot both at 10
            _events.Clear();

            _repo.GetComponentRW<WeaponState>(_member0).Ammo = 9;  // member0 fires
            // member1 ammo stays at 10

            _system.Run(_repo, _commander, _events);
            Assert.Single(_events);
            Assert.Equal(PhaseEventKind.ShotFired, _events[0].Kind);
        }
    }
}
