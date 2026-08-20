using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.Runtime;
using Hrot.Blueprints.Tests.Debug;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 102 (<c>102a</c>) — A PAUSED EDIT LANDS ON AN <c>Instance</c> BLUEPRINT.</b>
///
/// <para>🔴 <b>User:</b> <i>"what is correct about not being able to write into a live blackboard of
/// instance when simulation is paused?"</i> ⭐⭐ <b>Nothing</b> — 📌 <c>M-36</c> carries the coordinator's
/// retraction. The capability was simply unbuilt, and a refusal is what unbuilt looks like from
/// outside.</para>
///
/// <para>⭐⭐⭐ <b>WHY THIS RAIL USES A REAL SLOT.</b> An <c>Instance</c> blackboard is PARTITIONED: the
/// allocator places each blueprint's payload at a runtime-chosen offset, and several blueprints share
/// one component. ⇒ ⛔ <b>a rail with a hand-picked offset would prove the arithmetic against itself.</b>
/// ⭐ This attaches through the production <c>BlueprintAttachService</c>, so the slot offset is whatever
/// the allocator really chose, and the assertion is that the WRITE and the READ agree about it.</para>
///
/// <para>⚠ <b>WHICH LAYER IS FAKED</b> *(📌 <c>M-29</c>)*: ⛔ <b>nothing below the dialog.</b> A real
/// <c>EntityRepository</c>, the production attach service, a real <c>BlueprintDebugSession</c> and the
/// production <c>BlueprintLiveValueWriter</c>. ⭐ What is NOT covered: the ImGui click that produces the
/// bytes.</para>
/// </summary>
public sealed class TheInstanceWriteLandsInTheSlotTests
{
    /// <summary>⭐ The demo blueprint's own observable — ⛔ not a field this rail invented, so the
    /// offsets are the compiler's.</summary>
    private const string FieldName = "Count";

    private sealed record Rig(
        EntityRepository World, Entity Entity, Guid AssetId, int BlueprintId,
        BlueprintDebugSession Session,
        Hrot.Blueprints.Tests.Debug.TheSessionWritesWhileFrozenTests.RecordingManager Manager);

    private static Rig Attach(bool paused = true)
    {
        var world = new EntityRepository();
        BlueprintRuntimeWiring.RegisterTierComponents(world);

        var registry = new BlueprintRegistry();
        CounterDemoBlueprint.Register(registry);
        var asset = CounterDemoBlueprint.MakeAsset();

        var entity = world.CreateEntity();
        var result = BlueprintAttachService.AttachToEntity(world, registry, asset, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, result.Status);

        // ⭐ The session reads and resolves through THIS world — the same one the attach wrote into.
        var session = new BlueprintDebugSession(registry, world, new MockTimeController());
        var manager = new Hrot.Blueprints.Tests.Debug.TheSessionWritesWhileFrozenTests.RecordingManager();
        session.SetDataBreakpointManager(manager);
        if (paused) session.Pause();

        return new Rig(world, entity, asset.AssetId, CounterDemoBlueprint.BlueprintId, session, manager);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE ONE THAT MATTERS: an <c>Instance</c> field RESOLVES, and to the address the
    /// allocator really used.</b>
    ///
    /// <para>📐 The expected offset is computed the way the READ computes it — the production
    /// <c>TryGetSlotOffset</c> on the entity's own component bytes — ⛔ not a constant, and ⛔ not the
    /// resolver's own arithmetic re-run.</para>
    ///
    /// <para>⚠⚠ <b>And explicitly NOT <c>+8</c>.</b> An <c>Instance</c> payload opens with a 16-byte
    /// <c>BlueprintLatentCursor</c>, ⛔ not the 8-byte working-state header <c>AiPrimitive</c> has. ⭐ The
    /// second assertion pins that, because applying the wrong header is the specific way this write
    /// would corrupt the neighbouring blueprint's field rather than fail.</para>
    /// </summary>
    [Fact]
    public unsafe void AnInstanceFieldResolvesToTheAllocatorsOwnSlotOffset()
    {
        var rig = Attach();

        int payloadOffset = PayloadOffsetOf(rig);
        int expected      = payloadOffset + CounterDemoBlueprint.CountOffset;

        var field = rig.Session.ResolveWorkingStateField(rig.Entity, rig.AssetId, FieldName);

        Assert.NotNull(field);
        Assert.Equal(typeof(BlueprintBlackboard1024), field!.ComponentType);
        Assert.Equal(expected, field.ComponentOffsetBytes);

        // ⛔ The AiPrimitive convention must NOT have been applied here.
        Assert.NotEqual(WorkingStateLayout.ComponentOffsetOf(expected), field.ComponentOffsetBytes);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>And the WRITE stages at exactly the address the READ displays from.</b>
    ///
    /// <para>📌 <c>Q32</c> §2.1: <i>"an out-of-range offset is MEMORY CORRUPTION, not a wrong value."</i>
    /// ⭐ On a partitioned blackboard the neighbour may be ANOTHER BLUEPRINT'S field, so "close enough"
    /// is not a category that exists here.</para>
    /// </summary>
    [Fact]
    public void TheInstanceWriteStagesAtTheReadsOwnAddress()
    {
        var rig = Attach();
        int expected = PayloadOffsetOf(rig) + CounterDemoBlueprint.CountOffset;

        var field = rig.Session.ResolveWorkingStateField(rig.Entity, rig.AssetId, FieldName)!;
        Assert.True(rig.Session.TryWriteWorkingStateField(
            rig.Entity, field.ComponentType, field.ComponentOffsetBytes, BitConverter.GetBytes(4242)));

        var staged = Assert.Single(rig.Manager.Staged);
        Assert.Equal(typeof(BlueprintBlackboard1024), staged.ComponentType);
        Assert.Equal(expected, staged.ByteOffset);
        Assert.Equal(4242, BitConverter.ToInt32(staged.Bytes, 0));
    }

    /// <summary>
    /// ⭐⭐ <b>Ruling 15 still holds for the new arm.</b> ⛔ Building the Instance capability must not
    /// have opened a write path that ignores the freeze gate — ⚠ the gate is the SESSION's, and the arm
    /// is upstream of it, so this is worth asserting rather than assuming.
    /// </summary>
    [Fact]
    public void WhileNotFrozen_TheInstanceWriteStagesNothing()
    {
        var rig = Attach(paused: false);

        var field = rig.Session.ResolveWorkingStateField(rig.Entity, rig.AssetId, FieldName);
        Assert.NotNull(field);   // ⭐ resolving is not writing — the address is knowable either way

        Assert.False(rig.Session.TryWriteWorkingStateField(
            rig.Entity, field!.ComponentType, field.ComponentOffsetBytes, BitConverter.GetBytes(4242)));
        Assert.Empty(rig.Manager.Staged);
    }

    /// <summary>
    /// ⭐⭐ <b>An entity with no slot for THIS blueprint resolves to nothing</b> — ⛔ fail closed.
    /// ⚠ It carries the component *(it was attached for another blueprint)*, so this is the case a
    /// component-presence check alone would wave through: 📌 the slot lookup is what refuses.
    /// </summary>
    [Fact]
    public void AnEntityWithNoSlotForThisBlueprint_ResolvesToNothing()
    {
        var rig = Attach();
        var other = rig.World.CreateEntity();   // ⭐ no attach ⇒ no component, no slot

        Assert.Null(rig.Session.ResolveWorkingStateField(other, rig.AssetId, FieldName));
    }

    /// <summary>⭐ The READ's own answer for the slot start — the number the write must match.</summary>
    private static unsafe int PayloadOffsetOf(Rig rig)
    {
        ref var bb = ref rig.World.GetComponentRW<BlueprintBlackboard1024>(rig.Entity);
        byte* memory = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));

        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(
            memory, rig.BlueprintId, out int payloadOffset));
        return payloadOffset;
    }
}
