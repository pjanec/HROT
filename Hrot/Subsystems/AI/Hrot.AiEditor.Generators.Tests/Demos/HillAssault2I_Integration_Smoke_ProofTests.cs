using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// End-to-end proof of the Hill-attack tree-INTEGRATION chain (architect Q#9-A) for a REAL Hill-attack
/// node: the committed blueprint <c>Assets/Blueprints/HillAssault2I_CalculateSegments.bp.json</c>
/// (AiPrimitive, EMPTY <c>WorkingState</c>) reads/writes the shared
/// <see cref="Hrot.AI.Behaviors.Brains.HillAttackSharedState"/> struct purely via its
/// <c>GetShared</c>/<c>SetShared</c> graph nodes (<c>VariableId="state"</c>), and is composed into
/// the host tree <c>Assets/BTrees/Authoring/HillAssault2I_Smoke.btree.json</c> as a single Action node
/// via <c>DelegateShape: AiPrimitiveTickCore</c> -- mirroring <c>T32_ComposedGeneratedBlueprint</c>'s
/// composed-generated-blueprint pattern and <c>T37_SharedStateManifestProvisioning</c>'s standalone
/// Entity-scoped shared-variable manifest-provisioning pattern (NOT a direct
/// <see cref="BlueprintBlackboardPartitions.TryAttach"/> call -- <see cref="BehaviorIngressSystem"/>
/// provisions the "state" slot from the generated <c>StatefulWorkingSlots</c> manifest on
/// <see cref="AssignBehaviorEvent"/>).
///
/// <para>
/// The composed node's own Params carry StartX/StartY/EndX/EndY/TankSpacing, baked via the
/// <c>bpParams</c> blackboard variable's <c>DefaultValueJson</c> (mirrors
/// <c>T33_ComposedParamBlueprint</c>'s <c>ComposedAiPrimitiveNode_WithAuthoredDefault</c> pattern --
/// <c>BehaviorDefinition.ParseParams</c> deserializes the BAKED default into the Params struct on
/// every <see cref="AssignBehaviorEvent"/>, independent of <c>JsonParams</c>).
/// </para>
/// </summary>
public sealed class HillAssault2I_Integration_Smoke_ProofTests : IDisposable
{
    private readonly BehaviorRegistry  _registry  = new();
    private readonly BlueprintRegistry _blueprint = new();

    public void Dispose() => _registry.Clear();

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        world.RegisterComponent<BrainBlackboard>();
        world.RegisterComponent<BrainBTreeState>();
        world.RegisterComponent<BlueprintBlackboard1024>();
        world.RegisterComponent<BlueprintBlackboard4096>();
        world.RegisterComponent<BlueprintBlackboard16384>();
        return world;
    }

    /// <summary>
    /// FNV-1a-32("state") -- Entity scope excludes assetId/nodeVisualId per
    /// <see cref="BTreeBridgeEmitCore.ComputeStatefulSlotKey(Guid, Hrot.AiEditor.Persistence.WorkingStateScope, Guid, string)"/>,
    /// so this is the SAME key the emitted manifest entry carries and the SAME key
    /// <see cref="BlueprintSharedState"/> computes at read/write time.
    /// </summary>
    private static int StateSlotKey => BTreeBridgeEmitCore.ComputeStatefulSlotKey(
        Guid.Empty, Hrot.AiEditor.Persistence.WorkingStateScope.Entity, Guid.Empty, "state");

    private static uint ExpectedHash<T>() where T : unmanaged
        => unchecked(StatefulBTreeActionBinder.ComputeTypeNameHash(typeof(T).FullName ?? string.Empty)
                      ^ (uint)Marshal.SizeOf<T>());

    private BehaviorDefinition RegisterAndGetDefinition()
    {
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _blueprint.CommitStaging(staging);

        _registry.TryGetId("HillAssault2I_Smoke", out int id)
            .Should().BeTrue("HillAssault2I_Smoke must self-register via its generated [BlueprintRegistrar]");
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        return def;
    }

    [Fact]
    public void StandaloneStateVariable_EmitsManifestEntry_MatchingBlueprintSharedStateExpectedHash()
    {
        var def = RegisterAndGetDefinition();

        def.StatefulWorkingSlots.Should().NotBeNull(
            "the composed node's own (empty) WorkingState slot AND the standalone 'state' variable " +
            "must both trigger StatefulWorkingSlots emission");

        var stateEntry = def.StatefulWorkingSlots!.Should()
            .ContainSingle(s => s.SlotKey == StateSlotKey,
                "the standalone-variable pass in EmitStatefulWorkingSlotsArray must emit exactly one " +
                "entry for 'state' even though no node binds WorkingStateTargetField to it")
            .Subject;

        stateEntry.PayloadSize.Should().Be(Marshal.SizeOf<HillAttackSharedState>());
        stateEntry.StructureHash.Should().Be(ExpectedHash<HillAttackSharedState>(),
            "the manifest's StructureHash formula (ComputeTypeNameHash(typeId) ^ Marshal.SizeOf<T>()) must " +
            "match BlueprintSharedState.TryGetShared/TrySetShared's expected-hash guard bit-for-bit, or " +
            "GetShared/SetShared would treat the manifest-provisioned slot as drifted and always return false");
        stateEntry.WorkingStateType.Should().Be(typeof(HillAttackSharedState));
        stateEntry.NodeLabel.Should().Be("state");
        stateEntry.Role.Should().Be(1, "BlackboardVariableRole.State == 1");
        stateEntry.Scope.Should().Be(2, "WorkingStateScope.Entity == 2");
    }

    [Fact]
    public void StateSlot_ProvisionedFromManifest_ThenTicked_WritesHillAttackSharedState_ThroughRealInterpreter()
    {
        var def = RegisterAndGetDefinition();
        var interpreter = def.BTreeInterpreter!;
        interpreter.Should().NotBeNull("HillAssault2I_Smoke is a BTree behavior");

        var world  = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        // Assign -> BehaviorIngressSystem reads def.StatefulWorkingSlots and provisions BOTH the
        // composed node's own (Node-scoped, empty) slot AND the standalone Entity-scoped "state"
        // slot -- no direct BlueprintBlackboardPartitions.TryAttach call anywhere in this test. Also
        // runs def.ParseParams, which bakes StartX=0,StartY=0,EndX=100,EndY=0,TankSpacing=10 into
        // the composed node's Params (see HillAssault2I_Smoke.btree.json's bpParams DefaultValueJson).
        var ingress = new BehaviorIngressSystem(_registry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = "HillAssault2I_Smoke",
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        (world.HasComponent<BlueprintBlackboard1024>(entity)
         || world.HasComponent<BlueprintBlackboard4096>(entity)
         || world.HasComponent<BlueprintBlackboard16384>(entity))
            .Should().BeTrue("BehaviorIngressSystem must provision a partition tier for the manifest's slots");

        StateSlotIsAttached(world, entity).Should().BeTrue(
            "ProvisionStatefulSlots must have attached the 'state' Entity-scoped slot FROM THE MANIFEST " +
            "before the first tick -- proving the standalone variable's manifest entry is what provisions it");

        var ctx = new BTreeContext { Self = entity, World = world };
        NodeStatus Tick()
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            var state  = new BehaviorTreeState();
            return interpreter.Tick(ref bb, ref state, ref ctx);
        }

        // Root -> single composed Action node whose blueprint graph is
        // EventEntry -> GetShared("state") -> (chain of pure HillAttackSharedStateOps.With* calls) ->
        // SetShared("state") -> Return(Success) -- unconditionally Success on tick 1.
        Tick().Should().Be(NodeStatus.Success,
            "the composed HillAssault2I_CalculateSegments node's Return(Success) is unconditional");

        var shared = ReadSharedState(world, entity);

        // distance(start,end) = 100, spacing = 10 -> totalSlots = max(1, 100/10) = 10, within [1,16]
        // (SAME SegmentMath.TotalSlots math as the isolated twin HillAssault2_CalculateSegments).
        shared.TotalSlots.Should().Be(10,
            "WithTotalSlots must have written SegmentMath.TotalSlots(0,0,100,0,10) into the shared struct");
        shared.BurnedSlotsMask.Should().Be((ushort)0);
        shared.WaveUsedSlotsMask.Should().Be((ushort)0);
        shared.BaselineReservedMask.Should().Be((ushort)0);
        shared.CachedEqsRequestId.Should().Be(-1L);
        shared.CachedTargetGroupHandle.Should().Be(-1);
        shared.EqsRequestTime.Should().Be(0f);
        shared.CurrentWave.Should().Be((byte)0);

        world.Dispose();
    }

    private static unsafe bool StateSlotIsAttached(EntityRepository world, Entity entity)
    {
        int slotKey = StateSlotKey;
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = t.Memory)
                return BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out _);
        }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = t.Memory)
                return BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out _);
        }
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = t.Memory)
                return BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out _);
        }
        return false;
    }

    private static unsafe HillAttackSharedState ReadSharedState(EntityRepository world, Entity entity)
    {
        int slotKey = StateSlotKey;
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off).Should().BeTrue();
                return Unsafe.AsRef<HillAttackSharedState>(mem + off);
            }
        }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off).Should().BeTrue();
                return Unsafe.AsRef<HillAttackSharedState>(mem + off);
            }
        }
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off).Should().BeTrue();
                return Unsafe.AsRef<HillAttackSharedState>(mem + off);
            }
        }
        throw new InvalidOperationException("entity has no BlueprintBlackboard* tier -- slot cannot be read");
    }

    // ── Source-inspection evidence (mirrors HillAssault2_CalculateSegments_ProofTests) ─────────────

    /// <summary>Returns the generated .g.cs source text for the compiled integrated blueprint.</summary>
    private static string FindGeneratedSourceText()
    {
        var generatedDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Hrot.AI.Behaviors",
            "obj", "GeneratedFiles", "Hrot.Blueprints.Generators",
            "Hrot.Blueprints.Generators.BlueprintIncrementalGenerator");

        var file = Directory.Exists(generatedDir)
            ? Directory.GetFiles(generatedDir, "HillAssault2ICalculateSegments_*_Bp.g.cs").FirstOrDefault()
            : null;

        file.Should().NotBeNull(
            $"the generated .g.cs for HillAssault2I_CalculateSegments must exist under {generatedDir}");
        return File.ReadAllText(file!);
    }

    [Fact]
    public void GeneratedTickCore_SourceContainsGetSharedSetSharedAndWithTotalSlotsCall()
    {
        var source = FindGeneratedSourceText();

        source.Should().Contain("BlueprintSharedState.TryGetShared<global::Hrot.AI.Behaviors.Brains.HillAttackSharedState>(world, self, \"state\"",
            "the graph's GetShared(\"state\") node must compile to a BlueprintSharedState.TryGetShared call -- see generated TickCore below:\n" + source);
        source.Should().Contain("BlueprintSharedState.TrySetShared<global::Hrot.AI.Behaviors.Brains.HillAttackSharedState>(world, self, \"state\"",
            "the graph's SetShared(\"state\") node must compile to a BlueprintSharedState.TrySetShared call -- see generated TickCore below:\n" + source);
        source.Should().Contain("HillAttackSharedStateOps.WithTotalSlots(",
            "the TotalSlots field must be written via the curated HillAttackSharedStateOps.WithTotalSlots accessor -- see generated TickCore below:\n" + source);
        source.Should().Contain("SegmentMath.TotalSlots(",
            "TotalSlots' value must still be computed via the curated SegmentMath.TotalSlots helper, exactly like the isolated twin -- see generated TickCore below:\n" + source);
        source.Should().Contain("public struct WorkingState\n    {\n    }",
            "the integrated twin's native WorkingState must be EMPTY (architect Q#9-A) -- see generated TickCore below:\n" + source);
    }
}
