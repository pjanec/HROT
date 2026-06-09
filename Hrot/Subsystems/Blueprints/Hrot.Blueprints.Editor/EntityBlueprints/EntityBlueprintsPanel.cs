using System;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Events;
using Fdp.Toolkit.Blueprints.Partitioning;
using ImGuiNET;

namespace Hrot.Blueprints.Editor.EntityBlueprints;

/// <summary>
/// Thin ImGui window that renders an <see cref="EntityBlueprintsEditModel"/>.
/// All logic lives in the model; this class only draws UI and executes commit plans.
///
/// <para>The optional <c>entityResolver</c> is called each frame before refreshing reality,
/// so the panel tracks the editor's selection (e.g. the selected map entity). When
/// <c>null</c> the model is updated externally.</para>
/// </summary>
public sealed class EntityBlueprintsPanel : BlueprintEditorWindowBase
{
    private readonly EntityBlueprintsEditModel _model;
    private readonly EntityRepository _world;
    private readonly BlueprintRegistry _registry;
    private readonly Func<Entity?>? _entityResolver;
    private bool _isRunning;

    public override string Title => "Entity Blueprints";

    public EntityBlueprintsPanel(
        EntityBlueprintsEditModel model,
        EntityRepository world,
        BlueprintRegistry registry,
        Func<Entity?>? entityResolver = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _entityResolver = entityResolver;
    }

    /// <summary>Set to true when the simulation is running (for commit-timing choice).</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => _isRunning = value;
    }

    public override void DrawUI()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;

        // Resolve the selected entity before refreshing (selection may have changed).
        if (_entityResolver != null)
        {
            var selected = _entityResolver();
            if (selected.HasValue && selected.Value != default)
            {
                _model.SetEntity(selected.Value);
            }
        }

        if (!_model.HasValidEntity)
        {
            ImGui.TextDisabled("No entity selected. Select an entity on the map to edit its blueprints.");
            return;
        }

        _model.RefreshReality();

        // Header
        ImGui.Text("Entity Blueprints");
        ImGui.Separator();

        // Sim state
        ImGui.Text(_isRunning ? "Sim: Running" : "Sim: Paused");
        ImGui.Text($"Current tier: {_model.GetCurrentTier()}");

        // Projection bar
        var proj = _model.ComputeProjection();
        DrawProjectionBar(proj);

        ImGui.Separator();

        // + Add Blueprint button
        if (ImGui.Button("+ Add Blueprint..."))
        {
            // Placeholder: opens a blueprint picker filtered to Instance blueprints.
            // Full BlueprintPickerSources integration deferred to editor wiring.
        }

        ImGui.Separator();

        // Table: Name | Status | Size | Action
        if (ImGui.BeginTable("##entityBpTable", 4,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name");
            ImGui.TableSetupColumn("Status");
            ImGui.TableSetupColumn("Size");
            ImGui.TableSetupColumn("Actions");
            ImGui.TableHeadersRow();

            var diff = _model.ComputeDiff();
            var addedSet = new HashSet<Guid>();
            foreach (var a in diff.Added) addedSet.Add(a.AssetId);
            var removedSet = new HashSet<Guid>();
            foreach (var r in diff.Removed) removedSet.Add(r.AssetId);

            // Show all blueprint entries (from Reality + intended additions)
            var allEntries = new List<(SlotSummary? Slot, BlueprintAssignmentDto? Dto, string Status)>();
            foreach (var slot in _model.Reality)
            {
                string status = removedSet.Contains(slot.AssetId) ? "Removed" : "Active";
                allEntries.Add((slot, null, status));
            }
            foreach (var dto in _model.Intent)
            {
                if (!_model.Reality.Any(s => s.AssetId == dto.AssetId))
                    allEntries.Add((null, dto, "Added"));
            }

            foreach (var entry in allEntries)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(entry.Slot?.Name ?? $"0x{BlueprintIdHash.Compute(entry.Dto!.AssetId):X8}");

                ImGui.TableNextColumn();
                if (entry.Status == "Removed")
                    ImGui.TextColored(new System.Numerics.Vector4(1, 0.5f, 0, 1), "Removed");
                else if (entry.Status == "Added")
                    ImGui.TextColored(new System.Numerics.Vector4(0, 1, 0, 1), "Added");
                else
                    ImGui.Text("Active");

                ImGui.TableNextColumn();
                ImGui.Text(entry.Slot?.PayloadSize.ToString() ?? "-");

                ImGui.TableNextColumn();
                if (entry.Slot != null && entry.Status == "Active")
                {
                    if (ImGui.SmallButton($"Remove##{entry.Slot.Value.BlueprintId}"))
                    {
                        _model.StageRemove(new BlueprintAssignmentDto
                            { AssetId = entry.Slot.Value.AssetId });
                    }
                }
            }

            ImGui.EndTable();
        }

        ImGui.Separator();

        // Footer buttons
        bool overCeiling = proj.Status == UsageStatus.OverCeiling;
        if (overCeiling) ImGui.BeginDisabled();

        if (ImGui.Button("Apply"))
        {
            var timing = _isRunning ? CommitTiming.Running : CommitTiming.Paused;
            var plan = _model.BuildCommitPlan(timing);
            ExecuteCommitPlan(plan, timing);
            _model.RevertAll();
        }

        if (overCeiling) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Revert All"))
            _model.RevertAll();
    }

    private void DrawProjectionBar(Projection proj)
    {
        string label;
        switch (proj.Status)
        {
            case UsageStatus.OverCeiling:
                label = $"⚠ Over ceiling: {proj.Slots} slots / {proj.Bytes} bytes (max 16 / 16096)";
                break;
            case UsageStatus.UpgradeNeeded:
                label = $"Upgrade needed: {proj.Slots} slots / {proj.Bytes} bytes → {proj.Tier}";
                break;
            default:
                label = $"OK: {proj.Slots} slots / {proj.Bytes} bytes in {proj.Tier}";
                break;
        }
        ImGui.Text(label);
    }

    private unsafe void ExecuteCommitPlan(CommitPlan plan, CommitTiming timing)
    {
        if (timing == CommitTiming.Paused)
        {
            // Tier upgrade first
            if (plan.UpgradeToTier.HasValue)
            {
                UpgradeTier(_model.GetCurrentTier(), plan.UpgradeToTier.Value);
            }

            // Detaches then attaches (remove-before-add)
            foreach (int bpId in plan.DetachBlueprintIds)
                BlueprintInstanceService.DetachFromEntity(_world, bpId, _model.GetEntity());

            foreach (int bpId in plan.AttachBlueprintIds)
                BlueprintInstanceService.AttachToEntity(_world, _registry, bpId, _model.GetEntity());
        }
        else
        {
            // Running: publish events (BSA-301 will apply next Input phase)
            foreach (var evt in plan.RemoveEvents)
                _world.Bus.Publish(evt);
            foreach (var evt in plan.AttachEvents)
                _world.Bus.Publish(evt);
        }
    }

    private unsafe void UpgradeTier(BlackboardTier oldTier, BlackboardTier newTier)
    {
        var entity = _model.GetEntity();

        // 1. Add new tier component
        switch (newTier)
        {
            case BlackboardTier.B4096:
                if (!_world.HasComponent<BlueprintBlackboard4096>(entity))
                    _world.AddComponent(entity, default(BlueprintBlackboard4096));
                break;
            case BlackboardTier.B16384:
                if (!_world.HasComponent<BlueprintBlackboard16384>(entity))
                    _world.AddComponent(entity, default(BlueprintBlackboard16384));
                break;
        }

        // 2. CopyToLargerTier
        switch (oldTier)
        {
            case BlackboardTier.B1024 when newTier == BlackboardTier.B4096:
            {
                ref var oldBb = ref _world.GetComponentRW<BlueprintBlackboard1024>(entity);
                ref var newBb = ref _world.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* src = oldBb.Memory)
                fixed (byte* dst = newBb.Memory)
                {
                    BlueprintBlackboardPartitions.CopyToLargerTier(
                        src, BlueprintBlackboard1024.TotalSize,
                        dst, BlueprintBlackboard4096.TotalSize,
                        BlueprintBlackboard4096.MaxSlots);
                }
                break;
            }
            case BlackboardTier.B4096 when newTier == BlackboardTier.B16384:
            {
                ref var oldBb = ref _world.GetComponentRW<BlueprintBlackboard4096>(entity);
                ref var newBb = ref _world.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* src = oldBb.Memory)
                fixed (byte* dst = newBb.Memory)
                {
                    BlueprintBlackboardPartitions.CopyToLargerTier(
                        src, BlueprintBlackboard4096.TotalSize,
                        dst, BlueprintBlackboard16384.TotalSize,
                        BlueprintBlackboard16384.MaxSlots);
                }
                break;
            }
        }

        // 3. Remove old tier (CRITICAL — else double-tick)
        switch (oldTier)
        {
            case BlackboardTier.B1024:
                _world.RemoveComponent<BlueprintBlackboard1024>(entity);
                break;
            case BlackboardTier.B4096:
                _world.RemoveComponent<BlueprintBlackboard4096>(entity);
                break;
        }
    }

    public override void OnActivated()   { }
    public override void OnDeactivated() { }
}
