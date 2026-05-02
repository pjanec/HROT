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
    [ComponentId(222)]
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
            _repo.RegisterComponent<BrainBlackboard>();
            _repo.RegisterComponent<MissionPlanQueue>();
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
            freshRepo.RegisterComponent<BrainBlackboard>();
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

        // ── S301-SC3: Entity in InlineArray must throw ────────────────────────────

        /// <summary>
        /// S301-SC3 / S302-SC3: Build() must throw InvalidOperationException if an
        /// [InlineArray] field has element type Entity.
        /// </summary>
        [Fact]
        public void Build_ComponentWithEntityInInlineArray_Throws()
        {
            ComponentTypeRegistry.Clear();
            var tempRepo = new EntityRepository();
            tempRepo.RegisterComponent<EntityInlineComp>();
            var autoSerializer = new FdpAutoSerializer();
            Assert.Throws<InvalidOperationException>(() => autoSerializer.Build());
            tempRepo.Dispose();
            ComponentTypeRegistry.Clear();
            // Re-register for subsequent tests.
            _repo.RegisterComponent<FixedByteComp>();
            _repo.RegisterComponent<FixedLongComp>();
            _repo.RegisterComponent<InlineFloatComp>();
            _repo.RegisterComponent<BrainBlackboard>();
            _repo.RegisterComponent<MissionPlanQueue>();
        }

        // ── S303-SC1: BrainBlackboard excluded from DOM (DataPolicy.NoSave) ─────

        /// <summary>
        /// S303-SC1: <see cref="BrainBlackboard"/> is marked <c>[DataPolicy(DataPolicy.NoSave)]</c>
        /// and must therefore be absent from the serialized DOM.
        /// A co-present saveable component must still appear.
        /// </summary>
        [Fact]
        public unsafe void BrainBlackboard_DataPolicyNoSave_ExcludedFromDom()
        {
            var entity = _repo.CreateEntity();

            var bb = new BrainBlackboard();
            for (int i = 0; i < BehaviorConstants.BrainBlackboardByteSize; i++)
                bb.Memory[i] = (byte)(i & 0xFF);
            _repo.SetComponent(entity, bb);

            var fixedComp = new FixedByteComp();
            fixedComp.Data[0] = 0xAB;
            _repo.SetComponent(entity, fixedComp);

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader(SubsystemType));

            var entitiesNode = (JsonObject)dom["Entities"]!;
            var entityNode   = (JsonObject)entitiesNode.First().Value!;

            Assert.False(entityNode.ContainsKey("BrainBlackboard"),
                "BrainBlackboard must be excluded from the DOM (DataPolicy.NoSave).");
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
            freshRepo.RegisterComponent<BrainBlackboard>();
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

            var serializer = BuildSerializer();
            var dom        = serializer.Serialize(_repo, new ScenarioHeader(SubsystemType));

            var freshRepo = new EntityRepository();
            freshRepo.RegisterComponent<FixedByteComp>();
            freshRepo.RegisterComponent<FixedLongComp>();
            freshRepo.RegisterComponent<InlineFloatComp>();
            freshRepo.RegisterComponent<BrainBlackboard>();
            freshRepo.RegisterComponent<MissionPlanQueue>();
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
                var e = new Entity(i, repo.GetHeader(i).Generation);
                if (repo.IsAlive(e)) return e;
            }
            throw new InvalidOperationException("No alive entity found.");
        }
    }

    // ── Entity-in-InlineArray test component ────────────────────────────────
    // Used to verify Build() throws when Entity appears as an InlineArray element.

    /// <summary>
    /// [InlineArray] of Entity handles.
    /// <see cref="FdpAutoSerializer.Build"/> must throw for components using this type.
    /// </summary>
    [InlineArray(2)]
    [StructLayout(LayoutKind.Sequential)]
    public struct EntityBuffer2
    {
        private Entity _element;
    }

    /// <summary>
    /// Component with an [InlineArray] field of Entity elements.
    /// <see cref="FdpAutoSerializer.Build"/> must throw for this type.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(228)]
    public struct EntityInlineComp
    {
        /// <summary>Inline array of entity refs — intentionally invalid for serialization.</summary>
        public EntityBuffer2 Refs;
    }
}
