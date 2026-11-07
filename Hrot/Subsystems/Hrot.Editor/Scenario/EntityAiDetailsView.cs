using System;
using NodeEditor.Core.Widgets;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using System.Runtime.CompilerServices;
using Hrot.Editor.AiShared.Inspector;
using Hrot.Presentation.Behavior;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using ImGuiNET;
using StructEdit.Core;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐ <c>CE-3043</c> — the AI section of the Scenario Details panel: the unit's TASK, SOP and ROE, each with one params form
/// (<see cref="BehaviorParamsForm"/>), applied through <see cref="EntityAiEditModel"/>. 📄 §7.6.
/// </summary>
public sealed class EntityAiDetailsView : IDetailsViewInstance
{
    private readonly EntityAiEditModel _model;
    private readonly Func<IComponentEditService?> _editService;
    private readonly Func<Fdp.Presentation.Editing.IMapPickContext?> _picks;   // ⭐ CE-1023 — the editor's broker
    private readonly Func<EntityRepository?> _world;   // ⭐ CE-4210 P2 — the world whose resident header declares the params
    private Entity _entity = Entity.Null;
    private string _taskPick = "", _sopPick = "";
    private IEditSession? _taskForm, _sopForm;
    private Type? _taskType, _sopType;
    private readonly Dictionary<int, IEditSession> _instanceForms = new();   // CE-2086: one form per instance row
    private string _status = "";

    /// <param name="picks">⭐ CE-1023 — the map picks a param field of type <c>EntityRef</c> / <c>PickableGeoPoint</c> asks;
    /// ⛔ a host that holds a pick service must pass it (the silent-default rule); null ⇒ no Pick buttons.</param>
    public EntityAiDetailsView(EntityAiEditModel model, Func<IComponentEditService?> editService,
                               Func<Fdp.Presentation.Editing.IMapPickContext?>? picks = null,
                               Func<EntityRepository?>? world = null)
    {
        _world       = world ?? (() => null);
        _model       = model ?? throw new ArgumentNullException(nameof(model));
        _editService = editService ?? throw new ArgumentNullException(nameof(editService));
        _picks       = picks ?? (() => null);
    }

    /// <summary>⭐ CE-1023 — the pick context of one row's form: the editor's broker, scoped to the row (null ⇒ none).</summary>
    public Fdp.Presentation.Editing.IMapPickContext? PicksFor(string scope)
        => _picks() is { } p ? new Fdp.Presentation.Editing.ScopedMapPickContext(p, scope) : null;

    /// <summary>⭐ CE-4210 P2 (MA-1, D) — the bind context of one params form of the selected entity: <c>ParamBindings</c> under <paramref name="prefix"/>
    /// (<c>"Behavior.Params"</c> for the task, <c>"Sop.Params"</c> for the SOP); null when this host has no world (⛔ the descriptor passes it — silent-default rule).</summary>
    public Fdp.Presentation.Editing.IParamBindContext? BindsFor(Entity entity, string prefix)
        => _world() is { } w && !entity.IsNull
            ? new Fdp.Presentation.Editing.EntityBindContext(new Fdp.Presentation.Adapters.RepositoryAdapter(w), entity, prefix,
                  () => Fdp.Presentation.Editing.HeaderParams.Of(w))
            : null;

    public EntityAiEditModel Model => _model;

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities is not { Count: 1 }) { ImGui.TextDisabled("Select one unit."); return; }
        var entity = context.Entities[0];
        if (entity != _entity) { _entity = entity; _taskPick = _sopPick = ""; _taskForm = _sopForm = null; _instanceForms.Clear(); _status = ""; }
        if (_model.Read(entity) is not { } now) { ImGui.TextDisabled("This entity has no brain."); return; }
        bool running = context.Mode == VariableRunState.Running;

        ImGui.PushID(idScope + "/ai");
        // ── SOP ─────────────────────────────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.TextUnformatted("SOP — standard operating procedure");
        ImGui.TextUnformatted($"Runs: {now.Sop ?? "(none)"}{(now.Sop != null ? $"  ({now.SopOrigin}{(now.SopFaulted ? ", FAULTED" : "")})" : "")}");
        Row("sop", _model.SopChoices(), ref _sopPick, ref _sopForm, ref _sopType, now.Sop, now.SopParams,
            apply: (name, json) => _model.ApplySop(entity, name, json, running),
            clear: () => _model.ClearSop(entity, running),
            note: "Only behaviours known to drive no channel are offered (an SOP must not move or fire the unit).");

        // ── task ────────────────────────────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.TextUnformatted("Current behaviour (task)");
        ImGui.TextUnformatted($"Runs: {now.Task ?? "(none)"}{(now.Task != null ? $"  ({now.TaskOrigin})" : "")}");
        if (now.PausedTask != null) ImGui.TextUnformatted($"Paused by a reaction: {now.PausedTask}");
        Row("task", _model.TaskChoices(), ref _taskPick, ref _taskForm, ref _taskType, now.Task, now.TaskParams,
            apply: (name, json) => _model.ApplyTask(entity, name, json, running),
            clear: () => _model.ClearTask(entity, running),
            note: null);

        // ── ROE ─────────────────────────────────────────────────────────────────────────
        ImGui.Separator();
        ImGui.TextUnformatted("Rules of engagement");
        int fire = (int)now.Fire, reactions = (int)now.Reactions;
        bool changed = false;
        if (FormGrid.Begin("ai.roe", RoeLabels))
        {
            FormGrid.Row("Fire");
            changed = ImGui.Combo("##fire", ref fire, "(unset)\0Hold fire\0Return fire\0Fire at will\0");
            FormGrid.Row("Reactions");
            changed |= ImGui.Combo("##reactions", ref reactions, "(unset)\0Stay on task\0React\0");
            FormGrid.End();
        }
        if (changed)
            _status = _model.ApplyRoe(entity, (RoeFire)fire, (RoeReactions)reactions, running) ? "ROE applied." : "ROE refused (a higher rank set it).";
        ImGui.TextDisabled($"Set by: {now.RoeSetBy}");

        // ── instance blueprints (CE-2086, §7.6a) ────────────────────────────────────────
        var instances = _model.InstanceRows(entity);
        if (instances.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextUnformatted("Instance blueprints");
            foreach (var row in instances) InstanceRowUi(entity, row, running);
            ImGui.TextDisabled("Attach / detach in the Blueprint perspective; running: Apply restarts the instance with these params.");
        }

        if (_status.Length > 0) ImGui.TextWrapped(_status);
        ImGui.TextDisabled(running ? "Running: changes apply next frame." : "Paused: changes apply now.");
        ImGui.PopID();
    }

    private static readonly string[] RoeLabels = { "Fire", "Reactions" };

    private void Row(string id, IReadOnlyList<string> choices, ref string pick, ref IEditSession? form, ref Type? formType,
                     string? current, string currentParams, Func<string, string, bool> apply, Func<bool> clear, string? note)
    {
        ImGui.PushID(id);
        if (pick.Length == 0 && current != null) pick = current;
        // ⭐ CE-3164 (R-268) — the shared folder-tree picker
        string? pickedBehaviour = null;
        if (FormGrid.Begin("ai.behaviour"))
        {
            FormGrid.Row("Behaviour");
            if (Hrot.Presentation.Behavior.BehaviorPickerTree.Combo("##behaviour", pick.Length == 0 ? "(choose)" : pick,
                    _model.AsChoices(choices), pick, out var picked)) pickedBehaviour = picked;
            FormGrid.End();
        }
        if (pickedBehaviour != null) { pick = pickedBehaviour; form = null; }
        if (note != null) ImGui.TextDisabled(note);

        var type = pick.Length > 0 ? _model.ParamsTypeOf(pick) : null;
        if (type != null && (form == null || formType != type) && _editService() is { } svc)
        {
            form = BehaviorParamsForm.Open(svc, type, pick == current ? currentParams : "{}");
            formType = type;
        }
        if (type != null && form != null) BehaviorParamsForm.DrawFields(form, PicksFor("$." + id), "params",
            id == "sop" ? BindsFor(_entity, "Sop.Params") : id == "task" ? BindsFor(_entity, "Behavior.Params") : null);

        if (ImGui.Button("Apply") && pick.Length > 0)
        {
            string json = type != null && form != null ? BehaviorParamsForm.CommitToJson(form, type) : "{}";
            _status = apply(pick, json) ? $"Applied {pick}." : $"Refused: {pick} (see the log — the gate, a channel-driving SOP, or bad params).";
            form = null;
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear")) _status = clear() ? "Cleared." : "Clear refused (a higher rank set it).";
        ImGui.PopID();
    }

    /// <summary>⭐ <c>CE-2086</c> — one attached instance: its params in the ONE params form, committed through the model.</summary>
    private void InstanceRowUi(Entity entity, EntityAiEditModel.InstanceRow row, bool running)
    {
        ImGui.PushID("bp" + row.BlueprintId);
        ImGui.TextUnformatted(row.Name);
        if (row.ParamsType is { } type)
        {
            if (!_instanceForms.TryGetValue(row.BlueprintId, out var form) && _editService() is { } svc)
                _instanceForms[row.BlueprintId] = form = BehaviorParamsForm.Open(svc, type, row.ParamsJson);
            if (form != null) BehaviorParamsForm.DrawFields(form, PicksFor("$.bp" + row.BlueprintId));
            if (form != null && ImGui.Button("Apply"))
            {
                string json = BehaviorParamsForm.CommitToJson(form, type);
                _status = _model.ApplyInstanceParams(entity, row.BlueprintId, json, running)
                    ? $"Applied {row.Name}'s params." : $"Refused: {row.Name}'s params (see the log).";
                _instanceForms.Remove(row.BlueprintId);
            }
        }
        else ImGui.TextDisabled("(no parameters)");
        ImGui.PopID();
    }

    public void Dispose() { }
}

/// <summary>⭐ <c>CE-3043</c> — the AI section's descriptor (Scenario perspective).</summary>
public static class EntityAiDetailsViewDescriptor
{
    public const string ViewId = "details.ai";
    public const int Rank = 45;

    public static DetailsViewDescriptor For(Func<EntityRepository?> world, Func<BehaviorRegistry?> registry,
                                            Func<IComponentEditService?> editService,
                                            Func<Fdp.Toolkit.Blueprints.BlueprintRegistry?>? blueprints = null,
                                            Func<Fdp.Presentation.Editing.IMapPickContext?>? picks = null,
                                            Hrot.Presentation.Edits.EditHistory? history = null)   // ⭐ CE-4219 ① — the editor's shared history
    {
        var instance = new EntityAiDetailsView(new EntityAiEditModel(world, registry, blueprints, history), editService, picks, world);
        return new DetailsViewDescriptor(
            Id:        ViewId,
            Title:     "Assign: task, SOP, ROE",   // ⭐ CE-1314 — a section of the Brain tab now
            Rank:      Rank,
            AppliesTo: DetailsViewPredicates.OneEntityWithBrain(
                           e => world() is { } w && w.IsAlive(e) && w.HasComponent<BehaviorState>(e)),
            Create:    () => instance);
    }
}
