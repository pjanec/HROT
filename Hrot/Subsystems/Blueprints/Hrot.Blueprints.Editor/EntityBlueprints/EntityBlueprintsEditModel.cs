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

        // A2: the three-tier ladder, once, in OccurrenceStoreAccess.
        // ⚠ Deliberately the RW form, to preserve behaviour EXACTLY — the old code used
        //    GetComponentRW here even though it only reads, and GetComponentRW bumps the chunk
        //    version. Moving this to TryGetStoreReadOnly would be an improvement AND a behaviour
        //    change, so it is not A2's to make.
        byte* mem = Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess
                        .TryGetStore(_repo, _entity, out _);
        if (mem != null)
            BlueprintTierSummary.AppendSlots(mem, _registry, Reality);
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
        if (totalSlots > BlueprintTierTable.Largest.MaxSlots || totalBytes > BlueprintTierTable.Largest.PayloadSize)
        {
            tier = BlueprintTierTable.Largest.Tier;
            status = UsageStatus.OverCeiling;
        }
        else
        {
            BlackboardTier currentTier = GetCurrentTier();
            // ⛔⛔ B4 — §17.7: NOT `tier > currentTier`. BlackboardTier's ordinal is ABI, so B256
            //   is the LAST member and the SMALLEST tier ⇒ a DOWNGRADE compared as `greater`.
            if (BlueprintTierTable.IsLargerThan(tier, currentTier))
                status = UsageStatus.UpgradeNeeded;
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
            // ⛔⛔ B4 — §17.7: size order, not ordinal order. See IsLargerThan.
            if (BlueprintTierTable.IsLargerThan(proj.Tier, currentTier))
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
        // ⚠ NOT OccurrenceStoreAccess: this maps to the editor's BlackboardTier enum and
        //    deliberately answers B1024 for an entity with NO store at all (the default the panel
        //    opens on). The seam's GetStoreSize returns 0 there, which is a different answer.
        // ⭐ O3a / B3: the probe order is BlueprintTierTable.Descending; the "no store ⇒ smallest"
        //   default this method is documented for is spelled explicitly below.
        return BlueprintTierTable.Of(_repo, _entity)?.Tier
            ?? BlueprintTierTable.Ascending[0].Tier;
    }

    public static BlackboardTier ChooseTierFromAggregate(int totalSlots, int totalBytes)
    {
        return BlueprintTierTable.Select(totalBytes, totalSlots).Tier;
    }

    public string? GetBlueprintName(Guid assetId)
    {
        int bpId = BlueprintIdHash.Compute(assetId);
        return _registry.TryGetById(bpId, out var def) && def != null ? def.Name : null;
    }
}
