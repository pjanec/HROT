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
/// ⭐⭐ <c>CE-3043</c> — the testable half of the editor's AI section: READ a unit's live AI (task, SOP, ROE) and APPLY an
/// author's change through the one gate — the event when the sim runs, the ingress's own pipeline at once when it does not
/// (nothing ticks while paused). ⛔ It never writes a component itself. 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.6.
/// <para>⭐ Every edit is at origin <see cref="BehaviorOrigin.Superior"/> — R-193: an editor-authored assignment ranks as a
/// superior; an operator still outranks it.</para>
/// </summary>
public sealed class EntityAiEditModel
{
    /// <summary>The rank every editor edit carries.</summary>
    public const BehaviorOrigin EditorOrigin = BehaviorOrigin.Superior;

    private readonly Func<EntityRepository?> _world;
    private readonly Func<BehaviorRegistry?> _registry;
    private readonly Func<Fdp.Toolkit.Blueprints.BlueprintRegistry?> _blueprints;
    private readonly Hrot.Presentation.Edits.EditHistory? _history;
    private BehaviorRegistry? _ingressFor;
    private BehaviorIngressSystem? _ingress;

    /// <param name="blueprints">⭐ <c>CE-2086</c> — the instance blueprints' registry, for the params rows. ⚠ Optional for
    /// headless fixtures (no rows); ⛔ a host that has one must pass it (the silent-default rule).</param>
    /// <param name="history">⭐ <c>CE-4219</c> ① — the editor's ONE shared undo history: an EDIT-mode apply (not <c>running</c>) pushes ONE entry whose undo re-applies the PREVIOUS value (the precedent of <c>RuleDetails</c>); a live
    /// edit lasts the run only and is not pushed. ⚠ Null ⇒ no undo — only where the host has no history, or pushes its own entry (the rule editor's <c>ApplyTask</c> call: <c>RuleDetails</c> pushes the rule's entry itself — two entries for one gesture
    /// would be wrong). ⛔ A host that HAS the history must pass it (the silent-default rule): the editor does.</param>
    public EntityAiEditModel(Func<EntityRepository?> world, Func<BehaviorRegistry?> registry,
                             Func<Fdp.Toolkit.Blueprints.BlueprintRegistry?>? blueprints = null,
                             Hrot.Presentation.Edits.EditHistory? history = null)
    {
        _history    = history;
        _world      = world    ?? throw new ArgumentNullException(nameof(world));
        _registry   = registry ?? throw new ArgumentNullException(nameof(registry));
        _blueprints = blueprints ?? (() => null);
    }

    /// <summary>⭐ <c>CE-2086</c> — one attached instance blueprint: its params type (null = none) and params as JSON (only the
    /// fields that differ from the declared defaults — the save's own rule; <c>"{}"</c> = all defaults).</summary>
    public sealed record InstanceRow(int BlueprintId, string Name, Type? ParamsType, string ParamsJson);

    /// <summary>⭐ <c>CE-2086</c> — the unit's attached instances, walked as the scenario save walks them (§7.6a).</summary>
    public unsafe IReadOnlyList<InstanceRow> InstanceRows(Entity entity)
    {
        var rows = new List<InstanceRow>();
        if (_world() is not { } world || _blueprints() is not { } reg || !world.IsAlive(entity)) return rows;
        // ⭐ CE-3137 U-0: every block the unit carries.
        Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.GetBlocksReadOnly(world, entity, out var blocks);
        for (int b = 0; b < blocks.Count; b++)
        {
            byte* memory = blocks.Memory(b);
            if (Unsafe.AsRef<Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardHeader>(memory).MagicAndVersion != 0x42504257u)
                continue;
            int count = Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.GetSlotCount(memory);
            for (int i = 0; i < count; i++)
            {
                ref var slot = ref Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.GetSlot(memory, i);
                if (slot.BlueprintId == 0 || !reg.TryGetById(slot.BlueprintId, out var def) || def == null) continue;
                string? json = def.FormatParams != null && def.ParamsSize > 0
                    ? def.FormatParams(memory + slot.PayloadOffset + def.ParamsOffset, def.ParamsSize) : null;
                rows.Add(new InstanceRow(slot.BlueprintId, def.Name, def.ParamsClrType, json ?? "{}"));
            }
        }
        return rows;
    }

    /// <summary>
    /// ⭐ <c>CE-2086</c> (§7.6a A1) — commit an instance's params: paused ⇒ in place through the load's own
    /// <c>BlueprintInstanceService.ApplyParams</c> (the instance keeps its state); running ⇒ a replace of the instance by
    /// itself with the new params (a restart — its state was built from the old ones). <c>false</c> = not attached, or bad JSON.
    /// </summary>
    public bool ApplyInstanceParams(Entity entity, int blueprintId, string json, bool running)
    {
        // ⭐ CE-4219 ① — an edit-mode apply is ONE undo entry (undo re-applies the previous params, in place); captured BEFORE the write
        string? before = running || _history is null ? null : InstanceRows(entity).FirstOrDefault(r => r.BlueprintId == blueprintId)?.ParamsJson;
        bool ok = ApplyInstanceParamsRaw(entity, blueprintId, json, running);
        if (ok && !running && _history is not null && before is not null)
            _history.Push("Set blueprint params", () => ApplyInstanceParamsRaw(entity, blueprintId, json, false), () => ApplyInstanceParamsRaw(entity, blueprintId, before, false));
        return ok;
    }

    private unsafe bool ApplyInstanceParamsRaw(Entity entity, int blueprintId, string json, bool running)
    {
        if (_world() is not { } world || _blueprints() is not { } reg || !reg.TryGetById(blueprintId, out var def) || def == null)
            return false;
        if (running)
            return PublishManaged(new Fdp.Toolkit.Blueprints.Events.ReplaceInstanceBlueprintEvent
            {
                Entity = entity, OldBlueprintId = blueprintId, NewBlueprintId = blueprintId, ParamsJson = Json(json),
            });
        if (!Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.TryFindSlot(world, entity, blueprintId, out byte* memory, out int off, out _))
            return false;   // ⭐ CE-3137 U-0: any block
        return Fdp.Toolkit.Blueprints.BlueprintInstanceService.ApplyParams(memory + off, def, Json(json), world, entity) == null;
    }

    /// <summary>What a unit runs now — names and JSON from the start records, never a hash.</summary>
    public sealed record Snapshot(
        string? Task, string TaskParams, BehaviorOrigin TaskOrigin, string? PausedTask,
        string? Sop, string SopParams, BehaviorOrigin SopOrigin, bool SopFaulted,
        RoeFire Fire, RoeReactions Reactions, BehaviorOrigin RoeSetBy);

    public Snapshot? Read(Entity entity)
    {
        if (_world() is not { } world || !world.IsAlive(entity) || !world.HasComponent<BehaviorState>(entity)) return null;
        var view = (ISimulationView)world;
        var state = world.GetComponentRO<BehaviorState>(entity);

        string? task = null, taskJson = "{}";
        if (state.ActiveBehaviorHash != BehaviorIds.None)
        {
            if (world.HasManagedComponent<BehaviorStartRecord>(entity) && view.GetManagedComponentRO<BehaviorStartRecord>(entity) is { } r
                && r.InstanceId == state.InstanceId) { task = r.BehaviorName; taskJson = r.JsonParams; }
            else if (_registry() is { } reg && reg.TryGetName(state.ActiveBehaviorHash, out var n)) task = n;
        }

        string? sop = null, sopJson = "{}";
        BehaviorOrigin sopOrigin = BehaviorOrigin.Unmarked;
        bool faulted = false;
        if (world.IsComponentTypeRegistered<SopState>() && world.HasComponent<SopState>(entity))
        {
            var s = world.GetComponentRO<SopState>(entity);
            if (s.SopHash != BehaviorIds.None)
            {
                sopOrigin = s.SopOrigin;
                faulted   = s.SopFaulted != 0;
                if (world.HasManagedComponent<SopStartRecord>(entity) && view.GetManagedComponentRO<SopStartRecord>(entity) is { } sr)
                { sop = sr.BehaviorName; sopJson = sr.JsonParams; }
            }
        }

        var roe = world.IsComponentTypeRegistered<Roe>() && world.HasComponent<Roe>(entity) ? world.GetComponentRO<Roe>(entity) : default;
        return new Snapshot(task, Json(taskJson), state.Origin, BehaviorIngressSystem.PausedTaskOf(world, entity)?.BehaviorName,
                            sop, Json(sopJson), sopOrigin, faulted,
                            RoeOf.Fire(world, entity), RoeOf.Reactions(world, entity), roe.SetBy);
    }

    /// <summary>Every registered behaviour that is not an SOP, by name (⭐ <c>CE-1019</c>: an SOP is not a task).</summary>
    public IReadOnlyList<string> TaskChoices()
        => _registry() is { } r
            ? r.GetRegisteredNames().Where(n => !IsSop(r, n)).OrderBy(n => n, StringComparer.Ordinal).ToList()
            : Array.Empty<string>();

    /// <summary>⭐ The SOP row's choices: only SOPs (⭐ <c>CE-1019</c>: behaviours that carry SOP orders, the ruled mark) that are
    /// also KNOWN to drive no channel (§4.5 ②) — an unknown set is not offered.</summary>
    public IReadOnlyList<string> SopChoices()
    {
        if (_registry() is not { } r) return Array.Empty<string>();
        return r.GetRegisteredNames()
            .Where(n => r.TryGetId(n, out int id) && r.TryGetDefinition(id, out var d)
                        && d.CarriesSopOrders && d.WritesChannels is { Count: 0 })
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static bool IsSop(BehaviorRegistry r, string name)
        => r.TryGetId(name, out int id) && r.TryGetDefinition(id, out var d) && d is { CarriesSopOrders: true };

    /// <summary>⭐ <c>CE-3164</c> (R-268) — <paramref name="names"/> as picker choices: each with its technology and folder.</summary>
    public IReadOnlyList<Hrot.UI.Common.Facades.BehaviorChoice> AsChoices(IReadOnlyList<string> names)
    {
        if (_registry() is not { } r) return Array.Empty<Hrot.UI.Common.Facades.BehaviorChoice>();
        return names.Select(n => new Hrot.UI.Common.Facades.BehaviorChoice(n, Hrot.UI.Common.Adapters.ScenarioMissionService.TechnologyOf(r, n))
        {
            Group = r.GroupOf(n) ?? Hrot.UI.Common.Facades.BehaviorChoice.OtherGroup,
        }).ToList();
    }

    /// <summary>The behaviour's authored params type — what the params form edits; <c>null</c> = it takes none.</summary>
    /// <summary>⭐ CE-1023 — the one lookup (<see cref="BehaviorParamsForm.ParamsTypeOf"/>), the mission panel's too.</summary>
    public Type? ParamsTypeOf(string behaviour) => BehaviorParamsForm.ParamsTypeOf(behaviour, _registry());

    public bool ApplyTask(Entity entity, string behaviour, string json, bool running)
    {
        // ⭐ CE-4219 ① — edit mode: ONE undo entry, undo re-applies the previous task (or clears it when there was none); captured BEFORE the write
        var before = running || _history is null ? null : Read(entity);
        bool ok = ApplyTaskRaw(entity, behaviour, json, running);
        if (ok && !running && _history is not null)
            _history.Push("Set task", () => ApplyTaskRaw(entity, behaviour, json, false),
                () => { if (before?.Task is { } t) ApplyTaskRaw(entity, t, before.TaskParams, false); else ClearTask(entity, false); });
        return ok;
    }

    private bool ApplyTaskRaw(Entity entity, string behaviour, string json, bool running)
        => running
            ? PublishManaged(new AssignBehaviorEvent { Entity = entity, BehaviorName = behaviour, JsonParams = Json(json), Origin = EditorOrigin })
            : Ingress() is { } i && _world() is { } w && i.AssignNow(w, entity, behaviour, Json(json), EditorOrigin);

    public bool ClearTask(Entity entity, bool running)
        => running
            ? Publish(new ClearBehaviorEvent { Entity = entity, Origin = EditorOrigin })
            : Ingress() is { } i && _world() is { } w && i.ClearNow(w, entity, EditorOrigin);

    public bool ApplySop(Entity entity, string behaviour, string json, bool running)
    {
        // ⭐ CE-4219 ① — edit mode: ONE undo entry, undo re-applies the previous SOP (or clears it)
        var before = running || _history is null ? null : Read(entity);
        bool ok = ApplySopRaw(entity, behaviour, json, running);
        if (ok && !running && _history is not null)
            _history.Push("Set SOP", () => ApplySopRaw(entity, behaviour, json, false),
                () => { if (before?.Sop is { } s) ApplySopRaw(entity, s, before.SopParams, false); else ClearSop(entity, false); });
        return ok;
    }

    private bool ApplySopRaw(Entity entity, string behaviour, string json, bool running)
        => running
            ? PublishManaged(new AssignSopEvent { Entity = entity, BehaviorName = behaviour, JsonParams = Json(json), Origin = EditorOrigin })
            : Ingress() is { } i && _world() is { } w && i.AssignSopNow(w, entity, behaviour, Json(json), EditorOrigin);

    public bool ClearSop(Entity entity, bool running)
        => running
            ? Publish(new ClearSopEvent { Entity = entity, Origin = EditorOrigin })
            : Ingress() is { } i && _world() is { } w && i.ClearSopNow(w, entity, EditorOrigin);

    public bool ApplyRoe(Entity entity, RoeFire fire, RoeReactions reactions, bool running)
    {
        // ⭐ CE-4219 ① — edit mode: ONE undo entry, undo re-applies the previous ROE; captured BEFORE the write
        var before = running || _history is null ? null : Read(entity);
        bool ok = ApplyRoeRaw(entity, fire, reactions, running);
        if (ok && !running && _history is not null && before is not null)
            _history.Push("Set ROE", () => ApplyRoeRaw(entity, fire, reactions, false), () => ApplyRoeRaw(entity, before.Fire, before.Reactions, false));
        return ok;
    }

    private bool ApplyRoeRaw(Entity entity, RoeFire fire, RoeReactions reactions, bool running)
    {
        var evt = new SetRoeEvent { Entity = entity, Fire = fire, Reactions = reactions, Origin = EditorOrigin };
        return running ? Publish(evt) : _world() is { } w && RoeSystem.Apply(w, evt);
    }

    private static string Json(string? json) => string.IsNullOrWhiteSpace(json) ? "{}" : json!;

    private BehaviorIngressSystem? Ingress()
    {
        var reg = _registry();
        if (reg == null) return null;
        if (!ReferenceEquals(reg, _ingressFor)) { _ingress = new BehaviorIngressSystem(reg); _ingressFor = reg; }
        return _ingress;
    }

    private bool Publish<T>(T evt) where T : unmanaged
    {
        if (_world() is not { } w || !w.Bus.IsRegistered<T>()) return false;
        w.Bus.Publish(evt);
        return true;
    }

    private bool PublishManaged<T>(T evt) where T : class
    {
        if (_world() is not { } w || !w.Bus.IsRegisteredManaged<T>()) return false;
        w.Bus.PublishManaged(evt);
        return true;
    }
}

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
