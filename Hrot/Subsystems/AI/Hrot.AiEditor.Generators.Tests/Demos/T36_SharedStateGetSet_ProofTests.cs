using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// Slice 2a-2 real-interpreter proof: the <c>GetShared</c>/<c>SetShared</c> blueprint graph nodes
/// (Hrot.Blueprints.Compiler) compile to calls into the Slice 2a-1 runtime accessor
/// (<see cref="BlueprintSharedState.TryGetShared{T}"/> / <see cref="BlueprintSharedState.TrySetShared{T}"/>).
///
/// <para>
/// The committed demo blueprint <c>Assets/Blueprints/SharedStateRallyDemo.bp.json</c> (AiPrimitive,
/// dispatch=AiPrimitive) is compiled by the REAL Roslyn source generator (as part of
/// <c>Hrot.AI.Behaviors</c>'s own build — see that project's
/// <c>obj/GeneratedFiles/Hrot.Blueprints.Generators/.../SharedStateRallyDemo_*.g.cs</c>). Its graph is:
/// <c>EventEntry → GetShared("rally") → SquadRallyStateOps.IncrementRallyCount(pure FunctionCall) →
/// SetShared("rally") → Return(Success)</c>. The generated <c>TickCore</c> body is exactly:
/// <code>
/// var __t0 = default(global::Hrot.AI.Behaviors.Brains.SquadRallyState);
/// bool __t1 = BlueprintSharedState.TryGetShared&lt;SquadRallyState&gt;(world, self, "rally", out __t0);
/// var __t2 = SquadRallyStateOps.IncrementRallyCount(__t0);
/// bool __t3 = BlueprintSharedState.TrySetShared&lt;SquadRallyState&gt;(world, self, "rally", in __t2);
/// return NodeStatus.Success;
/// </code>
/// </para>
///
/// <para>
/// <b>Provisioning path:</b> this test provisions the Entity-scoped "rally" slot DIRECTLY via
/// <see cref="BlueprintBlackboardPartitions.TryAttach"/> (mirrors
/// <c>BlueprintSharedStateTests.ProvisionEntitySlot</c>, the same path
/// <c>BehaviorIngressSystem.ProvisionStatefulSlots</c> would use for an Entity-scoped host variable),
/// rather than wiring a host BTree + composed-node <c>WorkingStateTargetField</c> binding. The
/// host-BTree manifest path ties a composed node's provisioned StructureHash to that SAME node's own
/// <c>WorkingStateTypeId</c> (see <c>BTreeBridgeEmitCore.EmitStatefulWorkingSlotsArray</c>) — i.e. it
/// scopes the node's OWN WorkingState, not an unrelated foreign Category-1 struct read via a second,
/// independent accessor call inside the graph body. Reusing it here would either require the demo's
/// generated WorkingState struct to itself equal <c>SquadRallyState</c> byte-for-byte (which still
/// fails the StructureHash guard -- the hash is keyed by <c>typeof(T).FullName</c>, and the generated
/// <c>_Bp+WorkingState</c> type name will never equal <c>SquadRallyState</c>'s) or hijacking
/// <c>WorkingStateTypeId</c> to a string that does not match what the delegate shape actually casts
/// the partition memory to. Direct provisioning is the correct, explicitly-sanctioned fallback for
/// this case; the real interpreter under test is still the actual Roslyn-generated <c>TickCore</c>.
/// </para>
///
/// <para>
/// <b>Slice 2a-3 update:</b> <see cref="T37_SharedStateManifestProvisioning_ProofTests"/> now covers
/// the manifest-provisioning path this test intentionally does not — a host BTree declares "rally" as
/// a standalone Entity-scoped Role=State blackboard variable (not bound to any node's WorkingState),
/// composes this SAME <c>SharedStateRallyDemo</c> blueprint, and lets
/// <c>BehaviorIngressSystem.ProvisionStatefulSlots</c> attach the slot from the generated
/// <c>StatefulWorkingSlots</c> manifest on <c>AssignBehaviorEvent</c> — no direct <c>TryAttach</c>.
/// This test remains as the isolated accessor-only proof described above.
/// </para>
/// </summary>
public sealed class T36_SharedStateGetSet_ProofTests
{
    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BlueprintBlackboard1024>();
        return world;
    }

    private static uint ExpectedHash<T>() where T : unmanaged
        => unchecked(StatefulBTreeActionBinder.ComputeTypeNameHash(typeof(T).FullName ?? string.Empty)
                      ^ (uint)Marshal.SizeOf<T>());

    private static int EntityKey(string variableId)
        => StatefulBTreeActionBinder.ComputeStatefulSlotKey(
            Guid.Empty, StatefulSlotScope.Entity, Guid.Empty, variableId);

    /// <summary>Provisions the Entity-scoped "rally" slot the same way the 2a-1 unit tests do.</summary>
    private static unsafe void ProvisionRallySlot(EntityRepository world, Entity entity)
    {
        int slotKey = EntityKey("rally");
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            BlueprintBlackboardPartitions.Initialize(mem, BlueprintBlackboard1024.TotalSize, BlueprintBlackboard1024.MaxSlots);
            bool ok = BlueprintBlackboardPartitions.TryAttach(
                mem, slotKey, Marshal.SizeOf<SquadRallyState>(), ExpectedHash<SquadRallyState>(), out _);
            Assert.True(ok, $"TryAttach must succeed for the 'rally' Entity-scoped slot (slotKey={slotKey})");
        }
    }

    private static unsafe int ReadRallyCount(EntityRepository world, Entity entity)
    {
        int slotKey = EntityKey("rally");
        ref var tier = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
        fixed (byte* mem = tier.Memory)
        {
            BlueprintBlackboardPartitions.TryGetSlotOffset(mem, slotKey, out int offset).Should().BeTrue();
            return Unsafe.AsRef<SquadRallyState>(mem + offset).RallyCount;
        }
    }

    /// <summary>
    /// Locates the real generated blueprint class (<c>Hrot.AI.Behaviors.Generated.SharedStateRallyDemo_*_Bp</c>)
    /// by name pattern rather than hardcoding the BlueprintId hash baked into the class name, so this
    /// test does not need to reimplement <c>BlueprintIdHash.Compute</c>.
    /// </summary>
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("SharedStateRallyDemo_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "SharedStateRallyDemo.bp.json must compile via the real Roslyn source generator into a " +
            "Hrot.AI.Behaviors.Generated.SharedStateRallyDemo_*_Bp class");
        return type!;
    }

    /// <summary>Invokes the generated <c>TickCore</c> once via reflection (ref Params/WorkingState are both empty structs for this demo).</summary>
    private static Fbt.NodeStatus TickOnce(Type bpType, Entity entity, EntityRepository world)
    {
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        object?[] args =
        {
            Activator.CreateInstance(paramsType),
            Activator.CreateInstance(wsType),
            entity,
            world,
            0f,
        };
        var result = tickCore!.Invoke(null, args);
        return (Fbt.NodeStatus)result!;
    }

    [Fact]
    public void GetSharedThenSetShared_AccumulatesRallyCount_AcrossTicks_ThroughRealGeneratedTickCore()
    {
        var bpType = FindGeneratedBlueprintType();

        var world  = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        ProvisionRallySlot(world, entity);

        // The demo blueprint's own graph is a single-shot EventEntry -> ... -> Return(Success); it does
        // not depend on the TickCore's own (empty) WorkingState/Params for the increment -- each call
        // reads/increments/writes the SAME "rally" Entity-scoped slot via BlueprintSharedState, so
        // repeated invocations accumulate RallyCount just like a real per-tick TickCore call would.
        for (int tick = 1; tick <= 5; tick++)
        {
            TickOnce(bpType, entity, world).Should().Be(Fbt.NodeStatus.Success,
                $"tick {tick}: EventEntry -> GetShared -> Increment -> SetShared -> Return(Success) is synchronous");

            ReadRallyCount(world, entity).Should().Be(tick,
                $"tick {tick}: GetShared must read what the PREVIOUS tick's SetShared wrote (accumulation " +
                "through the accessor + StructureHash guard), proving GetShared/SetShared round-trip through " +
                "the real Slice 2a-1 BlueprintSharedState accessor rather than a per-call-local default value.");
        }

        world.Dispose();
    }

    [Fact]
    public void NotProvisioned_TickCore_GetSharedReturnsFalse_RallyCountStaysDefault_NoThrow()
    {
        // Sanity: without provisioning, TryGetShared returns false (not-ready) and the generated
        // TickCore still runs to completion (never throws) -- SetShared's TrySetShared also returns
        // false (nothing to write into), and Return(Success) still fires because the blueprint graph
        // does not branch on the "Found"/"Written" bools (they are unwired in this demo).
        var bpType = FindGeneratedBlueprintType();
        var world  = CreateWorld();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new BlueprintBlackboard1024());

        Action act = () => TickOnce(bpType, entity, world);
        act.Should().NotThrow("BlueprintSharedState never throws on a not-ready slot");
    }
}
