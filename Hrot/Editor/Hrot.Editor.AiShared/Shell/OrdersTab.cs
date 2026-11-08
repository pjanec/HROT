using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Hrot.Core.Mission;
using MissionTask = Hrot.Core.Mission.MissionTask;
using MissionPlan = Hrot.Core.Mission.MissionPlan;
using Hrot.UI.Common.Facades;
using ImGuiNET;
using NodeEditor.Core.Widgets;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐ P-7 / CE-4303 slice 7a (docs/UX/DESIGN_Mission_Plan_Editor.md §5.11, D8) — the <b>Orders</b> Details tab: the selected unit's plan as sentences
/// (<see cref="OrdersPlanModel"/>), the running task marked, and <i>Jump</i> / <i>Stop</i> through the ONE mission facade every host already passes
/// (<see cref="IMissionEditorService"/>). ⭐ On the shared Details bundle, so every host shows the same tab. ⭐ 7b: a draft per unit — reorder, remove, <i>+ Add task</i>
/// (Wait first, the unit's orders, its behaviours), the <i>then</i> presets and <i>give up after</i>, Apply = the whole plan with its base version. 7d: the three <c>MissionPanel</c> windows/views are retired — this tab is the only plan editor.
/// </summary>
public sealed class OrdersTab : IDetailsViewInstance
{
    private readonly Func<IMissionEditorService?> _missions;
    private readonly Func<EntityRepository?> _world;

    private readonly Func<Fdp.Presentation.Editing.IMapPickContext?>? _pick;
    private readonly Func<string, Type?>? _paramsTypeOf;

    /// <param name="pick">the host's map pick (a field's pick on the map); null ⇒ no pick.</param>
    /// <param name="paramsTypeOf">the host's behaviour → params type (its registry also knows blueprint behaviours); null ⇒ the declared contracts.</param>
    /// <param name="host">⭐ P-8 8b — the host's actions: the picker's <i>New rule…</i> asks <c>AddRuleAboutUnit</c> (the Rules gallery with this unit
    /// filled in, bound by the RulesBundle on every host); null ⇒ the entry is greyed.</param>
    public OrdersTab(Func<IMissionEditorService?> missions, Func<EntityRepository?> world,
                     Func<Fdp.Presentation.Editing.IMapPickContext?>? pick = null, Func<string, Type?>? paramsTypeOf = null, Func<Hrot.Common.Interactions.HostActions?>? host = null)
    { _missions = missions; _world = world; _pick = pick; _paramsTypeOf = paramsTypeOf; _host = host; }

    private readonly Func<Hrot.Common.Interactions.HostActions?>? _host;

    /// <summary>⭐ P-8 8b — the picker's <i>New rule…</i> can ask this host (it was handed the host's actions — the forwarding rail).</summary>
    public bool OffersNewRule => _host?.Invoke() != null;

    /// <summary>⭐ P-8 8b — the rules and phases the picker offers, read now from <paramref name="w"/> (rails; the draw reads them at most once a second).</summary>
    public IReadOnlyList<(int Net, string Title, int Phase)> RuleChoicesOf(EntityRepository w) { _choicesAt = long.MinValue; RefreshChoices(w); return _rules; }

    /// <summary>
    /// ⭐ 7b — a task's params type: an order's is its DTO from the ONE order registry (<c>OrderMenu</c>, the lookup G1 resolves its references
    /// by); a behaviour's the host's lookup (or the declared contracts). Null ⇒ the task has no fields.
    /// </summary>
    public Type? ParamsTypeOfTask(string? behaviourId)
    {
        if (string.IsNullOrEmpty(behaviourId)) return null;
        if (behaviourId.StartsWith("Order.", StringComparison.Ordinal))
            return Hrot.Common.Orders.OrderMenu.TryGetByName(behaviourId.Substring("Order.".Length), out var o) ? o.Dto : null;
        return _paramsTypeOf?.Invoke(behaviourId) ?? Hrot.Presentation.Behavior.BehaviorParamsForm.ParamsTypeOf(behaviourId, null);
    }

    private readonly StructEdit.Core.IComponentEditService _editService = Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build();
    private readonly Dictionary<Guid, Hrot.Presentation.Behavior.BehaviorParamsEditor> _forms = new();
    private readonly Dictionary<Guid, Fdp.Presentation.Editing.ConditionEditor> _conditions = new();
    /// <summary>⭐ P-7 7d — each task's own view of the shared pick context (<c>$.tasks[&lt;id&gt;]</c> and its <c>.then</c>), cached so a frame allocates nothing.
    /// ⛔ Unscoped, every task's form keyed its picks on the same <c>$.X</c> ⇒ a pick answered whichever task asked first (the retired panel scoped them).</summary>
    private readonly Dictionary<Guid, (Fdp.Presentation.Editing.IMapPickContext Inner, Fdp.Presentation.Editing.IMapPickContext Fields, Fdp.Presentation.Editing.IMapPickContext Then)> _scopes = new();
    private readonly HashSet<Guid> _advanced = new();

    /// <summary>The rows the last frame drew (a rail reads them).</summary>
    public IReadOnlyList<OrdersTaskRow> LastRows { get; private set; } = Array.Empty<OrdersTaskRow>();

    /// <summary>The rows for <paramref name="entity"/>, read now (headless).</summary>
    /// <summary>7b — a unit with no plan still gets the tab when it can be ordered (an order offered for its type) and the host has the facade.</summary>
    public bool CanTakeAPlan(Entity entity)
        => _missions() != null && _world() is { } w && w.IsAlive(entity) && w.HasComponent<NetworkIdentity>(entity)
           && Hrot.Common.Orders.OrderMenu.OfferedFor(w.GetDisType(entity)).Count > 0;

    public IReadOnlyList<OrdersTaskRow> RowsFor(Entity entity)
    {
        if (_missions() is not { } svc || _world() is not { } w || !w.IsAlive(entity) || !w.HasComponent<NetworkIdentity>(entity)) return Array.Empty<OrdersTaskRow>();
        int net = w.GetComponentRO<NetworkIdentity>(entity).Value;
        var (plan, _) = svc.GetMissionSnapshot(net);
        MissionPlanQueue? queue = w.IsComponentTypeRegistered<MissionPlanQueue>() && w.HasComponent<MissionPlanQueue>(entity) ? w.GetComponentRO<MissionPlanQueue>(entity) : null;
        return OrdersPlanModel.RowsOf(plan, queue);
    }

    // ── 7b: the draft the user edits (one per selected unit), applied as ONE plan with its base version ─────────────────────
    private int _draftNet;
    private MissionPlan? _draft;
    private long _baseVersion;
    private bool _dirty;
    private string? _status;

    /// <summary>The draft being edited (a rail reads and drives it), or null.</summary>
    public MissionPlan? Draft => _draft;
    public bool Dirty => _dirty;

    /// <summary>Starts (or keeps) the draft of <paramref name="net"/> from the service's snapshot.</summary>
    public MissionPlan EnsureDraft(int net)
    {
        if (_draft != null && _draftNet == net) return _draft;
        var (plan, version) = _missions()?.GetMissionSnapshot(net) ?? (null, 0);
        _draft = plan is null ? new MissionPlan() : OrdersPlanEdits.Copy(plan);
        _draftNet = net; _baseVersion = version; _dirty = false; _status = null;
        return _draft;
    }

    /// <summary>Marks the draft changed (a rail calls it after an edit it made).</summary>
    public void Touch() => _dirty = true;

    /// <summary>Applies the draft: the WHOLE plan with its base version through the one facade (D9); refused while a task cannot run.</summary>
    public string? Apply()
    {
        if (_draft is null || _missions() is not { } svc || _draftNet == 0) return "nothing to apply";
        if (OrdersPlanEdits.RefusalOf(_draft) is { } why) return _status = why;
        var sent = OrdersPlanEdits.Copy(_draft);
        _pending = svc.CommitMissionAsync(_draftNet, sent, _baseVersion);   // settled on the UI thread (PollCommit), never from a continuation
        _dirty = false;
        _status = "sent…";
        return null;
    }

    private System.Threading.Tasks.Task<Hrot.UI.Common.Models.MissionCommitResult>? _pending;

    /// <summary>Settles a finished commit: its result shown, the draft re-read from the snapshot (the new version). Called each frame by Draw.</summary>
    public void PollCommit()
    {
        if (_pending is not { IsCompleted: true } done) return;
        _pending = null;
        var r = done.IsCompletedSuccessfully ? done.Result : null;
        _status = r is null ? "the commit failed" : r.Success ? $"applied (version {r.NewVersion})" : $"not applied — {r.ErrorMessage}";
        Conflicted = r is { Success: false, Conflict: true };   // ⭐ 7c — the draft is KEPT: the user picks Reload or Overwrite
        if (r is { Success: true }) _draft = null;
    }

    /// <summary>⭐ P-7 7c (D9) — the last commit was refused because the plan changed since it was opened.</summary>
    public bool Conflicted { get; private set; }

    /// <summary>⭐ 7c — <i>Reload</i>: drops the draft; the tab re-reads the plan as it is now.</summary>
    public void Reload() { _draft = null; Conflicted = false; _status = null; }

    /// <summary>⭐ 7c — <i>Overwrite</i>: sends the draft again over the plan as it is NOW (its current version as the base).</summary>
    public string? Overwrite()
    {
        if (!Conflicted || _missions() is not { } svc) return "nothing to overwrite";
        _baseVersion = svc.GetMissionSnapshot(_draftNet).Version;
        Conflicted = false;
        return Apply();
    }

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities.Count != 1 || _world() is not { } w) return;
        var entity = context.Entities[0];
        if (!w.IsAlive(entity) || !w.HasComponent<NetworkIdentity>(entity)) return;
        int net = w.GetComponentRO<NetworkIdentity>(entity).Value;
        PollCommit();
        var draft = EnsureDraft(net);
        MissionPlanQueue? queue = w.IsComponentTypeRegistered<MissionPlanQueue>() && w.HasComponent<MissionPlanQueue>(entity) ? w.GetComponentRO<MissionPlanQueue>(entity) : null;
        // ⭐ CE-3403 — the rows (every task\'s sentence parses its conditions) and the list\'s summaries are rebuilt ONLY when the plan or the queue changed: a steady frame builds nothing (R-2084)
        var queueNow = _dirty ? null : queue;
        uint sig = PlanSignature(draft, queueNow);
        if (!ReferenceEquals(draft, _rowsFor) || sig != _rowsSig)
        {
            _rowsFor = draft; _rowsSig = sig;
            var built = LastRows = OrdersPlanModel.RowsOf(draft, queueNow);
            _summaries.Clear();
            int h = built.Count;
            for (int i = 0; i < built.Count; i++) { string line = built[i].Sentence + (built[i].Problem is { } pr ? "  ! " + pr : ""); _summaries.Add(line); h = Fdp.Core.StableHash.Combine(h, unchecked((int)Fdp.Core.StableHash.Fnv1a32(line)), 0); }
            _summariesHash = h;
        }
        var rows = LastRows;
        var svc = _missions();
        if (rows.Count == 0) ImGui.TextDisabled("No plan — this unit runs its standing behaviour.");
        // ⭐ CE-3395 — the plan\'s tasks are the kit\'s LIST block: one summary row per task (⋮⋮ drag · number · state glyph · the sentence · ⋮ menu), the task\'s fields / then / give-up open under the row; Add at the end opens the add menu.
        // The ops go to the EXISTING draft edits (OrdersPlanEdits.Move, draft.Tasks.RemoveAt, the add menu\'s NewWait / NewOrder / NewBehaviour).
        int hash = _summariesHash;
        ListBlock.Begin("tasks", _summaries, TaskOptions, hash);
        for (int v = 0; v < ListBlock.VisibleCount; v++)
        {
            int i = ListBlock.VisibleIndex(v);
            if (i >= rows.Count) break;
            var r = rows[i];
            var task = draft.Tasks[i];
            bool open = ListBlock.ItemBegin(i, new IconGlyph("\uf111", ColourOf(r.State)));   // a dot in the task's state colour
            if (r.Problem is { } why) { ImGui.TextColored(new System.Numerics.Vector4(1f, 0.55f, 0.2f, 1f), "⚠ " + why); }
            if (!_dirty && r.State is OrdersTaskState.Planned && svc != null && queue != null)
            {
                if (ImGui.SmallButton("Jump")) _ = svc.SendControlCommandAsync(net, eMissionCommandType.CMD_JUMP_TO_TASK, r.TaskId);
            }
            if (open)
            {
                if (DrawFields(task)) _dirty = true;
                if (DrawThen(task, entity, net)) _dirty = true;
                if (DrawGiveUp(task)) _dirty = true;
            }
            ListBlock.ItemEnd();
        }
        var taskOp = ListBlock.End();
        ApplyTaskOp(draft, taskOp);

        if (ImGui.BeginPopup("orders_add"))
        {
            if (ImGui.Selectable("Wait")) { draft.Tasks.Add(OrdersPlanEdits.NewWait()); _dirty = true; }   // E9: Wait first
            ImGui.Separator();
            foreach (var o in Hrot.Common.Orders.OrderMenu.OfferedFor(w.GetDisType(entity)))
                if (ImGui.Selectable(o.Name)) { draft.Tasks.Add(OrdersPlanEdits.NewOrder(o.Name)); _dirty = true; }
            if (svc != null)
            {
                var behaviours = svc.GetAvailableBehaviorChoices(net);
                if (behaviours.Count > 0) ImGui.Separator();
                for (int b = 0; b < behaviours.Count; b++)
                    if (ImGui.Selectable(behaviours[b].Name)) { draft.Tasks.Add(OrdersPlanEdits.NewBehaviour(behaviours[b].Name)); _dirty = true; }
            }
            ImGui.EndPopup();
        }

        string? refusal = OrdersPlanEdits.RefusalOf(draft);
        ImGui.SameLine();
        ImGui.BeginDisabled(!_dirty || refusal != null || svc == null);
        if (ImGui.Button("Apply")) Apply();
        ImGui.EndDisabled();
        if (refusal != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(refusal);
        ImGui.SameLine();
        ImGui.BeginDisabled(!_dirty);
        if (ImGui.Button("Revert")) _draft = null;
        ImGui.EndDisabled();
        if (svc != null && queue != null && !_dirty) { ImGui.SameLine(); if (ImGui.Button("Stop the plan")) _ = svc.SendControlCommandAsync(net, eMissionCommandType.CMD_ABORT_ALL, Guid.Empty); }
        if (Conflicted)
        {
            // ⭐ 7c (D9) — someone else changed this plan: keep mine over theirs, or take theirs
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.55f, 0.2f, 1f), "The plan was changed by someone else since you opened it.");
            if (ImGui.Button("Reload")) Reload();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Discard your changes and show the plan as it is now.");
            ImGui.SameLine();
            if (ImGui.Button("Overwrite")) Overwrite();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Apply your plan over theirs.");
        }
        else if (_status is { } st) ImGui.TextDisabled(st);
    }

    private readonly List<string> _summaries = new();
    private MissionPlan? _rowsFor; private uint _rowsSig; private int _summariesHash;

    /// <summary>⭐ CE-3403 — a cheap content signature of the plan and its running state (task ids, behaviours, every trigger\'s type and params, each task\'s state): allocation-free; equal ⇒ the rows are the cached ones.</summary>
    private static uint PlanSignature(MissionPlan plan, MissionPlanQueue? queue)
    {
        uint h = Fdp.Core.StableHash.Add(2166136261u, plan.Tasks.Count);
        for (int i = 0; i < plan.Tasks.Count; i++)
        {
            var t = plan.Tasks[i];
            h = Fdp.Core.StableHash.Add(h, t.TaskId.GetHashCode());
            h = Fdp.Core.StableHash.Add(h, unchecked((int)Fdp.Core.StableHash.Fnv1a32(t.BehaviorId ?? "")));
            var triggers = t.Triggers;
            if (triggers != null)
                for (int k = 0; k < triggers.Count; k++)
                {
                    h = Fdp.Core.StableHash.Add(h, unchecked((int)Fdp.Core.StableHash.Fnv1a32(triggers[k].Type ?? "")));
                    h = Fdp.Core.StableHash.Add(h, unchecked((int)Fdp.Core.StableHash.Fnv1a32(triggers[k].Params ?? "")));
                }
            h = Fdp.Core.StableHash.Add(h, (int)OrdersPlanModel.StateOf(i, queue));
        }
        return h;
    }
    private static readonly ListOptions TaskOptions = new()
    {
        AddLabel = "Add task",
        Disabled = ListCmd.InsertAbove | ListCmd.InsertBelow | ListCmd.Duplicate | ListCmd.Copy | ListCmd.Paste | ListCmd.Reset,   // the edits the plan has: Move · Remove · Add
    };

    /// <summary>The list's op applied to the draft's EXISTING edits (a drag moves by single steps through <see cref="OrdersPlanEdits.Move"/>); <i>Add</i> opens the add menu.</summary>
    internal void ApplyTaskOp(MissionPlan draft, ListOp op)
    {
        switch (op.Kind)
        {
            case ListOpKind.Add: ImGui.OpenPopup("orders_add"); break;
            case ListOpKind.Delete when op.Index >= 0 && op.Index < draft.Tasks.Count: draft.Tasks.RemoveAt(op.Index); _dirty = true; break;
            case ListOpKind.MoveUp: OrdersPlanEdits.Move(draft, op.Index, -1); _dirty = true; break;
            case ListOpKind.MoveDown: OrdersPlanEdits.Move(draft, op.Index, +1); _dirty = true; break;
            case ListOpKind.Move:
            {
                int at = op.Index;
                while (at < op.Target) { OrdersPlanEdits.Move(draft, at, +1); at++; }
                while (at > op.Target) { OrdersPlanEdits.Move(draft, at, -1); at--; }
                _dirty = op.Index != op.Target || _dirty;
                break;
            }
        }
    }

    /// <summary>⭐ P-7 7d — the pick context scoped to one task (its fields, or its <i>then</i> condition); null when the host gives none.</summary>
    internal Fdp.Presentation.Editing.IMapPickContext? PickFor(Guid taskId, bool then)
    {
        if (_pick?.Invoke() is not { } inner) return null;
        if (!_scopes.TryGetValue(taskId, out var s) || !ReferenceEquals(s.Inner, inner))
        {
            var scope = "$.tasks[" + taskId.ToString("N") + "]";
            _scopes[taskId] = s = (inner, new Fdp.Presentation.Editing.ScopedMapPickContext(inner, scope), new Fdp.Presentation.Editing.ScopedMapPickContext(inner, scope + ".then"));
        }
        return then ? s.Then : s.Fields;
    }

    /// <summary>The task's own fields (an order's target / place, a behaviour's params) through the ONE params form; an edit rewrites its params JSON.</summary>
    private bool DrawFields(MissionTask task)
    {
        if (ParamsTypeOfTask(task.BehaviorId) is not { } type) return false;
        if (!_forms.TryGetValue(task.TaskId, out var form)) _forms[task.TaskId] = form = new Hrot.Presentation.Behavior.BehaviorParamsEditor(_editService);
        form.Sync(type, task.BehaviorParams);
        if (form.Session is not { } session) return false;
        Hrot.Presentation.Behavior.BehaviorParamsForm.DrawFields(session, PickFor(task.TaskId, then: false), "fields");
        if (form.Edited() is not { } json) return false;
        task.BehaviorParams = json;
        return true;
    }

    // ⭐ P-8 8b (§5.6, mission-plan-detail-visual.svg A) — the picker in the mockup's order: the plain presets, then a phase / a unit's task / a rule,
    //   then Advanced condition… and New rule…. A phase and a rule store the SAME condition (the trigger fired); which one shows is the target's phase.
    private enum ThenItem { WhenDone, After, UnderFire, StrengthBelow, PhaseBegins, UnitFinishesTask, RuleFires, Advanced, NewRule }
    private static readonly string[] ThenLabels = { "when done", "after", "when under fire", "when strength below", "when a phase begins", "when a unit finishes a task", "when a rule fires", "an advanced condition", "New rule..." };

    /// <summary>The rules (and phases) and the units with a plan the picker offers, read at most once a second (never per frame), with each rule's title / phase.</summary>
    private readonly List<(int Net, string Title, int Phase)> _rules = new();
    private readonly List<(int Net, string Name)> _planUnits = new();
    private long _choicesAt = long.MinValue;

    private void RefreshChoices(EntityRepository w)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_choicesAt != long.MinValue && now - _choicesAt < System.Diagnostics.Stopwatch.Frequency) return;
        _choicesAt = now;
        _rules.Clear(); _planUnits.Clear();
        foreach (var r in Hrot.UI.Common.Rules.RulesReader.Read(w))
            if (r.Status.Role == Fdp.Toolkit.ScenarioRules.ObjectiveRole.Trigger) _rules.Add((r.NetworkId, r.Status.Title.ToString(), r.Status.Phase));
        _rules.Sort((a, b) => a.Phase != b.Phase ? (a.Phase == 0 ? 1 : b.Phase == 0 ? -1 : a.Phase.CompareTo(b.Phase)) : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        if (w.IsComponentTypeRegistered<MissionPlanQueue>())
            foreach (var e in w.Query().With<MissionPlanQueue>().With<NetworkIdentity>().Build())
            {
                int n = w.GetComponentRO<NetworkIdentity>(e).Value;
                string name = w.IsComponentTypeRegistered<Fdp.Core.EntityInfo>() && w.HasComponent<Fdp.Core.EntityInfo>(e)
                    ? w.GetComponentRO<Fdp.Core.EntityInfo>(e).Name.ToString() : $"#{n}";
                _planUnits.Add((n, name));
            }
        _planUnits.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    private string RuleLabel(int net)
    {
        foreach (var r in _rules) if (r.Net == net) return r.Phase > 0 ? $"Phase {r.Phase} {r.Title}" : r.Title;
        return net > 0 ? $"rule #{net}" : "(pick one)";
    }

    private bool IsPhase(int net) { foreach (var r in _rules) if (r.Net == net) return r.Phase > 0; return false; }

    private Fdp.Toolkit.Replication.EntityLink LinkTo(EntityRepository w, int net) => Hrot.UI.Common.Rules.RuleGallery.LinkTo(net, Hrot.UI.Common.Rules.AuthoredRules.ScopeOf(w));

    private bool DrawThen(MissionTask task, Entity unit, int unitNet)
    {
        if (_world() is not { } w) return false;
        RefreshChoices(w);
        var (preset, number) = OrdersPlanEdits.ThenOf(task);
        if (preset == ThenPreset.Advanced) _advanced.Add(task.TaskId);
        bool advanced = _advanced.Contains(task.TaskId);
        var target = OrdersPlanEdits.TargetOf(task);
        int targetNet = target.Ref.NetworkId;
        ThenItem shown = advanced ? ThenItem.Advanced : preset switch
        {
            ThenPreset.After => ThenItem.After, ThenPreset.UnderFire => ThenItem.UnderFire, ThenPreset.StrengthBelow => ThenItem.StrengthBelow,
            ThenPreset.RuleFires => IsPhase(targetNet) ? ThenItem.PhaseBegins : ThenItem.RuleFires,
            ThenPreset.UnitFinishesTask => ThenItem.UnitFinishesTask,
            _ => ThenItem.WhenDone,
        };
        bool changed = false;
        ImGui.SetNextItemWidth(190);
        int sel = (int)shown;
        if (ImGui.Combo("then", ref sel, ThenLabels, ThenLabels.Length) && sel != (int)shown)
        {
            var item = (ThenItem)sel;
            switch (item)
            {
                case ThenItem.Advanced:
                    _advanced.Add(task.TaskId);   // the condition editor opens on what the task says now (its preset's condition)
                    return false;
                case ThenItem.NewRule:
                    _host?.Invoke()?.Request(Hrot.Common.Constants.GlobalActionIds.AddRuleAboutUnit, unit);   // the gallery about this unit; the then is unchanged
                    return false;
                case ThenItem.PhaseBegins or ThenItem.RuleFires:
                {
                    _advanced.Remove(task.TaskId);
                    int first = 0;
                    foreach (var r in _rules) if ((r.Phase > 0) == (item == ThenItem.PhaseBegins)) { first = r.Net; break; }
                    OrdersPlanEdits.WithThen(task, OrdersPlanEdits.ThenRuleFires(first > 0 ? LinkTo(w, first) : Fdp.Toolkit.Replication.EntityLink.None));
                    return true;
                }
                case ThenItem.UnitFinishesTask:
                {
                    _advanced.Remove(task.TaskId);
                    int first = 0;
                    foreach (var u in _planUnits) if (u.Net != unitNet) { first = u.Net; break; }
                    OrdersPlanEdits.WithThen(task, OrdersPlanEdits.ThenUnitFinishes(first > 0 ? LinkTo(w, first) : Fdp.Toolkit.Replication.EntityLink.None, 1));
                    return true;
                }
            }
            _advanced.Remove(task.TaskId);
            var p = item switch { ThenItem.After => ThenPreset.After, ThenItem.UnderFire => ThenPreset.UnderFire, ThenItem.StrengthBelow => ThenPreset.StrengthBelow, _ => ThenPreset.WhenDone };
            OrdersPlanEdits.WithThen(task, OrdersPlanEdits.Then(p, p == ThenPreset.After ? 300 : p == ThenPreset.StrengthBelow ? 0.5 : 0));
            changed = true;
            (preset, number) = OrdersPlanEdits.ThenOf(task);
            shown = item;
        }
        if (_advanced.Contains(task.TaskId))
        {
            // ⭐ Advanced condition… — the ONE condition editor, Mission context (E5: its WHO defaults to this unit)
            if (!_conditions.TryGetValue(task.TaskId, out var editor)) _conditions[task.TaskId] = editor = new Fdp.Presentation.Editing.ConditionEditor(Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionContexts.Mission);
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? dto = OrdersPlanEdits.ConditionOf(task);
            if (editor.Draw("then_condition", ref dto, PickFor(task.TaskId, then: true))) { OrdersPlanEdits.WithThen(task, dto); return true; }
            return changed;
        }
        if (shown is ThenItem.PhaseBegins or ThenItem.RuleFires)
        {
            // which phase / rule (the mockup's ▸ submenu, as a second chooser beside the preset)
            bool phases = shown == ThenItem.PhaseBegins;
            ImGui.SameLine(); ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("##then-rule", RuleLabel(targetNet)))
            {
                foreach (var r in _rules)
                    if ((r.Phase > 0) == phases && ImGui.Selectable(r.Phase > 0 ? $"Phase {r.Phase} {r.Title}##{r.Net}" : $"{r.Title}##{r.Net}", r.Net == targetNet))
                    { OrdersPlanEdits.WithThen(task, OrdersPlanEdits.ThenRuleFires(LinkTo(w, r.Net))); changed = true; }
                ImGui.EndCombo();
            }
        }
        else if (shown == ThenItem.UnitFinishesTask)
        {
            ImGui.SameLine(); ImGui.SetNextItemWidth(160);
            string unitName = "(pick a unit)";
            foreach (var u in _planUnits) if (u.Net == targetNet) { unitName = u.Name; break; }
            if (ImGui.BeginCombo("##then-unit", unitName))
            {
                foreach (var u in _planUnits)
                    if (u.Net != unitNet && ImGui.Selectable($"{u.Name}##{u.Net}", u.Net == targetNet))
                    { OrdersPlanEdits.WithThen(task, OrdersPlanEdits.ThenUnitFinishes(LinkTo(w, u.Net), (int)Math.Max(1, number))); changed = true; }
                ImGui.EndCombo();
            }
            ImGui.SameLine(); ImGui.AlignTextToFramePadding(); ImGui.TextUnformatted("task"); ImGui.SameLine(); ImGui.SetNextItemWidth(60);
            int n = (int)Math.Max(1, number);
            if (ImGui.InputInt("##then-task", ref n, 1) && n >= 1 && n <= MissionPlanQueue.MaxTasks)
            { OrdersPlanEdits.WithThen(task, OrdersPlanEdits.ThenUnitFinishes(target, n)); changed = true; }
        }
        else if (preset == ThenPreset.After)
        {
            ImGui.SameLine(); ImGui.SetNextItemWidth(80);
            float min = (float)(number / 60.0);
            if (ImGui.DragFloat("min##then", ref min, 0.5f, 0f, 600f, "%.1f")) { OrdersPlanEdits.WithThen(task, OrdersPlanEdits.Then(ThenPreset.After, Math.Max(0, min) * 60.0)); changed = true; }
        }
        else if (preset == ThenPreset.StrengthBelow)
        {
            ImGui.SameLine(); ImGui.SetNextItemWidth(80);
            float pct = (float)(number * 100);
            if (ImGui.DragFloat("%##then", ref pct, 1f, 0f, 100f, "%.0f")) { OrdersPlanEdits.WithThen(task, OrdersPlanEdits.Then(ThenPreset.StrengthBelow, Math.Clamp(pct, 0, 100) / 100.0)); changed = true; }
        }
        return changed;
    }

    private static bool DrawGiveUp(MissionTask task)
    {
        double? giveUp = OrdersPlanEdits.GiveUpOf(task);
        bool on = giveUp.HasValue, changed = false;
        if (ImGui.Checkbox("give up after", ref on)) { OrdersPlanEdits.WithGiveUp(task, on ? 600 : null); changed = true; giveUp = OrdersPlanEdits.GiveUpOf(task); }
        if (giveUp is { } s)
        {
            ImGui.SameLine(); ImGui.SetNextItemWidth(80);
            float min = (float)(s / 60.0);
            if (ImGui.DragFloat("min##giveup", ref min, 0.5f, 0f, 600f, "%.1f")) { OrdersPlanEdits.WithGiveUp(task, Math.Max(0, min) * 60.0); changed = true; }
        }
        return changed;
    }

    internal static System.Numerics.Vector4 ColourOf(OrdersTaskState s) => s switch
    {
        OrdersTaskState.Running => new(0.35f, 0.75f, 1f, 1f),
        OrdersTaskState.Done    => new(0.4f, 0.85f, 0.4f, 1f),
        OrdersTaskState.Failed  => new(0.95f, 0.35f, 0.35f, 1f),
        OrdersTaskState.Halted  => new(1f, 0.7f, 0.2f, 1f),
        _                       => new(0.7f, 0.7f, 0.7f, 1f),
    };

    public void Dispose() { }
}

/// <summary>⭐ P-7 / 7a — the descriptor: offered when ONE selected unit has a plan (rank 32, beside the Rule tab).</summary>
public static class OrdersTabDescriptor
{
    public const string Id = "details.orders";

    public static DetailsViewDescriptor For(Func<IMissionEditorService?> missions, Func<EntityRepository?> world,
                                            Func<Fdp.Presentation.Editing.IMapPickContext?>? pick = null, Func<string, Type?>? paramsTypeOf = null,
                                            Func<Hrot.Common.Interactions.HostActions?>? host = null)
    {
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(world);
        var instance = new OrdersTab(missions, world, pick, paramsTypeOf, host);
        return new DetailsViewDescriptor(
            Id:        Id,
            Title:     "Orders",
            Rank:      32,
            AppliesTo: ctx => ctx.Entities.Count == 1 && (instance.RowsFor(ctx.Entities[0]).Count > 0 || instance.CanTakeAPlan(ctx.Entities[0])),
            Create:    () => instance);
    }
}
