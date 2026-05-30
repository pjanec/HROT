using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Squad.Tests.Inputs
{
    /// <summary>
    /// Tests for <see cref="SquadInputs"/> SquadKnowsContact and SquadContactThreatLevel readers.
    /// Success criteria: SC-P2-02-1 through SC-P2-02-4.
    /// </summary>
    public unsafe class SquadInputsTests : IDisposable
    {
        private EntityRepository _repo;

        public SquadInputsTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<UnitRoster>();
            _repo.RegisterComponent<UnitSubordinate>();
            _repo.RegisterComponent<Blackboard1024>();
            _repo.RegisterComponent<TargetMemory>();
            _repo.RegisterComponent<SquadStateMarker>();
            SquadInputs.RegisterAll();
        }

        public void Dispose()
        {
            UtilityInputReaderStore.Clear();
            _repo.Dispose();
        }

        // ── Helper ───────────────────────────────────────────────────────────────

        private (Entity commander, Entity[] members) CreateSquadWorld(int memberCount)
        {
            var commander = _repo.CreateEntity();
            _repo.AddComponent(commander, new UnitRoster());
            _repo.AddComponent(commander, new Blackboard1024());
            _repo.AddComponent(commander, new SquadStateMarker());

            var members = new Entity[memberCount];
            for (int i = 0; i < memberCount; i++)
            {
                var m = _repo.CreateEntity();
                _repo.AddComponent(m, new TargetMemory());
                _repo.AddComponent(m, new UnitSubordinate
                {
                    Commander   = commander,
                    Designation = TacticalDesignation.Undefined
                });
                ref var roster = ref _repo.GetComponentRW<UnitRoster>(commander);
                UnitRoster.Add(ref roster, (long)m.PackedValue);
                members[i] = m;
            }
            return (commander, members);
        }

        private UtilityInputCtx MakeCtx(Entity self, Entity context)
            => new UtilityInputCtx { Repo = _repo, Self = self, Context = context };

        // Creates a fake "candidate" entity with a given numeric packed id.
        // We store the raw packed id in a new entity and use it as the Context.
        private Entity FakeEntity(long packedId)
        {
            return new Entity((ulong)packedId);
        }

        // ── SC-P2-02-1: SquadKnowsContact returns 1f when pool has contact ───────

        [Fact]
        public void SquadKnowsContact_ReturnOneWhenCommanderPoolHasContact()
        {
            var (commander, members) = CreateSquadWorld(3);

            // Member 0 sees entity with packed id 100.
            ref var mem0 = ref _repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 5f, 0f, 0.5f, tick: 1);

            // Run merge so pool has entity 100.
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(_repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            // Member 1 does NOT have entity 100 in its own TargetMemory.
            var ctx = MakeCtx(members[1], FakeEntity(100L));
            float result = SquadInputs.SquadKnowsContact(in ctx);

            Assert.Equal(1f, result);
        }

        // ── SC-P2-02-2: SquadKnowsContact returns 0f when contact not in pool ────

        [Fact]
        public void SquadKnowsContact_ReturnZeroWhenContactNotInPool()
        {
            var (commander, members) = CreateSquadWorld(3);

            ref var mem0 = ref _repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 5f, 0f, 0.5f, tick: 1);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(_repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            var ctx = MakeCtx(members[1], FakeEntity(999L));
            float result = SquadInputs.SquadKnowsContact(in ctx);

            Assert.Equal(0f, result);
        }

        // ── SC-P2-02-3: SquadKnowsContact returns 0f for non-squad member ────────

        [Fact]
        public void SquadKnowsContact_ReturnZeroForNonSquadMember()
        {
            var (commander, members) = CreateSquadWorld(3);

            ref var mem0 = ref _repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 5f, 0f, 0.5f, tick: 1);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(_repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            // Standalone entity with no UnitSubordinate component.
            var standalone = _repo.CreateEntity();
            var ctx = MakeCtx(standalone, FakeEntity(100L));
            float result = SquadInputs.SquadKnowsContact(in ctx);

            Assert.Equal(0f, result);
        }

        // ── SC-P2-02-4: SquadContactThreatLevel matches pool score ───────────────

        [Fact]
        public void SquadContactThreatLevel_MatchesPoolScore()
        {
            var (commander, members) = CreateSquadWorld(2);

            ref var mem0 = ref _repo.GetComponentRW<TargetMemory>(members[0]);
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 5f, 0f, 0.5f, tick: 1);

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(_repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            var ctx = MakeCtx(members[1], FakeEntity(100L));
            float result = SquadInputs.SquadContactThreatLevel(in ctx);

            float expected = Math.Clamp(0.5f, 0f, 1f);
            Assert.Equal(expected, result, precision: 5);
        }
    }
}
