using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Core.Collections;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Spatial.Eqs;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Hrot.AiEditor.Persistence.Emit;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// End-to-end proof of the Hill-attack tree-INTEGRATION chain (architect Q#9-A) for the composed
/// commander tree <c>Assets/BTrees/Authoring/PlatoonHillAttack2.btree.json</c>: it mirrors
/// <c>Assets/BTrees/PlatoonHillAttack.btree.json</c>'s STRUCTURE (Sequence(CalculateSegments,
/// DispatchAllToBaseline, AreAllAtBaseline, Repeater(Sequence(RequestAreaQuery,
/// IsAreaQueryResolved, DispatchWaveWithTargets)))) but every node except AreAllAtBaseline is bound
/// to an INTEGRATED <c>HillAssault2I_*</c> blueprint (empty native <c>WorkingState</c>, converses
/// over the single standalone Entity-scoped <see cref="HillAttackSharedState"/> slot via
/// <c>GetShared</c>/<c>SetShared</c>) composed the same way <c>HillAssault2I_Smoke.btree.json</c>
/// composes <c>HillAssault2I_CalculateSegments</c> -- <c>DelegateShape: AiPrimitiveTickCore</c>,
/// <see cref="BehaviorIngressSystem"/> provisions the "state" slot from the generated
/// <c>StatefulWorkingSlots</c> manifest on <see cref="AssignBehaviorEvent"/> (NOT a direct
/// <see cref="BlueprintBlackboardPartitions.TryAttach"/> call).
///
/// <para>
/// <b>IsWaveCompleted is intentionally OMITTED</b> from the wave loop (see the PR report): its
/// oracle-mirroring <c>WaveMonitorOps.Update(WaveState, view)</c> kernel takes a curated
/// <c>WaveState</c> bundle (<c>Runners</c>+<c>BurnedSlotsMask</c>+<c>BaselineReservedMask</c>) that
/// has no clean in-graph construction path from <see cref="HillAttackSharedState"/> (no
/// struct-constructor node kind exists in the blueprint vocabulary, and adding a curated
/// <c>WaveMonitorOps</c>/<c>HillAttackSharedStateOps</c> overload is outside this task's authority
/// -- only the compiler/generator and existing Ops/Json/struct files are off-limits, but a NEW
/// overload is still new *Ops surface the task reserves for the orchestrator). The wave loop here
/// is <c>Sequence(RequestAreaQuery, IsAreaQueryResolved, DispatchWaveWithTargets)</c> only.
/// </para>
///
/// <para>
/// <b>Cross-node shared-state flow asserted:</b> after the setup <c>Sequence</c>'s first two
/// children have ticked (both unconditionally <c>Return(Success)</c>), the shared
/// <see cref="HillAttackSharedState"/> struct must show BOTH <c>TotalSlots</c> (written by
/// <c>CalculateSegments</c>) AND <c>BaselineReservedMask</c> (written by
/// <c>DispatchAllToBaseline</c>) reflecting their writes -- proving node B
/// (<c>DispatchAllToBaseline</c>) sees node A's (<c>CalculateSegments</c>) write through the SAME
/// shared slot, and that node B's own fresh-read-before-write (its <c>SetVariable</c> occurrences
/// were mechanically rewritten to <c>GetShared -&gt; With&lt;Field&gt; -&gt; SetShared</c> triples,
/// per node/per occurrence) did NOT clobber node A's OTHER field writes (BurnedSlotsMask/
/// WaveUsedSlotsMask/CachedEqsRequestId/CachedTargetGroupHandle/EqsRequestTime/CurrentWave all
/// still carry CalculateSegments' seeded defaults after DispatchAllToBaseline ran).
/// </para>
/// </summary>
public sealed class PlatoonHillAttack2_Integration_ProofTests : IDisposable
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
        world.RegisterComponent<UnitRoster>();
        world.RegisterComponent<NavigationStatus>();
        world.SetSingleton(new AreaQueryBatchData
        {
            Results = new NativeArray<AreaQueryResult>(AreaQueryBatchData.DefaultCapacity, Allocator.Persistent),
        });
        return world;
    }

    private static void DisposeWorld(EntityRepository world)
    {
        if (world.HasSingleton<AreaQueryBatchData>())
        {
            ref var batch = ref world.GetSingleton<AreaQueryBatchData>();
            if (batch.Results.IsCreated) batch.Results.Dispose();
        }
        world.Dispose();
    }

    /// <summary>FNV-1a-32("state") -- Entity scope excludes assetId/nodeVisualId, mirroring
    /// HillAssault2I_Integration_Smoke_ProofTests.StateSlotKey exactly.</summary>
    private static int StateSlotKey => BTreeBridgeEmitCore.ComputeStatefulSlotKey(
        Guid.Empty, Hrot.AiEditor.Persistence.WorkingStateScope.Entity, Guid.Empty, "state");

    private BehaviorDefinition RegisterAndGetDefinition()
    {
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _blueprint.CommitStaging(staging);

        _registry.TryGetId("PlatoonHillAttack2", out int id)
            .Should().BeTrue("PlatoonHillAttack2 must self-register via its generated [BlueprintRegistrar]");
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        return def;
    }

    private static Entity AddSubordinate(EntityRepository world, ref UnitRoster roster)
    {
        var sub = world.CreateEntity();
        // InProgress (the zero default) != Arrived, so AreAllAtBaseline correctly reports "not
        // arrived" instead of throwing on a missing NavigationStatus component.
        world.AddComponent(sub, new NavigationStatus { Result = NavigationResult.InProgress });
        UnitRoster.Add(ref roster, (long)sub.PackedValue);
        return sub;
    }

    [Fact]
    public void StandaloneStateVariable_EmitsManifestEntry_MatchingBlueprintSharedStateExpectedHash()
    {
        var def = RegisterAndGetDefinition();

        def.StatefulWorkingSlots.Should().NotBeNull(
            "the composed integrated nodes' own (empty) WorkingState slots AND the standalone " +
            "'state' variable must trigger StatefulWorkingSlots emission");

        var stateEntry = def.StatefulWorkingSlots!.Should()
            .ContainSingle(s => s.SlotKey == StateSlotKey,
                "the standalone-variable pass in EmitStatefulWorkingSlotsArray must emit exactly " +
                "one entry for 'state', shared by every integrated node")
            .Subject;

        stateEntry.PayloadSize.Should().Be(Marshal.SizeOf<HillAttackSharedState>());
        stateEntry.WorkingStateType.Should().Be(typeof(HillAttackSharedState));
        stateEntry.Role.Should().Be(1, "BlackboardVariableRole.State == 1");
        stateEntry.Scope.Should().Be(2, "WorkingStateScope.Entity == 2");
    }

    [Fact]
    public void ComposedTree_TicksThroughSetupSequence_SharedState_ShowsCrossNodeWriteFlow()
    {
        var def = RegisterAndGetDefinition();
        var interpreter = def.BTreeInterpreter!;
        interpreter.Should().NotBeNull("PlatoonHillAttack2 is a BTree behavior");

        var world     = CreateWorld();
        var commander = world.CreateEntity();
        world.AddComponent(commander, new BehaviorState());
        world.AddComponent(commander, new BrainBlackboard());
        world.AddComponent(commander, new BrainBTreeState());

        var roster = new UnitRoster();
        var sub1 = AddSubordinate(world, ref roster);
        var sub2 = AddSubordinate(world, ref roster);
        var sub3 = AddSubordinate(world, ref roster);
        world.AddComponent(commander, roster);

        // Assign -> BehaviorIngressSystem provisions BOTH each composed node's own (Node-scoped,
        // empty) WorkingState slot AND the standalone Entity-scoped "state" slot from the manifest,
        // and runs def.ParseParams (bakes the DefaultValueJson scenario for every bpParams* var).
        var ingress = new BehaviorIngressSystem(_registry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = commander,
            BehaviorName = "PlatoonHillAttack2",
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        (world.HasComponent<BlueprintBlackboard1024>(commander)
         || world.HasComponent<BlueprintBlackboard4096>(commander)
         || world.HasComponent<BlueprintBlackboard16384>(commander))
            .Should().BeTrue("BehaviorIngressSystem must provision a partition tier for the manifest's slots");

        StateSlotIsAttached(world, commander).Should().BeTrue(
            "ProvisionStatefulSlots must have attached the 'state' Entity-scoped slot FROM THE " +
            "MANIFEST before the first tick");

        var ctx = new BTreeContext { Self = commander, World = world };
        NodeStatus Tick()
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(commander);
            var state  = new BehaviorTreeState();
            return interpreter.Tick(ref bb, ref state, ref ctx);
        }

        // One Root Tick cascades the setup Sequence: CalculateSegments (unconditional Success) ->
        // DispatchAllToBaseline (unconditional Success) -> AreAllAtBaseline (Failure -- none of the
        // 3 fresh subordinates have NavigationResult.Arrived yet), so the Sequence -- and therefore
        // the Root -- reports Failure this tick. That is expected and does not affect the shared-
        // state assertions below: both setup nodes have already run by the time AreAllAtBaseline
        // stops the Sequence.
        Tick();

        var shared = ReadSharedState(world, commander);

        // Written by CalculateSegments: distance(0,0 -> 100,0) = 100, spacing = 10 -> 10 slots.
        shared.TotalSlots.Should().Be(10,
            "CalculateSegments' WithTotalSlots(SegmentMath.TotalSlots(...)) must have written into " +
            "the shared struct");

        // Written by DispatchAllToBaseline: 3 alive subordinates at FlowForEach indices 0,1,2 ->
        // MaskOps.WithBitSet folds bits 0,1,2 -> mask 0b111 = 7. Node B (DispatchAllToBaseline) is
        // reading/writing the SAME shared slot node A (CalculateSegments) just wrote TotalSlots
        // into -- this is the cross-node shared-state flow this proof exists to demonstrate.
        shared.BaselineReservedMask.Should().Be(0b111,
            "DispatchAllToBaseline must have folded all 3 alive subordinates' FlowForEach indices " +
            "into BaselineReservedMask via the shared 'state' slot -- proving DispatchAllToBaseline " +
            "observed the SAME struct CalculateSegments seeded (node B sees node A's write)");

        // Every OTHER field must still carry CalculateSegments' seeded defaults, proving
        // DispatchAllToBaseline's mechanically-generated GetShared->WithBaselineReservedMask->
        // SetShared triple read a FRESH full struct before writing back (so it did not clobber its
        // sibling fields with a stale/zeroed snapshot).
        shared.BurnedSlotsMask.Should().Be((ushort)0);
        shared.WaveUsedSlotsMask.Should().Be((ushort)0);
        shared.CachedEqsRequestId.Should().Be(-1L);
        shared.CachedTargetGroupHandle.Should().Be(-1);
        shared.EqsRequestTime.Should().Be(0f);
        shared.CurrentWave.Should().Be((byte)0);

        // Behavioral corroboration: DispatchAllToBaseline must have published a MoveToLocation
        // intent for each of the 3 subordinates (same event-publish path as the isolated twin).
        world.Bus.SwapBuffers();
        var moveEvents = world.Bus.ReadManaged<AssignTacticalIntentEvent>();
        moveEvents.Count.Should().Be(3, "DispatchAllToBaseline must publish one MoveToLocation intent per alive subordinate");
        foreach (var e in moveEvents)
            e.IntentId.Should().Be("MoveToLocation");

        DisposeWorld(world);
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
}
