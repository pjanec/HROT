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

/// <summary>Projected usage status after applying staged Intent to Reality.</summary>
public enum UsageStatus { Ok, UpgradeNeeded, OverCeiling }

/// <summary>Projected slot/byte usage and the tier that would be required.</summary>
public readonly record struct Projection(
    int Slots, int Bytes, BlackboardTier Tier, UsageStatus Status);

/// <summary>The net difference between Intent and Reality.</summary>
public sealed class DiffResult
{
    public List<BlueprintAssignmentDto> Added { get; } = new();
    public List<BlueprintAssignmentDto> Removed { get; } = new();
}

/// <summary>Controls how a commit is applied: direct mutation (paused) vs events (running).</summary>
public enum CommitTiming { Paused, Running }

/// <summary>
/// Ordered mutation plan produced by <see cref="EntityBlueprintsEditModel.BuildCommitPlan"/>.
/// For paused timing: tier-upgrade step + detach/attach id lists.
/// For running timing: ordered <c>Remove</c>/<c>AttachInstanceBlueprintEvent</c> lists.
/// </summary>
public sealed class CommitPlan
{
    // Paused path:
    public BlackboardTier? UpgradeToTier { get; set; }
    public List<int> DetachBlueprintIds { get; } = new();
    public List<int> AttachBlueprintIds { get; } = new();

    // Running path:
    public List<RemoveInstanceBlueprintEvent> RemoveEvents { get; } = new();
    public List<AttachInstanceBlueprintEvent> AttachEvents { get; } = new();
}

/// <summary>
/// Headless (no ImGui) view-model for the "Entity Blueprints" authoring panel.
/// Reality = per-frame scan across all three blackboard tiers.
/// Intent = local (uncommitted) staging list of <see cref="BlueprintAssignmentDto"/>.
///
/// <para>
/// All logic is testable via public API; the panel (Task 2) only renders this model.
/// </para>
/// </summary>
public sealed class EntityBlueprintsEditModel
{
    private readonly EntityRepository _repo;
    private readonly BlueprintRegistry _registry;
    private Entity _entity;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Live snapshot of all attached blueprint slots across all tiers.</summary>
    public List<SlotSummary> Reality { get; } = new();

    /// <summary>Staged (uncommitted) adds and removes.</summary>
    public List<BlueprintAssignmentDto> Intent { get; } = new();

    /// <summary>Last computed diff (populated by <see cref="ComputeDiff"/>).</summary>
    public DiffResult Diff { get; private set; } = new();

    /// <summary>Last computed projection (populated by <see cref="ComputeProjection"/>).</summary>
    public Projection Projection { get; private set; }

    public EntityBlueprintsEditModel(EntityRepository repo, BlueprintRegistry registry, Entity entity)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _entity = entity;
    }

    // ── Reality ──────────────────────────────────────────────────────────────

    /// <summary>Scan all three tiers into Reality. Call every frame to keep live.</summary>
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

    // ── Intent staging ───────────────────────────────────────────────────────

    /// <summary>Stage an add. Does NOT mutate live memory.</summary>
    public void StageAdd(BlueprintAssignmentDto dto) => Intent.Add(dto);

    /// <summary>
    /// Stage a remove by AssetId. Does NOT mutate live memory.
    /// Intent represents the desired end state; this removes the matching entry.
    /// </summary>
    public void StageRemove(BlueprintAssignmentDto dto)
        => Intent.RemoveAll(i => i.AssetId == dto.AssetId);

    /// <summary>Clear all staged changes, discarding Intent.</summary>
    public void RevertAll() => Intent.Clear();

    /// <summary>
    /// Copies all Reality entries into Intent so Intent represents the current
    /// desired state (no changes). Call before staging user edits.
    /// </summary>
    public void LoadIntentFromReality()
    {
        Intent.Clear();
        foreach (var slot in Reality)
            Intent.Add(new BlueprintAssignmentDto { AssetId = slot.AssetId });
    }

    // ── Diff ─────────────────────────────────────────────────────────────────

    /// <summary>Compute Diff of Intent vs Reality. Stores result in <see cref="Diff"/>.</summary>
    public DiffResult ComputeDiff()
    {
        var diff = new DiffResult();

        // If Intent is empty, no changes have been staged — diff is empty.
        if (Intent.Count == 0)
        {
            Diff = diff;
            return diff;
        }

        // Reality AssetIds as set
        var realityIds = new HashSet<Guid>(Reality.Select(s => s.AssetId));
        // Intent AssetIds as set
        var intentIds = new HashSet<Guid>(Intent.Select(d => d.AssetId));

        foreach (var dto in Intent)
        {
            if (!realityIds.Contains(dto.AssetId))
                diff.Added.Add(dto);
        }
        foreach (var slot in Reality)
        {
            if (!intentIds.Contains(slot.AssetId))
                diff.Removed.Add(new BlueprintAssignmentDto { AssetId = slot.AssetId });
        }

        Diff = diff;
        return diff;
    }

    // ── Projection ───────────────────────────────────────────────────────────

    /// <summary>Compute projected usage after applying staged Intent.</summary>
    public Projection ComputeProjection()
    {
        // Total = Reality + staged adds - staged removes
        var realityIds = new HashSet<Guid>(Reality.Select(s => s.AssetId));
        var intentIds = new HashSet<Guid>(Intent.Select(d => d.AssetId));

        int totalSlots = Reality.Count;
        int totalBytes = Reality.Sum(s => s.PayloadSize);

        foreach (var dto in Intent)
        {
            if (!realityIds.Contains(dto.AssetId))
            {
                totalSlots++;
                // Look up def to get StateSize
                int bpId = BlueprintIdHash.Compute(dto.AssetId);
                if (_registry.TryGetById(bpId, out var def) && def != null)
                    totalBytes += def.StateSize;
            }
        }

        // Only subtract Reality items not in Intent when Intent represents a desired
        // state (non-empty). An empty Intent means "no changes staged yet."
        if (Intent.Count > 0)
        {
            foreach (var slot in Reality)
            {
                if (!intentIds.Contains(slot.AssetId))
                {
                    totalSlots--;
                    totalBytes -= slot.PayloadSize;
                }
            }
        }

        // Pick tier + status
        BlackboardTier tier = ChooseTierFromAggregate(totalSlots, totalBytes);
        UsageStatus status = UsageStatus.Ok;
        if (totalSlots > BlueprintBlackboard16384.MaxSlots || totalBytes > BlueprintBlackboard16384.PayloadSize)
        {
            tier = BlackboardTier.B16384;
            status = UsageStatus.OverCeiling;
        }
        else
        {
            // Determine current tier
            BlackboardTier currentTier = GetCurrentTier();
            if (tier > currentTier) status = UsageStatus.UpgradeNeeded;
        }

        Projection = new Projection(totalSlots, totalBytes, tier, status);
        return Projection;
    }

    // ── Commit plan ──────────────────────────────────────────────────────────

    /// <summary>Build the commit plan for paused or running timing.</summary>
    public CommitPlan BuildCommitPlan(CommitTiming timing)
    {
        var diff = ComputeDiff();
        var proj = ComputeProjection();
        var plan = new CommitPlan();

        if (timing == CommitTiming.Paused)
        {
            // Check if tier upgrade needed
            BlackboardTier currentTier = GetCurrentTier();
            if (proj.Tier > currentTier)
                plan.UpgradeToTier = proj.Tier;

            // Detaches
            foreach (var dto in diff.Removed)
                plan.DetachBlueprintIds.Add(BlueprintIdHash.Compute(dto.AssetId));

            // Attaches
            foreach (var dto in diff.Added)
                plan.AttachBlueprintIds.Add(BlueprintIdHash.Compute(dto.AssetId));
        }
        else
        {
            // Running — publish events (remove-before-add per BSA-301)
            foreach (var dto in diff.Removed)
                plan.RemoveEvents.Add(new RemoveInstanceBlueprintEvent
                {
                    Entity = _entity,
                    BlueprintId = BlueprintIdHash.Compute(dto.AssetId),
                });

            foreach (var dto in diff.Added)
                plan.AttachEvents.Add(new AttachInstanceBlueprintEvent
                {
                    Entity = _entity,
                    BlueprintId = BlueprintIdHash.Compute(dto.AssetId),
                });
        }

        return plan;
    }

    // ── Public helpers (used by panel) ───────────────────────────────────────

    /// <summary>The entity this model is editing.</summary>
    public Entity GetEntity() => _entity;

    /// <summary>
    /// Update the entity being edited. Called by the panel when the editor selection
    /// changes so the model tracks the newly-selected entity without rebuilding.
    /// </summary>
    public void SetEntity(Entity entity) => _entity = entity;

    /// <summary>
    /// True when a valid entity is set (non-null and non-default).
    /// </summary>
    public bool HasValidEntity => _entity != default;

    /// <summary>Returns the highest tier component present on the entity (B1024 if none).</summary>
    public BlackboardTier GetCurrentTier()
    {
        if (_repo.HasComponent<BlueprintBlackboard16384>(_entity))
            return BlackboardTier.B16384;
        if (_repo.HasComponent<BlueprintBlackboard4096>(_entity))
            return BlackboardTier.B4096;
        if (_repo.HasComponent<BlueprintBlackboard1024>(_entity))
            return BlackboardTier.B1024;
        return BlackboardTier.B1024;
    }

    /// <summary>
    /// Returns the smallest blackboard tier that can hold <paramref name="totalSlots"/>
    /// slots and <paramref name="totalBytes"/> bytes of payload (same logic as BSA-203).
    /// </summary>
    public static BlackboardTier ChooseTierFromAggregate(int totalSlots, int totalBytes)
    {
        if (totalSlots <= BlueprintBlackboard1024.MaxSlots && totalBytes <= BlueprintBlackboard1024.PayloadSize)
            return BlackboardTier.B1024;
        if (totalSlots <= BlueprintBlackboard4096.MaxSlots && totalBytes <= BlueprintBlackboard4096.PayloadSize)
            return BlackboardTier.B4096;
        return BlackboardTier.B16384;
    }
}
