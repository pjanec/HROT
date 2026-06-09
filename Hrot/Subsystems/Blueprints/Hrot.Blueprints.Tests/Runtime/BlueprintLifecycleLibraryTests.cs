using System;
using System.Reflection;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Actions;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Events;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Blueprints.Systems;
using Xunit;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// Tests for BSA-302: [SharedAiAction] BlueprintLifecycleLibrary nodes.
/// </summary>
public sealed unsafe class BlueprintLifecycleLibraryTests : IDisposable
{
    private readonly BlueprintRegistry _registry;
    private readonly EntityRepository _repo;

    // Fake blueprint IDs for testing.
    private const int FakeBpA_Id = unchecked((int)0xBBB00001);
    private const int FakeBpB_Id = unchecked((int)0xBBB00002);
    private const int FakeBpC_Id = unchecked((int)0xBBB00003);

    private const int SmallStateSize = 64;  // fits in B1024

    public BlueprintLifecycleLibraryTests()
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

    // ── Test 1: Method signature (reflection) ───────────────────────────────

    [Fact]
    public void AttachInstanceBlueprint_IsStatic_ReturnsNodeStatus_HasSharedAiAction()
    {
        var method = typeof(BlueprintLifecycleLibrary).GetMethod(
            nameof(BlueprintLifecycleLibrary.AttachInstanceBlueprint),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True(method!.IsStatic);
        Assert.Equal(typeof(NodeStatus), method.ReturnType);

        var attr = method.GetCustomAttribute<SharedAiActionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(typeof(AttachInstanceBlueprintSlot), attr!.DtoType);
        Assert.Equal(nameof(AttachInstanceBlueprintSlot.Params), attr.FieldName);

        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.True(parameters[0].ParameterType.IsByRef);
        Assert.Equal(typeof(AttachInstanceBlueprintParams), parameters[0].ParameterType.GetElementType());
        Assert.Equal(typeof(Entity), parameters[1].ParameterType);
        Assert.Equal(typeof(EntityRepository), parameters[2].ParameterType);
    }

    [Fact]
    public void RemoveInstanceBlueprint_IsStatic_ReturnsNodeStatus_HasSharedAiAction()
    {
        var method = typeof(BlueprintLifecycleLibrary).GetMethod(
            nameof(BlueprintLifecycleLibrary.RemoveInstanceBlueprint),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True(method!.IsStatic);
        Assert.Equal(typeof(NodeStatus), method.ReturnType);

        var attr = method.GetCustomAttribute<SharedAiActionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(typeof(RemoveInstanceBlueprintSlot), attr!.DtoType);
        Assert.Equal(nameof(RemoveInstanceBlueprintSlot.Params), attr.FieldName);

        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.True(parameters[0].ParameterType.IsByRef);
        Assert.Equal(typeof(RemoveInstanceBlueprintParams), parameters[0].ParameterType.GetElementType());
        Assert.Equal(typeof(Entity), parameters[1].ParameterType);
        Assert.Equal(typeof(EntityRepository), parameters[2].ParameterType);
    }

    [Fact]
    public void ReplaceInstanceBlueprint_IsStatic_ReturnsNodeStatus_HasSharedAiAction()
    {
        var method = typeof(BlueprintLifecycleLibrary).GetMethod(
            nameof(BlueprintLifecycleLibrary.ReplaceInstanceBlueprint),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True(method!.IsStatic);
        Assert.Equal(typeof(NodeStatus), method.ReturnType);

        var attr = method.GetCustomAttribute<SharedAiActionAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(typeof(ReplaceInstanceBlueprintSlot), attr!.DtoType);
        Assert.Equal(nameof(ReplaceInstanceBlueprintSlot.Params), attr.FieldName);

        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.True(parameters[0].ParameterType.IsByRef);
        Assert.Equal(typeof(ReplaceInstanceBlueprintParams), parameters[0].ParameterType.GetElementType());
        Assert.Equal(typeof(Entity), parameters[1].ParameterType);
        Assert.Equal(typeof(EntityRepository), parameters[2].ParameterType);
    }

    // ── Test 2: Attach publishes correct event ──────────────────────────────

    [Fact]
    public void AttachInstanceBlueprint_PublishesAttachEvent_WithCorrectFields()
    {
        var entity = _repo.CreateEntity();
        var dto = new AttachInstanceBlueprintParams
        {
            BlueprintId = FakeBpA_Id,
            TargetEntityPacked = 0, // self
        };

        var status = BlueprintLifecycleLibrary.AttachInstanceBlueprint(ref dto, entity, _repo);
        Assert.Equal(NodeStatus.Success, status);

        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.Read<AttachInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(entity, readSpan[0].Entity);
        Assert.Equal(FakeBpA_Id, readSpan[0].BlueprintId);
    }

    // ── Test 3: Remove publishes correct event ──────────────────────────────

    [Fact]
    public void RemoveInstanceBlueprint_PublishesRemoveEvent_WithCorrectFields()
    {
        var entity = _repo.CreateEntity();
        var dto = new RemoveInstanceBlueprintParams
        {
            BlueprintId = FakeBpB_Id,
            TargetEntityPacked = 0, // self
        };

        var status = BlueprintLifecycleLibrary.RemoveInstanceBlueprint(ref dto, entity, _repo);
        Assert.Equal(NodeStatus.Success, status);

        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.Read<RemoveInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(entity, readSpan[0].Entity);
        Assert.Equal(FakeBpB_Id, readSpan[0].BlueprintId);
    }

    // ── Test 4: Replace publishes correct event ─────────────────────────────

    [Fact]
    public void ReplaceInstanceBlueprint_PublishesReplaceEvent_WithCorrectFields()
    {
        var entity = _repo.CreateEntity();
        var dto = new ReplaceInstanceBlueprintParams
        {
            OldBlueprintId = FakeBpA_Id,
            NewBlueprintId = FakeBpB_Id,
            TargetEntityPacked = 0, // self
        };

        var status = BlueprintLifecycleLibrary.ReplaceInstanceBlueprint(ref dto, entity, _repo);
        Assert.Equal(NodeStatus.Success, status);

        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.Read<ReplaceInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(entity, readSpan[0].Entity);
        Assert.Equal(FakeBpA_Id, readSpan[0].OldBlueprintId);
        Assert.Equal(FakeBpB_Id, readSpan[0].NewBlueprintId);
    }

    // ── Test 5: Target resolution ──────────────────────────────────────────

    [Fact]
    public void Attach_WithTargetEntityPackedZero_ResolvesToSelf()
    {
        var self = _repo.CreateEntity();
        var dto = new AttachInstanceBlueprintParams
        {
            BlueprintId = FakeBpA_Id,
            TargetEntityPacked = 0,
        };

        BlueprintLifecycleLibrary.AttachInstanceBlueprint(ref dto, self, _repo);

        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.Read<AttachInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(self, readSpan[0].Entity);
    }

    [Fact]
    public void Attach_WithSpecificTargetEntityPacked_ResolvesToThatEntity()
    {
        var self = _repo.CreateEntity();
        var target = _repo.CreateEntity();
        var dto = new AttachInstanceBlueprintParams
        {
            BlueprintId = FakeBpA_Id,
            TargetEntityPacked = target.PackedValue,
        };

        BlueprintLifecycleLibrary.AttachInstanceBlueprint(ref dto, self, _repo);

        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.Read<AttachInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(target, readSpan[0].Entity);
        Assert.NotEqual(self, readSpan[0].Entity);
    }

    [Fact]
    public void Remove_WithTargetEntityPackedZero_ResolvesToSelf()
    {
        var self = _repo.CreateEntity();
        var dto = new RemoveInstanceBlueprintParams
        {
            BlueprintId = FakeBpA_Id,
            TargetEntityPacked = 0,
        };

        BlueprintLifecycleLibrary.RemoveInstanceBlueprint(ref dto, self, _repo);

        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.Read<RemoveInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(self, readSpan[0].Entity);
    }

    [Fact]
    public void Replace_WithTargetEntityPackedZero_ResolvesToSelf()
    {
        var self = _repo.CreateEntity();
        var dto = new ReplaceInstanceBlueprintParams
        {
            OldBlueprintId = FakeBpA_Id,
            NewBlueprintId = FakeBpB_Id,
            TargetEntityPacked = 0,
        };

        BlueprintLifecycleLibrary.ReplaceInstanceBlueprint(ref dto, self, _repo);

        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.Read<ReplaceInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(self, readSpan[0].Entity);
    }

    [Fact]
    public void Replace_WithSpecificTargetEntityPacked_ResolvesToThatEntity()
    {
        var self = _repo.CreateEntity();
        var target = _repo.CreateEntity();
        var dto = new ReplaceInstanceBlueprintParams
        {
            OldBlueprintId = FakeBpA_Id,
            NewBlueprintId = FakeBpB_Id,
            TargetEntityPacked = target.PackedValue,
        };

        BlueprintLifecycleLibrary.ReplaceInstanceBlueprint(ref dto, self, _repo);

        _repo.Bus.SwapBuffers();
        var readSpan = _repo.Bus.Read<ReplaceInstanceBlueprintEvent>();
        Assert.Equal(1, readSpan.Length);
        Assert.Equal(target, readSpan[0].Entity);
        Assert.NotEqual(self, readSpan[0].Entity);
    }

    // ── Test 6: Integration end-to-end (action → event → ingress → attach) ──

    [Fact]
    public void AttachAction_FullPipeline_BlueprintAttachedToEntity()
    {
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        var entity = _repo.CreateEntity();
        var sys = new BlueprintEventIngressSystem(_registry);

        // Step 1: Call the action method (publishes event to Bus).
        var dto = new AttachInstanceBlueprintParams
        {
            BlueprintId = FakeBpA_Id,
            TargetEntityPacked = 0,
        };
        var status = BlueprintLifecycleLibrary.AttachInstanceBlueprint(ref dto, entity, _repo);
        Assert.Equal(NodeStatus.Success, status);

        // Step 2: Advance the event bus so the event is readable.
        _repo.Bus.SwapBuffers();

        // Step 3: Execute the ingress system (simulates next frame's Input phase).
        var ex = Record.Exception(() => sys.Execute(_repo, 0f));
        Assert.Null(ex);

        // Step 4: Verify the blueprint is attached to the entity.
        Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));
        ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        byte* memory = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.CompilerServices.Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
        int slotCount = BlueprintBlackboardPartitions.GetSlotCount(memory);
        Assert.Equal(1, slotCount);
        Assert.True(BlueprintBlackboardPartitions.TryGetSlotOffset(memory, FakeBpA_Id, out _));
    }

    [Fact]
    public void RemoveAction_FullPipeline_BlueprintDetachedFromEntity()
    {
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        var entity = _repo.CreateEntity();

        // Attach directly via core seam first.
        var attachResult = BlueprintInstanceService.AttachToEntity(
            _repo, _registry, FakeBpA_Id, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, attachResult.Status);

        // Publish remove event via action method.
        var dto = new RemoveInstanceBlueprintParams
        {
            BlueprintId = FakeBpA_Id,
            TargetEntityPacked = 0,
        };
        var status = BlueprintLifecycleLibrary.RemoveInstanceBlueprint(ref dto, entity, _repo);
        Assert.Equal(NodeStatus.Success, status);

        // Advance bus + execute ingress system.
        var sys = new BlueprintEventIngressSystem(_registry);
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

    [Fact]
    public void ReplaceAction_FullPipeline_OldDetachedNewAttached()
    {
        RegisterFakeBp(FakeBpA_Id, "FakeBpA");
        RegisterFakeBp(FakeBpB_Id, "FakeBpB");
        var entity = _repo.CreateEntity();

        // Attach A directly via core seam first.
        var attachResult = BlueprintInstanceService.AttachToEntity(
            _repo, _registry, FakeBpA_Id, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, attachResult.Status);

        // Publish replace event via action method.
        var dto = new ReplaceInstanceBlueprintParams
        {
            OldBlueprintId = FakeBpA_Id,
            NewBlueprintId = FakeBpB_Id,
            TargetEntityPacked = 0,
        };
        var status = BlueprintLifecycleLibrary.ReplaceInstanceBlueprint(ref dto, entity, _repo);
        Assert.Equal(NodeStatus.Success, status);

        // Advance bus + execute ingress system.
        var sys = new BlueprintEventIngressSystem(_registry);
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

    // ── Test: DTOs are blittable value types ────────────────────────────────

    [Fact]
    public void AttachInstanceBlueprintParams_IsValueType()
    {
        Assert.True(typeof(AttachInstanceBlueprintParams).IsValueType);
    }

    [Fact]
    public void RemoveInstanceBlueprintParams_IsValueType()
    {
        Assert.True(typeof(RemoveInstanceBlueprintParams).IsValueType);
    }

    [Fact]
    public void ReplaceInstanceBlueprintParams_IsValueType()
    {
        Assert.True(typeof(ReplaceInstanceBlueprintParams).IsValueType);
    }

    [Fact]
    public void AttachInstanceBlueprintSlot_IsValueType()
    {
        Assert.True(typeof(AttachInstanceBlueprintSlot).IsValueType);
    }

    [Fact]
    public void RemoveInstanceBlueprintSlot_IsValueType()
    {
        Assert.True(typeof(RemoveInstanceBlueprintSlot).IsValueType);
    }

    [Fact]
    public void ReplaceInstanceBlueprintSlot_IsValueType()
    {
        Assert.True(typeof(ReplaceInstanceBlueprintSlot).IsValueType);
    }
}
