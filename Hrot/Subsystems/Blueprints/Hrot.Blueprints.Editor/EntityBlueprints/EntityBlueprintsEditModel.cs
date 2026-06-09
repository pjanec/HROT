using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Events;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Hrot.Blueprints.Editor.EntityBlueprints;

public enum UsageStatus { Ok, UpgradeNeeded, OverCeiling }

public readonly record struct Projection(
    int Slots, int Bytes, BlackboardTier Tier, UsageStatus Status);

public enum CommitTiming { Paused, Running }

public sealed class CommitPlan
{
    public BlackboardTier? UpgradeToTier { get; set; }
    public List<int> DetachBlueprintIds { get; } = new();
    public List<int> AttachBlueprintIds { get; } = new();
    public List<RemoveInstanceBlueprintEvent> RemoveEvents { get; } = new();
    public List<AttachInstanceBlueprintEvent> AttachEvents { get; } = new();
}

/// <summary>
/// Headless view-model for the "Entity Blueprints" authoring panel.
/// Reality = per-frame scan across all three blackboard tiers.
/// Staging = simple Adds/Removes sets — no Intent, no LoadFromReality complexity.
/// </summary>
public sealed class EntityBlueprintsEditModel
{
    private readonly EntityRepository _repo;
    private readonly BlueprintRegistry _registry;
    private Entity _entity;

    public List<SlotSummary> Reality { get; } = new();
    public HashSet<Guid> StagedRemoves { get; } = new();
    public List<Guid> StagedAdds { get; } = new();

    public bool HasStagedChanges => StagedRemoves.Count > 0 || StagedAdds.Count > 0;

    public EntityBlueprintsEditModel(EntityRepository repo, BlueprintRegistry registry, Entity entity)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _entity = entity;
    }

    public void SetEntity(Entity entity)
    {
        if (_entity != entity)
        {
            _entity = entity;
            StagedRemoves.Clear();
            StagedAdds.Clear();
        }
    }

    public Entity GetEntity() => _entity;
    public bool HasValidEntity => _entity != default;

    // ── Staging ──────────────────────────────────────────────────────────────

    public void StageRemove(Guid assetId)
    {
        // Toggle: if already staged for removal, restore it.
        // If currently staged as an add, cancel the add instead.
        if (StagedAdds.Contains(assetId))
        {
            StagedAdds.Remove(assetId);
            return;
        }
        if (StagedRemoves.Contains(assetId))
            StagedRemoves.Remove(assetId);
        else
            StagedRemoves.Add(assetId);
    }

    public void StageAdd(Guid assetId)
    {
        if (StagedRemoves.Contains(assetId))
        {
            // Adding something marked for removal = restore it
            StagedRemoves.Remove(assetId);
            return;
        }
        if (!StagedAdds.Contains(assetId))
            StagedAdds.Add(assetId);
    }

    public void CancelAdd(Guid assetId) => StagedAdds.Remove(assetId);

    public void RevertAll()
    {
        StagedRemoves.Clear();
        StagedAdds.Clear();
    }

    // ── Reality ──────────────────────────────────────────────────────────────

    public unsafe void RefreshReality()
    {
        Reality.Clear();
        if (_repo.HasComponent<BlueprintBlackboard1024>(_entity))
        {
            ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard1024>(_entity);
            byte* mem = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb));
            BlueprintTierSummary.AppendSlots(mem, _registry, Reality);
        }
        if (_repo.HasComponent<BlueprintBlackboard4096>(_entity))
        {
            ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard4096>(_entity);
            byte* mem = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard4096, byte>(ref bb));
            BlueprintTierSummary.AppendSlots(mem, _registry, Reality);
        }
        if (_repo.HasComponent<BlueprintBlackboard16384>(_entity))
        {
            ref var bb = ref _repo.GetComponentRW<BlueprintBlackboard16384>(_entity);
            byte* mem = (byte*)Unsafe.AsPointer(ref Unsafe.As<BlueprintBlackboard16384, byte>(ref bb));
            BlueprintTierSummary.AppendSlots(mem, _registry, Reality);
        }
    }

    // ── Projection ───────────────────────────────────────────────────────────

    public Projection ComputeProjection()
    {
        int totalSlots = Reality.Count - StagedRemoves.Count(r => Reality.Any(s => s.AssetId == r))
                         + StagedAdds.Count(a => !Reality.Any(s => s.AssetId == a));

        int totalBytes = Reality.Where(s => !StagedRemoves.Contains(s.AssetId)).Sum(s => s.PayloadSize);
        foreach (var assetId in StagedAdds)
        {
            if (Reality.Any(s => s.AssetId == assetId)) continue;
            int bpId = BlueprintIdHash.Compute(assetId);
            if (_registry.TryGetById(bpId, out var def) && def != null)
                totalBytes += def.StateSize;
        }

        BlackboardTier tier = ChooseTierFromAggregate(totalSlots, totalBytes);
        UsageStatus status = UsageStatus.Ok;
        if (totalSlots > BlueprintBlackboard16384.MaxSlots || totalBytes > BlueprintBlackboard16384.PayloadSize)
        {
            tier = BlackboardTier.B16384;
            status = UsageStatus.OverCeiling;
        }
        else
        {
            BlackboardTier currentTier = GetCurrentTier();
            if (tier > currentTier) status = UsageStatus.UpgradeNeeded;
        }

        return new Projection(totalSlots, totalBytes, tier, status);
    }

    // ── Commit plan ──────────────────────────────────────────────────────────

    public CommitPlan BuildCommitPlan(CommitTiming timing)
    {
        var plan = new CommitPlan();

        if (timing == CommitTiming.Paused)
        {
            var proj = ComputeProjection();
            BlackboardTier currentTier = GetCurrentTier();
            if (proj.Tier > currentTier)
                plan.UpgradeToTier = proj.Tier;

            foreach (var assetId in StagedRemoves)
                plan.DetachBlueprintIds.Add(BlueprintIdHash.Compute(assetId));

            foreach (var assetId in StagedAdds)
            {
                if (Reality.Any(s => s.AssetId == assetId)) continue; // already attached
                plan.AttachBlueprintIds.Add(BlueprintIdHash.Compute(assetId));
            }
        }
        else
        {
            foreach (var assetId in StagedRemoves)
                plan.RemoveEvents.Add(new RemoveInstanceBlueprintEvent
                {
                    Entity = _entity,
                    BlueprintId = BlueprintIdHash.Compute(assetId),
                });

            foreach (var assetId in StagedAdds)
            {
                if (Reality.Any(s => s.AssetId == assetId)) continue;
                plan.AttachEvents.Add(new AttachInstanceBlueprintEvent
                {
                    Entity = _entity,
                    BlueprintId = BlueprintIdHash.Compute(assetId),
                });
            }
        }

        return plan;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    public BlackboardTier GetCurrentTier()
    {
        if (_repo.HasComponent<BlueprintBlackboard16384>(_entity)) return BlackboardTier.B16384;
        if (_repo.HasComponent<BlueprintBlackboard4096>(_entity)) return BlackboardTier.B4096;
        return BlackboardTier.B1024;
    }

    public static BlackboardTier ChooseTierFromAggregate(int totalSlots, int totalBytes)
    {
        if (totalSlots <= BlueprintBlackboard1024.MaxSlots && totalBytes <= BlueprintBlackboard1024.PayloadSize)
            return BlackboardTier.B1024;
        if (totalSlots <= BlueprintBlackboard4096.MaxSlots && totalBytes <= BlueprintBlackboard4096.PayloadSize)
            return BlackboardTier.B4096;
        return BlackboardTier.B16384;
    }

    public string? GetBlueprintName(Guid assetId)
    {
        int bpId = BlueprintIdHash.Compute(assetId);
        return _registry.TryGetById(bpId, out var def) && def != null ? def.Name : null;
    }
}
