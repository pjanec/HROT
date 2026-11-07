using System;
using System.Collections.Generic;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Core.Mission;
using Hrot.Editor.AiShared.Shell;
using Xunit;
using MissionTrigger = Hrot.Core.Mission.MissionTrigger;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐ P-7 / CE-4303 slice 7a (docs/UX/DESIGN_Mission_Plan_Editor.md §5.11) — the Orders tab's rows: each task one sentence, the running task from the
/// queue. 🔴 Red-proved (compiling mutant): <c>StateOf</c> never answering Running ⇒ <c>P7a_TheQueueMarks…</c> red. ⚠ Dropping the legacy
/// <c>BehaviorFinished</c> translation is EQUIVALENT (no condition already reads <i>when done</i>, P-2) — the <c>TimerElapsed</c> row is the one that pins the translation.
/// </summary>
public sealed class TheOrdersTabTests
{
    private static MissionTask Task(string behaviour, params MissionTrigger[] triggers)
        => new() { TaskId = Guid.NewGuid(), BehaviorId = behaviour, Triggers = new List<MissionTrigger>(triggers) };

    private static MissionTrigger Then(SearchPredicateDto c) => new() { Type = MissionTriggerHelper.ThenType, Params = ConditionCache.ToJson(c) };

    [Fact]
    public void P7a_EachTaskIsOneSentence_AnOrderByItsKind_AWaitUntil_NoConditionIsWhenDone()
    {
        var underFire = new InputAbovePredicateDto { Input = "UnderFire", Threshold = 0f };
        var plan = new MissionPlan { Tasks =
        {
            Task("Order.Move"),                                                       // no condition = when done (P-2)
            Task("Order.Hold", new MissionTrigger { Type = "BehaviorFinished" }),    // the legacy entry, translated
            Task("Patrol", new MissionTrigger { Type = "TimerElapsed", Params = "30" }),
            Task("", Then(underFire)),                                                // a Wait (E9)
            Task(""),                                                                 // a Wait with nothing to end it
        } };

        var rows = OrdersPlanModel.RowsOf(plan, null);

        Assert.Equal("Move, then when done", rows[0].Sentence);
        Assert.Equal("Hold, then when done", rows[1].Sentence);
        Assert.Equal("Patrol, then after 30 s", rows[2].Sentence);
        Assert.StartsWith("Wait — until ", rows[3].Sentence);
        Assert.Equal("Wait — until " + Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionSentence.Summary(underFire), rows[3].Sentence);   // the ONE summary
        Assert.Null(rows[3].Problem);
        Assert.NotNull(rows[4].Problem);                                          // E9: a Wait needs a condition
        Assert.All(rows, r => Assert.Equal(OrdersTaskState.Planned, r.State));   // no queue ⇒ nothing runs yet
    }

    [Fact]
    public void P7a_TheQueueMarksDoneFailedRunningAndPlanned()
    {
        var plan = new MissionPlan { Tasks = { Task("Order.Move"), Task("Order.Attack"), Task("Order.Hold"), Task("Order.Move") } };
        var q = new MissionPlanQueue { TaskCount = 4, CurrentTask = 2 };
        q.Outcomes[0] = MissionPhaseOutcome.Done;
        q.Outcomes[1] = MissionPhaseOutcome.Failed;

        var rows = OrdersPlanModel.RowsOf(plan, q);
        Assert.Equal(new[] { OrdersTaskState.Done, OrdersTaskState.Failed, OrdersTaskState.Running, OrdersTaskState.Planned }, new[] { rows[0].State, rows[1].State, rows[2].State, rows[3].State });

        q.Halted = 1;
        Assert.Equal(OrdersTaskState.Halted, OrdersPlanModel.RowsOf(plan, q)[2].State);
    }

    [Fact]
    public void P7a_AGiveUpReadsAfterTheThen()
    {
        var t = Task("Order.Attack",
            new MissionTrigger { Type = "BehaviorFinished" },
            new MissionTrigger { Type = MissionTriggerHelper.GiveUpType, Params = ConditionCache.ToJson(new InputAbovePredicateDto { Input = "UnderFire", Threshold = 0.5f }) });
        Assert.StartsWith("Attack, then when done · give up ", OrdersPlanModel.SentenceOf(t));
    }
}
