using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Kernel;
using FDP.Toolkit.Scenario;

namespace FDP.Toolkit.Scenario.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ScenarioSerializer"/> (CGF1-S0306 success conditions).
    /// </summary>
    public sealed class ScenarioSerializerTests : IDisposable
    {
        // ── Setup / Teardown ─────────────────────────────────────────────────────

        private readonly EntityRepository _repo;

        public ScenarioSerializerTests()
        {
            // Clear global static registry so each test starts from a predictable state.
            // WARNING: ComponentTypeRegistry is a shared static — tests run sequentially
            // within this class via xUnit's default serial ordering per class.
            ComponentTypeRegistry.Clear();

            _repo = new EntityRepository();
            RegisterCommonComponents(_repo);
        }

        public void Dispose() => _repo.Dispose();

        /// <summary>Registers the component types used across all tests in this class.</summary>
        private static void RegisterCommonComponents(EntityRepository repo)
        {
            repo.RegisterComponent<DummyPosition>();
            repo.RegisterComponent<TestBallisticProjectile>();
            repo.RegisterComponent<TestPhysicsCollider>();
            repo.RegisterComponent<GuidedTarget>();
            repo.RegisterComponent<CachedSpeedComponent>();
            repo.RegisterComponent<NoSaveVelocity>(); // [DataPolicy(DataPolicy.NoSave)]
            repo.RegisterComponent<ScenarioIgnoreTag>();
            repo.RegisterComponent<StoryTag>();
        }

        // ── Helper ───────────────────────────────────────────────────────────────

        private static ScenarioSerializer BuildSerializer(
            string subsystemType = "TestSubsystem",
            params IEntityScenarioTranslator[] translators)
        {
            var builder = new ScenarioSerializerBuilder(subsystemType);
            foreach (var t in translators) builder.RegisterTranslator(t);
            return builder.Build();
        }

        // ── RoundTrip_1to1_PreservesAllFields ────────────────────────────────────

        /// <summary>
        /// No custom translators; <c>FdpAutoSerializer</c> round-trips 3 entities each
        /// with a <c>DummyPosition</c> component.
        /// </summary>
        [Fact]
        public void RoundTrip_1to1_PreservesAllFields()
        {
            var e1 = _repo.CreateEntity(); _repo.SetComponent(e1, new DummyPosition { X = 1f, Y = 2f, Z = 3f });
            var e2 = _repo.CreateEntity(); _repo.SetComponent(e2, new DummyPosition { X = 4f, Y = 5f, Z = 6f });
            var e3 = _repo.CreateEntity(); _repo.SetComponent(e3, new DummyPosition { X = 7f, Y = 8f, Z = 9f });

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);
            serializer.Deserialize(freshRepo, dom);

            Assert.Equal(3, freshRepo.EntityCount);

            // Collect all DummyPosition values from the fresh repo.
            var positions = new System.Collections.Generic.List<DummyPosition>();
            for (int i = 0; i <= freshRepo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, freshRepo.GetHeader(i).Generation);
                if (!freshRepo.IsAlive(e)) continue;
                if (freshRepo.HasComponent<DummyPosition>(e))
                    positions.Add(freshRepo.GetComponent<DummyPosition>(e));
            }
            positions.Sort((a, b) => a.X.CompareTo(b.X));

            Assert.Equal(3, positions.Count);
            Assert.Equal(1f, positions[0].X); Assert.Equal(2f, positions[0].Y); Assert.Equal(3f, positions[0].Z);
            Assert.Equal(4f, positions[1].X); Assert.Equal(5f, positions[1].Y); Assert.Equal(6f, positions[1].Z);
            Assert.Equal(7f, positions[2].X); Assert.Equal(8f, positions[2].Y); Assert.Equal(9f, positions[2].Z);

            freshRepo.Dispose();
        }

        // ── NtoM_CustomTranslator_CompressesComponents ────────────────────────────

        /// <summary>
        /// <c>MissileOrdnanceTranslator</c> compresses <c>TestBallisticProjectile</c> +
        /// <c>TestPhysicsCollider</c> into a single <c>"OrdnanceDef"</c> DOM key.
        /// Round-trip restores both components.
        /// </summary>
        [Fact]
        public void NtoM_CustomTranslator_CompressesComponents()
        {
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new TestBallisticProjectile { Damage = 50f, Speed = 800f });
            _repo.SetComponent(entity, new TestPhysicsCollider     { Radius = 0.3f });

            var serializer = BuildSerializer("TestSubsystem", new MissileOrdnanceTranslator());
            var dom        = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            // Assert DOM shape.
            var entitiesNode  = (JsonObject)dom["Entities"]!;
            var entityNode    = (JsonObject)entitiesNode.First().Value!;

            Assert.True(entityNode.ContainsKey("OrdnanceDef"),              "DOM must contain OrdnanceDef key");
            Assert.False(entityNode.ContainsKey("TestBallisticProjectile"), "BP must NOT appear separately");
            Assert.False(entityNode.ContainsKey("TestPhysicsCollider"),     "Collider must NOT appear separately");

            // Round-trip: deserialize into a fresh repo and verify both components were restored.
            var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);
            serializer.Deserialize(freshRepo, dom);

            Assert.Equal(1, freshRepo.EntityCount);
            Entity loaded = GetSingleEntity(freshRepo);
            Assert.True(freshRepo.HasComponent<TestBallisticProjectile>(loaded));
            Assert.True(freshRepo.HasComponent<TestPhysicsCollider>(loaded));

            var bp = freshRepo.GetComponent<TestBallisticProjectile>(loaded);
            var pc = freshRepo.GetComponent<TestPhysicsCollider>(loaded);
            Assert.Equal(50f,  bp.Damage);
            Assert.Equal(800f, bp.Speed);
            Assert.Equal(0.3f, pc.Radius, precision: 5);

            freshRepo.Dispose();
        }

        // ── ConsumptionMask_PreventsDuplication ───────────────────────────────────

        /// <summary>
        /// After the translator's <c>Extract</c> runs, the consumed bits are cleared from
        /// <c>remainingMask</c> so <c>FdpAutoSerializer</c> does not emit those components.
        /// </summary>
        [Fact]
        public void ConsumptionMask_PreventsDuplication()
        {
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new TestBallisticProjectile { Damage = 10f, Speed = 200f });
            _repo.SetComponent(entity, new TestPhysicsCollider     { Radius = 1f });

            var serializer = BuildSerializer("TestSubsystem", new MissileOrdnanceTranslator());
            var dom        = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            var entitiesNode = (JsonObject)dom["Entities"]!;
            var entityNode   = (JsonObject)entitiesNode.First().Value!;

            // Neither component should appear as a top-level auto-serialized key.
            Assert.False(entityNode.ContainsKey("TestBallisticProjectile"),
                "Auto-serializer must not emit TestBallisticProjectile after translator consumed it.");
            Assert.False(entityNode.ContainsKey("TestPhysicsCollider"),
                "Auto-serializer must not emit TestPhysicsCollider after translator consumed it.");
        }

        // ── EntityCrossReference_ResolvedViaIGuidResolver ────────────────────────

        /// <summary>
        /// <c>GuidedTarget.TargetId: Entity</c> is serialized as a GUID string and
        /// resolved back to a live entity handle on deserialization.
        /// </summary>
        [Fact]
        public void EntityCrossReference_ResolvedViaIGuidResolver()
        {
            var targetEntity  = _repo.CreateEntity();
            _repo.SetComponent(targetEntity, new DummyPosition { X = 99f, Y = 0f, Z = 0f });

            var trackerEntity = _repo.CreateEntity();
            _repo.SetComponent(trackerEntity, new GuidedTarget { TargetId = targetEntity });

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            // Verify the DOM contains a GUID string (not an integer) for TargetId.
            var entitiesNode = (JsonObject)dom["Entities"]!;
            bool foundGuidField = false;
            foreach (var ekv in entitiesNode)
            {
                var eNode = (JsonObject?)ekv.Value;
                if (eNode == null || !eNode.ContainsKey("GuidedTarget")) continue;
                var guidedNode = (JsonObject)eNode["GuidedTarget"]!;
                var rawTargetId = guidedNode["TargetId"]?.GetValue<string>();
                Assert.True(Guid.TryParse(rawTargetId, out _),
                    "TargetId in DOM must be a valid GUID string.");
                foundGuidField = true;
            }
            Assert.True(foundGuidField, "GuidedTarget component must appear in the DOM.");

            // Round-trip into fresh repo.
            var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);
            serializer.Deserialize(freshRepo, dom);

            Assert.Equal(2, freshRepo.EntityCount);

            // Find the tracker entity and verify its TargetId resolves to a valid entity.
            Entity resolvedTracker = default;
            for (int i = 0; i <= freshRepo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, freshRepo.GetHeader(i).Generation);
                if (!freshRepo.IsAlive(e)) continue;
                if (freshRepo.HasComponent<GuidedTarget>(e)) { resolvedTracker = e; break; }
            }
            Assert.True(freshRepo.IsAlive(resolvedTracker), "Tracker entity must exist.");

            var gt = freshRepo.GetComponent<GuidedTarget>(resolvedTracker);
            Assert.True(freshRepo.IsAlive(gt.TargetId), "Resolved TargetId must be a live entity.");

            // The target entity should still have the DummyPosition.
            Assert.True(freshRepo.HasComponent<DummyPosition>(gt.TargetId));
            Assert.Equal(99f, freshRepo.GetComponent<DummyPosition>(gt.TargetId).X);

            freshRepo.Dispose();
        }

        // ── DataPolicyNoSave_ComponentExcluded ───────────────────────────────────

        /// <summary>
        /// <c>NoSaveVelocity</c> is marked <c>[DataPolicy(DataPolicy.NoSave)]</c> and
        /// must be absent from the serialized DOM.
        /// </summary>
        [Fact]
        public void DataPolicyNoSave_ComponentExcluded()
        {
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new DummyPosition    { X = 1f   });
            _repo.SetComponent(entity, new NoSaveVelocity   { Speed = 5f });

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            var entitiesNode = (JsonObject)dom["Entities"]!;
            var entityNode   = (JsonObject)entitiesNode.First().Value!;

            Assert.False(entityNode.ContainsKey("NoSaveVelocity"),
                "NoSave component must be absent from the DOM.");
            Assert.True(entityNode.ContainsKey("DummyPosition"),
                "Saveable component must still appear in the DOM.");
        }

        // ── ScenarioIgnore_FieldExcluded ──────────────────────────────────────────

        /// <summary>
        /// <c>CachedSpeedComponent.MaxSpeed</c> (saved) appears in the DOM;
        /// <c>CachedSpeedComponent.CachedWheelAngle</c> (<c>[ScenarioIgnore]</c>) does not.
        /// </summary>
        [Fact]
        public void ScenarioIgnore_FieldExcluded()
        {
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new CachedSpeedComponent
            {
                MaxSpeed        = 60f,
                CachedWheelAngle = 1.5f,
            });

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            var entitiesNode     = (JsonObject)dom["Entities"]!;
            var entityNode       = (JsonObject)entitiesNode.First().Value!;
            var speedNode        = (JsonObject)entityNode["CachedSpeedComponent"]!;

            Assert.True(speedNode.ContainsKey("MaxSpeed"),
                "MaxSpeed must be present in the DOM.");
            Assert.False(speedNode.ContainsKey("CachedWheelAngle"),
                "CachedWheelAngle must be absent (annotated [ScenarioIgnore]).");
        }

        // ── ScenarioIgnoreTag_EntitySkipped ──────────────────────────────────────

        /// <summary>
        /// An entity bearing <see cref="ScenarioIgnoreTag"/> must not appear in
        /// <c>dom["Entities"]</c>.
        /// </summary>
        [Fact]
        public void ScenarioIgnoreTag_EntitySkipped()
        {
            var invisibleEntity = _repo.CreateEntity();
            _repo.SetComponent(invisibleEntity, new DummyPosition { X = 5f });
            _repo.SetComponent(invisibleEntity, new ScenarioIgnoreTag());

            var visibleEntity = _repo.CreateEntity();
            _repo.SetComponent(visibleEntity, new DummyPosition { X = 10f });

            var serializer   = BuildSerializer();
            var dom          = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));
            var entitiesNode = (JsonObject)dom["Entities"]!;

            Assert.Single(entitiesNode);
        }

        // ── StoryLoad_StampsStoryTag ──────────────────────────────────────────────

        /// <summary>
        /// Deserializing with <c>asStory: true</c> stamps <see cref="StoryTag"/> on every
        /// created entity.
        /// </summary>
        [Fact]
        public void StoryLoad_StampsStoryTag()
        {
            var e1 = _repo.CreateEntity(); _repo.SetComponent(e1, new DummyPosition { X = 1f });
            var e2 = _repo.CreateEntity(); _repo.SetComponent(e2, new DummyPosition { X = 2f });

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));

            var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);
            serializer.Deserialize(freshRepo, dom, asStory: true, storyId: "story_01");

            Assert.Equal(2, freshRepo.EntityCount);
            for (int i = 0; i <= freshRepo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, freshRepo.GetHeader(i).Generation);
                if (!freshRepo.IsAlive(e)) continue;

                Assert.True(freshRepo.HasComponent<StoryTag>(e),
                    $"Entity {e} must have StoryTag after story load.");
                var tag = freshRepo.GetComponent<StoryTag>(e);
                Assert.Equal("story_01", tag.StoryId);
            }

            freshRepo.Dispose();
        }

        // ── SubsystemType_MismatchSkipsDeserialize ───────────────────────────────

        /// <summary>
        /// A DOM with a mismatched <c>SubsystemType</c> must not cause any entity
        /// creation.
        /// </summary>
        [Fact]
        public void SubsystemType_MismatchSkipsDeserialize()
        {
            var sourceRepo = new EntityRepository();
            RegisterCommonComponents(sourceRepo);
            var e = sourceRepo.CreateEntity();
            sourceRepo.SetComponent(e, new DummyPosition { X = 1f });

            var cgfSerializer  = BuildSerializer("Bagira.CGF");
            var simhostSerializer = BuildSerializer("Bagira.SimHost");

            // Serialize as SimHost.
            var dom = simhostSerializer.Serialize(sourceRepo, new ScenarioHeader("Bagira.SimHost"));

            Assert.Equal(0, _repo.EntityCount);
            // Deserialize using a serializer configured for CGF — should be a no-op.
            cgfSerializer.Deserialize(_repo, dom);
            Assert.Equal(0, _repo.EntityCount);

            sourceRepo.Dispose();
        }

        // ── FdpAutoSerializer_NoReflectionOnHotPath ──────────────────────────────

        /// <summary>
        /// After <c>Build()</c>, the <c>FdpAutoSerializer</c> operates through compiled
        /// delegates instead of <c>PropertyInfo.GetValue</c> calls.
        /// </summary>
        [Fact]
        public void FdpAutoSerializer_NoReflectionOnHotPath()
        {
            var serializer = BuildSerializer();

            // Assert: the auto-serializer is in "compiled delegate" mode, not runtime-reflection mode.
            Assert.True(serializer.AutoSerializer.IsBuilt,
                "FdpAutoSerializer must be built (delegates compiled) after ScenarioSerializerBuilder.Build().");
            Assert.False(serializer.AutoSerializer.UsesRuntimeReflection,
                "FdpAutoSerializer must not use PropertyInfo.GetValue on the hot path.");

            // Functional assertion: if compiled delegates execute correctly, a round-trip
            // returns matching values — proving field access works without reflection.
            var entity = _repo.CreateEntity();
            _repo.SetComponent(entity, new DummyPosition { X = 42f, Y = 43f, Z = 44f });

            var dom       = serializer.Serialize(_repo, new ScenarioHeader("TestSubsystem"));
            var freshRepo = new EntityRepository();
            RegisterCommonComponents(freshRepo);
            serializer.Deserialize(freshRepo, dom);

            Assert.Equal(1, freshRepo.EntityCount);
            var loaded = GetSingleEntity(freshRepo);
            var pos    = freshRepo.GetComponent<DummyPosition>(loaded);
            Assert.Equal(42f, pos.X);
            Assert.Equal(43f, pos.Y);
            Assert.Equal(44f, pos.Z);

            freshRepo.Dispose();
        }

        // ── Utility ─────────────────────────────────────────────────────────────

        private static Entity GetSingleEntity(EntityRepository repo)
        {
            for (int i = 0; i <= repo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, repo.GetHeader(i).Generation);
                if (repo.IsAlive(e)) return e;
            }
            throw new InvalidOperationException("No alive entity found in repository.");
        }
    }
}
