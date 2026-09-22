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
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// I2/I3 runtime proof: a blueprint-authored AiPrimitive action composed as a host-BTree node
/// (<c>DelegateShape = AiPrimitiveTickCore</c>) executes through a <b>real FastBTree interpreter</b>
/// against a <b>partition slot</b>, dispatching to the blueprint's <c>TickCore</c>.
///
/// <para>
/// The host tree <c>T31_ComposedAiPrimitive</c> (committed .btree.json) binds one node to
/// <see cref="DemoAiPrimitiveNodes.TickCore"/> — the compile-time stand-in for a blueprint's
/// generated output. Its <c>WorkingState.Ticks</c> lives in a <c>BlueprintBlackboard*</c> partition
/// slot (key 555711831), provisioned by <see cref="BehaviorIngressSystem"/>. With
/// <c>RunsNeeded = 3</c> the node returns Running on ticks 1-2 and Success on tick 3, and the slot's
/// <c>Ticks</c> reaches 3 — proving the composed blueprint action runs against the partition slot
/// (not re-zeroed each tick, and not the fixed Blackboard1024+8 rail).
/// </para>
/// </summary>
public sealed class T31_ComposedAiPrimitive_ProofTests : IDisposable
{
    // Slot key baked at code-gen time — see T31_ComposedAiPrimitive.Registrar.g.cs.
    private const int SlotKey = 555711831;

    private readonly BehaviorRegistry  _registry  = new();
    private readonly BlueprintRegistry _blueprint = new();

    public void Dispose() => _registry.Clear();

    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        world.RegisterComponent<BrainBlackboard>();
        world.RegisterComponent<BrainBTreeState>();
        // ⭐ B4: register from the LADDER, not a hand-list. ⛔ This was three explicit
        //   RegisterComponent calls and it did NOT know about the 256 tier — 11 tests
        //   failed with "Component BlueprintBlackboard256 is not registered" the moment
        //   O3b added one. Production never had the bug: it registers from the table.
        BlueprintTierTable.RegisterAll(world);
        return world;
    }

    [Fact]
    public unsafe void ComposedBlueprintAction_RunsTickCore_OverPartitionSlot()
    {
        // Register from the real (compile-time) AI behaviors assembly — the same attribute-driven
        // scan the game uses. T31's generated registrar builds its interpreter over the injected
        // ActionRegistry (into which its blueprint TickCore thunk is registered).
        var staging = new BlueprintRegistryStaging();
        BlueprintRegistrarScanner.Scan(typeof(DemoAiPrimitiveNodes).Assembly, staging, _registry);
        _blueprint.CommitStaging(staging);

        _registry.TryGetId("T31_ComposedAiPrimitive", out int id)
            .Should().BeTrue("T31 must self-register via its generated [BlueprintRegistrar]");
        _registry.TryGetDefinition(id, out var def).Should().BeTrue();
        var interpreter = def.BTreeInterpreter!;
        interpreter.Should().NotBeNull("T31 is a BTree behavior");

        var world  = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        // Assign → BehaviorIngressSystem parses params (RunsNeeded=3) and provisions the slot.
        var ingress = new BehaviorIngressSystem(_registry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = "T31_ComposedAiPrimitive",
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        (OccurrenceStoreAccess.HasStore(world, entity))
            .Should().BeTrue("BehaviorIngressSystem must provision a partition tier for the blueprint slot");

        // Tick the real interpreter. RunsNeeded=3 → Running, Running, Success.
        var ctx = new BTreeContext { Self = entity, World = world };
        NodeStatus Tick()
        {
            ref byte bb = ref global::Fdp.Toolkit.Behavior.RootParamsAccess.RootRef(world, entity);   // P4-②: the ROOT PARAMS SLOT base, exactly as BTreeTickSystem hands it to the interpreter
            var state  = new BehaviorTreeState();
            return interpreter.Tick(ref bb, ref state, ref ctx);
        }

        Tick().Should().Be(NodeStatus.Running, "tick 1 of 3 — TickCore ran against the slot, Ticks=1");
        Tick().Should().Be(NodeStatus.Running, "tick 2 of 3 — slot persisted, Ticks=2");
        Tick().Should().Be(NodeStatus.Success, "tick 3 of 3 — Ticks reached RunsNeeded=3");

        ReadSlotTicks(world, entity).Should().Be(3,
            "WorkingState.Ticks lives in the partition slot and persisted across all three ticks");

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
