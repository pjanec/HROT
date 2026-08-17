using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Events;
using Fdp.Toolkit.Blueprints.Partitioning;
using ImGuiNET;

namespace Hrot.Blueprints.Editor.EntityBlueprints;

public sealed class EntityBlueprintsPanel : BlueprintEditorWindowBase
{
    private readonly EntityBlueprintsEditModel _model;
    private readonly EntityRepository _world;
    private readonly BlueprintRegistry _registry;
    private readonly Func<Entity?>? _entityResolver;
    private bool _isRunning;
    private Entity? _lastEntity;

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

    public bool IsRunning { get => _isRunning; set => _isRunning = value; }

    public override void DrawUI()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;

        // Resolve entity selection
        if (_entityResolver != null)
        {
            var selected = _entityResolver();
            if (selected.HasValue && selected.Value != default)
            {
                _model.SetEntity(selected.Value);
                _lastEntity = selected.Value;
            }
        }

        if (!_model.HasValidEntity)
        {
            ImGui.TextDisabled("No entity selected. Select an entity on the map to edit its blueprints.");
            return;
        }

        _model.RefreshReality();

        ImGui.Text("Entity Blueprints");
        ImGui.Separator();
        ImGui.Text(_isRunning ? "Sim: Running" : "Sim: Paused");
        ImGui.Text($"Current tier: {_model.GetCurrentTier()}");

        var proj = _model.ComputeProjection();
        DrawProjectionBar(proj);
        ImGui.Separator();

        // + Add button with popup
        if (ImGui.Button("+ Add Blueprint..."))
            ImGui.OpenPopup("##addBlueprintPopup");

        if (ImGui.BeginPopup("##addBlueprintPopup"))
        {
            foreach (var (id, def) in _registry.GetAll())
            {
                if (def.Kind != BlueprintDispatchKind.Instance) continue;

                bool inReality = _model.Reality.Any(s => s.BlueprintId == id);
                bool inAdds = _model.StagedAdds.Contains(def.AssetId);
                bool inRemoves = _model.StagedRemoves.Contains(def.AssetId);

                if (inReality && !inRemoves)
                {
                    ImGui.BeginDisabled();
                    ImGui.Text($"{def.Name} (attached)");
                    ImGui.EndDisabled();
                }
                else if (inAdds)
                {
                    ImGui.BeginDisabled();
                    ImGui.Text($"{def.Name} (staged)");
                    ImGui.EndDisabled();
                }
                else if (ImGui.Selectable($"{def.Name}##{id}"))
                {
                    _model.StageAdd(def.AssetId);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }

        ImGui.Separator();

        // Table
        bool hasChanges = _model.HasStagedChanges;
        bool overCeiling = proj.Status == UsageStatus.OverCeiling;

        if (ImGui.BeginTable("##entityBpTable", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Blueprint");
            ImGui.TableSetupColumn("Status");
            ImGui.TableSetupColumn("");
            ImGui.TableHeadersRow();

            // Reality rows — show "Active" or "Removed"
            foreach (var slot in _model.Reality)
            {
                bool isRemoved = _model.StagedRemoves.Contains(slot.AssetId);
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(slot.Name);

                ImGui.TableNextColumn();
                if (isRemoved)
                    ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Remove pending");
                else
                    ImGui.Text("Active");

                ImGui.TableNextColumn();
                if (ImGui.SmallButton(isRemoved ? $"Restore##{slot.BlueprintId}" : $"Remove##{slot.BlueprintId}"))
                    _model.StageRemove(slot.AssetId);
            }

            // Added rows (staged adds not in Reality) — iterate copy to avoid mutation crash
            foreach (var assetId in _model.StagedAdds.ToList())
            {
                if (_model.Reality.Any(s => s.AssetId == assetId)) continue;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(_model.GetBlueprintName(assetId) ?? $"0x{BlueprintIdHash.Compute(assetId):X8}");

                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Add pending");

                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Cancel##add_{BlueprintIdHash.Compute(assetId):X8}"))
                    _model.CancelAdd(assetId);
            }

            ImGui.EndTable();
        }

        ImGui.Separator();

        // Footer
        bool canApply = hasChanges && !overCeiling;
        bool canRevert = hasChanges;

        if (!canApply) ImGui.BeginDisabled();
        if (ImGui.Button("Apply"))
        {
            var timing = _isRunning ? CommitTiming.Running : CommitTiming.Paused;
            var plan = _model.BuildCommitPlan(timing);
            ExecuteCommitPlan(plan, timing);
            _model.RevertAll();
            _model.RefreshReality();
        }
        if (!canApply) ImGui.EndDisabled();

        ImGui.SameLine();
        if (!canRevert) ImGui.BeginDisabled();
        if (ImGui.Button("Revert All"))
            _model.RevertAll();
        if (!canRevert) ImGui.EndDisabled();
    }

    private void DrawProjectionBar(Projection proj)
    {
        string label = proj.Status switch
        {
            UsageStatus.OverCeiling => $"⚠ Over ceiling: {proj.Slots}/{proj.Bytes} (max 16/16096)",
            UsageStatus.UpgradeNeeded => $"Upgrade needed: {proj.Slots}/{proj.Bytes} → {proj.Tier}",
            _ => $"OK: {proj.Slots}/{proj.Bytes} in {proj.Tier}",
        };
        ImGui.Text(label);
    }

    private unsafe void ExecuteCommitPlan(CommitPlan plan, CommitTiming timing)
    {
        if (timing == CommitTiming.Paused)
        {
            if (plan.UpgradeToTier.HasValue)
                UpgradeTier(_model.GetCurrentTier(), plan.UpgradeToTier.Value);

            foreach (int bpId in plan.DetachBlueprintIds)
                BlueprintInstanceService.DetachFromEntity(_world, bpId, _model.GetEntity());

            foreach (int bpId in plan.AttachBlueprintIds)
                BlueprintInstanceService.AttachToEntity(_world, _registry, bpId, _model.GetEntity());
        }
        else
        {
            foreach (var evt in plan.RemoveEvents) _world.Bus.Publish(evt);
            // ⭐ Batch 70 — the attach event is managed now (it carries params JSON), so it rides the
            //   managed bus. The plan built in the editor is the OTHER producer of this event; it
            //   supplies no params, so the blueprint's declared defaults stand.
            foreach (var evt in plan.AttachEvents) _world.Bus.PublishManaged(evt);
        }
    }

    private unsafe void UpgradeTier(BlackboardTier oldTier, BlackboardTier newTier)
    {
        var entity = _model.GetEntity();
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

        switch (oldTier)
        {
            case BlackboardTier.B1024 when newTier == BlackboardTier.B4096:
                ref var old1024 = ref _world.GetComponentRW<BlueprintBlackboard1024>(entity);
                ref var new4096 = ref _world.GetComponentRW<BlueprintBlackboard4096>(entity);
                fixed (byte* src = old1024.Memory) fixed (byte* dst = new4096.Memory)
                    BlueprintBlackboardPartitions.CopyToLargerTier(src, BlueprintBlackboard1024.TotalSize, dst, BlueprintBlackboard4096.TotalSize, BlueprintBlackboard4096.MaxSlots);
                _world.RemoveComponent<BlueprintBlackboard1024>(entity);
                break;
            case BlackboardTier.B4096 when newTier == BlackboardTier.B16384:
                ref var old4096 = ref _world.GetComponentRW<BlueprintBlackboard4096>(entity);
                ref var new16384 = ref _world.GetComponentRW<BlueprintBlackboard16384>(entity);
                fixed (byte* src = old4096.Memory) fixed (byte* dst = new16384.Memory)
                    BlueprintBlackboardPartitions.CopyToLargerTier(src, BlueprintBlackboard4096.TotalSize, dst, BlueprintBlackboard16384.TotalSize, BlueprintBlackboard16384.MaxSlots);
                _world.RemoveComponent<BlueprintBlackboard4096>(entity);
                break;
        }
    }

    public override void OnActivated() { }
    public override void OnDeactivated() { }
}
