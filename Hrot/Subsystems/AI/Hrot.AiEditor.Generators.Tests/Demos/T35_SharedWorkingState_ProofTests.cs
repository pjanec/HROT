using System;
using System.Runtime.CompilerServices;
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
/// Slice 1 runtime proof (Blueprint_SharedState_GetShared_Design.md §4): two composed AiPrimitive
/// ACTION nodes whose <c>WorkingStateTargetField</c> both bind to the SAME Behavior-scoped
/// blackboard variable ("bpSharedWorkingState") resolve — via <see cref="BTreeBridgeEmitCore.ResolveStatefulSlotKey"/>
/// — to the SAME FNV-1a partition-slot key, so they share ONE <see cref="DemoAiPrimitiveNodes.WorkingState"/>
/// instance. A third Node-scoped control node (no WorkingStateTargetField authored) stays isolated on
/// its own private slot, proving Slice 1 is additive: existing Node-scoped semantics are unaffected.
///
/// <para>
/// The host tree <c>T35_SharedWorkingState</c> (committed .btree.json) is a <c>Parallel(RequireAll)</c>
/// of three composed AiPrimitive actions (SharedA, SharedB, Control), each bound to
/// <see cref="DemoAiPrimitiveNodes.TickCore"/> with <c>RunsNeeded = 100</c> so none reaches Success
/// within the test's tick count — Parallel(RequireAll) therefore re-executes ALL THREE children every
/// tick (none is ever marked "finished"). Each tick: SharedA increments the shared slot, then SharedB
/// increments the SAME shared slot (reading SharedA's already-incremented value), then Control
/// increments its own independent slot. After N ticks the shared slot's Ticks == 2N (both nodes
/// contributed) while the control slot's Ticks == N (only itself).
/// </para>
/// </summary>
public sealed class T35_SharedWorkingState_ProofTests : IDisposable
{
    private static readonly Guid AssetId = Guid.Parse("bb000035-0000-0000-0000-000000000000");
    private static readonly Guid ControlNodeVisualId = Guid.Parse("bb350000-0000-0000-0000-000000000005");

    // Slot key baked at code-gen time — see T35_SharedWorkingState.Registrar.g.cs.
    // Shared slot: FNV-1a-32(assetId ++ "bpSharedWorkingState") -- Behavior scope, name-keyed, so
    // BOTH SharedA and SharedB bake this SAME constant despite having distinct VisualIds/Params vars.
    private static readonly int SharedSlotKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(
        AssetId, Hrot.AiEditor.Persistence.WorkingStateScope.Behavior, Guid.Empty, "bpSharedWorkingState");

    // Control slot: FNV-1a-32(assetId ++ nodeVisualId) -- Node scope (default, no WorkingStateTargetField
    // authored), independent of the shared slot above.
    private static readonly int ControlSlotKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(
        AssetId, ControlNodeVisualId);

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

    [Fact]
    public void SharedSlotKey_DiffersFrom_ControlSlotKey()
    {
        // Sanity: the two constants baked into the generated registrar must NOT collide, or the
        // "isolation" half of the proof below would be vacuous.
        SharedSlotKey.Should().NotBe(ControlSlotKey,
            "the Behavior-scoped shared slot and the Node-scoped control slot must be distinct");
    }

    [Fact]
    public unsafe void TwoComposedActions_BoundToSameBehaviorScopedVariable_ShareOnePartitionSlot()
    {
        // Register from the real (compile-time) AI behaviors assembly — same attribute-driven scan
        // the game uses. T35's generated registrar builds its interpreter over the injected
        // ActionRegistry (into which both blueprint TickCore thunks are registered — one per node).
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _blueprint.CommitStaging(staging);

        _registry.TryGetId("T35_SharedWorkingState", out int id)
            .Should().BeTrue("T35 must self-register via its generated [BlueprintRegistrar]");
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        var interpreter = def.BTreeInterpreter!;
        interpreter.Should().NotBeNull("T35 is a BTree behavior");

        var world  = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        // Assign -> BehaviorIngressSystem parses params (RunsNeeded=100 x3) and provisions BOTH
        // partition slots (the shared one dedup'd to a single manifest entry, plus the control's own).
        var ingress = new BehaviorIngressSystem(_registry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = "T35_SharedWorkingState",
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        (world.HasComponent<BlueprintBlackboard1024>(entity)
         || world.HasComponent<BlueprintBlackboard4096>(entity)
         || world.HasComponent<BlueprintBlackboard16384>(entity))
            .Should().BeTrue("BehaviorIngressSystem must provision a partition tier for the stateful slots");

        var ctx = new BTreeContext { Self = entity, World = world };
        NodeStatus Tick()
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            var state  = new BehaviorTreeState();
            return interpreter.Tick(ref bb, ref state, ref ctx);
        }

        // RunsNeeded=100 on all three composed nodes -> every node stays Running for the whole test,
        // so Parallel(RequireAll) re-executes ALL THREE every tick (none ever marked "finished").
        for (int tick = 1; tick <= 3; tick++)
        {
            Tick().Should().Be(NodeStatus.Running,
                $"tick {tick} of 3 — RunsNeeded=100 keeps every composed node Running");

            ReadSlotTicks(world, entity, SharedSlotKey).Should().Be(2 * tick,
                $"tick {tick}: SharedA and SharedB both increment the SAME shared slot each tick " +
                "(2 increments/tick) -- proves they resolve to ONE partition slot, not two independent ones");

            ReadSlotTicks(world, entity, ControlSlotKey).Should().Be(tick,
                $"tick {tick}: the Node-scoped control increments only its OWN slot (1 increment/tick) — " +
                "stays isolated from the shared slot even though it runs in the same Parallel");
        }
    }

    private static unsafe int ReadSlotTicks(EntityRepository world, Entity entity, int slotKey)
    {
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off).Should().BeTrue();
                return Unsafe.AsRef<DemoAiPrimitiveNodes.WorkingState>(mem + off).Ticks;
            }
        }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off).Should().BeTrue();
                return Unsafe.AsRef<DemoAiPrimitiveNodes.WorkingState>(mem + off).Ticks;
            }
        }
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int off).Should().BeTrue();
                return Unsafe.AsRef<DemoAiPrimitiveNodes.WorkingState>(mem + off).Ticks;
            }
        }
        throw new InvalidOperationException("entity has no BlueprintBlackboard* tier — slot cannot be read");
    }
}
