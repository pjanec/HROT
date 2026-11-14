using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
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
            _repo.RegisterComponent<SquadCognitiveState>();
            _repo.RegisterComponent<TargetMemory>();
            _repo.RegisterComponent<SquadCognitiveState>();
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
            _repo.AddComponent(commander, default(SquadCognitiveState));
            _repo.AddComponent(commander, default(SquadCognitiveState));

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
                UnitRoster.Add(ref roster, m);
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
            TargetMemory.AddOrUpdateTarget(ref mem0, 100L, 5f, 0f, 250f, tick: 1);   // ⭐ CE-3054 — score 250 = freshness 0.5; the fake contact is not alive here ⇒ danger 1 (unknown)

            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(_repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            var ctx = MakeCtx(members[1], FakeEntity(100L));
            float result = SquadInputs.SquadContactThreatLevel(in ctx);

            float expected = Math.Clamp(0.5f, 0f, 1f);
            Assert.Equal(expected, result, precision: 5);
        }
    
        // ── CE-2123 S2: SquadEnemyStrengthRatio ─────────────────────────────────

        /// <summary>⭐ <c>CE-2123</c> S2 — one armed enemy in the pool against four healthy armed members is 1 ÷ (1 + 4) = 0.2;
        /// an empty pool is 0; a dead member adds no strength.</summary>
        [Fact]
        public void CE2123_SquadEnemyStrengthRatio_OneArmedEnemyAgainstFourHealthyArmed_IsOneFifth()
        {
            _repo.RegisterComponent<Health>();
            _repo.RegisterComponent<WeaponState>();
            var (commander, members) = CreateSquadWorld(4);
            Assert.Equal(0f, SquadInputs.SquadEnemyStrengthRatio(MakeCtx(commander, Entity.Null)));

            foreach (var m in members)
            {
                _repo.AddComponent(m, new Health { Current = 100f, Max = 100f });
                _repo.AddComponent(m, new WeaponState { Ammo = 30, MaxAmmo = 30 });
            }
            var enemy = _repo.CreateEntity();
            _repo.AddComponent(enemy, new WeaponState { Ammo = 30, MaxAmmo = 30 });
            TargetMemory.AddOrUpdateTarget(ref _repo.GetComponentRW<TargetMemory>(members[0]), (long)enemy.PackedValue, 5f, 0f, 0.5f, tick: 1);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(_repo, commander, currentTick: 10, mergeIntervalTicks: 1);

            Assert.Equal(0.2f, SquadInputs.SquadEnemyStrengthRatio(MakeCtx(commander, Entity.Null)), 3);

            _repo.GetComponentRW<Health>(members[3]).Current = 0f;
            Assert.Equal(0.25f, SquadInputs.SquadEnemyStrengthRatio(MakeCtx(commander, Entity.Null)), 3);
        }

        // ── CE-2123 S2: the squad's contact response, decided once ───────────────

        private (Entity commander, Entity[] members) ArmedSquad(int memberCount, int enemies)
        {
            _repo.RegisterComponent<Health>();
            _repo.RegisterComponent<WeaponState>();
            var (commander, members) = CreateSquadWorld(memberCount);
            foreach (var m in members)
            {
                _repo.AddComponent(m, new Health { Current = 100f, Max = 100f });
                _repo.AddComponent(m, new WeaponState { Ammo = 30, MaxAmmo = 30 });
            }
            for (int e = 0; e < enemies; e++) AddEnemy(members[0]);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(_repo, commander, currentTick: 10, mergeIntervalTicks: 1);
            return (commander, members);
        }

        private void AddEnemy(Entity seenBy)
        {
            var enemy = _repo.CreateEntity();
            _repo.AddComponent(enemy, new WeaponState { Ammo = 30, MaxAmmo = 30 });
            TargetMemory.AddOrUpdateTarget(ref _repo.GetComponentRW<TargetMemory>(seenBy), (long)enemy.PackedValue, 5f, 0f, 0.5f, tick: 1);
        }

        private static ChooseOptionParams ContactResponse()
        {
            UtilityAutoDiscovery.ScanAndRegister();
            StandardInputs.RegisterAll();     // ⚠ Dispose clears the readers and the scan runs once per process
            SquadInputs.RegisterAll();
            UtilityDecisionCatalog.EnsureRegistered();
            return new ChooseOptionParams { Decision = UtilityDecisionRef.FromAssetId(SquadContactResponseDecision.DecisionAssetId) };
        }

        /// <summary>⭐ <c>CE-2123</c> S2-D — four healthy riflemen against one armed enemy ASSAULT; one rifleman against four BREAKS CONTACT.</summary>
        [Fact]
        public void CE2123_SquadContactResponse_TheStrongSquadAssaults_TheOutmatchedOneBreaksContact()
        {
            var p = ContactResponse();
            var (strong, _) = ArmedSquad(memberCount: 4, enemies: 1);
            var (weak, _)   = ArmedSquad(memberCount: 1, enemies: 4);

            var choice = default(UtilityChoice);
            UtilityNodes.ChooseOption(ref p, ref choice, strong, _repo);
            Assert.Equal((byte)SquadContactResponse.Assault, choice.Winner);

            choice = default;
            UtilityNodes.ChooseOption(ref p, ref choice, weak, _repo);
            Assert.Equal((byte)SquadContactResponse.BreakContact, choice.Winner);
        }

        /// <summary>⭐ <c>CE-2123</c> S2-E — <c>ChooseOptionOnce</c> keeps its first winner when the world changes; <c>ChooseOption</c>
        /// follows it. ✅ Red-proof: make <c>ChooseOptionOnce</c> score every call ⇒ red.</summary>
        [Fact]
        public void CE2123_ChooseOptionOnce_KeepsItsFirstWinner_WhenTheScoresChange()
        {
            var p = ContactResponse();
            var (commander, members) = ArmedSquad(memberCount: 2, enemies: 1);
            var once = default(UtilityChoice);
            var every = default(UtilityChoice);
            UtilityNodes.ChooseOptionOnce(ref p, ref once, commander, _repo);
            UtilityNodes.ChooseOption(ref p, ref every, commander, _repo);
            Assert.Equal((byte)SquadContactResponse.Assault, once.Winner);
            Assert.Equal((byte)SquadContactResponse.Assault, every.Winner);

            for (int e = 0; e < 6; e++) AddEnemy(members[1]);
            Fdp.Toolkit.Squad.Systems.SquadPerceptionMergeSystem.Run(_repo, commander, currentTick: 20, mergeIntervalTicks: 1);
            UtilityNodes.ChooseOptionOnce(ref p, ref once, commander, _repo);
            UtilityNodes.ChooseOption(ref p, ref every, commander, _repo);

            Assert.Equal((byte)SquadContactResponse.Assault, once.Winner);
            Assert.Equal((byte)SquadContactResponse.BreakContact, every.Winner);
        }
}
}
