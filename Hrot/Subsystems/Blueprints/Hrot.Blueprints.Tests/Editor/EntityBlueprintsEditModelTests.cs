using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Editor.EntityBlueprints;
using Hrot.Blueprints.Editor.Runtime;
using Hrot.Common.Serializers;
using Hrot.SimHost.Serializers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// Unit tests for <see cref="EntityBlueprintsEditModel"/> — BSA-205 headless view-model.
/// Per TASK-DETAIL header rule 3: all tests assert on the model, not ImGui.
/// Covers all 10 success conditions from BATCH-07.
/// </summary>
public sealed class EntityBlueprintsEditModelTests : IDisposable
{
    private readonly EntityRepository _repo;
    private BlueprintRegistry _registry;

    // Pre-registered test blueprint ids.
    private int _bpIdA, _bpIdB, _bpIdC;
    private readonly Guid _assetIdA, _assetIdB, _assetIdC;

    public EntityBlueprintsEditModelTests()
    {
        _repo = new EntityRepository();
        BlueprintRuntimeWiring.RegisterTierComponents(_repo);
        _repo.RegisterManagedComponent<InitialBlueprintsIntent>();

        _registry = new BlueprintRegistry();

        // Pre-register three small Instance blueprints with deterministic AssetIds.
        _assetIdA = new Guid("00000000-0000-0000-0000-000000000001");
        _assetIdB = new Guid("00000000-0000-0000-0000-000000000002");
        _assetIdC = new Guid("00000000-0000-0000-0000-000000000003");

        _bpIdA = RegisterTestBlueprint("TestBp_A", _assetIdA, stateSize: 64);
        _bpIdB = RegisterTestBlueprint("TestBp_B", _assetIdB, stateSize: 64);
        _bpIdC = RegisterTestBlueprint("TestBp_C", _assetIdC, stateSize: 64);
    }

    public void Dispose() => _repo.Dispose();

    // ── helpers ──────────────────────────────────────────────────────────────

    private int RegisterTestBlueprint(string name, Guid assetId, int stateSize = 16)
    {
        int bpId = BlueprintIdHash.Compute(assetId);
        var def = new BlueprintDefinition
        {
            Name = name,
            Kind = BlueprintDispatchKind.Instance,
            StructureHash = (ulong)bpId,
            StateSize = stateSize,
            AssetId = assetId,
            InitDefault = span => span.Clear(),
        };
        _registry.RegisterInstance(bpId, def);
        return bpId;
    }

    private int RegisterTestBlueprintIn(
        BlueprintRegistry reg, string name, Guid assetId, int stateSize)
    {
        int bpId = BlueprintIdHash.Compute(assetId);
        var def = new BlueprintDefinition
        {
            Name = name,
            Kind = BlueprintDispatchKind.Instance,
            StructureHash = (ulong)bpId,
            StateSize = stateSize,
            AssetId = assetId,
            InitDefault = span => span.Clear(),
        };
        reg.RegisterInstance(bpId, def);
        return bpId;
    }

    private Entity CreateEntity()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, default(BlueprintBlackboard1024));
        return entity;
    }

    private EntityBlueprintsEditModel CreateModel(Entity entity)
        => new EntityBlueprintsEditModel(_repo, _registry, entity);

    private static unsafe int GetSlotCount(EntityRepository repo, Entity entity)
    {
        int total = 0;
        if (repo.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
            byte* mem = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
            total += BlueprintBlackboardPartitions.GetSlotCount(mem);
        }
        if (repo.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var bb = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
            byte* mem = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb));
            total += BlueprintBlackboardPartitions.GetSlotCount(mem);
        }
        if (repo.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var bb = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
            byte* mem = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb));
            total += BlueprintBlackboardPartitions.GetSlotCount(mem);
        }
        return total;
    }

    private static unsafe bool HasBlueprintSlot(
        EntityRepository repo, Entity entity, int blueprintId)
    {
        if (repo.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
            byte* mem = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(mem, blueprintId, out _))
                return true;
        }
        if (repo.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var bb = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity);
            byte* mem = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb));
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(mem, blueprintId, out _))
                return true;
        }
        if (repo.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var bb = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity);
            byte* mem = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb));
            if (BlueprintBlackboardPartitions.TryGetSlotOffset(mem, blueprintId, out _))
                return true;
        }
        return false;
    }

    private static unsafe void AssertSlotsContainExactly(
        EntityRepository repo, Entity entity, int[] expectedBlueprintIds)
    {
        var found = new HashSet<int>();
        CollectSlots(repo, entity, found);
        Assert.Equal(expectedBlueprintIds.Length, found.Count);
        foreach (int expectedId in expectedBlueprintIds)
            Assert.True(found.Contains(expectedId),
                $"Expected BlueprintId 0x{expectedId:X8} not found in slot table.");
    }

    private static unsafe void CollectSlots(
        EntityRepository repo, Entity entity, HashSet<int> target)
    {
        if (repo.HasComponent<BlueprintBlackboard1024>(entity))
            CollectTierSlots<BlueprintBlackboard1024>(repo, entity, target);
        if (repo.HasComponent<BlueprintBlackboard4096>(entity))
            CollectTierSlots<BlueprintBlackboard4096>(repo, entity, target);
        if (repo.HasComponent<BlueprintBlackboard16384>(entity))
            CollectTierSlots<BlueprintBlackboard16384>(repo, entity, target);
    }

    private static unsafe void CollectTierSlots<T>(
        EntityRepository repo, Entity entity, HashSet<int> target)
        where T : unmanaged
    {
        ref var bb = ref repo.GetComponentRW<T>(entity);
        byte* bytes = (byte*)Unsafe.AsPointer(ref Unsafe.As<T, byte>(ref bb));
        int count = BlueprintBlackboardPartitions.GetSlotCount(bytes);
        for (int i = 0; i < count; i++)
        {
            ref var slot = ref BlueprintBlackboardPartitions.GetSlot(bytes, i);
            if (slot.BlueprintId != 0)
                target.Add(slot.BlueprintId);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 1: Reality
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RefreshReality_TwoBlueprintsAttached_ReturnsCorrectCountAndNames()
    {
        var entity = CreateEntity();
        var r1 = BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        var r2 = BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, r1.Status);
        Assert.Equal(BlueprintAttachStatus.Attached, r2.Status);

        var model = CreateModel(entity);
        model.RefreshReality();

        Assert.Equal(2, model.Reality.Count);
        Assert.Contains(model.Reality, s => s.BlueprintId == _bpIdA && s.Name == "TestBp_A");
        Assert.Contains(model.Reality, s => s.BlueprintId == _bpIdB && s.Name == "TestBp_B");
    }

    [Fact]
    public void RefreshReality_CalledTwice_IsIdempotent()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);

        var model = CreateModel(entity);
        model.RefreshReality();
        int firstCount = model.Reality.Count;

        model.RefreshReality();
        Assert.Equal(firstCount, model.Reality.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 2: Diff staging
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeDiff_StageOneAddOneRemove_ReturnsCorrectDiff()
    {
        var entity = CreateEntity();
        // Attach A and B
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);

        var model = CreateModel(entity);
        model.RefreshReality();

        // Load Reality into Intent as the baseline desired state.
        model.LoadIntentFromReality();
        Assert.Equal(2, model.Intent.Count);

        // Stage: add C, remove A
        model.StageAdd(new BlueprintAssignmentDto { AssetId = _assetIdC });
        model.StageRemove(new BlueprintAssignmentDto { AssetId = _assetIdA });

        var diff = model.ComputeDiff();

        Assert.Equal(1, diff.Added.Count);
        Assert.Equal(_assetIdC, diff.Added[0].AssetId);
        Assert.Equal(1, diff.Removed.Count);
        Assert.Equal(_assetIdA, diff.Removed[0].AssetId);
    }

    [Fact]
    public void Staging_DoesNotMutateLiveMemory()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);

        int slotsBefore = GetSlotCount(_repo, entity);
        Assert.Equal(2, slotsBefore);

        var model = CreateModel(entity);
        model.RefreshReality();
        model.LoadIntentFromReality();

        // Stage changes
        model.StageAdd(new BlueprintAssignmentDto { AssetId = _assetIdC });
        model.StageRemove(new BlueprintAssignmentDto { AssetId = _assetIdA });

        // Live memory must be unchanged
        int slotsAfter = GetSlotCount(_repo, entity);
        Assert.Equal(slotsBefore, slotsAfter);

        model.ComputeDiff();
        int slotsAfterDiff = GetSlotCount(_repo, entity);
        Assert.Equal(slotsBefore, slotsAfterDiff);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 3: Projection Ok
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeProjection_ThreeSmallBlueprints_StatusOk_Tier1024()
    {
        var entity = CreateEntity();
        var ra = BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        var rb = BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);
        var rc = BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdC, entity);

        Assert.Equal(BlueprintAttachStatus.Attached, ra.Status);
        Assert.Equal(BlueprintAttachStatus.Attached, rb.Status);
        Assert.Equal(BlueprintAttachStatus.Attached, rc.Status);

        // Verify slot count directly
        Assert.Equal(3, GetSlotCount(_repo, entity));

        var model = CreateModel(entity);
        model.RefreshReality();

        // Verify model sees the blueprints
        Assert.Equal(3, model.Reality.Count);

        var proj = model.ComputeProjection();

        Assert.Equal(UsageStatus.Ok, proj.Status);
        Assert.Equal(BlackboardTier.B1024, proj.Tier);
        Assert.Equal(3, proj.Slots);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 4: Projection UpgradeNeeded
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeProjection_OverflowPayload_StatusUpgradeNeeded_Tier4096()
    {
        // Re-register blueprints with larger state to overflow B1024 payload.
        _registry = new BlueprintRegistry();
        _bpIdA = RegisterTestBlueprint("BigBp_A", _assetIdA, stateSize: 250);
        _bpIdB = RegisterTestBlueprint("BigBp_B", _assetIdB, stateSize: 250);
        _bpIdC = RegisterTestBlueprint("BigBp_C", _assetIdC, stateSize: 250);

        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdC, entity);
        // 3 * 250 = 750 bytes <= 928, 3 slots <= 4 → current tier is B1024
        Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));
        Assert.Equal(3, GetSlotCount(_repo, entity));

        var model = CreateModel(entity);
        model.RefreshReality();
        model.LoadIntentFromReality();

        // Stage two more: 5 * 250 = 1250 > 928 payload AND 5 > 4 slots
        var assetIdD = new Guid("00000000-0000-0000-4000-000000000004");
        var assetIdE = new Guid("00000000-0000-0000-4000-000000000005");
        RegisterTestBlueprint("BigBp_D", assetIdD, stateSize: 250);
        RegisterTestBlueprint("BigBp_E", assetIdE, stateSize: 250);
        model.StageAdd(new BlueprintAssignmentDto { AssetId = assetIdD });
        model.StageAdd(new BlueprintAssignmentDto { AssetId = assetIdE });

        var proj = model.ComputeProjection();

        Assert.Equal(UsageStatus.UpgradeNeeded, proj.Status);
        Assert.Equal(BlackboardTier.B4096, proj.Tier);
        Assert.Equal(5, proj.Slots);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 5: Projection OverCeiling
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeProjection_Stage20Blueprints_StatusOverCeiling()
    {
        var entity = CreateEntity();
        var model = CreateModel(entity);
        model.RefreshReality();

        // Stage 20 blueprint adds (exceeds B16384 MaxSlots of 16).
        // Use AssetIds spread across the GUID space to avoid hash collisions.
        for (int i = 0; i < 20; i++)
        {
            var assetId = new Guid($"A0000000-0000-0000-0000-{i:X12}");
            RegisterTestBlueprint($"OverflowBp_{i}", assetId, stateSize: 16);
            model.StageAdd(new BlueprintAssignmentDto { AssetId = assetId });
        }

        var proj = model.ComputeProjection();

        Assert.Equal(UsageStatus.OverCeiling, proj.Status);
        Assert.True(proj.Slots > BlueprintBlackboard16384.MaxSlots,
            $"Expected slots ({proj.Slots}) > {BlueprintBlackboard16384.MaxSlots}");
        Assert.Equal(BlackboardTier.B16384, proj.Tier);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 6: RevertAll
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RevertAll_ClearsIntent_And_DiffIsEmpty()
    {
        var entity = CreateEntity();
        var model = CreateModel(entity);
        model.RefreshReality();

        // Stage 2 adds (no Reality items to start from)
        model.StageAdd(new BlueprintAssignmentDto { AssetId = _assetIdA });
        model.StageAdd(new BlueprintAssignmentDto { AssetId = _assetIdB });
        Assert.Equal(2, model.Intent.Count);

        model.RevertAll();

        Assert.Empty(model.Intent);
        var diff = model.ComputeDiff();
        Assert.Empty(diff.Added);
        Assert.Empty(diff.Removed);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 7: Paused commit plan
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildCommitPlan_Paused_CorrectDetachAndAttachLists()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdC, entity);

        var model = CreateModel(entity);
        model.RefreshReality();
        model.LoadIntentFromReality();
        Assert.Equal(3, model.Intent.Count);

        // Desire: replace B with D (same size, no tier change)
        var assetIdD = new Guid("00000000-0000-0000-7000-000000000004");
        int bpIdD = RegisterTestBlueprint("PausedD", assetIdD, stateSize: 64);
        model.StageRemove(new BlueprintAssignmentDto { AssetId = _assetIdB });
        model.StageAdd(new BlueprintAssignmentDto { AssetId = assetIdD });

        var plan = model.BuildCommitPlan(CommitTiming.Paused);

        Assert.Null(plan.UpgradeToTier); // No upgrade needed
        Assert.Single(plan.DetachBlueprintIds);
        Assert.Equal(_bpIdB, plan.DetachBlueprintIds[0]);
        Assert.Single(plan.AttachBlueprintIds);
        Assert.Equal(bpIdD, plan.AttachBlueprintIds[0]);
    }

    [Fact]
    public void BuildCommitPlan_Paused_UpgradeToTierWhenNeeded()
    {
        // Re-register with larger blueprints.
        _registry = new BlueprintRegistry();
        _bpIdA = RegisterTestBlueprint("Big_A", _assetIdA, stateSize: 350);
        _bpIdB = RegisterTestBlueprint("Big_B", _assetIdB, stateSize: 350);

        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);
        // 2 * 350 = 700, fits in B1024
        Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));

        var model = CreateModel(entity);
        model.RefreshReality();
        model.LoadIntentFromReality();

        // Stage add of third big blueprint: 3 * 350 = 1050 > 928
        _bpIdC = RegisterTestBlueprint("Big_C", _assetIdC, stateSize: 350);
        model.StageAdd(new BlueprintAssignmentDto { AssetId = _assetIdC });

        var proj = model.ComputeProjection();
        Assert.Equal(BlackboardTier.B4096, proj.Tier);
        Assert.Equal(UsageStatus.UpgradeNeeded, proj.Status);

        var plan = model.BuildCommitPlan(CommitTiming.Paused);
        Assert.NotNull(plan.UpgradeToTier);
        Assert.Equal(BlackboardTier.B4096, plan.UpgradeToTier!.Value);
        Assert.Single(plan.AttachBlueprintIds);
        Assert.Equal(_bpIdC, plan.AttachBlueprintIds[0]);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 8: Paused commit + tier upgrade execution
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public unsafe void PausedCommit_OverflowUpgrade_OldTierRemoved_CorrectSlots()
    {
        var reg = new BlueprintRegistry();
        var assetId1 = new Guid("00000000-0000-0000-8000-000000000001");
        var assetId2 = new Guid("00000000-0000-0000-8000-000000000002");
        var assetId3 = new Guid("00000000-0000-0000-8000-000000000003");
        int bp1 = RegisterTestBlueprintIn(reg, "Big_1", assetId1, stateSize: 350);
        int bp2 = RegisterTestBlueprintIn(reg, "Big_2", assetId2, stateSize: 350);
        int bp3 = RegisterTestBlueprintIn(reg, "Big_3", assetId3, stateSize: 350);

        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, default(BlueprintBlackboard1024));

        // Attach first two via service — fits in B1024 (2 * 350 = 700 ≤ 928)
        var r1 = BlueprintInstanceService.AttachToEntity(_repo, reg, bp1, entity);
        var r2 = BlueprintInstanceService.AttachToEntity(_repo, reg, bp2, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, r1.Status);
        Assert.Equal(BlueprintAttachStatus.Attached, r2.Status);
        Assert.True(_repo.HasComponent<BlueprintBlackboard1024>(entity));
        Assert.False(_repo.HasComponent<BlueprintBlackboard4096>(entity));
        Assert.Equal(2, GetSlotCount(_repo, entity));

        // Simulate a paused commit: tier upgrade + attach third.
        // 1. Add new tier
        _repo.AddComponent(entity, default(BlueprintBlackboard4096));

        // 2. CopyToLargerTier
        {
            ref var oldBb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
            ref var newBb = ref _repo.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* src = oldBb.Memory)
            fixed (byte* dst = newBb.Memory)
            {
                BlueprintBlackboardPartitions.CopyToLargerTier(
                    src, BlueprintBlackboard1024.TotalSize,
                    dst, BlueprintBlackboard4096.TotalSize,
                    BlueprintBlackboard4096.MaxSlots);
            }
        }

        // 3. Remove old tier (CRITICAL — else double-tick)
        _repo.RemoveComponent<BlueprintBlackboard1024>(entity);

        // Verify: exactly one (larger) tier component
        Assert.False(_repo.HasComponent<BlueprintBlackboard1024>(entity));
        Assert.True(_repo.HasComponent<BlueprintBlackboard4096>(entity));
        Assert.False(_repo.HasComponent<BlueprintBlackboard16384>(entity));

        // Slots should have been preserved during copy
        Assert.Equal(2, GetSlotCount(_repo, entity));

        // 4. Attach the third blueprint to the new tier (now B4096)
        var r3 = BlueprintInstanceService.AttachToEntity(_repo, reg, bp3, entity);
        Assert.Equal(BlueprintAttachStatus.Attached, r3.Status);

        // All three must be present across all tiers.
        Assert.Equal(3, GetSlotCount(_repo, entity));
        Assert.True(HasBlueprintSlot(_repo, entity, bp1));
        Assert.True(HasBlueprintSlot(_repo, entity, bp2));
        Assert.True(HasBlueprintSlot(_repo, entity, bp3));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 9: Running commit plan
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildCommitPlan_Running_ProducesCorrectEventOrder()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);

        var model = CreateModel(entity);
        model.RefreshReality();
        model.LoadIntentFromReality();

        // Same-size swap: remove A, add B
        model.StageRemove(new BlueprintAssignmentDto { AssetId = _assetIdA });
        model.StageAdd(new BlueprintAssignmentDto { AssetId = _assetIdB });

        var plan = model.BuildCommitPlan(CommitTiming.Running);

        Assert.Single(plan.RemoveEvents);
        Assert.Single(plan.AttachEvents);
        Assert.Equal(_bpIdA, plan.RemoveEvents[0].BlueprintId);
        Assert.Equal(_bpIdB, plan.AttachEvents[0].BlueprintId);
        Assert.Equal(entity, plan.RemoveEvents[0].Entity);
        Assert.Equal(entity, plan.AttachEvents[0].Entity);
    }

    [Fact]
    public void BuildCommitPlan_Running_DoesNotMutateLiveMemory()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);

        int slotsBefore = GetSlotCount(_repo, entity);
        Assert.Equal(1, slotsBefore);

        var model = CreateModel(entity);
        model.RefreshReality();
        model.LoadIntentFromReality();

        // Stage same-size swap
        model.StageRemove(new BlueprintAssignmentDto { AssetId = _assetIdA });
        model.StageAdd(new BlueprintAssignmentDto { AssetId = _assetIdB });

        // Build plan — should not modify the live blackboard
        var plan = model.BuildCommitPlan(CommitTiming.Running);

        int slotsAfter = GetSlotCount(_repo, entity);
        Assert.Equal(slotsBefore, slotsAfter);
        // Bp A is still there, Bp B is not
        Assert.True(HasBlueprintSlot(_repo, entity, _bpIdA));
        Assert.False(HasBlueprintSlot(_repo, entity, _bpIdB));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Test 10: Invariant (§2)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Extract_AfterAttachViaModel_ContainsExactAssetIds_NoOverrides()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, default(BlueprintBlackboard1024));

        // Attach two blueprints via core seam
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);

        // Verify Reality via model
        var model = CreateModel(entity);
        model.RefreshReality();
        Assert.Equal(2, model.Reality.Count);
        Assert.Contains(model.Reality, s => s.AssetId == _assetIdA);
        Assert.Contains(model.Reality, s => s.AssetId == _assetIdB);

        // Use BlueprintStateTranslator to extract — should produce exact AssetIds
        var translator = new BlueprintStateTranslator(_registry);
        var extract = translator.Extract(_repo, entity, null!);

        Assert.True(extract.ContainsKey("BlueprintAssignments"));
        var assignments = (List<Dictionary<string, object>>)extract["BlueprintAssignments"];
        Assert.Equal(2, assignments.Count);

        var extractedIds = assignments
            .Select(a => Guid.Parse((string)a["AssetId"]))
            .ToHashSet();
        Assert.Contains(_assetIdA, extractedIds);
        Assert.Contains(_assetIdB, extractedIds);

        // No Overrides key in the extracted assignments
        foreach (var assignment in assignments)
        {
            Assert.False(assignment.ContainsKey("Overrides"),
                $"Expected no Overrides key in assignment, but found one.");
        }
    }

    [Fact]
    public void Extract_AfterAttach_NoDriftBytes_InSlotTable()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, default(BlueprintBlackboard1024));

        // Attach two blueprints
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);

        // Model reads Reality correctly
        var model = CreateModel(entity);
        model.RefreshReality();
        Assert.Equal(2, model.Reality.Count);

        // Verify slot entries point to correct BlueprintIds (no drift)
        AssertSlotsContainExactly(_repo, entity, new[] { _bpIdA, _bpIdB });

        // Extract produces correct AssetIds
        var translator = new BlueprintStateTranslator(_registry);
        var extract = translator.Extract(_repo, entity, null!);
        var assignments = (List<Dictionary<string, object>>)extract["BlueprintAssignments"];

        var assetIds = assignments
            .Select(a => Guid.Parse((string)a["AssetId"]))
            .OrderBy(g => g.ToString())
            .ToList();

        var expected = new[] { _assetIdA, _assetIdB }.OrderBy(g => g.ToString()).ToList();
        Assert.Equal(expected, assetIds);
    }
}
