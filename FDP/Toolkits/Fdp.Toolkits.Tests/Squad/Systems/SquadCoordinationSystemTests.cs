using System;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
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
