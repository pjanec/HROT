using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.CGF.Orchestration;
using Hrot.Common.Serializers;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="StagingEntityExtractor"/> — TASK-C004.
    ///
    /// Pattern: each test creates a gold <see cref="EntityRepository"/>, seeds
    /// it with known components, serialises to JSON via <see cref="ScenarioSerializer"/>,
    /// then calls <see cref="StagingEntityExtractor.Extract"/> and asserts on the
    /// returned <see cref="EntityCreationRequest"/> list.
    /// </summary>
    public sealed class StagingEntityExtractorTests : IDisposable
    {
        private const string SubsystemType = "Test.Scenario";

        // ── Test-internal helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="IEntityScenarioTranslator"/> that handles
        /// <see cref="ActiveMissionPlan"/> round-trips via a plain JSON string.
        ///
        /// <para>
        /// <b>GetConsumedComponentsMask returns EMPTY</b> intentionally: <c>ActiveMissionPlan</c>
        /// carries no volatile <see cref="Entity"/> handles, so the
        /// <see cref="StagingEntityExtractor"/> must NOT exclude it from
        /// <see cref="EntityCreationRequest.InitialComponents"/>.
        /// </para>
        /// </summary>
        private sealed class MissionPlanTranslator : IEntityScenarioTranslator
        {
            // Key used in the scenario DOM for the serialised plan JSON string.
            private const string DomKey = "activeMissionPlan";

            public BitMask256 GetConsumedComponentsMask() => new BitMask256();

            public IEnumerable<string> GetOutputDomKeys() { yield return DomKey; }

            public bool CanTranslate(EntityRepository repo, Entity entity)
            {
                int typeId = BehaviorApplicationComponentIds.ActiveMissionPlan;
                return repo.GetHeader(entity.Index).ComponentMask.IsSet(typeId);
            }

            public Dictionary<string, object> Extract(
                EntityRepository repo, Entity entity, IGuidResolver guidResolver)
            {
                var plan = repo.GetComponent<ActiveMissionPlan>(entity);
                var planJson = JsonSerializer.Serialize(plan.Plan,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                return new Dictionary<string, object> { [DomKey] = planJson };
            }

            public void Inject(
                EntityRepository repo, Entity entity,
                Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
            {
                if (!scenarioData.TryGetValue(DomKey, out var raw)) return;
                var planJsonStr = ((JsonNode)raw).GetValue<string>();
                var plan = JsonSerializer.Deserialize<DomainMissionPlan>(
                    planJsonStr,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
                repo.SetComponent(entity, new ActiveMissionPlan { Plan = plan });
            }
        }

        /// <summary>
        /// Minimal translator that marks a given component type ID as consumed
        /// (used to verify translator-consumed components are excluded by extractor).
        /// </summary>
        private sealed class ConsumeOneBitTranslator : IEntityScenarioTranslator
        {
            private readonly BitMask256 _consumed;

            public ConsumeOneBitTranslator(int componentTypeId)
            {
                _consumed = new BitMask256();
                _consumed.SetBit(componentTypeId);
            }

            public BitMask256 GetConsumedComponentsMask() => _consumed;

            public IEnumerable<string> GetOutputDomKeys() => Array.Empty<string>();

            public bool CanTranslate(EntityRepository repo, Entity entity) => false;

            public Dictionary<string, object> Extract(
                EntityRepository repo, Entity entity, IGuidResolver guidResolver)
                => new Dictionary<string, object>();

            public void Inject(
                EntityRepository repo, Entity entity,
                Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
            { }
        }

        // ── Test fixture ──────────────────────────────────────────────────────────

        // Gold repo: populated per test, disposed in Dispose().
        private readonly EntityRepository _goldRepo;

        public StagingEntityExtractorTests()
        {
            _goldRepo = new EntityRepository();
            // Register all component types used across tests so the static
            // ComponentTypeRegistry includes them when ScenarioSerializerBuilder.Build() runs.
            _goldRepo.RegisterComponent<SimTransform>();
            _goldRepo.RegisterComponent<SimVelocity>();
            _goldRepo.RegisterComponent<TkbIdentity>();
            _goldRepo.RegisterComponent<NetworkIdentity>();
            _goldRepo.RegisterComponent<NetworkAuthority>();
            _goldRepo.RegisterComponent<PartMetadata>();
            _goldRepo.RegisterComponent<WeaponState>();
            _goldRepo.RegisterComponent<ActiveMissionPlan>();
            _goldRepo.RegisterComponent<EpisodeTag>();
            _goldRepo.RegisterManagedComponent<InitialPassengersIntent>();
        }

        public void Dispose() => _goldRepo.Dispose();

        // ── Shared helpers ────────────────────────────────────────────────────────

        private static ScenarioSerializer BuildSerializer(
            IEntityScenarioTranslator? extraTranslator = null)
        {
            var builder = new ScenarioSerializerBuilder(SubsystemType);
            if (extraTranslator != null) builder.RegisterTranslator(extraTranslator);
            return builder.Build();
        }

        private static string SerializeGoldRepo(EntityRepository repo, ScenarioSerializer serializer)
            => serializer.Serialize(repo, new ScenarioHeader(SubsystemType)).ToJsonString();

        // ── Test 1: Basic extraction — single root entity ─────────────────────────

        [Fact]
        public void Extract_SingleRootEntity_ReturnsSingleRequestWithCorrectTkbType()
        {
            var e = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(e, new SimTransform { Position = new Vector3(1, 2, 3) });
            _goldRepo.SetComponent(e, new TkbIdentity { TkbType = 42L });

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(100));

            var req = Assert.Single(requests);
            Assert.Equal(42L, req.TkbType);

            var types = req.InitialComponents!.Select(c => c.GetType()).ToHashSet();
            Assert.Contains(typeof(SimTransform), types);
            Assert.DoesNotContain(typeof(NetworkIdentity), types);
            Assert.DoesNotContain(typeof(NetworkAuthority), types);
            Assert.DoesNotContain(typeof(TkbIdentity), types);
        }

        // ── Test 2: TKB structural child entities are filtered out ────────────────

        [Fact]
        public void Extract_EntityWithPartMetadata_IsFilteredOutFromResults()
        {
            // Root entity
            var root = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(root, new SimTransform());

            // Child entity bearing PartMetadata
            var child = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(child, new SimTransform());
            _goldRepo.SetComponent(child,
                new PartMetadata { ParentEntity = root, InstanceId = 1 });

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(100));

            // Only the root entity produces a request; child is harvested into ChildComponentOverrides.
            Assert.Single(requests);
        }

        // ── Test 3a: ORBAT subordinates are NOT filtered out ──────────────────────

        [Fact]
        public void Extract_TwoEntitiesNeitherHasPartMetadata_BothExtracted()
        {
            _goldRepo.CreateEntity(); // commander
            _goldRepo.CreateEntity(); // subordinate (neither has PartMetadata)

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(100));

            Assert.Equal(2, requests.Count);
        }

        // ── Test 3b: Episode tag is appended last to InitialComponents ────────────

        [Fact]
        public void Extract_WithEpisodeId_AppendsEpisodeTagToComponents()
        {
            var e = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(e, new SimTransform());

            var episodeId = Guid.NewGuid();
            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(100), episodeId: episodeId);

            var req = Assert.Single(requests);
            var last = req.InitialComponents!.Last();
            Assert.IsType<EpisodeTag>(last);
            Assert.Equal(episodeId, ((EpisodeTag)last).EpisodeId);
        }

        // ── Test 4: Network ID remapping in ActiveMissionPlan BehaviorParams ──────

        [Fact]
        public void Extract_WithBehaviorRemapper_ReplacesNetworkIdInBehaviorParams()
        {
            // Entity with NetworkIdentity and ActiveMissionPlan referencing same ID.
            const long oldNetId = 1001L;
            const long newNetId = 2001L;

            var e = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(e, new NetworkIdentity { Value = oldNetId });
            _goldRepo.SetComponent(e, new ActiveMissionPlan
            {
                Plan = new DomainMissionPlan
                {
                    Tasks = new List<DomainMissionTask>
                    {
                        new DomainMissionTask
                        {
                            BehaviorId     = "FireAtTarget",
                            BehaviorParams = "{\"targetNetworkId\":1001,\"maxRounds\":5,\"cooldownSeconds\":1.0}",
                        }
                    }
                }
            });

            var serializer = BuildSerializer(extraTranslator: new MissionPlanTranslator());
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var remapper = new ScenarioBehaviorRemapper();
            remapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget");

            // Allocator: entity has NetworkIdentity 1001, receives new ID 2001.
            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(newNetId),
                    behaviorRemapper: remapper);

            var req = Assert.Single(requests);
            var plan = req.InitialComponents!
                .OfType<ActiveMissionPlan>()
                .Single();

            Assert.Contains("2001", plan.Plan.Tasks[0].BehaviorParams);
            Assert.DoesNotContain("1001", plan.Plan.Tasks[0].BehaviorParams);
        }

        // ── Test 5: Entity without NetworkIdentity — no exception in Pass 1 ───────

        [Fact]
        public void Extract_EntityWithoutNetworkIdentity_NoExceptionReturnsSingleRequest()
        {
            var e = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(e, new SimTransform { Position = new Vector3(5, 6, 7) });

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(100));

            var req = Assert.Single(requests);
            var types = req.InitialComponents!.Select(c => c.GetType()).ToHashSet();
            Assert.Contains(typeof(SimTransform), types);
        }

        // ── Test 6: Translator-consumed components are excluded ───────────────────

        [Fact]
        public void Extract_TranslatorConsumedComponent_IsExcludedFromInitialComponents()
        {
            // SimVelocity has type ID GlobalComponentIds.SimVelocity (1).
            // A translator that marks that bit as consumed should cause exclusion.
            int consumedTypeId = GlobalComponentIds.SimVelocity;

            var e = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(e, new SimTransform());
            _goldRepo.SetComponent(e, new SimVelocity { Linear = new Vector3(1, 0, 0) });

            var consumingTranslator = new ConsumeOneBitTranslator(consumedTypeId);
            var serializer = BuildSerializer(extraTranslator: consumingTranslator);
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(100));

            var req = Assert.Single(requests);
            var types = req.InitialComponents!.Select(c => c.GetType()).ToHashSet();
            Assert.DoesNotContain(typeof(SimVelocity), types);
            Assert.Contains(typeof(SimTransform), types); // non-consumed still present
        }

        // ── Test 7: Staging repo is disposed after extraction ────────────────────

        [Fact]
        public void Extract_Always_DisposesStagingRepository()
        {
            var e = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(e, new SimTransform());

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            bool disposeCalled = false;
            var extractor = new StagingEntityExtractor();
            extractor.StagingRepositoryDisposedCallback = () => disposeCalled = true;
            extractor.Extract(serializer, json, new StubIdAllocator(100));

            Assert.True(disposeCalled);
        }

        // ── Test 8: PreAllocatedNetworkId is set from Pass 1 allocation ───────────

        [Fact]
        public void Extract_EntityWithNetworkIdentity_SetsPreAllocatedNetworkId()
        {
            const long oldNetId = 1001L;
            const long expectedNewId = 2001L;

            var e = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(e, new SimTransform());
            _goldRepo.SetComponent(e, new NetworkIdentity { Value = oldNetId });

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(expectedNewId));

            var req = Assert.Single(requests);
            Assert.Equal(expectedNewId, req.PreAllocatedNetworkId);

            // NetworkIdentity must NOT appear in InitialComponents (excluded).
            var types = req.InitialComponents!.Select(c => c.GetType()).ToHashSet();
            Assert.DoesNotContain(typeof(NetworkIdentity), types);
        }

        // ── Test 9: No NetworkIdentity → PreAllocatedNetworkId = 0 ───────────────

        [Fact]
        public void Extract_EntityWithoutNetworkIdentity_PreAllocatedNetworkIdIsZero()
        {
            var e = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(e, new SimTransform());

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(100));

            Assert.Equal(0L, Assert.Single(requests).PreAllocatedNetworkId);
        }

        // ── Test 10: ChildComponentOverrides populated from PartMetadata children ─

        [Fact]
        public void Extract_WithChildEntity_PopulatesChildComponentOverrides()
        {
            const long rootOldId  = 1000L;
            const long childOldId = 1001L;
            const int  instanceId = 3;

            // Root
            var root = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(root, new NetworkIdentity { Value = rootOldId });
            _goldRepo.SetComponent(root, new SimTransform());

            // Child bearing PartMetadata + NetworkIdentity + WeaponState
            var child = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(child, new NetworkIdentity { Value = childOldId });
            _goldRepo.SetComponent(child, new WeaponState { Ammo = 5 });
            _goldRepo.SetComponent(child,
                new PartMetadata { ParentEntity = root, InstanceId = instanceId });

            // Stub allocator: root gets 2000, child gets 2001.
            var allocator = new StubIdAllocator(2000);

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, allocator);

            // Only one request (root).
            var req = Assert.Single(requests);

            Assert.NotNull(req.ChildComponentOverrides);
            Assert.True(req.ChildComponentOverrides!.ContainsKey(instanceId));

            var overrideEntry = req.ChildComponentOverrides[instanceId];
            Assert.Equal(2001L, overrideEntry.PreAllocatedId);

            var childTypes = overrideEntry.Components.Select(c => c.GetType()).ToHashSet();
            Assert.Contains(typeof(WeaponState), childTypes);
            Assert.DoesNotContain(typeof(PartMetadata), childTypes);
            Assert.DoesNotContain(typeof(NetworkIdentity), childTypes);
        }

        // ── Test 11: ChildComponentOverrides is null when root has no children ────

        [Fact]
        public void Extract_RootEntityWithNoChildren_ChildComponentOverridesIsNull()
        {
            var e = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(e, new SimTransform());

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, new StubIdAllocator(100));

            Assert.Null(Assert.Single(requests).ChildComponentOverrides);
        }

        // ── Test 12: Child pre-allocated ID carried to ChildComponentOverrides ────

        [Fact]
        public void Extract_ChildWithNetworkIdentity_CarriesPreAllocatedIdToOverrides()
        {
            const long rootOldId  = 1000L;
            const long childOldId = 1001L;
            const int  instanceId = 3;

            var root = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(root, new NetworkIdentity { Value = rootOldId });

            var child = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(child, new NetworkIdentity { Value = childOldId });
            _goldRepo.SetComponent(child,
                new PartMetadata { ParentEntity = root, InstanceId = instanceId });

            // Allocator: first call (root) → 2000, second call (child) → 2001.
            var allocator = new StubIdAllocator(2000);

            var serializer = BuildSerializer();
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, allocator);

            var req = Assert.Single(requests);
            Assert.NotNull(req.ChildComponentOverrides);
            Assert.Equal(2001L, req.ChildComponentOverrides![instanceId].PreAllocatedId);
        }

        // ── Stub translator for Intent DTO remapping tests ────────────────────────

        /// <summary>
        /// Writes a fixed passenger NetworkId to the scenario DOM during Extract;
        /// reads it back and injects <see cref="InitialPassengersIntent"/> during Inject.
        /// </summary>
        private sealed class StubPassengersIntentTranslator : IEntityScenarioTranslator
        {
            private const string DomKey = "_stubPassengers";
            private readonly long _passengerNetId;

            public StubPassengersIntentTranslator(long passengerNetId)
                => _passengerNetId = passengerNetId;

            public BitMask256 GetConsumedComponentsMask() => new BitMask256();

            public IEnumerable<string> GetOutputDomKeys() { yield return DomKey; }

            // Only fires for entities that have SimTransform (our "vehicle" marker).
            public bool CanTranslate(EntityRepository repo, Entity entity)
            {
                int typeId = GlobalComponentIds.SimTransform;
                return repo.GetHeader(entity.Index).ComponentMask.IsSet(typeId);
            }

            public Dictionary<string, object> Extract(
                EntityRepository repo, Entity entity, IGuidResolver guidResolver)
                => new Dictionary<string, object>
                {
                    [DomKey] = new JsonObject { ["PassengerId"] = _passengerNetId }
                };

            public void Inject(
                EntityRepository repo, Entity entity,
                Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
            {
                if (!scenarioData.TryGetValue(DomKey, out var raw)) return;
                var id = ((JsonObject)raw)["PassengerId"]!.GetValue<long>();
                var intent = new InitialPassengersIntent();
                intent.PassengerNetworkIds.Add(id);
                repo.SetManagedComponent(entity, intent);
            }
        }

        // ── Test 13: InitialPassengersIntent NetworkIds remapped via oldToNewMap ──────

        [Fact]
        public void Extract_InitialPassengersIntent_RemapsPassengerNetworkIdsViaOldToNewMap()
        {
            const long vehicleOldId   = 1001L;
            const long passengerOldId = 2001L;

            // Vehicle entity triggers the stub translator.
            var vehicle = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(vehicle, new SimTransform());
            _goldRepo.SetComponent(vehicle, new NetworkIdentity { Value = vehicleOldId });

            // Passenger entity — its old ID must appear in the Intent DTO.
            var passenger = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(passenger, new NetworkIdentity { Value = passengerOldId });

            // Allocator: vehicle → 3001, passenger → 3002.
            var allocator = new StubIdAllocator(3001);

            var serializer = BuildSerializer(new StubPassengersIntentTranslator(passengerOldId));
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, allocator);

            // There are 2 entities but passenger has no SimTransform, so only vehicle is a
            // root request carrying the Intent DTO.
            var vehicleReq = requests.Single(r => r.PreAllocatedNetworkId == 3001L);

            var intent = vehicleReq.InitialComponents!
                .OfType<InitialPassengersIntent>()
                .Single();

            // Passenger old ID 2001 must have been remapped to 3002.
            Assert.Equal(1, intent.PassengerNetworkIds.Count);
            Assert.Equal(3002L, intent.PassengerNetworkIds[0]);
        }

        // ── Test 14: Unknown passenger NetworkId preserved as-is ──────────────────────

        [Fact]
        public void Extract_InitialPassengersIntent_PreservesUnknownNetworkId()
        {
            const long vehicleOldId      = 1001L;
            const long unknownPassengerId = 9999L; // no entity with this NetworkIdentity exists

            var vehicle = _goldRepo.CreateEntity();
            _goldRepo.SetComponent(vehicle, new SimTransform());
            _goldRepo.SetComponent(vehicle, new NetworkIdentity { Value = vehicleOldId });

            var allocator = new StubIdAllocator(3001); // vehicle → 3001 only

            var serializer = BuildSerializer(new StubPassengersIntentTranslator(unknownPassengerId));
            var json = SerializeGoldRepo(_goldRepo, serializer);

            var requests = new StagingEntityExtractor()
                .Extract(serializer, json, allocator);

            var req = Assert.Single(requests);
            var intent = req.InitialComponents!
                .OfType<InitialPassengersIntent>()
                .Single();

            // Unknown ID 9999 has no mapping — it must be preserved unchanged.
            Assert.Equal(1, intent.PassengerNetworkIds.Count);
            Assert.Equal(unknownPassengerId, intent.PassengerNetworkIds[0]);
        }
    }
}
