using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.Scenario;
using Xunit;

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// Tests for <see cref="ScenarioSerializer.DeserializeWith"/> (RBF-P3T3).
    /// </summary>
    public sealed class ScenarioSerializerDeserializeWithTests : IDisposable
    {
        // ── Setup / Teardown ──────────────────────────────────────────────────

        private readonly EntityRepository _repo;

        public ScenarioSerializerDeserializeWithTests()
        {
            ComponentTypeRegistry.Clear();
            _repo = new EntityRepository();
            RegisterCommonComponents(_repo);
        }

        public void Dispose() => _repo.Dispose();

        private static void RegisterCommonComponents(EntityRepository repo)
        {
            repo.RegisterComponent<DummyPosition>();
            repo.RegisterComponent<GuidedTarget>();
        }

        private static ScenarioSerializer BuildSerializer(string subsystemType = "TestSubsystem")
        {
            var builder = new ScenarioSerializerBuilder(subsystemType);
            return builder.Build();
        }

        // Builds a preAllocated map by creating one entity per key in dom["Entities"].
        private static Dictionary<string, Entity> BuildPreAllocated(
            JsonObject dom, EntityRepository freshRepo)
        {
            var result = new Dictionary<string, Entity>(StringComparer.Ordinal);
            var entitiesNode = (JsonObject)dom["Entities"]!;
            foreach (var kvp in entitiesNode)
                result[kvp.Key] = freshRepo.CreateEntity();
            return result;
        }

        // ── RBF-P3T3: DeserializeWith ─────────────────────────────────────────

        /// <summary>
        /// DeserializeWith must NOT apply the SubsystemType header filter.
        /// A DOM produced by "Hrot.SimHost" must still inject components when
        /// DeserializeWith is called on a serializer configured for "Hrot.CGF".
        /// </summary>
        [Fact]
        public void RBF_P3T3_DeserializeWith_IgnoresSubsystemFilter()
        {
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new DummyPosition { X = 3f, Y = 0f, Z = 0f });

            var simHostSerializer = BuildSerializer("Hrot.SimHost");
            var dom = simHostSerializer.Serialize(_repo, new ScenarioHeader("Hrot.SimHost"));

            // Standard Deserialize with a mismatched serializer is a no-op.
            var cgfSerializer = BuildSerializer("Hrot.CGF");
            using var checkRepo = new EntityRepository();
            RegisterCommonComponents(checkRepo);
            cgfSerializer.Deserialize(checkRepo, dom);
            Assert.Equal(0, checkRepo.EntityCount);

            // DeserializeWith on the same mismatched serializer must inject regardless.
            using var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);
            var preAllocated = BuildPreAllocated(dom, freshRepo);
            var resolver = new FederatedGuidResolver();
            cgfSerializer.DeserializeWith(freshRepo, dom, resolver, preAllocated);

            var loadedEntity = preAllocated.Values.Single();
            Assert.True(freshRepo.HasComponent<DummyPosition>(loadedEntity),
                "DeserializeWith must inject DummyPosition regardless of SubsystemType mismatch.");
            Assert.Equal(3f, freshRepo.GetComponent<DummyPosition>(loadedEntity).X);
        }

        /// <summary>
        /// DeserializeWith injects components via FdpAutoSerializer using the caller-supplied
        /// resolver. Basic round-trip with DummyPosition (no Entity cross-references).
        /// </summary>
        [Fact]
        public void RBF_P3T3_DeserializeWith_InjectsComponentsViaCustomResolver()
        {
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new DummyPosition { X = 1f, Y = 2f, Z = 3f });

            var serializer = BuildSerializer();
            var dom = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            using var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);
            var preAllocated = BuildPreAllocated(dom, freshRepo);
            var resolver = new FederatedGuidResolver();

            serializer.DeserializeWith(freshRepo, dom, resolver, preAllocated);

            var loadedEntity = preAllocated.Values.Single();
            Assert.True(freshRepo.HasComponent<DummyPosition>(loadedEntity));
            var pos = freshRepo.GetComponent<DummyPosition>(loadedEntity);
            Assert.Equal(1f, pos.X);
            Assert.Equal(2f, pos.Y);
            Assert.Equal(3f, pos.Z);
        }

        /// <summary>
        /// When the caller-supplied resolver returns Entity.Null for an unknown cross-reference
        /// GUID, DeserializeWith must not throw; the component is injected with Entity.Null
        /// as the TargetId.
        /// </summary>
        [Fact]
        public void RBF_P3T3_DeserializeWith_AcceptsEntityNullFromResolver()
        {
            // Entity A references entity B via GuidedTarget.
            var entityA = _repo.CreateEntity();
            var entityB = _repo.CreateEntity();
            _repo.SetComponent(entityB, new DummyPosition { X = 0f });
            _repo.SetComponent(entityA, new GuidedTarget  { TargetId = entityB });

            var serializer = BuildSerializer();
            var dom = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            using var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);

            // preAllocated only contains entity A; entity B is NOT pre-allocated.
            // When AutoSerializer tries to resolve the GuidedTarget.TargetId GUID via the
            // resolver (empty load map), FederatedGuidResolver returns Entity.Null.
            var entitiesNode = (JsonObject)dom["Entities"]!;
            var preAllocated = new Dictionary<string, Entity>(StringComparer.Ordinal);
            bool foundA = false;
            foreach (var kvp in entitiesNode)
            {
                var entityNode = (JsonObject?)kvp.Value;
                if (entityNode == null) continue;
                if (entityNode.ContainsKey("GuidedTarget"))
                {
                    // This is entity A (has GuidedTarget component).
                    preAllocated[kvp.Key] = freshRepo.CreateEntity();
                    foundA = true;
                    break;
                }
            }
            Assert.True(foundA, "DOM must contain an entity with GuidedTarget.");

            // Resolver has empty load map: all cross-ref GUIDs resolve to Entity.Null.
            var resolver = new FederatedGuidResolver();

            // Must not throw even though TargetId GUID can't be resolved.
            var exception = Record.Exception(() =>
                serializer.DeserializeWith(freshRepo, dom, resolver, preAllocated));
            Assert.Null(exception);

            // The component must have been injected with Entity.Null as the TargetId.
            var loadedA = preAllocated.Values.Single();
            Assert.True(freshRepo.HasComponent<GuidedTarget>(loadedA));
            var gt = freshRepo.GetComponent<GuidedTarget>(loadedA);
            Assert.Equal(Entity.Null, gt.TargetId);
        }

        /// <summary>
        /// The caller-supplied resolver is forwarded to the auto-serializer for Entity-typed
        /// fields. Verify the resolver's Resolve(string) is called at least once when a
        /// GuidedTarget component is present in the DOM.
        /// </summary>
        [Fact]
        public void RBF_P3T3_DeserializeWith_ResolverReachesAutoSerializer()
        {
            var targetEntity  = _repo.CreateEntity();
            var trackerEntity = _repo.CreateEntity();
            _repo.SetComponent(targetEntity,  new DummyPosition { X = 0f });
            _repo.SetComponent(trackerEntity, new GuidedTarget  { TargetId = targetEntity });

            var serializer = BuildSerializer();
            var dom = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            using var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);
            var preAllocated = BuildPreAllocated(dom, freshRepo);

            var countingResolver = new CountingResolver();
            serializer.DeserializeWith(freshRepo, dom, countingResolver, preAllocated);

            Assert.True(countingResolver.ResolveStringCount > 0,
                "Resolver.Resolve(string) must be called at least once for GuidedTarget.TargetId.");
        }

        /// <summary>
        /// The regular <see cref="ScenarioSerializer.Deserialize"/> must still throw when a
        /// component cross-references a GUID that is not in the Entities section of the DOM.
        /// This proves the strict LoadResolver is unchanged and DeserializeWith is a separate path.
        /// </summary>
        [Fact]
        public void RBF_P3T3_DeserializeWith_DefaultDeserializeStillThrowsOnMissingGuid()
        {
            var entityA = _repo.CreateEntity();
            var entityB = _repo.CreateEntity();
            _repo.SetComponent(entityB, new DummyPosition { X = 1f });
            _repo.SetComponent(entityA, new GuidedTarget  { TargetId = entityB });

            var serializer = BuildSerializer();
            var dom = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            // Tamper: replace GuidedTarget.TargetId with a foreign GUID not in the Entities map.
            var entitiesNode = (JsonObject)dom["Entities"]!;
            foreach (var kvp in entitiesNode)
            {
                var entityNode = (JsonObject?)kvp.Value;
                if (entityNode == null || !entityNode.ContainsKey("GuidedTarget")) continue;
                var guidedNode = (JsonObject)entityNode["GuidedTarget"]!;
                guidedNode["TargetId"] = JsonValue.Create(Guid.NewGuid().ToString());
                break;
            }

            using var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);

            // Regular Deserialize must throw because LoadResolver can't find the foreign GUID.
            Assert.Throws<InvalidOperationException>(() => serializer.Deserialize(freshRepo, dom));
        }

        // ── Helper: counting IGuidResolver ────────────────────────────────────

        private sealed class CountingResolver : IGuidResolver
        {
            public int ResolveStringCount { get; private set; }

            public string Resolve(Entity entity) => "null";

            public Entity Resolve(string guidStr)
            {
                ResolveStringCount++;
                return Entity.Null;
            }
        }
    }
}
