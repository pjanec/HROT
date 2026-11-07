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

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐ P-7 / CE-4303 slice 7a (docs/UX/DESIGN_Mission_Plan_Editor.md §5.11, D8) — the <b>Orders</b> Details tab: the selected unit's plan as sentences
/// (<see cref="OrdersPlanModel"/>), the running task marked, and <i>Jump</i> / <i>Stop</i> through the ONE mission facade every host already passes
/// (<see cref="IMissionEditorService"/>). ⭐ On the shared Details bundle, so every host shows the same tab. ⭐ 7b: a draft per unit — reorder, remove, <i>+ Add task</i>
/// (Wait first, the unit's orders, its behaviours), the <i>then</i> presets and <i>give up after</i>, Apply = the whole plan with its base version. ⛔ Until 7d
/// retires them, the three <c>MissionPanel</c>s still edit too.
/// </summary>
public sealed class OrdersTab : IDetailsViewInstance
{
    private readonly Func<IMissionEditorService?> _missions;
    private readonly Func<EntityRepository?> _world;

    private readonly Func<Fdp.Presentation.Editing.IMapPickContext?>? _pick;
    private readonly Func<string, Type?>? _paramsTypeOf;

    /// <param name="pick">the host's map pick (a field's pick on the map); null ⇒ no pick.</param>
    /// <param name="paramsTypeOf">the host's behaviour → params type (its registry also knows blueprint behaviours); null ⇒ the declared contracts.</param>
    public OrdersTab(Func<IMissionEditorService?> missions, Func<EntityRepository?> world,
                     Func<Fdp.Presentation.Editing.IMapPickContext?>? pick = null, Func<string, Type?>? paramsTypeOf = null)
    { _missions = missions; _world = world; _pick = pick; _paramsTypeOf = paramsTypeOf; }

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
        if (r is { Success: true }) _draft = null;
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
        var rows = LastRows = OrdersPlanModel.RowsOf(draft, _dirty ? null : queue);
        var svc = _missions();
        if (rows.Count == 0) ImGui.TextDisabled("No plan — this unit runs its standing behaviour.");
        int remove = -1, up = -1, down = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var task = draft.Tasks[i];
            ImGui.PushID(i);
            ImGui.TextColored(ColourOf(r.State), $"{i + 1}.");
            ImGui.SameLine();
            ImGui.TextUnformatted(r.Sentence);
            if (r.Problem is { } why) { ImGui.SameLine(); ImGui.TextColored(new System.Numerics.Vector4(1f, 0.55f, 0.2f, 1f), "⚠"); if (ImGui.IsItemHovered()) ImGui.SetTooltip(why); }
            ImGui.SameLine();
            if (ImGui.SmallButton("↑")) up = i;
            ImGui.SameLine();
            if (ImGui.SmallButton("↓")) down = i;
            ImGui.SameLine();
            if (ImGui.SmallButton("✕")) remove = i;
            if (!_dirty && r.State is OrdersTaskState.Planned && svc != null && queue != null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Jump")) _ = svc.SendControlCommandAsync(net, eMissionCommandType.CMD_JUMP_TO_TASK, r.TaskId);
            }
            ImGui.Indent();
            if (DrawFields(task)) _dirty = true;
            if (DrawThen(task)) _dirty = true;
            if (DrawGiveUp(task)) _dirty = true;
            ImGui.Unindent();
            ImGui.PopID();
        }
        if (remove >= 0) { draft.Tasks.RemoveAt(remove); _dirty = true; }
        if (up >= 0) { OrdersPlanEdits.Move(draft, up, -1); _dirty = true; }
        if (down >= 0) { OrdersPlanEdits.Move(draft, down, +1); _dirty = true; }

        if (ImGui.Button("+ Add task")) ImGui.OpenPopup("orders_add");
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
        if (_status is { } st) ImGui.TextDisabled(st);
    }

    /// <summary>The task's own fields (an order's target / place, a behaviour's params) through the ONE params form; an edit rewrites its params JSON.</summary>
    private bool DrawFields(MissionTask task)
    {
        if (ParamsTypeOfTask(task.BehaviorId) is not { } type) return false;
        if (!_forms.TryGetValue(task.TaskId, out var form)) _forms[task.TaskId] = form = new Hrot.Presentation.Behavior.BehaviorParamsEditor(_editService);
        form.Sync(type, task.BehaviorParams);
        if (form.Session is not { } session) return false;
        Hrot.Presentation.Behavior.BehaviorParamsForm.DrawFields(session, _pick?.Invoke(), "fields");
        if (form.Edited() is not { } json) return false;
        task.BehaviorParams = json;
        return true;
    }

    private static readonly string[] ThenLabels = { "when done", "after", "when under fire", "when strength below", "an advanced condition" };

    private bool DrawThen(MissionTask task)
    {
        var (preset, number) = OrdersPlanEdits.ThenOf(task);
        if (preset == ThenPreset.Advanced) _advanced.Add(task.TaskId);
        bool advanced = _advanced.Contains(task.TaskId);
        bool changed = false;
        ImGui.SetNextItemWidth(170);
        int sel = advanced ? (int)ThenPreset.Advanced : (int)preset;
        if (ImGui.Combo("then", ref sel, ThenLabels, ThenLabels.Length) && (ThenPreset)sel == ThenPreset.Advanced && !advanced)
        {
            _advanced.Add(task.TaskId);   // the condition editor opens on what the task says now (its preset's condition)
            return false;
        }
        if ((ThenPreset)sel != ThenPreset.Advanced && advanced) _advanced.Remove(task.TaskId);
        if (_advanced.Contains(task.TaskId))
        {
            // ⭐ Advanced condition… — the ONE condition editor, Mission context (E5: its WHO defaults to this unit)
            if (!_conditions.TryGetValue(task.TaskId, out var editor)) _conditions[task.TaskId] = editor = new Fdp.Presentation.Editing.ConditionEditor(Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionContexts.Mission);
            Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? dto = OrdersPlanEdits.ConditionOf(task);
            if (editor.Draw("then_condition", ref dto, _pick?.Invoke())) { OrdersPlanEdits.WithThen(task, dto); return true; }
            return false;
        }
        if (sel != (int)preset)
        {
            var p = (ThenPreset)sel;
            OrdersPlanEdits.WithThen(task, OrdersPlanEdits.Then(p, p == ThenPreset.After ? 300 : p == ThenPreset.StrengthBelow ? 0.5 : 0));
            changed = true;
            (preset, number) = OrdersPlanEdits.ThenOf(task);
        }
        if (preset == ThenPreset.After)
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

    private static System.Numerics.Vector4 ColourOf(OrdersTaskState s) => s switch
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
                                            Func<Fdp.Presentation.Editing.IMapPickContext?>? pick = null, Func<string, Type?>? paramsTypeOf = null)
    {
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(world);
        var instance = new OrdersTab(missions, world, pick, paramsTypeOf);
        return new DetailsViewDescriptor(
            Id:        Id,
            Title:     "Orders",
            Rank:      32,
            AppliesTo: ctx => ctx.Entities.Count == 1 && (instance.RowsFor(ctx.Entities[0]).Count > 0 || instance.CanTakeAPlan(ctx.Entities[0])),
            Create:    () => instance);
    }
}
