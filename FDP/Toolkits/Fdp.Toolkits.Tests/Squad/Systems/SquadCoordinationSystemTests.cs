using System;
using System.Numerics;
using System.Linq;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Systems;
using Xunit;

namespace Fdp.Toolkits.Tests.Squad.Systems
{
    /// <summary>
    /// ⭐ <c>CE-454</c> W2 — the squad layer's frame driver.
    /// 📄 <c>docs/designs/group-maneuvers/DESIGN_Squad_Wiring.md</c> §3. (The through-the-real-pack proof is
    /// <c>CgfLogicPackTests.CE454_TheSquadLayerRunsThroughTheRealCgfPack</c>.)
    /// </summary>
    public sealed class SquadCoordinationSystemTests : IDisposable
    {
        private readonly EntityRepository _repo = new();

        public SquadCoordinationSystemTests()
        {
            _repo.RegisterComponent<BehaviorState>();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<SquadCognitiveState>();
            _repo.RegisterComponent<TargetMemory>();
            _repo.RegisterComponent<SquadLeadership>();
            _repo.RegisterComponent<UnitEntity>();
            _repo.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<Fdp.Toolkit.Combat.Components.InjuryThresholds>();
        }

        public void Dispose() => _repo.Dispose();

        private Entity Squad(bool owned)
        {
            var commander = _repo.CreateEntity();
            _repo.AddComponent(commander, new BehaviorState());
            _repo.SetAuthority<BehaviorState>(commander, owned);
            _repo.AddComponent(commander, new UnitRoster());
            _repo.AddComponent(commander, default(SquadCognitiveState));
            var member = _repo.CreateEntity();
            _repo.AddComponent(member, new TargetMemory());
            _repo.AddComponent(member, new UnitSubordinate { Commander = commander });
            UnitRoster.Add(ref _repo.GetComponentRW<UnitRoster>(commander), member);
            TargetMemory.AddOrUpdateTarget(ref _repo.GetComponentRW<TargetMemory>(member), 100L, 1f, 2f, 0.5f, tick: 1);
            return commander;
        }

        private int PoolCount(Entity commander) => _repo.GetComponentRO<SquadCognitiveState>(commander).Contacts.Count;

        /// <summary>⭐ Every commander's pool is merged from its members — the system, not a hand call.</summary>
        [Fact]
        public void Execute_MergesEveryCommandersPool()
        {
            var a = Squad(owned: true);
            var b = Squad(owned: true);

            new SquadCoordinationSystem().Execute(_repo, 0.016f);

            Assert.Equal(1, PoolCount(a));
            Assert.Equal(1, PoolCount(b));
        }

        /// <summary>⭐ The pack's execution gate: a commander this node does not own is left alone.
        /// ✅ Red-proof: build the query with <c>With&lt;BehaviorState&gt;</c> instead of <c>WithOwnedWhen</c> ⇒ red.</summary>
        [Fact]
        public void Execute_WithTheGate_SkipsACommanderThisNodeDoesNotOwn()
        {
            var owned  = Squad(owned: true);
            var remote = Squad(owned: false);

            new SquadCoordinationSystem(gateOnAuthority: true).Execute(_repo, 0.016f);

            Assert.Equal(1, PoolCount(owned));
            Assert.Equal(0, PoolCount(remote));
        }

        private SensorChangedEvent[] EdgesAfter(Action frame)
        {
            frame();
            _repo.Bus.SwapBuffers();
            return _repo.Bus.Read<SensorChangedEvent>().ToArray();
        }

        private Entity MemberOf(Entity commander) => _repo.GetComponentRO<UnitRoster>(commander).SubordinateEntities[0];

        /// <summary>⭐ <c>CE-2123</c> S2 — the merge that takes the pool from empty to a contact raises <c>SquadContact</c> on the
        /// commander, once; the merge that empties it raises <c>SquadAllClear</c>. ✅ Red-proof: drop the <c>PoolEdges</c> call ⇒ red.</summary>
        [Fact]
        public void CE2123_ThePoolCrossingZero_RaisesSquadContactThenSquadAllClear_OnTheCommander()
        {
            var commander = Squad(owned: true);
            var system = new SquadCoordinationSystem();

            var first = EdgesAfter(() => system.Execute(_repo, 0.016f));
            Assert.Equal(new[] { (commander, SensorChange.SquadContact) }, first.Select(e => (e.Unit, e.What)));

            _repo.Tick();
            var steady = EdgesAfter(() => system.Execute(_repo, 0.016f));
            Assert.Empty(steady);

            TargetMemory.Forget(ref _repo.GetComponentRW<TargetMemory>(MemberOf(commander)), 0);
            for (int i = 0; i < 6; i++) _repo.Tick();
            var cleared = EdgesAfter(() => system.Execute(_repo, 0.016f));
            Assert.Equal(new[] { (commander, SensorChange.SquadAllClear) }, cleared.Select(e => (e.Unit, e.What)));
        }

        /// <summary>⭐ <c>CE-2123</c> S2 — a member's Hit or NearMiss edge (last frame) raises <c>SquadUnderFire</c> on its commander;
        /// not the commander's own (CE-2125: it is body-less); a unit in no squad raises nothing.</summary>
        [Fact]
        public void CE2123_AMemberHitOrShotAt_RaisesSquadUnderFire_OnItsCommander()
        {
            var commander = Squad(owned: true);
            var system = new SquadCoordinationSystem();
            EdgesAfter(() => system.Execute(_repo, 0.016f));   // the first merge's SquadContact
            var loner = _repo.CreateEntity();

            _repo.Bus.Publish(new SensorChangedEvent { Unit = MemberOf(commander), What = SensorChange.Hit });
            _repo.Bus.Publish(new SensorChangedEvent { Unit = MemberOf(commander), What = SensorChange.FirstThreat });
            _repo.Bus.Publish(new SensorChangedEvent { Unit = commander, What = SensorChange.NearMiss });
            _repo.Bus.Publish(new SensorChangedEvent { Unit = loner, What = SensorChange.Hit });
            _repo.Bus.SwapBuffers();
            var edges = EdgesAfter(() => system.Execute(_repo, 0.016f));

            // ⭐ CE-2125 (R-272) — the commander's OWN near miss is no longer the squad's: it is body-less, the leader is a member.
            Assert.Equal(new[] { (commander, SensorChange.SquadUnderFire) }, edges.Select(e => (e.Unit, e.What)));
        }

        /// <summary>⭐ <c>CE-1056</c> I-2d (Squad Tactics §5a) — a CARRIER's Hit or NearMiss edge raises <c>SquadUnderFire</c> on the
        /// commander of the members it carries, once per commander (an enclosed passenger is never hit itself, R-271); an empty
        /// carrier raises nothing.</summary>
        [Fact]
        public void CE1056_ACarrierUnderFire_PutsTheSquadItCarries_UnderFire_Once()
        {
            if (!_repo.IsComponentTypeRegistered<PassengerBuffer>()) _repo.RegisterComponent<PassengerBuffer>();
            if (!_repo.IsComponentTypeRegistered<IsEmbarkedTag>()) _repo.RegisterComponent<IsEmbarkedTag>();
            var commander = Squad(owned: true);
            var system = new SquadCoordinationSystem();
            EdgesAfter(() => system.Execute(_repo, 0.016f));   // the first merge's SquadContact
            var second = _repo.CreateEntity();
            _repo.AddComponent(second, new UnitSubordinate { Commander = commander });
            UnitRoster.Add(ref _repo.GetComponentRW<UnitRoster>(commander), second);
            var roster = _repo.GetComponentRO<UnitRoster>(commander);
            var apc = _repo.CreateEntity();
            _repo.AddComponent(apc, new PassengerBuffer());
            var empty = _repo.CreateEntity();
            _repo.AddComponent(empty, new PassengerBuffer());
            for (int i = 0; i < roster.Count; i++)
                Fdp.Toolkit.Behavior.Embarkation.TryEmbark(_repo, roster.SubordinateEntities[i], apc);
            Assert.Equal(2, _repo.GetComponentRO<PassengerBuffer>(apc).Count);   // two members aboard ⇒ still ONE edge

            _repo.Bus.Publish(new SensorChangedEvent { Unit = apc, What = SensorChange.NearMiss });
            _repo.Bus.Publish(new SensorChangedEvent { Unit = empty, What = SensorChange.Hit });
            _repo.Bus.SwapBuffers();
            var edges = EdgesAfter(() => system.Execute(_repo, 0.016f));

            Assert.Equal(new[] { (commander, SensorChange.SquadUnderFire) }, edges.Select(e => (e.Unit, e.What)));
        }

        /// <summary>⭐ <c>CE-2123</c> S2 — with the pack's gate, a commander this node does not own gets no squad edge.</summary>
        [Fact]
        public void CE2123_WithTheGate_ACommanderThisNodeDoesNotOwn_GetsNoSquadEdges()
        {
            var remote = Squad(owned: false);
            var system = new SquadCoordinationSystem(gateOnAuthority: true);

            _repo.Bus.Publish(new SensorChangedEvent { Unit = MemberOf(remote), What = SensorChange.Hit });
            _repo.Bus.SwapBuffers();
            var edges = EdgesAfter(() => system.Execute(_repo, 0.016f));

            Assert.Empty(edges);
        }

        // ── CE-2125 — the leader soldier falls ────────────────────────────────

        private (Entity commander, Entity[] members) Designated(params TacticalDesignation[] designations)
        {
            var commander = _repo.CreateEntity();
            _repo.AddComponent(commander, new BehaviorState());
            _repo.AddComponent(commander, new UnitRoster());
            _repo.AddComponent(commander, default(SquadCognitiveState));
            _repo.AddComponent(commander, new UnitEntity { CommandDisruptionSeconds = 4f });
            var members = new Entity[designations.Length];
            for (int i = 0; i < designations.Length; i++)
            {
                var m = _repo.CreateEntity();
                _repo.AddComponent(m, new TargetMemory());
                _repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
                _repo.AddComponent(m, new UnitSubordinate { Commander = commander, Designation = designations[i] });
                UnitRoster.Add(ref _repo.GetComponentRW<UnitRoster>(commander), m);
                members[i] = m;
            }
            return (commander, members);
        }

        private void Kill(Entity m) => _repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(m).Current = 0f;
        private void At(double t) => _repo.SetSingleton(new GlobalTime { TotalTime = t, DeltaTime = 0.016f, TimeScale = 1f });

        /// <summary>⭐ §7.2 L1–L3 — the leader falls ⇒ the first live TeamLeader (not the first member) takes over, the commander is silent
        /// for its type's lull, and ONE <c>SquadLeaderDown</c> is published on it. ✅ Red-proof: drop the <c>Succession</c> call ⇒ red.</summary>
        [Fact]
        public void CE2125_TheLeaderFalls_TheTeamLeaderTakesOver_TheCommanderFallsSilent_AndSaysSo()
        {
            var (commander, m) = Designated(TacticalDesignation.SquadLeader, TacticalDesignation.Wingman, TacticalDesignation.TeamLeader);
            var system = new SquadCoordinationSystem();
            At(10); EdgesAfter(() => system.Execute(_repo, 0.016f));
            Assert.Equal(m[0], _repo.GetComponentRO<SquadLeadership>(commander).Leader);

            Kill(m[0]);
            At(11);
            var edges = EdgesAfter(() => system.Execute(_repo, 0.016f));

            Assert.Contains((commander, SensorChange.SquadLeaderDown), edges.Select(e => (e.Unit, e.What)));
            var lead = _repo.GetComponentRO<SquadLeadership>(commander);
            Assert.Equal(m[2], lead.Leader);
            Assert.Equal(15d, lead.SilentUntil, 3);
            Assert.Equal(TacticalDesignation.SquadLeader, _repo.GetComponentRO<UnitSubordinate>(m[2]).Designation);
            Assert.Equal((ushort)TacticalDesignation.SquadLeader, _repo.GetComponentRO<UnitRoster>(commander).TacticalDesignations[2]);

            At(12);
            Assert.DoesNotContain((commander, SensorChange.SquadLeaderDown), EdgesAfter(() => system.Execute(_repo, 0.016f)).Select(e => (e.Unit, e.What)));
        }

        /// <summary>A man DOWN: alive, under the type's incapacitated band (25 of 100 HP, band 0.3).</summary>
        private void Down(Entity m)
        {
            _repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
            _repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(m).Current = 25f;
        }

        /// <summary>⭐ <c>CE-2152</c> C-c (R-2012 K6, Squad Tactics §7.5) — a leader who is DOWN (not dead) hands over exactly like a dead
        /// one, and a down man is never the one picked. ✅ Red-proof: <c>Succession</c> back on <c>CombatLife.IsAlive</c> ⇒ red.</summary>
        [Fact]
        public void CE2152_ADownLeader_HandsOverLikeADeadOne_AndADownManIsNeverPicked()
        {
            var (commander, m) = Designated(TacticalDesignation.SquadLeader, TacticalDesignation.TeamLeader, TacticalDesignation.Wingman);
            var system = new SquadCoordinationSystem();
            At(1); EdgesAfter(() => system.Execute(_repo, 0.016f));
            Down(m[1]);                                   // the team leader is down first
            Down(m[0]);                                   // then the leader
            At(2);
            var edges = EdgesAfter(() => system.Execute(_repo, 0.016f)).Select(e => (e.Unit, e.What)).ToArray();

            Assert.Contains((commander, SensorChange.SquadLeaderDown), edges);
            Assert.Equal(m[2], _repo.GetComponentRO<SquadLeadership>(commander).Leader);   // not the down team leader
        }

        /// <summary>⭐ <c>CE-2152</c> C-c (R-2012 K1) — a man going down raises ONE <c>SquadMemberDown</c> on the commander, naming him;
        /// never again for him, once for the next man; a dead man is not "down". ✅ Red-proof: drop the <c>MemberDown</c> call ⇒ red.</summary>
        [Fact]
        public void CE2152_AManGoingDown_RaisesSquadMemberDown_OnceForHim_OnTheCommander()
        {
            var (commander, m) = Designated(TacticalDesignation.SquadLeader, TacticalDesignation.Wingman, TacticalDesignation.Wingman);
            var system = new SquadCoordinationSystem();
            At(1); EdgesAfter(() => system.Execute(_repo, 0.016f));

            Down(m[1]);
            At(2);
            var first = EdgesAfter(() => system.Execute(_repo, 0.016f)).Where(e => e.What == SensorChange.SquadMemberDown).ToArray();
            Assert.Equal(commander, Assert.Single(first).Unit);
            Assert.Equal(m[1], first[0].Target);

            At(3);
            Assert.DoesNotContain(EdgesAfter(() => system.Execute(_repo, 0.016f)), e => e.What == SensorChange.SquadMemberDown);

            Kill(m[2]);                                   // dead is not down
            Down(m[2]);
            Kill(m[2]);
            At(4);
            Assert.DoesNotContain(EdgesAfter(() => system.Execute(_repo, 0.016f)), e => e.What == SensorChange.SquadMemberDown);
        }

        /// <summary>⭐ <c>CE-2152</c> R-e — a man who is DOWN bleeds, and each drop of his health is a <c>Hit</c> edge: his own hits must not
        /// keep the squad "under fire" (📐 measured live: the bleeding casualty kept it so for as long as he lived). A hit on a man still in
        /// the fight does. 🔴 Red before: every Hit raised <c>SquadUnderFire</c>.</summary>
        [Fact]
        public void CE2152_ADownMansBleeding_DoesNotPutTheSquadUnderFire_AHitOnAFightingManDoes()
        {
            var (commander, m) = Designated(TacticalDesignation.SquadLeader, TacticalDesignation.Wingman, TacticalDesignation.Wingman);
            var system = new SquadCoordinationSystem();
            At(1); EdgesAfter(() => system.Execute(_repo, 0.016f));
            Down(m[1]);
            At(2); EdgesAfter(() => system.Execute(_repo, 0.016f));

            _repo.Bus.Publish(new SensorChangedEvent { Unit = m[1], What = SensorChange.Hit });   // he bleeds
            _repo.Bus.SwapBuffers();
            At(3);
            Assert.DoesNotContain(EdgesAfter(() => system.Execute(_repo, 0.016f)), e => e.What == SensorChange.SquadUnderFire);

            _repo.Bus.Publish(new SensorChangedEvent { Unit = m[2], What = SensorChange.Hit });   // a man still fighting is hit
            _repo.Bus.SwapBuffers();
            At(4);
            Assert.Contains(EdgesAfter(() => system.Execute(_repo, 0.016f)), e => e.What == SensorChange.SquadUnderFire && e.Unit == commander);
        }

        /// <summary>⭐ L1 — no team leader ⇒ the first live member in roster order; a squad that never had a designated leader loses none.</summary>
        [Fact]
        public void CE2125_NoTeamLeader_TheFirstLiveMemberTakesOver_AndALeaderlessSquadHasNoneToLose()
        {
            var (commander, m) = Designated(TacticalDesignation.Wingman, TacticalDesignation.SquadLeader, TacticalDesignation.Wingman);
            var (leaderless, n) = Designated(TacticalDesignation.Wingman, TacticalDesignation.Wingman);
            var system = new SquadCoordinationSystem();
            At(1); EdgesAfter(() => system.Execute(_repo, 0.016f));
            Kill(m[1]); Kill(n[0]);
            At(2);
            var edges = EdgesAfter(() => system.Execute(_repo, 0.016f)).Select(e => (e.Unit, e.What)).ToArray();

            Assert.Equal(m[0], _repo.GetComponentRO<SquadLeadership>(commander).Leader);
            Assert.Contains((commander, SensorChange.SquadLeaderDown), edges);
            Assert.DoesNotContain((leaderless, SensorChange.SquadLeaderDown), edges);
        }

        /// <summary>⭐ C-3 (CE-514) — the owned unit stands at its LIVE members' centre; a dead member does not count; a unit this node
        /// does not own is left where it is. ✅ Red-proof: drop the liveness check ⇒ red.</summary>
        [Fact]
        public void CE2125_AUnitStandsAtItsLiveMembersCentre_OnItsOwner()
        {
            var (owned, a) = Designated(TacticalDesignation.SquadLeader, TacticalDesignation.Wingman, TacticalDesignation.Wingman);
            var (remote, b) = Designated(TacticalDesignation.Wingman);
            foreach (var u in new[] { owned, remote }) _repo.AddComponent(u, new SimTransform { Position = new Vector3(-1, -1, -1) });
            _repo.SetAuthority<SimTransform>(owned, true);
            _repo.SetAuthority<SimTransform>(remote, false);
            _repo.AddComponent(a[0], new SimTransform { Position = new Vector3(0, 0, 0) });
            _repo.AddComponent(a[1], new SimTransform { Position = new Vector3(10, 0, 0) });
            _repo.AddComponent(a[2], new SimTransform { Position = new Vector3(100, 100, 0) });
            _repo.AddComponent(b[0], new SimTransform { Position = new Vector3(7, 7, 0) });
            Kill(a[2]);

            new UnitCentroidSystem().Execute(_repo, 0.016f);

            Assert.Equal(new Vector3(5, 0, 0), _repo.GetComponentRO<SimTransform>(owned).Position);
            Assert.Equal(new Vector3(-1, -1, -1), _repo.GetComponentRO<SimTransform>(remote).Position);
        }

        /// <summary>⭐⭐ User ruling: no allocation on the hot path. The query is built once and cached.</summary>
        [Fact]
        public void Execute_DoesNotAllocate_OnTheSteadyStatePath()
        {
            Squad(owned: true);
            Squad(owned: true);
            var system = new SquadCoordinationSystem();
            for (int i = 0; i < 100; i++) system.Execute(_repo, 0.016f);   // warm up (query build, JIT)

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) system.Execute(_repo, 0.016f);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0, after - before);
        }

        /// <summary>⭐ <c>CE-3088</c> — the fire distribution it now runs after each merge keeps the hot path allocation-free.</summary>
        [Fact]
        public void CE3088_Execute_WithFireAssignment_DoesNotAllocate()
        {
            Fdp.Toolkit.Utility.UtilityAutoDiscovery.ScanAndRegister();
            Fdp.Toolkit.Utility.UtilityDecisionCatalog.EnsureRegistered();
            Squad(owned: true);
            Squad(owned: true);
            var system = new SquadCoordinationSystem(
                fire: new Fdp.Toolkit.Utility.ThreatMatrixAssignmentSystem(Fdp.Toolkit.Utility.LeaderAssignmentDecision.Id));
            void Frame() { _repo.Tick(); system.Execute(_repo, 0.016f); }
            for (int i = 0; i < 100; i++) Frame();   // warm up

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) Frame();   // ≈ 166 merges + assignments
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0, after - before);
        }
    }
}
