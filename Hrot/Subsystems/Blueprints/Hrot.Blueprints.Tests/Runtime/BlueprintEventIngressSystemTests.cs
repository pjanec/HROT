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

        _repo.RegisterComponent<BlueprintBlackboard1024>();
        _repo.RegisterComponent<BlueprintBlackboard4096>();
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

    [Fact]
    public void AttachInstanceBlueprintEvent_IsValueType()
    {
        Assert.True(typeof(AttachInstanceBlueprintEvent).IsValueType);
    }

    [Fact]
    public void RemoveInstanceBlueprintEvent_IsValueType()
    {
        Assert.True(typeof(RemoveInstanceBlueprintEvent).IsValueType);
    }

    [Fact]
    public void ReplaceInstanceBlueprintEvent_IsValueType()
    {
        Assert.True(typeof(ReplaceInstanceBlueprintEvent).IsValueType);
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
        _repo.Bus.Publish(new AttachInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = 42,
        });
        _repo.Bus.SwapBuffers();

        var readSpan = _repo.Bus.Read<AttachInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
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
        _repo.Bus.Publish(new ReplaceInstanceBlueprintEvent
        {
            Entity = entity,
            OldBlueprintId = 10,
            NewBlueprintId = 20,
        });
        _repo.Bus.SwapBuffers();

        var readSpan = _repo.Bus.Read<ReplaceInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(entity, readSpan[0].Entity);
        Assert.Equal(10, readSpan[0].OldBlueprintId);
        Assert.Equal(20, readSpan[0].NewBlueprintId);
    }

    [Fact]
    public void EmptyBus_Read_ReturnsEmptySpan()
    {
        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.Read<AttachInstanceBlueprintEvent>();
        Assert.Equal(0, readSpan.Length);
    }

    // ── Test 3: Attach event via system ───────────────────────────────────────

    [Fact]
    public void System_PublishAttachEvent_BlueprintAttachedToEntity()
    {
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        var entity = _repo.CreateEntity();
        var sys = new BlueprintEventIngressSystem(_registry);

        _repo.Bus.Publish(new AttachInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = FakeBpA_Id,
        });
        _repo.Bus.SwapBuffers();
        sys.Execute(_repo, 0f);

        // Verify slot exists on B1024 tier.
        Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));
        ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* memory = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.CompilerServices.Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
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
        ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* memory = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.CompilerServices.Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
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
        _repo.Bus.Publish(new ReplaceInstanceBlueprintEvent
        {
            Entity = entity,
            OldBlueprintId = FakeBpA_Id,
            NewBlueprintId = FakeBpB_Id,
        });
        _repo.Bus.SwapBuffers();
        sys.Execute(_repo, 0f);

        // A detached, B attached.
        ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* memory = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.CompilerServices.Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
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
        _repo.Bus.Publish(new ReplaceInstanceBlueprintEvent
        {
            Entity = entity,
            OldBlueprintId = FakeBpA_Id,  // not attached
            NewBlueprintId = FakeBpB_Id,
        });
        _repo.Bus.SwapBuffers();
        var ex = Record.Exception(() => sys.Execute(_repo, 0f));
        Assert.Null(ex);

        // B should be attached.
        Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));
        ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* memory = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.CompilerServices.Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(memory, FakeBpB_Id, out _));
    }

    // ── Test 7: Drain ordering (remove-before-add prevents spurious upgrade) ──

    [Fact]
    public void System_DrainOrdering_RemoveBeforeAdd_NoSpuriousTierUpgrade()
    {
        // Register 5 fake blueprints — we'll fill the B1024 tier (max 4 slots).
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        RegisterFakeBp(FakeBpB_Id, "FakeBpB");
        RegisterFakeBp(FakeBpC_Id, "FakeBpC");
        RegisterFakeBp(FakeBpD_Id, "FakeBpD");
        RegisterFakeBp(FakeBpE_Id, "FakeBpE");
        var entity = _repo.CreateEntity();

        // Fill B1024 to capacity: attach A, B, C, D (all same size).
        BlueprintInstanceService.AttachToEntity(_repo, _registry, FakeBpA_Id, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, FakeBpB_Id, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, FakeBpC_Id, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, FakeBpD_Id, entity);

        // Verify tier is at capacity (4 slots, B1024).
        Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));
        Assert.False(_repo.HasComponent<BlueprintBlackboard4096>(entity));
        ref var bb1 = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem1 = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.CompilerServices.Unsafe.As<BlueprintBlackboard1024, byte>(ref bb1));
        Assert.Equal(4, BlueprintBlackboardPartitions.GetSlotCount(mem1));

        // Publish Remove(A) + Attach(E) in the same frame.
        var sys = new BlueprintEventIngressSystem(_registry);
        _repo.Bus.Publish(new RemoveInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = FakeBpA_Id,
        });
        _repo.Bus.Publish(new AttachInstanceBlueprintEvent
        {
            Entity = entity,
            BlueprintId = FakeBpE_Id,
        });
        _repo.Bus.SwapBuffers();
        sys.Execute(_repo, 0f);

        // After system execution: A detached, E attached, still at 4 slots, B1024.
        Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));
        Assert.False(_repo.HasComponent<BlueprintBlackboard4096>(entity),
            "Tier should NOT upgrade — remove-before-add allowed E to reuse A's freed slot");

        ref var bb2 = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* mem2 = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.CompilerServices.Unsafe.As<BlueprintBlackboard1024, byte>(ref bb2));
        Assert.Equal(4, BlueprintBlackboardPartitions.GetSlotCount(mem2));
        Assert.False(BlueprintBlackboardPartitions.TryGetSlotOffset(mem2, FakeBpA_Id, out _),
            "A should be removed");
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(mem2, FakeBpE_Id, out _),
            "E should be attached");
    }
}
