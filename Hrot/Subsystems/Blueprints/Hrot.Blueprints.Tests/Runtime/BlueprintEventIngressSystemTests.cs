using System;
using System.Collections.Generic;
using System.Reflection;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Events;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Blueprints.Systems;
using Hrot.Blueprints.Tests.Runtime;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Tests for BSA-301: Runtime Mutation Events + Consuming System.
/// </summary>
public sealed unsafe class BlueprintEventIngressSystemTests : IDisposable
{
    private readonly BlueprintRegistry _registry;
    private readonly EntityRepository _repo;

    // Fake blueprint IDs and definitions for testing.
    private const int FakeBpA_Id = unchecked((int)0xAAA00001);
    private const int FakeBpB_Id = unchecked((int)0xAAA00002);
    private const int FakeBpC_Id = unchecked((int)0xAAA00003);
    private const int FakeBpD_Id = unchecked((int)0xAAA00004);
    private const int FakeBpE_Id = unchecked((int)0xAAA00005);

    private const int SmallStateSize = 64;  // fits in B1024

    public BlueprintEventIngressSystemTests()
    {
        _registry = new BlueprintRegistry();
        _repo = new EntityRepository();

        // ⭐ B4: register from the LADDER, not a hand-list. ⛔ A hand-list silently leaves a
        //   newly-appended tier unregistered — O3b's 256 tier reddened 192 tests this way.
        //   The bound keeps this world's deliberate exclusion of the larger tiers (their
        //   virtual-address reservation exceeds the allocator's paranoid-mode cap).
        BlueprintTierTable.RegisterUpTo(_repo, maxTotalSize: 4096);
    }

    public void Dispose()
    {
        _repo?.Dispose();
    }

    // ── Helper: register a fake Instance blueprint ──────────────────────────

    private void RegisterFakeBp(int id, string name)
    {
        _registry.RegisterInstance(id, new BlueprintDefinition
        {
            Name = name,
            Kind = BlueprintDispatchKind.Instance,
            StructureHash = (ulong)id,
            StateSize = SmallStateSize,
            InitDefault = span => span.Clear(),
        });
    }

    // ── Test 1: Event struct layout ──────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 70 — this assertion is INVERTED, deliberately.</b> It read
    /// <c>Assert.True(…IsValueType)</c>, and that was the shipped truth right up until
    /// <c>DESIGN_Parameter_Model.md</c> §3.3 ruled that an Instance attach carries its params as JSON.
    /// A managed string cannot ride the native bus ⇒ the event becomes a <b>class</b>, on the precedent
    /// <c>AssignBehaviorEvent</c> set: <i>"must be a class (not a struct) because it carries managed
    /// string fields."</i>
    ///
    /// <para>
    /// ⭐ Kept rather than deleted, and inverted rather than relaxed: which bus an event rides is a
    /// decision with a two-phase-drain consequence (see <c>BlueprintEventIngressSystem</c>), so it
    /// stays asserted in both directions.
    /// </para>
    /// </summary>
    [Fact]
    public void AttachInstanceBlueprintEvent_IsAManagedClass_BecauseItCarriesParamsJson()
    {
        Assert.False(typeof(AttachInstanceBlueprintEvent).IsValueType);
        Assert.Equal(typeof(string), typeof(AttachInstanceBlueprintEvent).GetField("ParamsJson")!.FieldType);
    }

    /// <summary>
    /// ⭐ <b>Remove stays a struct</b> — a detach carries no params, so nothing forces it onto the
    /// managed bus. ⛔ Converting it "for symmetry" would cost an allocation per event for no gain.
    /// </summary>
    [Fact]
    public void RemoveInstanceBlueprintEvent_IsValueType()
    {
        Assert.True(typeof(RemoveInstanceBlueprintEvent).IsValueType);
    }

    /// <summary>⭐ Replace is a class for the same reason Attach is: its add half attaches.</summary>
    [Fact]
    public void ReplaceInstanceBlueprintEvent_IsAManagedClass_BecauseItsAddHalfAttaches()
    {
        Assert.False(typeof(ReplaceInstanceBlueprintEvent).IsValueType);
        Assert.Equal(typeof(string), typeof(ReplaceInstanceBlueprintEvent).GetField("ParamsJson")!.FieldType);
    }

    [Fact]
    public void AttachInstanceBlueprintEvent_HasCorrectEventId()
    {
        var attr = typeof(AttachInstanceBlueprintEvent)
            .GetCustomAttribute<EventIdAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(BlueprintConstants.EventId_AttachInstanceBlueprint, attr!.Id);
    }

    [Fact]
    public void RemoveInstanceBlueprintEvent_HasCorrectEventId()
    {
        var attr = typeof(RemoveInstanceBlueprintEvent)
            .GetCustomAttribute<EventIdAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(BlueprintConstants.EventId_RemoveInstanceBlueprint, attr!.Id);
    }

    [Fact]
    public void ReplaceInstanceBlueprintEvent_HasCorrectEventId()
    {
        var attr = typeof(ReplaceInstanceBlueprintEvent)
            .GetCustomAttribute<EventIdAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(BlueprintConstants.EventId_ReplaceInstanceBlueprint, attr!.Id);
    }

    [Fact]
    public void AttachInstanceBlueprintEvent_HasCorrectFields()
    {
        var entityField = typeof(AttachInstanceBlueprintEvent).GetField("Entity");
        var bpIdField = typeof(AttachInstanceBlueprintEvent).GetField("BlueprintId");
        Assert.NotNull(entityField);
        Assert.NotNull(bpIdField);
        Assert.Equal(typeof(Entity), entityField!.FieldType);
        Assert.Equal(typeof(int), bpIdField!.FieldType);
    }

    [Fact]
    public void ReplaceInstanceBlueprintEvent_HasCorrectFields()
    {
        var entityField = typeof(ReplaceInstanceBlueprintEvent).GetField("Entity");
        var oldBpField = typeof(ReplaceInstanceBlueprintEvent).GetField("OldBlueprintId");
        var newBpField = typeof(ReplaceInstanceBlueprintEvent).GetField("NewBlueprintId");
        Assert.NotNull(entityField);
        Assert.NotNull(oldBpField);
        Assert.NotNull(newBpField);
        Assert.Equal(typeof(Entity), entityField!.FieldType);
        Assert.Equal(typeof(int), oldBpField!.FieldType);
        Assert.Equal(typeof(int), newBpField!.FieldType);
    }

    // ── Test 2: Publish/Read round-trip ───────────────────────────────────────

    [Fact]
    public void AttachEvent_PublishReadRoundTrip_FieldsMatch()
    {
        var entity = _repo.CreateEntity();
        _repo.Bus.PublishManaged(new AttachInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = 42,
        });
        _repo.Bus.SwapBuffers();

        var readSpan = _repo.Bus.ReadManaged<AttachInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Count);
        Assert.Equal(entity, readSpan[0].Entity);
        Assert.Equal(42, readSpan[0].BlueprintId);
    }

    [Fact]
    public void RemoveEvent_PublishReadRoundTrip_FieldsMatch()
    {
        var entity = _repo.CreateEntity();
        _repo.Bus.Publish(new RemoveInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = 99,
        });
        _repo.Bus.SwapBuffers();

        var readSpan = _repo.Bus.Read<RemoveInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(entity, readSpan[0].Entity);
        Assert.Equal(99, readSpan[0].BlueprintId);
    }

    [Fact]
    public void ReplaceEvent_PublishReadRoundTrip_FieldsMatch()
    {
        var entity = _repo.CreateEntity();
        _repo.Bus.PublishManaged(new ReplaceInstanceBlueprintEvent
        {
            Entity = entity,
            OldBlueprintId = 10,
            NewBlueprintId = 20,
        });
        _repo.Bus.SwapBuffers();

        var readSpan = _repo.Bus.ReadManaged<ReplaceInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Count);
        Assert.Equal(entity, readSpan[0].Entity);
        Assert.Equal(10, readSpan[0].OldBlueprintId);
        Assert.Equal(20, readSpan[0].NewBlueprintId);
    }

    [Fact]
    public void EmptyBus_Read_ReturnsEmptySpan()
    {
        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.ReadManaged<AttachInstanceBlueprintEvent>();
        Assert.Equal(0, readSpan.Count);
    }

    // ── Test 3: Attach event via system ───────────────────────────────────────

    [Fact]
    public void System_PublishAttachEvent_BlueprintAttachedToEntity()
    {
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        var entity = _repo.CreateEntity();
        var sys = new BlueprintEventIngressSystem(_registry);

        _repo.Bus.PublishManaged(new AttachInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = FakeBpA_Id,
        });
        _repo.Bus.SwapBuffers();
        sys.Execute(_repo, 0f);

        // Verify slot exists on B1024 tier.
        Assert.True(OccurrenceStoreAccess.HasStore(_repo, entity));
        // ⭐ B4 — §17.7: the store through the SEAM, not a named tier.
        byte* memory = OccurrenceStoreAccess.TryGetStore(_repo, entity, out _);
        int slotCount = BlueprintBlackboardPartitions.GetSlotCount(memory);
        Assert.Equal(1, slotCount);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(memory, FakeBpA_Id, out _));
    }

    // ── Test 4: Remove event via system ───────────────────────────────────────

    [Fact]
    public void System_PublishRemoveEvent_BlueprintDetachedFromEntity()
    {
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        var entity = _repo.CreateEntity();

        // Attach directly via core seam first.
        var attachResult = BlueprintInstanceService.AttachToEntity(
            _repo, _registry, FakeBpA_Id, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, attachResult.Status);

        // Now publish remove event to detach.
        var sys = new BlueprintEventIngressSystem(_registry);
        _repo.Bus.Publish(new RemoveInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = FakeBpA_Id,
        });
        _repo.Bus.SwapBuffers();
        sys.Execute(_repo, 0f);

        // Verify slot is gone.
        // ⭐ B4 — §17.7: the store through the SEAM, not a named tier.
        byte* memory = OccurrenceStoreAccess.TryGetStore(_repo, entity, out _);
        int slotCount = BlueprintBlackboardPartitions.GetSlotCount(memory);
        Assert.Equal(0, slotCount);
        Assert.False(BlueprintBlackboardPartitions.TryGetSlotOffset(memory, FakeBpA_Id, out _));
    }

    // ── Test 5: Replace event via system ──────────────────────────────────────

    [Fact]
    public void System_PublishReplaceEvent_OldDetachedNewAttached()
    {
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        RegisterFakeBp(FakeBpB_Id, "FakeBpB");
        var entity = _repo.CreateEntity();

        // Attach A directly via core seam first.
        var attachResult = BlueprintInstanceService.AttachToEntity(
            _repo, _registry, FakeBpA_Id, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, attachResult.Status);

        // Publish replace event: A → B.
        var sys = new BlueprintEventIngressSystem(_registry);
        _repo.Bus.PublishManaged(new ReplaceInstanceBlueprintEvent
        {
            Entity = entity,
            OldBlueprintId = FakeBpA_Id,
            NewBlueprintId = FakeBpB_Id,
        });
        _repo.Bus.SwapBuffers();
        sys.Execute(_repo, 0f);

        // A detached, B attached.
        // ⭐ B4 — §17.7: the store through the SEAM, not a named tier.
        byte* memory = OccurrenceStoreAccess.TryGetStore(_repo, entity, out _);
        int slotCount = BlueprintBlackboardPartitions.GetSlotCount(memory);
        Assert.Equal(1, slotCount);
        Assert.False(BlueprintBlackboardPartitions.TryGetSlotOffset(memory, FakeBpA_Id, out _));
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(memory, FakeBpB_Id, out _));
    }

    // ── Test 6: Idempotent / no-op ────────────────────────────────────────────

    [Fact]
    public void System_RemoveAbsentBlueprint_DoesNotThrow()
    {
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        var entity = _repo.CreateEntity();
        var sys = new BlueprintEventIngressSystem(_registry);

        // Remove a blueprint that was never attached — should not throw.
        _repo.Bus.Publish(new RemoveInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = FakeBpA_Id,
        });
        _repo.Bus.SwapBuffers();
        var ex = Record.Exception(() => sys.Execute(_repo, 0f));
        Assert.Null(ex);
    }

    [Fact]
    public void System_ReplaceWithAbsentOld_AttachStillProceeds()
    {
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        RegisterFakeBp(FakeBpB_Id, "FakeBpB");
        var entity = _repo.CreateEntity();
        var sys = new BlueprintEventIngressSystem(_registry);

        // Replace where old blueprint is absent — should not throw; new should attach.
        _repo.Bus.PublishManaged(new ReplaceInstanceBlueprintEvent
        {
            Entity = entity,
            OldBlueprintId = FakeBpA_Id,  // not attached
            NewBlueprintId = FakeBpB_Id,
        });
        _repo.Bus.SwapBuffers();
        var ex = Record.Exception(() => sys.Execute(_repo, 0f));
        Assert.Null(ex);

        // B should be attached.
        Assert.True(OccurrenceStoreAccess.HasStore(_repo, entity));
        // ⭐ B4 — §17.7: the store through the SEAM, not a named tier.
        byte* memory = OccurrenceStoreAccess.TryGetStore(_repo, entity, out _);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(memory, FakeBpB_Id, out _));
    }

    // ── Test 7: Drain ordering (remove-before-add prevents spurious upgrade) ──

    [Fact]
    public void System_DrainOrdering_RemoveBeforeAdd_NoSpuriousTierUpgrade()
    {
        // ⭐ B4 — design §17.7. This said "fill the B1024 tier (max 4 slots)" — the PRE-B3②
        //   ladder, and it also assumed a small blueprint lands on 1024. Both moved.
        // ⛔⛔ And "fill to MaxSlots" is ALSO wrong, which is worth stating: on the 256 tier the
        //   binding limit is BYTES, not slots — 64 B of state costs 64 B of payload out of 176,
        //   so it fills at 2 while MaxSlots is 3. ⇒ fill until the store actually reports full,
        //   and let the one that did not fit be the spare. The property under test is unchanged:
        //   a remove and an add drained in ONE frame must reuse the freed slot rather than force
        //   a promotion.
        var ids = new[] { FakeBpA_Id, FakeBpB_Id, FakeBpC_Id, FakeBpD_Id, FakeBpE_Id };
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        RegisterFakeBp(FakeBpB_Id, "FakeBpB");
        RegisterFakeBp(FakeBpC_Id, "FakeBpC");
        RegisterFakeBp(FakeBpD_Id, "FakeBpD");
        RegisterFakeBp(FakeBpE_Id, "FakeBpE");
        // ⛔ CE-3137 U-0 (R-236): an attach no longer FAILS when the store is full — it APPENDS a block. So "full"
        //   is measured as "one more would add a block", on a PROBE entity, and the property becomes: a remove and
        //   an add drained in ONE frame reuse the freed slot rather than GROW the store (was: "force a promotion").
        var probe = _repo.CreateEntity();
        int fits = 0;
        foreach (int id in ids)
        {
            Assert.Equal(BlueprintAttachStatus.Attached,
                BlueprintInstanceService.AttachToEntity(_repo, _registry, id, probe).Status);
            if (OccurrenceStoreAccess.Measure(_repo, probe).Blocks > 1) break;
            fits++;
        }

        Assert.True(fits >= 2, $"expected one block to seat at least two blueprints, seated {fits}");
        Assert.True(fits < ids.Length,
            "the pool must be large enough that one blueprint does NOT fit one block; add ids if the ladder grows");

        var entity = _repo.CreateEntity();
        for (int i = 0; i < fits; i++)
            Assert.Equal(BlueprintAttachStatus.Attached,
                BlueprintInstanceService.AttachToEntity(_repo, _registry, ids[i], entity).Status);
        int spare = ids[fits];

        var before = OccurrenceStoreAccess.Measure(_repo, entity);
        Assert.Equal(1, before.Blocks);
        Assert.Equal(fits, before.SlotCount);
        var tier = OccurrenceStoreAccess.LargestBlock(_repo, entity)!;

        // Publish Remove(A) + Attach(spare) in the same frame.
        var sys = new BlueprintEventIngressSystem(_registry);
        _repo.Bus.Publish(new RemoveInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = ids[0],
        });
        _repo.Bus.PublishManaged(new AttachInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = spare,
        });
        _repo.Bus.SwapBuffers();
        sys.Execute(_repo, 0f);

        // After execution: A detached, the spare attached, still ONE block of the SAME tier.
        var after = OccurrenceStoreAccess.Measure(_repo, entity);
        Assert.Equal(1, after.Blocks);
        Assert.Same(tier, OccurrenceStoreAccess.LargestBlock(_repo, entity));
        Assert.Equal(fits, after.SlotCount);
        Assert.False(OccurrenceStoreAccess.TryFindSlot(_repo, entity, ids[0], out _, out _, out _),
            "A should be removed");
        Assert.True(OccurrenceStoreAccess.TryFindSlot(_repo, entity, spare, out _, out _, out _),
            "the spare should be attached — it reused A's freed slot rather than growing the store");
    }
}
