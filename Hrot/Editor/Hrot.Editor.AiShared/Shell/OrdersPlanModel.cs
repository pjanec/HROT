using System;
using System.Collections.Generic;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Core.Mission;
using MissionTrigger = Hrot.Core.Mission.MissionTrigger;
using MissionTask = Hrot.Core.Mission.MissionTask;
using MissionPlan = Hrot.Core.Mission.MissionPlan;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>Where a task stands in a running plan (P-7 / 7a).</summary>
public enum OrdersTaskState { Planned, Running, Done, Failed, Halted }

/// <summary>One task of a plan as the Orders tab shows it: the sentence, its state, its id (for Jump).</summary>
public sealed record OrdersTaskRow(Guid TaskId, string Sentence, OrdersTaskState State, string? Problem);

/// <summary>
/// ⭐ P-7 / CE-4303 slice 7a (docs/UX/DESIGN_Mission_Plan_Editor.md §5.11, D8) — the headless half of the Orders tab: a plan as SENTENCES, one per
/// task — <i>"Move, then when done"</i>, a Wait <i>"Wait — until …"</i> (E9) — with the running task taken from the unit's replicated
/// <see cref="MissionPlanQueue"/>. ⭐ The <i>then</i> / <i>give up</i> text is the ONE condition summary (<c>ConditionSentence.Summary</c>), never a
/// second wording; a task with no condition reads <i>when done</i> — the runtime's default (P-2), so nothing is written for it.
/// </summary>
public static class OrdersPlanModel
{
    public const string WhenDone = "when done";

    /// <summary>The rows of <paramref name="plan"/>; <paramref name="queue"/> = the unit's running queue (null in Edit / before it runs).</summary>
    public static IReadOnlyList<OrdersTaskRow> RowsOf(MissionPlan? plan, MissionPlanQueue? queue)
    {
        if (plan is null || plan.Tasks.Count == 0) return Array.Empty<OrdersTaskRow>();
        var rows = new List<OrdersTaskRow>(plan.Tasks.Count);
        for (int i = 0; i < plan.Tasks.Count; i++)
        {
            var t = plan.Tasks[i];
            rows.Add(new OrdersTaskRow(t.TaskId, SentenceOf(t), StateOf(i, queue), ProblemOf(t)));
        }
        return rows;
    }

    /// <summary>The task's verb: a Wait (no behaviour, E9), an order (<c>Order.&lt;Kind&gt;</c>, D1) or a behaviour by name.</summary>
    public static string VerbOf(MissionTask t)
        => string.IsNullOrEmpty(t.BehaviorId) ? "Wait"
         : t.BehaviorId.StartsWith("Order.", StringComparison.Ordinal) ? t.BehaviorId.Substring("Order.".Length)
         : t.BehaviorId;

    /// <summary>The task as one sentence.</summary>
    public static string SentenceOf(MissionTask t)
    {
        string? then = ConditionText(t, MissionTriggerHelper.ThenType);
        string? giveUp = ConditionText(t, MissionTriggerHelper.GiveUpType);
        string s = string.IsNullOrEmpty(t.BehaviorId)
            ? $"Wait — until {then ?? "…"}"
            : $"{VerbOf(t)}, then {then ?? WhenDone}";
        return giveUp is null ? s : $"{s} · give up {giveUp}";
    }

    /// <summary>Why the task cannot run as written, or null — a Wait needs a <i>then</i> (E9: it has no behaviour to finish).</summary>
    public static string? ProblemOf(MissionTask t)
        => string.IsNullOrEmpty(t.BehaviorId) && ConditionText(t, MissionTriggerHelper.ThenType) is null ? "a Wait needs a condition to end it" : null;

    /// <summary>The text of the task's <paramref name="type"/> entry (Then / GiveUp), translating the two legacy types the files still hold (§5.7).</summary>
    public static string? ConditionText(MissionTask t, string type)
    {
        foreach (var trig in t.Triggers)
        {
            if (trig.Type == type)
            {
                try { return Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionSentence.Summary(Fdp.Toolkit.ReplayBrowser.Search.ConditionCache.Parse(trig.Params)); }
                catch (Exception) { return "an unreadable condition"; }
            }
            if (type == MissionTriggerHelper.ThenType)
            {
                if (trig.Type == "BehaviorFinished") return WhenDone;
                if (trig.Type == "TimerElapsed") return float.TryParse(trig.Params, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? $"after {s:0.#} s" : "after a time";
            }
        }
        return null;
    }

    /// <summary>The task's state from the running queue: before the current task its outcome, the current one running (or halted), after it planned.</summary>
    public static OrdersTaskState StateOf(int index, MissionPlanQueue? queue)
    {
        if (queue is not { } q || index >= q.TaskCount) return OrdersTaskState.Planned;
        if (index < q.CurrentTask) return q.Outcomes[index] == MissionPhaseOutcome.Failed ? OrdersTaskState.Failed : OrdersTaskState.Done;
        if (index == q.CurrentTask) return q.Halted != 0 ? OrdersTaskState.Halted : OrdersTaskState.Running;
        return OrdersTaskState.Planned;
    }
}

/// <summary>The plain <i>then</i> choices (Mission plan §5.6); <see cref="Advanced"/> = any condition from the condition editor.</summary>
public enum ThenPreset { WhenDone, After, UnderFire, StrengthBelow, Advanced }

/// <summary>
/// ⭐ P-7 / CE-4303 slice 7b (Mission plan §5.6, D4, E5, E9) — the plan's EDITS, headless: a new task (a Wait first, an order by its kind, a behaviour),
/// the <i>then</i> presets written as ONE shared condition each (<c>{type: Then, params: condition}</c>), <i>give up after</i> as the second entry,
/// remove / reorder. ⭐ Every preset is a row of the ONE fact catalogue (<c>FactCatalogue</c>) projected by <c>RowProjection</c> with WHO = the
/// world (E5: the plan runs on the unit — no <c>OnEntity</c> wrapper); <i>when done</i> writes NOTHING (the runtime's default, P-2). A task keeps its
/// <c>TaskId</c> across edits (P-4 carries progress by it).
/// </summary>
public static class OrdersPlanEdits
{
    /// <summary>A Wait (E9): no behaviour; its <i>then</i> is required — 5 min by default so it is never written empty.</summary>
    public static MissionTask NewWait() => WithThen(new MissionTask { TaskId = Guid.NewGuid() }, Then(ThenPreset.After, 300));

    /// <summary>An order task (D1): <c>Order.&lt;Kind&gt;</c>, the order DTO as its params.</summary>
    public static MissionTask NewOrder(string kind, string paramsJson = "{}") => new() { TaskId = Guid.NewGuid(), BehaviorId = "Order." + kind, BehaviorParams = paramsJson };

    /// <summary>A behaviour task, by name.</summary>
    public static MissionTask NewBehaviour(string name) => new() { TaskId = Guid.NewGuid(), BehaviorId = name };

    /// <summary>The condition a preset stands for; null = <i>when done</i> (write nothing). <paramref name="number"/>: seconds for After, a fraction for StrengthBelow.</summary>
    public static Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? Then(ThenPreset preset, double number = 0)
    {
        var world = Fdp.Toolkit.ReplayBrowser.Search.Conditions.Subject.World;
        return preset switch
        {
            ThenPreset.WhenDone      => null,
            ThenPreset.After         => Row(new(world, "task-time", Values: new(Number: number))),
            ThenPreset.UnderFire     => Row(new(world, "under-fire", Values: new(Number: 0.5))),
            ThenPreset.StrengthBelow => Row(new(world, "strength", Negated: true, Values: new(Number: number))),
            _ => throw new ArgumentException("Advanced is the condition editor's — pass its condition to SetThen", nameof(preset)),
        };

        static Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto Row(Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionRow r)
            => Fdp.Toolkit.ReplayBrowser.Search.Conditions.RowProjection.ToDto(r);
    }

    /// <summary>Which preset <paramref name="t"/>'s <i>then</i> is, with its number (Advanced for anything the presets do not say).</summary>
    public static (ThenPreset Preset, double Number) ThenOf(MissionTask t)
    {
        foreach (var trig in t.Triggers)
        {
            if (trig.Type == "BehaviorFinished") return (ThenPreset.WhenDone, 0);
            if (trig.Type == "TimerElapsed" && double.TryParse(trig.Params, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s)) return (ThenPreset.After, s);
            if (trig.Type != MissionTriggerHelper.ThenType) continue;
            if (FactIn(trig.Params) is { } f)
            {
                if (f.Id == "task-time" && !f.Negated) return (ThenPreset.After, f.Number);
                if (f.Id == "under-fire" && !f.Negated) return (ThenPreset.UnderFire, f.Number);
                if (f.Id == "strength" && f.Negated) return (ThenPreset.StrengthBelow, f.Number);
            }
            return (ThenPreset.Advanced, 0);
        }
        return (ThenPreset.WhenDone, 0);
    }

    /// <summary>The task's <i>then</i> as a condition (a preset's, or the stored one); null = <i>when done</i> or unreadable.</summary>
    public static Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? ConditionOf(MissionTask t)
    {
        foreach (var trig in t.Triggers)
        {
            if (trig.Type == MissionTriggerHelper.ThenType)
            {
                try { return Fdp.Toolkit.ReplayBrowser.Search.ConditionCache.Parse(trig.Params); }
                catch (Exception) { return null; }
            }
            if (trig.Type == "TimerElapsed" && double.TryParse(trig.Params, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
                return Then(ThenPreset.After, s);
        }
        return null;
    }

    /// <summary>Sets the <i>then</i>: null = <i>when done</i> (the entry removed — nothing written). Replaces any legacy entry.</summary>
    public static MissionTask WithThen(MissionTask t, Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto? condition)
    {
        t.Triggers.RemoveAll(x => x.Type is "BehaviorFinished" or "TimerElapsed" or "ReachedDestination" or "HealthCritical" or "UnderAttack" || x.Type == MissionTriggerHelper.ThenType);
        if (condition != null)
            t.Triggers.Insert(0, new MissionTrigger { Type = MissionTriggerHelper.ThenType, Params = Fdp.Toolkit.ReplayBrowser.Search.ConditionCache.ToJson(condition) });
        return t;
    }

    /// <summary>Sets <i>give up after N s</i> (D4); null removes it.</summary>
    public static MissionTask WithGiveUp(MissionTask t, double? afterSeconds)
    {
        t.Triggers.RemoveAll(x => x.Type == MissionTriggerHelper.GiveUpType);
        if (afterSeconds is { } s)
            t.Triggers.Add(new MissionTrigger { Type = MissionTriggerHelper.GiveUpType, Params = Fdp.Toolkit.ReplayBrowser.Search.ConditionCache.ToJson(Then(ThenPreset.After, s)!) });
        return t;
    }

    /// <summary>The give-up's seconds, or null.</summary>
    public static double? GiveUpOf(MissionTask t)
    {
        foreach (var trig in t.Triggers)
            if (trig.Type == MissionTriggerHelper.GiveUpType)
            {
                return FactIn(trig.Params) is { Id: "task-time", Negated: false } f ? f.Number : 0;
            }
        return null;
    }

    /// <summary>
    /// The ONE catalogue fact a stored condition is, on the unit itself (E5: no <c>OnEntity</c> wrapper) — negated when it is a one-child <c>None</c>.
    /// ⚠ Not <c>RowProjection.FromDto</c>: it reads an unwrapped leaf as a row only for a world fact, and a task's facts are about the unit.
    /// </summary>
    private static (string Id, bool Negated, double Number)? FactIn(string json)
    {
        try
        {
            var d = Fdp.Toolkit.ReplayBrowser.Search.ConditionCache.Parse(json);
            bool negated = false;
            if (d is Fdp.Toolkit.ReplayBrowser.Search.CompoundPredicateDto { Operator: Fdp.Toolkit.ReplayBrowser.Search.LogicalOperator.None, Conditions.Count: 1 } none)
            { negated = true; d = none.Conditions[0]; }
            return Fdp.Toolkit.ReplayBrowser.Search.Conditions.FactCatalogue.Recognise(d, out var v) is { } e ? (e.Id, negated, v.Number) : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Moves task <paramref name="index"/> by <paramref name="delta"/> (clamped); its id travels with it.</summary>
    public static void Move(MissionPlan plan, int index, int delta)
    {
        int to = Math.Clamp(index + delta, 0, plan.Tasks.Count - 1);
        if (to == index || index < 0 || index >= plan.Tasks.Count) return;
        var t = plan.Tasks[index];
        plan.Tasks.RemoveAt(index);
        plan.Tasks.Insert(to, t);
    }

    /// <summary>Why <paramref name="plan"/> may not be applied, or null: a Wait with no <i>then</i> (E9), more tasks than the queue holds.</summary>
    public static string? RefusalOf(MissionPlan plan)
    {
        if (plan.Tasks.Count > MissionPlanQueue.MaxTasks) return $"a plan holds at most {MissionPlanQueue.MaxTasks} tasks";
        for (int i = 0; i < plan.Tasks.Count; i++)
            if (OrdersPlanModel.ProblemOf(plan.Tasks[i]) is { } why) return $"task {i + 1}: {why}";
        return null;
    }

    /// <summary>A deep copy (the draft the tab edits; the snapshot stays untouched).</summary>
    public static MissionPlan Copy(MissionPlan plan)
    {
        var c = new MissionPlan { ActiveTaskId = plan.ActiveTaskId };
        foreach (var t in plan.Tasks)
        {
            var nt = new MissionTask { TaskId = t.TaskId, ExecutingEngine = t.ExecutingEngine, BehaviorId = t.BehaviorId, BehaviorParams = t.BehaviorParams, State = t.State };
            foreach (var trig in t.Triggers) nt.Triggers.Add(new MissionTrigger { Type = trig.Type, Params = trig.Params });
            c.Tasks.Add(nt);
        }
        return c;
    }
}
