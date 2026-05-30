using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Squad.Primitives;
using Fdp.Toolkit.Squad.Systems;
using Xunit;

namespace Fdp.Toolkits.Tests.Squad.Maneuvers
{
    /// <summary>
    /// Integration tests for <see cref="HillCrestHullDownManeuver"/>.
    /// Success criteria: SC-P5-04-1 through SC-P5-04-4.
    /// </summary>
    public class HillCrestHullDownManeuverTests
    {
        // ── SC-P5-04-1: Full wave cycle (Deploying -> Firing -> Retiring -> Deploying) ──

        [Fact]
        public void WaveCycle_DeployFireRetire_CyclesBackToDeploying()
        {
            var state = default(SquadCognitiveState);
            state.PhaseId          = HillCrestHullDownManeuver.PhaseDeploying;
            state.PhaseEnteredTick = 0;
            var table = HillCrestHullDownManeuver.BuildTransitionTable();

            // Deploy -> Firing (FarSideReached)
            bool t1 = PhaseSequencer.Advance(ref state,
                new System.ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.FarSideReached) }),
                table, currentTick: 1, dwellTimeoutTicks: 100, recoveryPhaseId: 3);
            Assert.True(t1);
            Assert.Equal(HillCrestHullDownManeuver.PhaseFiring, state.PhaseId);

            // Firing -> Retiring (ShotFired)
            bool t2 = PhaseSequencer.Advance(ref state,
                new System.ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.ShotFired) }),
                table, currentTick: 2, dwellTimeoutTicks: 100, recoveryPhaseId: 3);
            Assert.True(t2);
            Assert.Equal(HillCrestHullDownManeuver.PhaseRetiring, state.PhaseId);

            // Retiring -> Deploying (DefiladeReached = next wave cycles back)
            bool t3 = PhaseSequencer.Advance(ref state,
                new System.ReadOnlySpan<PhaseEvent>(new[] { new PhaseEvent(PhaseEventKind.DefiladeReached) }),
                table, currentTick: 3, dwellTimeoutTicks: 100, recoveryPhaseId: 3);
            Assert.True(t3);
            Assert.Equal(HillCrestHullDownManeuver.PhaseDeploying, state.PhaseId);
        }

        // ── SC-P5-04-2: Burn/reuse semantics -- 2 burns over 6 slots leave 4 usable ──

        [Fact]
        public void SlotRotation_2Burns_Over6Slots_Leave4Usable_InOrder()
        {
            var rotation = default(SlotRotationState);
            const int totalSlots = 6;

            // Burn slots 0 and 2 (simulates 2 tank losses).
            SlotRotation.BurnSlot(ref rotation, 0);
            SlotRotation.BurnSlot(ref rotation, 2);

            // Remaining slots in order: 1, 3, 4, 5.
            int s1 = SlotRotation.AcquireSlot(ref rotation, totalSlots);
            int s2 = SlotRotation.AcquireSlot(ref rotation, totalSlots);
            int s3 = SlotRotation.AcquireSlot(ref rotation, totalSlots);
            int s4 = SlotRotation.AcquireSlot(ref rotation, totalSlots);
            int s5 = SlotRotation.AcquireSlot(ref rotation, totalSlots);  // should be -1

            Assert.Equal(1, s1);
            Assert.Equal(3, s2);
            Assert.Equal(4, s3);
            Assert.Equal(5, s4);
            Assert.Equal(-1, s5);  // all usable slots exhausted
        }

        // ── SC-P5-04-3: Resume-trap -- live NavigationStatus checked per tick (not cached) ──

        [Fact]
        public void SquadEventIngressSystem_DetectsFarSideReached_FromLiveNavStatus()
        {
            var (repo, commander, members) = BuildFixture(memberCount: 2);
            var system = new SquadEventIngressSystem { FarSideIntentId = 77u };
            var events = new List<PhaseEvent>();

            // First tick: ammo snapshot; nav not arrived.
            system.Run(repo, commander, events);
            Assert.Empty(events);

            // Simulate "arrived mid-tick" by setting Result AFTER the first tick.
            ref var ns = ref repo.GetComponentRW<NavigationStatus>(members[0]);
            ns.Result   = NavigationResult.Arrived;
            ns.IntentId = 77u;

            // Second tick: system reads live state and detects arrival.
            events.Clear();
            system.Run(repo, commander, events);
            Assert.Contains(events, e => e.Kind == PhaseEventKind.FarSideReached);

            repo.Dispose();
        }

        // ── SC-P5-04-4: ComputeTotalSlots matches legacy formula ──────────────────

        [Fact]
        public void ComputeTotalSlots_MatchesLegacyFormula()
        {
            // Legacy: Math.Max(1, (int)(segLen / spacing)), capped at 16.
            // 150m / 30m = 5 slots.
            Assert.Equal(5, HillCrestHullDownManeuver.ComputeTotalSlots(150f, 30f));
            // 0m -> at least 1.
            Assert.Equal(1, HillCrestHullDownManeuver.ComputeTotalSlots(0f, 30f));
            // 500m / 30m = 16 (capped).
            Assert.Equal(16, HillCrestHullDownManeuver.ComputeTotalSlots(500f, 30f));
            // Default spacing when 0 supplied: treats as 30m.
            Assert.Equal(5, HillCrestHullDownManeuver.ComputeTotalSlots(150f, 0f));
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
