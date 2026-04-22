using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Scenario;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="MissionPlanTranslator"/> — TASK-S201.
    /// </summary>
    [Collection("MissionPlanTranslator")]
    public sealed class MissionPlanTranslatorTests : IDisposable
    {
        private const string SubsystemType = "Test.Scenario";

        private readonly EntityRepository _repo;
        private readonly DoctrineRegistry  _registry;

        // FireAtTarget doctrine ID used across tests.
        private const int FireAtTargetId = 99;

        public MissionPlanTranslatorTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<MissionPlanQueue>();
            _repo.RegisterManagedComponent<ActiveMissionPlan>();

            _registry = new DoctrineRegistry();
            _registry.Register(
                FireAtTargetId, "FireAtTarget",
                new DoctrineDefinition
                {
                    Name = "FireAtTarget",
                    BrainTier = BehaviorConstants.BrainTierBTree,
                });
        }

        public void Dispose()
        {
            _repo.Dispose();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private MissionPlanTranslator MakeTranslator() => new MissionPlanTranslator(_registry);

        private static NullGuidResolver MakeResolver() => new NullGuidResolver();

        /// <summary>Creates an entity with <see cref="ActiveMissionPlan"/> and a matching <see cref="MissionPlanQueue"/>.</summary>
        private Entity CreateMissionEntity(string behaviorId = "FireAtTarget")
        {
            var entity = _repo.CreateEntity();

            var plan = new DomainMissionPlan
            {
                ActiveTaskId = Guid.NewGuid(),
                Tasks        = new System.Collections.Generic.List<DomainMissionTask>
                {
                    new DomainMissionTask
                    {
                        TaskId    = Guid.NewGuid(),
                        BehaviorId = behaviorId,
                        BehaviorParams = string.Empty,
                    }
                }
            };
            _repo.SetManagedComponent(entity, new ActiveMissionPlan { Plan = plan });

            _registry.TryGetId(behaviorId, out int doctrineId);
            var queue = new MissionPlanQueue
            {
                CurrentPhase        = 0,
                PhaseElapsedSeconds = 1.5f,
                PhaseCount          = 1,
            };
            // Mutate Phases via local copy pattern (safe for [InlineArray]).
            Span<MissionPhase> phases = queue.Phases;
            phases[0] = new MissionPhase
            {
                DoctrineId   = doctrineId,
                Trigger      = MissionTrigger.DoctrineFinished,
                TriggerParam = 0f,
            };
            _repo.SetComponent(entity, queue);

            return entity;
        }

        // ── Test 1: Extract returns expected DOM keys and values ──────────────────

        /// <summary>
        /// S201-SC1: Extract produces a dictionary with key "MissionPlan" containing
        /// PlanData, CurrentPhase, and PhaseElapsedSeconds.
        /// </summary>
        [Fact]
        public void Extract_EntityWithActiveMissionPlan_ReturnsMissionPlanDomObject()
        {
            var entity     = CreateMissionEntity();
            var translator = MakeTranslator();

            var result = translator.Extract(_repo, entity, MakeResolver());

            Assert.True(result.ContainsKey("MissionPlan"),
                "Expected 'MissionPlan' key in Extract result.");

            var obj = result["MissionPlan"] as JsonObject;
            Assert.NotNull(obj);
            Assert.NotNull(obj!["PlanData"]);
            Assert.Equal(0, obj["CurrentPhase"]!.GetValue<int>());
            Assert.Equal(1.5f, obj["PhaseElapsedSeconds"]!.GetValue<float>(), precision: 5);
        }

        // ── Test 2: Inject restores ActiveMissionPlan and MissionPlanQueue ────────

        /// <summary>
        /// S201-SC2: Inject with DOM from Extract restores ActiveMissionPlan.Plan.Tasks[0].BehaviorId
        /// and the corresponding MissionPlanQueue.Phases[0].DoctrineId.
        /// </summary>
        [Fact]
        public void Inject_WithExtractedDom_RestoresActivePlanAndQueue()
        {
            var entity     = CreateMissionEntity();
            var translator = MakeTranslator();
            var resolver   = MakeResolver();

            // Extract then wipe both components.
            var dom = translator.Extract(_repo, entity, resolver);
            _repo.SetManagedComponent(entity, new ActiveMissionPlan { Plan = new DomainMissionPlan() });
            _repo.SetComponent(entity, default(MissionPlanQueue));

            // Inject.
            translator.Inject(_repo, entity, dom, resolver);

            // Assert ActiveMissionPlan.
            var activePlan = ((ISimulationView)_repo).GetManagedComponentRO<ActiveMissionPlan>(entity);
            Assert.NotNull(activePlan);
            Assert.Single(activePlan!.Plan.Tasks);
            Assert.Equal("FireAtTarget", activePlan.Plan.Tasks[0].BehaviorId);

            // Assert MissionPlanQueue.
            var queue = _repo.GetComponent<MissionPlanQueue>(entity);
            Assert.Equal(1, queue.PhaseCount);
            Assert.Equal(FireAtTargetId, queue.Phases[0].DoctrineId);
            Assert.Equal(MissionTrigger.DoctrineFinished, queue.Phases[0].Trigger);
        }

        // ── Test 3: CanTranslate returns false without ActiveMissionPlan ──────────

        /// <summary>
        /// S201-SC3: CanTranslate returns false for an entity without ActiveMissionPlan.
        /// </summary>
        [Fact]
        public void CanTranslate_EntityWithoutActiveMissionPlan_ReturnsFalse()
        {
            var entity     = _repo.CreateEntity();
            var translator = MakeTranslator();

            Assert.False(translator.CanTranslate(_repo, entity));
        }

        // ── Test 4: Round-trip via ScenarioSerializer preserves all mission data ──

        /// <summary>
        /// S201-SC4: Full round-trip via ScenarioSerializer preserves Tasks.Count and
        /// all BehaviorId strings.
        /// </summary>
        [Fact]
        public void RoundTrip_ViaScenarioSerializer_PreservesMissionData()
        {
            var entity = CreateMissionEntity("FireAtTarget");

            var serializer = new ScenarioSerializerBuilder(SubsystemType)
                .RegisterTranslator(MakeTranslator())
                .Build();

            var dom = serializer.Serialize(_repo, new ScenarioHeader(SubsystemType));

            var freshRepo = new EntityRepository();
            freshRepo.RegisterComponent<MissionPlanQueue>();
            freshRepo.RegisterManagedComponent<ActiveMissionPlan>();

            serializer.Deserialize(freshRepo, dom);

            Assert.Equal(1, freshRepo.EntityCount);

            // Find the deserialized entity.
            Entity freshEntity = default;
            for (int i = 0; i <= freshRepo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, freshRepo.GetHeader(i).Generation);
                if (freshRepo.IsAlive(e)) { freshEntity = e; break; }
            }
            Assert.True(freshRepo.IsAlive(freshEntity));

            // Verify ActiveMissionPlan.
            Assert.True(freshRepo.HasManagedComponent<ActiveMissionPlan>(freshEntity));
            var plan = ((ISimulationView)freshRepo).GetManagedComponentRO<ActiveMissionPlan>(freshEntity);
            Assert.NotNull(plan);
            Assert.Equal(1, plan!.Plan.Tasks.Count);
            Assert.Equal("FireAtTarget", plan.Plan.Tasks[0].BehaviorId);

            freshRepo.Dispose();
        }

        // ── Null-resolver stub ────────────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="IGuidResolver"/> that returns empty GUID strings
        /// (no entity cross-refs are resolved in MissionPlanTranslator).
        /// </summary>
        private sealed class NullGuidResolver : IGuidResolver
        {
            public string Resolve(Entity entity) => string.Empty;
            public Entity Resolve(string guid) => Entity.Null;
        }
    }
}
