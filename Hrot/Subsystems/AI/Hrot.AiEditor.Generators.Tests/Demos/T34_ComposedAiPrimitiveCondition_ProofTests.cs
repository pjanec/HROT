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
/// E2 runtime proof: a blueprint-authored AiPrimitive CONDITION composed as a host-BTree node
/// (<c>DelegateShape = AiPrimitiveTickCore</c>) executes through a <b>real FastBTree interpreter</b>
/// against a <b>partition slot</b>, dispatching to the blueprint's <c>TickCore</c> and comparing the
/// result against <see cref="NodeStatus.Success"/> — the condition-side mirror of
/// <see cref="T31_ComposedAiPrimitive_ProofTests"/>.
///
/// <para>
/// The host tree <c>T34_ComposedAiPrimitiveCondition</c> (committed .btree.json) binds one Condition
/// node to the SAME stand-in <see cref="DemoAiPrimitiveNodes.TickCore"/> used by T31 — reused here as
/// a condition, since a TickCore already returning <see cref="NodeStatus"/> composes into either host
/// shape (only the registration + comparison differ). Its <c>WorkingState.Ticks</c> lives in a
/// <c>BlueprintBlackboard*</c> partition slot — the SAME rail an action uses, because edge-detection
/// conditions need cross-tick memory too. With <c>RunsNeeded = 3</c> the condition reports Failure on
/// ticks 1-2 (TickCore returns Running, which is not Success) and Success from tick 3 onward, while
/// the slot's <c>Ticks</c> keeps incrementing underneath — proving the composed blueprint condition
/// runs against the partition slot (not re-zeroed each tick, and not a transient/zeroed state).
/// </para>
/// </summary>
public sealed class T34_ComposedAiPrimitiveCondition_ProofTests : IDisposable
{
    // Slot key baked at code-gen time from FNV-1a-32(assetId, nodeVisualId) — same algorithm the
    // bridge emitter uses (BTreeBridgeEmitCore.ComputeStatefulSlotKey), computed here rather than
    // hardcoded so the test stays correct if the asset's GUIDs ever change.
    private static readonly int SlotKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(
        Guid.Parse("bb000034-0000-0000-0000-000000000000"),
        Guid.Parse("bb340000-0000-0000-0000-000000000002"));

    private readonly BehaviorRegistry  _registry  = new();
    private readonly BlueprintRegistry _blueprint = new();

    public void Dispose() => _registry.Clear();

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        // ⭐ B4: register from the LADDER, not a hand-list. ⛔ This was three explicit
        //   RegisterComponent calls and it did NOT know about the 256 tier — 11 tests
        //   failed with "Component BlueprintBlackboard256 is not registered" the moment
        //   O3b added one. Production never had the bug: it registers from the table.
        BlueprintTierTable.RegisterAll(world);
        return world;
    }

    [Fact]
    public unsafe void ComposedBlueprintCondition_RunsTickCore_OverPartitionSlot()
    {
        // Register from the real (compile-time) AI behaviors assembly — the same attribute-driven
        // scan the game uses. T33's generated registrar builds its interpreter over the injected
        // ActionRegistry (into which its blueprint TickCore condition thunk is registered via
        // RegisterCondition).
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _blueprint.CommitStaging(staging);

        _registry.TryGetId("T34_ComposedAiPrimitiveCondition", out int id)
            .Should().BeTrue("T33 must self-register via its generated [BlueprintRegistrar]");
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        var interpreter = def.BTreeInterpreter!;
        interpreter.Should().NotBeNull("T33 is a BTree behavior");

        var world  = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        RootStateAccess.EnsureRootState(world, entity);   // ⛔ O7c-②: BrainBTreeState retired — the root cursor is an occurrence slot (§31).

        // Assign → BehaviorIngressSystem parses params (RunsNeeded=3) and provisions the slot.
        var ingress = new BehaviorIngressSystem(_registry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = "T34_ComposedAiPrimitiveCondition",
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        (OccurrenceStoreAccess.HasStore(world, entity))
            .Should().BeTrue("BehaviorIngressSystem must provision a partition tier for the blueprint slot");

        // Tick the real interpreter. RunsNeeded=3 → TickCore returns Running, Running, Success;
        // the condition wraps that as Failure, Failure, Success (only exact Success counts as true).
        var ctx = new BTreeContext { Self = entity, World = world };
        NodeStatus Tick()
        {
            ref byte bb = ref global::Fdp.Toolkit.Behavior.RootParamsAccess.RootRef(world, entity);   // P4-②: the ROOT PARAMS SLOT base, exactly as BTreeTickSystem hands it to the interpreter
            var state  = new BehaviorTreeState();
            return interpreter.Tick(ref bb, ref state, ref ctx);
        }

        Tick().Should().Be(NodeStatus.Failure, "tick 1 of 3 — TickCore ran against the slot (Ticks=1), Running != Success");
        Tick().Should().Be(NodeStatus.Failure, "tick 2 of 3 — slot persisted (Ticks=2), still Running != Success");
        Tick().Should().Be(NodeStatus.Success, "tick 3 of 3 — Ticks reached RunsNeeded=3, TickCore returns Success");

        ReadSlotTicks(world, entity).Should().Be(3,
            "WorkingState.Ticks lives in the partition slot and persisted across all three ticks, " +
            "even on the ticks where the condition itself reported Failure");

        world.Dispose();
    }

    private static unsafe int ReadSlotTicks(EntityRepository world, Entity entity)
    {
        // ⭐ B4: was THREE arms over the tier trio and knew nothing about the 256 tier.
        //   OccurrenceStoreAccess is the seam production uses for exactly this.
        byte* mem = OccurrenceStoreAccess.TryGetStore(world, entity, out _);
        if (mem != null)
        {
            BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKey, out int off).Should().BeTrue();
            return Unsafe.AsRef<DemoAiPrimitiveNodes.WorkingState>(mem + off).Ticks;
        }
        throw new InvalidOperationException("entity has no BlueprintBlackboard* tier — slot cannot be read");
    }
}
