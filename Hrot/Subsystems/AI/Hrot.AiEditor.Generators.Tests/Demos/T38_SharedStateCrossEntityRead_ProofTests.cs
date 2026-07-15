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
/// Slice 2b real-interpreter proof: cross-entity SHARED-STATE READ for blueprint AiPrimitives.
///
/// <para>
/// The committed demo blueprint <c>Assets/Blueprints/SharedStateCrossEntityDemo.bp.json</c>
/// (AiPrimitive, dispatch=AiPrimitive) is compiled by the REAL Roslyn source generator (as part of
/// <c>Hrot.AI.Behaviors</c>'s own build). Its graph is:
/// <c>EventEntry -&gt; SetVariable(RallyCountMirror) -&gt; SetVariable(FoundMirror) -&gt; Return(Success)</c>,
/// with a pure data side-chain
/// <c>GetVariable(Commander) -&gt; GetShared("rally").Target -&gt; [Value] -&gt; SquadRallyStateOps.ReadRallyCount
/// -&gt; SetVariable(RallyCountMirror).Value</c> and <c>GetShared("rally").Found -&gt; SetVariable(FoundMirror).Value</c>.
/// The <c>GetShared</c> node's OPTIONAL "Target" data-in pin is wired to
/// <c>WorkingState.Commander</c> (an <c>Entity</c> field the test sets directly via reflection,
/// standing in for "read off <c>UnitSubordinate</c>'s commander ref via an impure ECS-read node" --
/// authoring guidance, not built here). The generated <c>TickCore</c> body is exactly:
/// <code>
/// var __t0 = ws.Commander;
/// var __t1 = default(global::Hrot.AI.Behaviors.Brains.SquadRallyState);
/// bool __t2 = BlueprintSharedState.TryGetShared&lt;SquadRallyState&gt;(world, __t0, "rally", out __t1);
/// var __t3 = SquadRallyStateOps.ReadRallyCount(__t1);
/// ws.RallyCountMirror = __t3;
/// ws.FoundMirror = __t2;
/// return NodeStatus.Success;
/// </code>
/// proving the wired "Target" pin resolves to the accessor's entity arg INSTEAD of <c>self</c> --
/// the whole point of cross-entity read. Contrast <see cref="T36_SharedStateGetSet_ProofTests"/>'s
/// generated body, which passes <c>self</c> (unwired Target pin, byte-identical to pre-Slice-2b).
/// </para>
///
/// <para>
/// <b>Two entities:</b> "commander" (A) provisions + writes its OWN Entity-scoped "rally" slot
/// directly via <see cref="BlueprintSharedState.TrySetShared{T}"/> (same-entity write -- SetShared
/// stays self-only by construction; cross-entity WRITE is a separate future slice, NOT exercised
/// here). "member" (B) ticks the demo blueprint above with <c>WorkingState.Commander = A</c>, and
/// the real generated <c>TickCore</c> reads A's slot cross-entity and mirrors it into B's own
/// WorkingState -- direct synchronous read, so B observes A's write with no more than the ≤1-frame
/// staleness the design allows (here: zero -- the read is synchronous within the same tick call).
/// </para>
/// </summary>
public sealed class T38_SharedStateCrossEntityRead_ProofTests
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

    /// <summary>Provisions the Entity-scoped "rally" slot on <paramref name="entity"/> (mirrors T36).</summary>
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

    /// <summary>
    /// Locates the real generated blueprint class
    /// (<c>Hrot.AI.Behaviors.Generated.SharedStateCrossEntityDemo_*_Bp</c>) by name pattern, mirroring
    /// <c>T36_SharedStateGetSet_ProofTests.FindGeneratedBlueprintType</c>.
    /// </summary>
    private static Type FindGeneratedBlueprintType()
    {
        var type = typeof(DemoAiPrimitiveNodes).Assembly.GetTypes()
            .SingleOrDefault(t =>
                t.Namespace == "Hrot.AI.Behaviors.Generated"
                && t.Name.StartsWith("SharedStateCrossEntityDemo_", StringComparison.Ordinal)
                && t.Name.EndsWith("_Bp", StringComparison.Ordinal));
        type.Should().NotBeNull(
            "SharedStateCrossEntityDemo.bp.json must compile via the real Roslyn source generator " +
            "into a Hrot.AI.Behaviors.Generated.SharedStateCrossEntityDemo_*_Bp class");
        return type!;
    }

    /// <summary>
    /// Invokes the generated member blueprint's <c>TickCore</c> once via reflection, setting
    /// <c>WorkingState.Commander</c> beforehand and reading <c>RallyCountMirror</c>/<c>FoundMirror</c>
    /// back afterward (via the mutated <c>ref WorkingState</c> boxed arg -- mirrors T36's TickOnce).
    /// </summary>
    private static (Fbt.NodeStatus Status, int RallyCountMirror, bool FoundMirror) TickMember(
        Type bpType, Entity member, Entity commander, EntityRepository world)
    {
        var tickCore = bpType.GetMethod("TickCore", BindingFlags.Public | BindingFlags.Static);
        tickCore.Should().NotBeNull("the generated blueprint class must expose a static TickCore method");

        var paramsType = bpType.GetNestedType("Params")!;
        var wsType     = bpType.GetNestedType("WorkingState")!;

        object ws = Activator.CreateInstance(wsType)!;
        wsType.GetField("Commander")!.SetValue(ws, commander);

        object?[] args = { Activator.CreateInstance(paramsType), ws, member, world, 0f };
        var result = (Fbt.NodeStatus)tickCore!.Invoke(null, args)!;

        object wsAfter = args[1]!;
        int rallyCountMirror = (int)wsType.GetField("RallyCountMirror")!.GetValue(wsAfter)!;
        bool foundMirror     = (bool)wsType.GetField("FoundMirror")!.GetValue(wsAfter)!;
        return (result, rallyCountMirror, foundMirror);
    }

    [Fact]
    public void MemberReadsCommandersSlot_CrossEntity_ThroughRealGeneratedTickCore()
    {
        var bpType = FindGeneratedBlueprintType();

        var world     = CreateWorld();
        var commander = world.CreateEntity();
        var member    = world.CreateEntity();
        world.AddComponent(commander, new BlueprintBlackboard1024());
        // The member entity never gets its own "rally" slot -- Slice 2b's whole point is that it
        // reads the COMMANDER's slot cross-entity; it must not need one of its own.

        ProvisionRallySlot(world, commander);

        // Commander writes its OWN slot -- same-entity SetShared/TrySetShared (self-only by
        // construction; cross-entity WRITE is a separate future slice, not exercised here).
        BlueprintSharedState.TrySetShared(world, commander, "rally", new SquadRallyState { RallyCount = 5 })
            .Should().BeTrue("same-entity TrySetShared on the commander's own provisioned slot must succeed");

        // Member ticks with WorkingState.Commander = commander -- the generated TickCore's GetShared
        // node reads the COMMANDER's slot (via the wired "Target" pin), not its own.
        var (status1, rally1, found1) = TickMember(bpType, member, commander, world);
        status1.Should().Be(Fbt.NodeStatus.Success);
        found1.Should().BeTrue("GetShared must find the commander's provisioned+written slot cross-entity");
        rally1.Should().Be(5, "the member must read the COMMANDER's RallyCount, not a Node/self-scoped default");

        // Commander advances its own state on its "next tick" (simulated here as a direct write --
        // SetShared itself is self-only); the member's VERY NEXT read must see it with no more than
        // ≤1-frame staleness. Because the read is a direct synchronous copy of live partition memory
        // (BlueprintSharedState.TryGetShared, Slice 2a-1), there is no additional buffering delay.
        BlueprintSharedState.TrySetShared(world, commander, "rally", new SquadRallyState { RallyCount = 9 })
            .Should().BeTrue();

        var (status2, rally2, found2) = TickMember(bpType, member, commander, world);
        status2.Should().Be(Fbt.NodeStatus.Success);
        found2.Should().BeTrue();
        rally2.Should().Be(9,
            "the member's read must track the commander's write across the tick boundary with " +
            "no more than ≤1-frame staleness (here: synchronous, zero additional lag)");

        world.Dispose();
    }

    [Fact]
    public void CommanderNotProvisioned_MemberGetSharedReturnsFalse_RallyCountMirrorStaysDefault_NoThrow()
    {
        // Sanity: the "not-ready" cross-entity path. The commander entity exists but has never
        // provisioned "rally" (no BlueprintBlackboard* component at all) -- TryGetShared must return
        // false (never throw), and the generated TickCore must still run to completion with
        // RallyCountMirror/FoundMirror left at their defaults (0 / false).
        var bpType = FindGeneratedBlueprintType();

        var world     = CreateWorld();
        var commander = world.CreateEntity(); // no BlueprintBlackboard1024 -- never provisioned
        var member    = world.CreateEntity();

        (Fbt.NodeStatus Status, int RallyCountMirror, bool FoundMirror) result = default;
        Action act = () => result = TickMember(bpType, member, commander, world);
        act.Should().NotThrow("BlueprintSharedState never throws on a not-ready target slot");

        result.Status.Should().Be(Fbt.NodeStatus.Success);
        result.FoundMirror.Should().BeFalse("the commander never provisioned 'rally' -- Found must be false");
        result.RallyCountMirror.Should().Be(0, "RallyCountMirror must stay at its WorkingState default");

        world.Dispose();
    }
}
