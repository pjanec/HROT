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

public sealed class EntityBlueprintsEditModelTests : IDisposable
{
    private readonly EntityRepository _repo;
    private BlueprintRegistry _registry;
    private int _bpIdA, _bpIdB, _bpIdC;
    private readonly Guid _assetIdA, _assetIdB, _assetIdC;

    public EntityBlueprintsEditModelTests()
    {
        _repo = new EntityRepository();
        BlueprintRuntimeWiring.RegisterTierComponents(_repo);
        _repo.RegisterManagedComponent<InitialBlueprintsIntent>();
        _registry = new BlueprintRegistry();

        _assetIdA = new Guid("00000000-0000-0000-0000-000000000001");
        _assetIdB = new Guid("00000000-0000-0000-0000-000000000002");
        _assetIdC = new Guid("00000000-0000-0000-0000-000000000003");

        _bpIdA = RegisterTestBlueprint("TestBp_A", _assetIdA, stateSize: 64);
        _bpIdB = RegisterTestBlueprint("TestBp_B", _assetIdB, stateSize: 64);
        _bpIdC = RegisterTestBlueprint("TestBp_C", _assetIdC, stateSize: 64);
    }

    public void Dispose() => _repo.Dispose();

    private int RegisterTestBlueprint(string name, Guid assetId, int stateSize = 16)
        => RegisterTestBlueprintIn(_registry, name, assetId, stateSize);

    private static int RegisterTestBlueprintIn(BlueprintRegistry reg, string name, Guid assetId, int stateSize)
    {
        int bpId = BlueprintIdHash.Compute(assetId);
        reg.RegisterInstance(bpId, new BlueprintDefinition
        {
            Name = name, Kind = BlueprintDispatchKind.Instance,
            StructureHash = (ulong)bpId, StateSize = stateSize,
            AssetId = assetId, InitDefault = span => span.Clear(),
        });
        return bpId;
    }

    private Entity CreateEntity()
    {
        var e = _repo.CreateEntity();
        _repo.AddComponent(e, default(BlueprintBlackboard1024));
        return e;
    }

    private EntityBlueprintsEditModel CreateModel(Entity entity)
        => new(_repo, _registry, entity);

    private static unsafe int GetSlotCount(EntityRepository repo, Entity entity)
    {
        int total = 0;
        if (repo.HasComponent<BlueprintBlackboard1024>(entity))
        { ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity); byte* m = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb)); total += BlueprintBlackboardPartitions.GetSlotCount(m); }
        if (repo.HasComponent<BlueprintBlackboard4096>(entity))
        { ref var bb = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity); byte* m = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb)); total += BlueprintBlackboardPartitions.GetSlotCount(m); }
        if (repo.HasComponent<BlueprintBlackboard16384>(entity))
        { ref var bb = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity); byte* m = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb)); total += BlueprintBlackboardPartitions.GetSlotCount(m); }
        return total;
    }

    private static unsafe bool HasBlueprintSlot(EntityRepository repo, Entity entity, int blueprintId)
    {
        if (repo.HasComponent<BlueprintBlackboard1024>(entity))
        { ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity); byte* m = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb)); if (BlueprintBlackboardPartitions.TryGetSlotOffset(m, blueprintId, out _)) return true; }
        if (repo.HasComponent<BlueprintBlackboard4096>(entity))
        { ref var bb = ref repo.GetComponentRW<BlueprintBlackboard4096>(entity); byte* m = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb)); if (BlueprintBlackboardPartitions.TryGetSlotOffset(m, blueprintId, out _)) return true; }
        if (repo.HasComponent<BlueprintBlackboard16384>(entity))
        { ref var bb = ref repo.GetComponentRW<BlueprintBlackboard16384>(entity); byte* m = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb)); if (BlueprintBlackboardPartitions.TryGetSlotOffset(m, blueprintId, out _)) return true; }
        return false;
    }

    // ═══ Test 1: Reality ═══════════════════════════════════════════════════════

    [Fact]
    public void RefreshReality_TwoBlueprints_Count2_NamesCorrect()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);

        var model = CreateModel(entity);
        model.RefreshReality();
        Assert.Equal(2, model.Reality.Count);
        Assert.Contains(model.Reality, s => s.BlueprintId == _bpIdA && s.Name == "TestBp_A");
        Assert.Contains(model.Reality, s => s.BlueprintId == _bpIdB && s.Name == "TestBp_B");
    }

    [Fact]
    public void RefreshReality_Idempotent()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        var model = CreateModel(entity);
        model.RefreshReality();
        int c = model.Reality.Count;
        model.RefreshReality();
        Assert.Equal(c, model.Reality.Count);
    }

    // ═══ Test 2: Staging + no live mutation ════════════════════════════════════

    [Fact]
    public void StageRemove_AddsToStagedRemoves()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        var model = CreateModel(entity);
        model.RefreshReality();

        model.StageRemove(_assetIdA);
        Assert.Contains(_assetIdA, model.StagedRemoves);
        Assert.True(model.HasStagedChanges);
    }

    [Fact]
    public void StageRemove_Toggle_RestoresWhenClickedAgain()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        var model = CreateModel(entity);
        model.RefreshReality();

        model.StageRemove(_assetIdA);
        Assert.Contains(_assetIdA, model.StagedRemoves);

        model.StageRemove(_assetIdA); // toggle back
        Assert.DoesNotContain(_assetIdA, model.StagedRemoves);
        Assert.False(model.HasStagedChanges);
    }

    [Fact]
    public void StageAdd_AddedBlueprint_AppearsInStagedAdds()
    {
        var entity = CreateEntity();
        var model = CreateModel(entity);
        model.RefreshReality();

        model.StageAdd(_assetIdA);
        Assert.Contains(_assetIdA, model.StagedAdds);
        Assert.True(model.HasStagedChanges);
    }

    [Fact]
    public void StageAdd_ThenCancelAdd_RemovesIt()
    {
        var entity = CreateEntity();
        var model = CreateModel(entity);
        model.RefreshReality();

        model.StageAdd(_assetIdA);
        model.CancelAdd(_assetIdA);
        Assert.DoesNotContain(_assetIdA, model.StagedAdds);
        Assert.False(model.HasStagedChanges);
    }

    [Fact]
    public void Staging_DoesNotMutateLiveMemory()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        int slotsBefore = GetSlotCount(_repo, entity);

        var model = CreateModel(entity);
        model.RefreshReality();
        model.StageRemove(_assetIdA);
        model.StageAdd(_assetIdB);

        Assert.Equal(slotsBefore, GetSlotCount(_repo, entity));
        Assert.True(HasBlueprintSlot(_repo, entity, _bpIdA)); // still there
    }

    // ═══ Test 3: Projection Ok ════════════════════════════════════════════════

    [Fact]
    public void ComputeProjection_ThreeBlueprints_Ok_B1024()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdC, entity);
        var model = CreateModel(entity);
        model.RefreshReality();

        var proj = model.ComputeProjection();
        Assert.Equal(UsageStatus.Ok, proj.Status);
        Assert.Equal(BlackboardTier.B1024, proj.Tier);
        Assert.Equal(3, proj.Slots);
    }

    // ═══ Test 4: Projection UpgradeNeeded ══════════════════════════════════════

    [Fact]
    public void ComputeProjection_PayloadOverflow_UpgradeNeeded_B4096()
    {
        _registry = new BlueprintRegistry();
        _bpIdA = RegisterTestBlueprint("Big_A", _assetIdA, 250);
        _bpIdB = RegisterTestBlueprint("Big_B", _assetIdB, 250);
        _bpIdC = RegisterTestBlueprint("Big_C", _assetIdC, 250);

        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdC, entity);

        var model = CreateModel(entity);
        model.RefreshReality();
        // Stage 2 more: 5 * 250 = 1250 > 928
        var g4 = new Guid("00000000-0000-0000-4000-000000000004");
        var g5 = new Guid("00000000-0000-0000-4000-000000000005");
        RegisterTestBlueprint("Big_D", g4, 250);
        RegisterTestBlueprint("Big_E", g5, 250);
        model.StageAdd(g4);
        model.StageAdd(g5);

        var proj = model.ComputeProjection();
        Assert.Equal(UsageStatus.UpgradeNeeded, proj.Status);
        Assert.Equal(BlackboardTier.B4096, proj.Tier);
        Assert.Equal(5, proj.Slots);
    }

    // ═══ Test 5: Projection OverCeiling ════════════════════════════════════════

    [Fact]
    public void ComputeProjection_20Adds_OverCeiling()
    {
        var entity = CreateEntity();
        var model = CreateModel(entity);
        model.RefreshReality();
        for (int i = 0; i < 20; i++)
        {
            var g = new Guid($"A0000000-0000-0000-0000-{i:X12}");
            RegisterTestBlueprint($"Bp_{i}", g, 16);
            model.StageAdd(g);
        }
        var proj = model.ComputeProjection();
        Assert.Equal(UsageStatus.OverCeiling, proj.Status);
    }

    // ═══ Test 6: RevertAll ════════════════════════════════════════════════════

    [Fact]
    public void RevertAll_ClearsEverything()
    {
        var entity = CreateEntity();
        var model = CreateModel(entity);
        model.RefreshReality();
        model.StageAdd(_assetIdA);
        model.StageAdd(_assetIdB);
        Assert.True(model.HasStagedChanges);

        model.RevertAll();
        Assert.False(model.HasStagedChanges);
        Assert.Empty(model.StagedAdds);
        Assert.Empty(model.StagedRemoves);
    }

    // ═══ Test 7: Paused commit plan ════════════════════════════════════════════

    [Fact]
    public void BuildCommitPlan_Paused_RemoveAndAdd()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);
        var model = CreateModel(entity);
        model.RefreshReality();

        model.StageRemove(_assetIdB);
        model.StageAdd(_assetIdC);

        var plan = model.BuildCommitPlan(CommitTiming.Paused);
        Assert.Null(plan.UpgradeToTier);
        Assert.Single(plan.DetachBlueprintIds);
        Assert.Equal(_bpIdB, plan.DetachBlueprintIds[0]);
        Assert.Single(plan.AttachBlueprintIds);
        Assert.Equal(_bpIdC, plan.AttachBlueprintIds[0]);
    }

    [Fact]
    public void BuildCommitPlan_Paused_UpgradeTier()
    {
        _registry = new BlueprintRegistry();
        _bpIdA = RegisterTestBlueprint("Big_A", _assetIdA, 350);
        _bpIdB = RegisterTestBlueprint("Big_B", _assetIdB, 350);
        _bpIdC = RegisterTestBlueprint("Big_C", _assetIdC, 350);
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);
        var model = CreateModel(entity);
        model.RefreshReality();
        model.StageAdd(_assetIdC);

        var plan = model.BuildCommitPlan(CommitTiming.Paused);
        Assert.NotNull(plan.UpgradeToTier);
        Assert.Equal(BlackboardTier.B4096, plan.UpgradeToTier!.Value);
    }

    // ═══ Test 8: Paused commit + tier upgrade ══════════════════════════════════

    [Fact]
    public unsafe void PausedCommit_Upgrade_OldTierRemoved_SlotsPreserved()
    {
        var reg = new BlueprintRegistry();
        var g1 = new Guid("00000000-0000-0000-8000-000000000001");
        var g2 = new Guid("00000000-0000-0000-8000-000000000002");
        var g3 = new Guid("00000000-0000-0000-8000-000000000003");
        int b1 = RegisterTestBlueprintIn(reg, "Big_1", g1, 350);
        int b2 = RegisterTestBlueprintIn(reg, "Big_2", g2, 350);
        int b3 = RegisterTestBlueprintIn(reg, "Big_3", g3, 350);

        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, default(BlueprintBlackboard1024));
        BlueprintInstanceService.AttachToEntity(_repo, reg, b1, entity);
        BlueprintInstanceService.AttachToEntity(_repo, reg, b2, entity);

        // Upgrade: add B4096, copy, remove B1024
        _repo.AddComponent(entity, default(BlueprintBlackboard4096));
        {
            ref var oldBb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(entity);
            ref var newBb = ref _repo.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* src = oldBb.Memory) fixed (byte* dst = newBb.Memory)
                BlueprintBlackboardPartitions.CopyToLargerTier(src, BlueprintBlackboard1024.TotalSize, dst, BlueprintBlackboard4096.TotalSize, BlueprintBlackboard4096.MaxSlots);
        }
        _repo.RemoveComponent<BlueprintBlackboard1024>(entity);

        Assert.False(_repo.HasComponent<BlueprintBlackboard1024>(entity));
        Assert.True(_repo.HasComponent<BlueprintBlackboard4096>(entity));
        Assert.Equal(2, GetSlotCount(_repo, entity));

        BlueprintInstanceService.AttachToEntity(_repo, reg, b3, entity);
        Assert.Equal(3, GetSlotCount(_repo, entity));
        Assert.True(HasBlueprintSlot(_repo, entity, b1));
        Assert.True(HasBlueprintSlot(_repo, entity, b2));
        Assert.True(HasBlueprintSlot(_repo, entity, b3));
    }

    // ═══ Test 9: Running commit plan ═══════════════════════════════════════════

    [Fact]
    public void BuildCommitPlan_Running_RemoveThenAttach()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        var model = CreateModel(entity);
        model.RefreshReality();

        model.StageRemove(_assetIdA);
        model.StageAdd(_assetIdB);

        var plan = model.BuildCommitPlan(CommitTiming.Running);
        Assert.Single(plan.RemoveEvents);
        Assert.Equal(_bpIdA, plan.RemoveEvents[0].BlueprintId);
        Assert.Single(plan.AttachEvents);
        Assert.Equal(_bpIdB, plan.AttachEvents[0].BlueprintId);
    }

    [Fact]
    public void BuildCommitPlan_Running_NoLiveMutation()
    {
        var entity = CreateEntity();
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        int slotsBefore = GetSlotCount(_repo, entity);

        var model = CreateModel(entity);
        model.RefreshReality();
        model.StageRemove(_assetIdA);
        model.StageAdd(_assetIdB);
        model.BuildCommitPlan(CommitTiming.Running);

        Assert.Equal(slotsBefore, GetSlotCount(_repo, entity));
        Assert.True(HasBlueprintSlot(_repo, entity, _bpIdA));
    }

    // ═══ Test 10: Invariant (§2) ══════════════════════════════════════════════

    [Fact]
    public void Extract_AfterAttach_ExactAssetIds_NoOverrides()
    {
        var entity = _repo.CreateEntity();
        _repo.AddComponent(entity, default(BlueprintBlackboard1024));
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdA, entity);
        BlueprintInstanceService.AttachToEntity(_repo, _registry, _bpIdB, entity);

        var model = CreateModel(entity);
        model.RefreshReality();
        Assert.Equal(2, model.Reality.Count);

        var translator = new BlueprintStateTranslator(_registry);
        var extract = translator.Extract(_repo, entity, null!);
        Assert.True(extract.ContainsKey("BlueprintAssignments"));
        var assignments = (List<Dictionary<string, object>>)extract["BlueprintAssignments"];
        Assert.Equal(2, assignments.Count);

        var ids = assignments.Select(a => Guid.Parse((string)a["AssetId"])).ToHashSet();
        Assert.Contains(_assetIdA, ids);
        Assert.Contains(_assetIdB, ids);
        foreach (var a in assignments)
            Assert.False(a.ContainsKey("Overrides"));
    }
}
