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
using Fdp.Diagnostics.Contracts.Panels;
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

        // ⭐⭐⭐ U1b — DECLARED AT CONSTRUCTION, ALWAYS, and NOT gated on CaptureEnabled.
        //    ⛔ A panel whose window is never opened never draws; if instrumentation were declared by
        //      DRAWING, this panel would be indistinguishable from one nobody has converted ⇒ the
        //      reader could not tell "showed nothing" from "not instrumented". 📌 That is the false
        //      green the opt-in registry exists to prevent.
        PanelSnapshot.DeclareInstrumented(PanelIds.EntityBlueprints);
    }

    public bool IsRunning { get => _isRunning; set => _isRunning = value; }

    /// <summary>
    /// ⭐⭐⭐ <b><c>U-obs-1</c>: BUILD · CAPTURE · RENDER.</b>
    /// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
    ///
    /// <para>⚠⚠ <b>ONE DEVIATION FROM §Example's ORDER, and it is deliberate — capture happens BEFORE the
    /// ImGui-context guard, not after the render.</b> 📐 §Example shows build → render → capture, which
    /// would make the dump <b>depend on a live GPU context</b> ⇒ ⛔ a headless run would observe nothing.
    /// ⭐ That defeats the umbrella purpose *(<c>DESIGN_Headless_Testability.md</c> — this programme is the
    /// step that makes the UI checkable WITHOUT a display)*. ⇒ ⭐⭐ <b>the model is the panel's truth
    /// whether or not anyone paints it</b>, so it is published first.</para>
    /// </summary>
    public override void DrawUI()
    {
        var vm = BuildViewModel();

        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);

        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        Render(vm);
    }

    /// <summary>
    /// ⭐⭐ <b>A pure-ish projection of current state into the model the draw reads.</b>
    /// ⚠ "Pure-ish": it still RESOLVES the selected entity and refreshes reality, because those ARE how the
    /// panel learns what it is showing — ⛔ they were in the draw before and they are state ACQUISITION,
    /// not rendering.
    /// </summary>
    public EntityBlueprintsViewModel BuildViewModel()
    {
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
            return new EntityBlueprintsViewModel
            {
                HasEntity    = false,
                EmptyMessage = "No entity selected. Select an entity on the map to edit its blueprints.",
            };
        }

        _model.RefreshReality();

        var proj = _model.ComputeProjection();

        var addOptions = new List<EntityBlueprintAddOption>();
        foreach (var (id, def) in _registry.GetAll())
        {
            if (def.Kind != BlueprintDispatchKind.Instance) continue;

            bool inReality  = _model.Reality.Any(s => s.BlueprintId == id);
            bool inAdds     = _model.StagedAdds.Contains(def.AssetId);
            bool inRemoves  = _model.StagedRemoves.Contains(def.AssetId);

            if (inReality && !inRemoves)
                addOptions.Add(new EntityBlueprintAddOption($"{def.Name} (attached)", "attached", def.AssetId, id.ToString()));
            else if (inAdds)
                addOptions.Add(new EntityBlueprintAddOption($"{def.Name} (staged)", "staged", def.AssetId, id.ToString()));
            else
                addOptions.Add(new EntityBlueprintAddOption(def.Name, "selectable", def.AssetId, id.ToString()));
        }

        var rows = new List<EntityBlueprintRow>();
        foreach (var slot in _model.Reality)
        {
            bool isRemoved = _model.StagedRemoves.Contains(slot.AssetId);
            rows.Add(new EntityBlueprintRow(
                Name:        slot.Name,
                Status:      isRemoved ? "Remove pending" : "Active",
                Emphasis:    isRemoved ? "warning" : "none",
                ActionLabel: isRemoved ? "Restore" : "Remove",
                AssetId:     slot.AssetId,
                ActionScope: slot.BlueprintId.ToString()));
        }

        // ⚠ Iterate a COPY — the render's Cancel button mutates StagedAdds, and the original code took
        //   this copy for that reason. Building the model early does not remove the need for it.
        foreach (var assetId in _model.StagedAdds.ToList())
        {
            if (_model.Reality.Any(s => s.AssetId == assetId)) continue;

            rows.Add(new EntityBlueprintRow(
                Name:        _model.GetBlueprintName(assetId) ?? $"0x{BlueprintIdHash.Compute(assetId):X8}",
                Status:      "Add pending",
                Emphasis:    "success",
                ActionLabel: "Cancel",
                AssetId:     assetId,
                ActionScope: $"add_{BlueprintIdHash.Compute(assetId):X8}"));
        }

        bool hasChanges = _model.HasStagedChanges;

        return new EntityBlueprintsViewModel
        {
            HasEntity        = true,
            SimState         = _isRunning ? "Running" : "Paused",
            Tier             = _model.GetCurrentTier().ToString(),
            ProjectionLabel  = ProjectionLabelOf(proj),
            ProjectionStatus = proj.Status.ToString(),
            AddOptions       = addOptions,
            Rows             = rows,
            CanApply         = hasChanges && proj.Status != UsageStatus.OverCeiling,
            CanRevert        = hasChanges,
        };
    }

    /// <summary>
    /// ⛔⛔ <b>THE INVARIANT LIVES HERE: every state-derived value drawn below comes from <paramref name="vm"/>.</b>
    /// ⚠ The literals that remain — the <c>+ Add Blueprint...</c> caption, the column headers, the
    /// <c>Apply</c>/<c>Revert All</c> captions — are constant chrome, 📄 which §Adoption explicitly says not
    /// to refactor. ⭐ <b>A reviewer's check is simple: any <c>ImGui.Text</c> whose argument reaches
    /// <c>_model</c> or <c>_registry</c> is a defect.</b>
    /// </summary>
    private void Render(EntityBlueprintsViewModel vm)
    {
        if (!vm.HasEntity)
        {
            ImGui.TextDisabled(vm.EmptyMessage ?? string.Empty);
            return;
        }

        ImGui.Text(vm.Title);
        ImGui.Separator();
        ImGui.Text($"Sim: {vm.SimState}");
        ImGui.Text($"Current tier: {vm.Tier}");
        ImGui.Text(vm.ProjectionLabel);
        ImGui.Separator();

        if (ImGui.Button("+ Add Blueprint..."))
            ImGui.OpenPopup("##addBlueprintPopup");

        if (ImGui.BeginPopup("##addBlueprintPopup"))
        {
            foreach (var option in vm.AddOptions)
            {
                if (option.State != "selectable")
                {
                    ImGui.BeginDisabled();
                    ImGui.Text(option.Label);
                    ImGui.EndDisabled();
                }
                else if (ImGui.Selectable($"{option.Label}##{option.ActionScope}"))
                {
                    _model.StageAdd(option.AssetId);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }

        ImGui.Separator();

        if (ImGui.BeginTable("##entityBpTable", 3,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Blueprint");
            ImGui.TableSetupColumn("Status");
            ImGui.TableSetupColumn("");
            ImGui.TableHeadersRow();

            foreach (var row in vm.Rows)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(row.Name);

                ImGui.TableNextColumn();
                if (EmphasisColor(row.Emphasis) is { } color)
                    ImGui.TextColored(color, row.Status);
                else
                    ImGui.Text(row.Status);

                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"{row.ActionLabel}##{row.ActionScope}"))
                {
                    if (row.Status == "Add pending") _model.CancelAdd(row.AssetId);
                    else                             _model.StageRemove(row.AssetId);
                }
            }

            ImGui.EndTable();
        }

        ImGui.Separator();

        if (!vm.CanApply) ImGui.BeginDisabled();
        if (ImGui.Button("Apply"))
        {
            var timing = _isRunning ? CommitTiming.Running : CommitTiming.Paused;
            var plan = _model.BuildCommitPlan(timing);
            ExecuteCommitPlan(plan, timing);
            _model.RevertAll();
            _model.RefreshReality();
        }
        if (!vm.CanApply) ImGui.EndDisabled();

        ImGui.SameLine();
        if (!vm.CanRevert) ImGui.BeginDisabled();
        if (ImGui.Button("Revert All"))
            _model.RevertAll();
        if (!vm.CanRevert) ImGui.EndDisabled();
    }

    /// <summary>⭐ The colour ROLE resolves to a colour here, in the render — ⛔ never in the model, so a
    /// cross-host diff does not fail on theming.</summary>
    private static Vector4? EmphasisColor(string emphasis) => emphasis switch
    {
        "warning" => new Vector4(1, 0.5f, 0, 1),
        "success" => new Vector4(0, 1, 0, 1),
        _         => null,
    };

    /// <summary>⭐ The projection bar's whole line, composed once so the dump carries what the eye reads.</summary>
    private static string ProjectionLabelOf(Projection proj) => proj.Status switch
    {
        UsageStatus.OverCeiling   => $"⚠ Over ceiling: {proj.Slots}/{proj.Bytes} (max 16/16096)",
        UsageStatus.UpgradeNeeded => $"Upgrade needed: {proj.Slots}/{proj.Bytes} → {proj.Tier}",
        _                         => $"OK: {proj.Slots}/{proj.Bytes} in {proj.Tier}",
    };

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
