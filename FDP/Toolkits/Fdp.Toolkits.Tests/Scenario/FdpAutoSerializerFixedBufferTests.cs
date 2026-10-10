using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Scenario;
using Xunit;

// ── Test-only components for fixed-buffer and InlineArray serialization ──────
// IDs 220–229 reserved for this test file.

namespace Fdp.Toolkit.Scenario.Tests
{
    /// <summary>
    /// Unsafe component with a <c>fixed byte</c> buffer — the primary TASK-S301 use case.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(220)]
    public unsafe struct FixedByteComp
    {
        public fixed byte Data[4];
    }

    /// <summary>
    /// Component with a fixed buffer whose element type is <c>long</c>.
    /// Used to verify that <c>long</c>-typed fixed buffers are serialized correctly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(221)]
    public unsafe struct FixedLongComp
    {
        public fixed long Values[2];
    }

    /// <summary>Simple [InlineArray] of floats.</summary>
    [InlineArray(3)]
    [StructLayout(LayoutKind.Sequential)]
    public struct Float3Buffer
    {
        private float _element;
    }

    /// <summary>
    /// Component with an [InlineArray] float field.
    /// Primary TASK-S302 use case (alongside MissionPlanQueue).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    // ⚠ CE-3107 — was 222, which is the production StanceIntent (GlobalComponentIds.StanceIntent): in one test process the two
    //   collided and whichever registered second threw "Component ID collision", failing a rotating set of combat/stance tests.
    //   Test-only ids live high (480–511), clear of every production block — R-44.
    [ComponentId(505)]
    public struct InlineFloatComp
    {
        public Float3Buffer Values;
    }

    // ── Test class ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Tests for <see cref="FdpAutoSerializer"/> fixed-buffer and [InlineArray] support
    /// (TASK-S301, TASK-S302).
    /// </summary>
    [Collection("FdpAutoSerializerFixedBuffer")]
    public sealed class FdpAutoSerializerFixedBufferTests : IDisposable
    {
        private const string SubsystemType = "Test.FixedBuffer";
        private readonly EntityRepository _repo;

        public FdpAutoSerializerFixedBufferTests()
        {
            ComponentTypeRegistry.Clear();

            _repo = new EntityRepository();
            _repo.RegisterComponent<FixedByteComp>();
            _repo.RegisterComponent<FixedLongComp>();
            _repo.RegisterComponent<InlineFloatComp>();
            // ⭐ P4: was RegisterComponent<BrainBlackboard>(), the NoScenario stand-in this fixture
            //   needs for the DOM-exclusion test. BehaviorState carries the same attribute and
            //   outlives the retired component. ⚠ It must be REGISTERED, not merely set: SetComponent
            //   goes through AddUnmanagedComponent, which refuses an unregistered type.
            _repo.RegisterComponent<BehaviorState>();
            // Override NoScenario so FdpAutoSerializer includes MissionPlanQueue in round-trip tests.
            _repo.RegisterComponent<MissionPlanQueue>(DataPolicy.Default);
        }

        public void Dispose()
        {
            _repo.Dispose();
            ComponentTypeRegistry.Clear();
        }

        private static ScenarioSerializer BuildSerializer()
            => new ScenarioSerializerBuilder(SubsystemType).Build();

        // ── S301-SC1: Fixed byte buffer — extract produces JsonArray ─────────────

        /// <summary>
        /// S301-SC1: A <c>fixed byte</c> buffer is serialized as a JSON array of integers.
        /// </summary>
        [Fact]
        public unsafe void Extract_FixedByteBuffer_ProducesJsonArray()
        {
            var entity = _repo.CreateEntity();
            var comp   = new FixedByteComp();
            comp.Data[0] = 1; comp.Data[1] = 2; comp.Data[2] = 3; comp.Data[3] = 4;
            _repo.SetComponent(entity, comp);

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader(SubsystemType));

            var entityNode = (JsonObject)((JsonObject)dom["Entities"]!).First().Value!;
            var dataArr    = (JsonArray)entityNode["FixedByteComp"]!["Data"]!;

            Assert.Equal(4, dataArr.Count);
            Assert.Equal(1, dataArr[0]!.GetValue<byte>());
            Assert.Equal(2, dataArr[1]!.GetValue<byte>());
            Assert.Equal(3, dataArr[2]!.GetValue<byte>());
            Assert.Equal(4, dataArr[3]!.GetValue<byte>());
        }

        // ── S301-SC2: Fixed byte buffer — inject restores values ─────────────────

        /// <summary>
        /// S301-SC2: Injecting from a JsonArray restores fixed byte buffer values.
        /// </summary>
        [Fact]
        public unsafe void Inject_FixedByteBuffer_RestoresValues()
        {
            var entity = _repo.CreateEntity();
            var comp   = new FixedByteComp();
            comp.Data[0] = 5; comp.Data[1] = 6; comp.Data[2] = 7; comp.Data[3] = 8;
            _repo.SetComponent(entity, comp);

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader(SubsystemType));

            var freshRepo = new EntityRepository();
            freshRepo.RegisterComponent<FixedByteComp>();
            freshRepo.RegisterComponent<FixedLongComp>();
            freshRepo.RegisterComponent<InlineFloatComp>();
            freshRepo.RegisterComponent<MissionPlanQueue>();

            serializer.Deserialize(freshRepo, dom);

            Entity freshEntity = GetSingleEntity(freshRepo);
            var restored = freshRepo.GetComponent<FixedByteComp>(freshEntity);
            Assert.Equal(5, restored.Data[0]);
            Assert.Equal(6, restored.Data[1]);
            Assert.Equal(7, restored.Data[2]);
            Assert.Equal(8, restored.Data[3]);

            freshRepo.Dispose();
        }

        // ── CE-467-A: Entity in InlineArray round-trips through the IGuidResolver ──

        /// <summary>
        /// ⭐ CE-467-A (supersedes S301-SC3's "must throw"): an <c>[InlineArray]</c> of <see cref="Entity"/> is
        /// serialized element-by-element through the <see cref="IGuidResolver"/> — GUID strings in the DOM, entities
        /// back on inject — exactly like a scalar <see cref="Entity"/> field. This is the route the cgf-scn-2 design
        /// talk named for it; invariant 3 (<i>"entity handles never cross scenario boundaries"</i>) holds because no raw
        /// handle is written. Needed by <c>UnitRoster.SubordinateEntities</c> (CE-467).
        /// </summary>
        [Fact]
        public void EntityInInlineArray_RoundTripsThroughTheGuidResolver()
        {
            ComponentTypeRegistry.Clear();
            var repo = new EntityRepository();
            repo.RegisterComponent<EntityInlineComp>(DataPolicy.Default);
            var serializer = new FdpAutoSerializer();
            serializer.Build();   // ⛔ used to throw here
            int typeId = ComponentType<EntityInlineComp>.ID;

            var owner = repo.CreateEntity();
            var a = repo.CreateEntity();
            var b = repo.CreateEntity();
            var comp = new EntityInlineComp();
            comp.Refs[0] = a;
            comp.Refs[1] = b;
            repo.SetComponent(owner, comp);

            var resolver = new MapResolver();
            resolver.Add(a, "guid-a");
            resolver.Add(b, "guid-b");
            var json = serializer.TryExtract(repo, owner, typeId, resolver);

            Assert.NotNull(json);
            var arr = Assert.IsType<JsonArray>(json!["Refs"]);
            Assert.Equal("guid-a", arr[0]!.GetValue<string>());
            Assert.Equal("guid-b", arr[1]!.GetValue<string>());

            var target = new EntityRepository();
            target.RegisterComponent<EntityInlineComp>(DataPolicy.Default);
            var t = target.CreateEntity();
            var a2 = target.CreateEntity();
            var b2 = target.CreateEntity();
            var back = new MapResolver();
            back.Add(a2, "guid-a");
            back.Add(b2, "guid-b");
            serializer.TryInject(target, t, typeId, json, back);

            var restored = target.GetComponent<EntityInlineComp>(t);
            Assert.Equal(a2, restored.Refs[0]);
            Assert.Equal(b2, restored.Refs[1]);

            target.Dispose();
            repo.Dispose();
            ComponentTypeRegistry.Clear();
            // Re-register for subsequent tests.
            _repo.RegisterComponent<FixedByteComp>();
            _repo.RegisterComponent<FixedLongComp>();
            _repo.RegisterComponent<InlineFloatComp>();
            _repo.RegisterComponent<MissionPlanQueue>();
        }

        private sealed class MapResolver : IGuidResolver
        {
            private readonly System.Collections.Generic.Dictionary<Entity, string> _toGuid = new();
            private readonly System.Collections.Generic.Dictionary<string, Entity> _toEntity = new();
            public void Add(Entity e, string guid) { _toGuid[e] = guid; _toEntity[guid] = e; }
            public string Resolve(Entity entity) => _toGuid.TryGetValue(entity, out var g) ? g : "";
            public Entity Resolve(string guidStr) => _toEntity.TryGetValue(guidStr, out var e) ? e : Entity.Null;
        }

        // ── S303-SC1: BrainBlackboard excluded from DOM (DataPolicy.NoScenario) ─────

        /// <summary>
        /// S303-SC1: a component marked <c>[DataPolicy(DataPolicy.NoScenario)]</c> must be absent
        /// from the serialized DOM, and a co-present saveable component must still appear.
        ///
        /// <para>⭐⭐ <b><c>P4</c> (2026-09-22) — RE-HOMED FROM <c>BrainBlackboard</c>, not deleted with
        /// it.</b> ⛔ The retired component was only this test's FIXTURE; the claim under test is the
        /// serializer's <c>DataPolicy</c> exclusion, which is very much alive. ⚠ <c>BehaviorState</c>
        /// carries the same attribute and is the natural stand-in. 🔒 The <c>CE-314</c> lesson applied:
        /// before deleting a test with the thing it names, ask which of its assertions were ABOUT that
        /// thing and which merely USED it.</para>
        /// </summary>
        [Fact]
        public unsafe void NoScenarioComponent_IsExcludedFromDom_WhileSaveableCoPresentOneRemains()
        {
            var entity = _repo.CreateEntity();

            _repo.SetComponent(entity, new BehaviorState { ActiveBehaviorHash = 1234, BrainTier = 2 });

            var fixedComp = new FixedByteComp();
            fixedComp.Data[0] = 0xAB;
            _repo.SetComponent(entity, fixedComp);

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader(SubsystemType));

            var entitiesNode = (JsonObject)dom["Entities"]!;
            var entityNode   = (JsonObject)entitiesNode.First().Value!;

            Assert.False(entityNode.ContainsKey("BehaviorState"),
                "A DataPolicy.NoScenario component must be excluded from the DOM.");
            Assert.True(entityNode.ContainsKey("FixedByteComp"),
                "Saveable co-present component must still appear in the DOM.");
        }

        // ── S302-SC1: InlineArray of float — extract produces JsonArray ───────────

        /// <summary>
        /// S302-SC1: An [InlineArray] float field is serialized as a JSON array of numbers.
        /// </summary>
        [Fact]
        public void Extract_InlineFloatArray_ProducesJsonArray()
        {
            var entity = _repo.CreateEntity();
            var comp   = new InlineFloatComp();
            Span<float> vals = comp.Values;
            vals[0] = 1.1f; vals[1] = 2.2f; vals[2] = 3.3f;
            _repo.SetComponent(entity, comp);

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader(SubsystemType));

            var entityNode = (JsonObject)((JsonObject)dom["Entities"]!).First().Value!;
            var arr        = (JsonArray)entityNode["InlineFloatComp"]!["Values"]!;

            Assert.Equal(3, arr.Count);
            Assert.Equal(1.1f, arr[0]!.GetValue<float>(), precision: 5);
            Assert.Equal(2.2f, arr[1]!.GetValue<float>(), precision: 5);
            Assert.Equal(3.3f, arr[2]!.GetValue<float>(), precision: 5);
        }

        // ── S302-SC2: InlineArray of float — inject restores values ──────────────

        /// <summary>
        /// S302-SC2: Injecting from a JsonArray restores inline-array float values.
        /// </summary>
        [Fact]
        public void Inject_InlineFloatArray_RestoresValues()
        {
            var entity = _repo.CreateEntity();
            var comp   = new InlineFloatComp();
            Span<float> vals = comp.Values;
            vals[0] = 9.9f; vals[1] = 8.8f; vals[2] = 7.7f;
            _repo.SetComponent(entity, comp);

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader(SubsystemType));

            var freshRepo = new EntityRepository();
            freshRepo.RegisterComponent<FixedByteComp>();
            freshRepo.RegisterComponent<FixedLongComp>();
            freshRepo.RegisterComponent<InlineFloatComp>();
            freshRepo.RegisterComponent<MissionPlanQueue>();
            serializer.Deserialize(freshRepo, dom);

            Entity freshEntity = GetSingleEntity(freshRepo);
            var restored = freshRepo.GetComponent<InlineFloatComp>(freshEntity);
            Span<float> restoredVals = restored.Values;
            Assert.Equal(9.9f, restoredVals[0], precision: 5);
            Assert.Equal(8.8f, restoredVals[1], precision: 5);
            Assert.Equal(7.7f, restoredVals[2], precision: 5);

            freshRepo.Dispose();
        }

        // ── S302-SC3: MissionPlanQueue round-trip ─────────────────────────────────

        /// <summary>
        /// S302-SC3: MissionPlanQueue (which has MissionPhaseBuffer [InlineArray(8)])
        /// round-trips phase data correctly.
        /// </summary>
        [Fact]
        public void RoundTrip_MissionPlanQueue_PreservesPhaseData()
        {
            // MissionPlanQueue is registered with DataPolicy.Default in the fixture ctor
            // so FdpAutoSerializer includes it in serialization.
            var serializer = BuildSerializer();

            var entity = _repo.CreateEntity();
            var queue  = new MissionPlanQueue
            {
                CurrentPhase        = 1,
                PhaseCount          = 2,
                PhaseElapsedSeconds = 3.14f,
            };
            Span<MissionPhase> phases = queue.Phases;
            phases[0] = new MissionPhase { BehaviorId = 42, Trigger = MissionTrigger.BehaviorFinished, TriggerParam = 0f };
            phases[1] = new MissionPhase { BehaviorId = 99, Trigger = MissionTrigger.TimerElapsed,      TriggerParam = 5f };
            _repo.SetComponent(entity, queue);

            var dom = serializer.Serialize(_repo, new ScenarioHeader(SubsystemType));

            var freshRepo = new EntityRepository();
            freshRepo.RegisterComponent<FixedByteComp>();
            freshRepo.RegisterComponent<FixedLongComp>();
            freshRepo.RegisterComponent<InlineFloatComp>();
            freshRepo.RegisterComponent<MissionPlanQueue>(DataPolicy.Default);
            serializer.Deserialize(freshRepo, dom);

            Entity freshEntity = GetSingleEntity(freshRepo);
            var restored = freshRepo.GetComponent<MissionPlanQueue>(freshEntity);
            Assert.Equal(1,    restored.CurrentPhase);
            Assert.Equal(2,    restored.PhaseCount);
            Assert.Equal(3.14f, restored.PhaseElapsedSeconds, precision: 5);
            Span<MissionPhase> rPhases = restored.Phases;
            Assert.Equal(42,                           rPhases[0].BehaviorId);
            Assert.Equal(MissionTrigger.BehaviorFinished, rPhases[0].Trigger);
            Assert.Equal(99,                           rPhases[1].BehaviorId);
            Assert.Equal(MissionTrigger.TimerElapsed,  rPhases[1].Trigger);
            Assert.Equal(5f, rPhases[1].TriggerParam,  precision: 5);

            freshRepo.Dispose();
        }

        // ── Utility ──────────────────────────────────────────────────────────────

        private static Entity GetSingleEntity(EntityRepository repo)
        {
            for (int i = 0; i <= repo.MaxEntityIndex; i++)
            {
                var e = new Entity(i, repo.GetMetadata(i).Generation);
                if (repo.IsAlive(e)) return e;
            }
            throw new InvalidOperationException("No alive entity found.");
        }
    }

    // ── Entity-in-InlineArray test component ────────────────────────────────
    // CE-467-A: used to prove an Entity InlineArray round-trips through the IGuidResolver.

    /// <summary>
    /// [InlineArray] of Entity handles — serialized through the <see cref="IGuidResolver"/> (CE-467-A).
    /// </summary>
    [InlineArray(2)]
    [StructLayout(LayoutKind.Sequential)]
    public struct EntityBuffer2
    {
        private Entity _element;
    }

    /// <summary>
    /// Component with an [InlineArray] field of Entity elements (CE-467-A round-trip rail).
    /// Marked NoPreview/NoScenario/NoReplay so AutoRegisterAllComponentTypes does not pick it up in other
    /// tests; the rail registers it with <see cref="DataPolicy.Default"/> explicitly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(228)]
    [DataPolicy(DataPolicy.NoPreview | DataPolicy.NoScenario | DataPolicy.NoReplay)]
    public struct EntityInlineComp
    {
        /// <summary>Inline array of entity refs.</summary>
        public EntityBuffer2 Refs;
    }
}
