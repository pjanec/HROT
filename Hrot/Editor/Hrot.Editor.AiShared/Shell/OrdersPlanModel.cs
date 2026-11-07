using System;
using System.Collections.Generic;
using Fdp.Toolkit.Behavior.Components;
using Hrot.Core.Mission;

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
