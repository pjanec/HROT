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

/// <summary>
/// ⭐ P-7 / CE-4303 slice 7b (Mission plan §5.6, D4, E9) — the edits: each <i>then</i> preset stored as ONE condition (the catalogue's facts, WHO = this
/// unit), <i>when done</i> writing nothing, <i>after N</i> the SAME condition the runtime translates <c>TimerElapsed</c> into, give up as the second entry,
/// reorder keeping ids, a Wait never applied empty, Apply = the whole plan with its base version. 🔴 Red-proof (compiles): the task-time fact's path
/// changed ⇒ the Translate-equality row ⇒ red; <c>RefusalOf</c> skipping the Wait check ⇒ the refusal row ⇒ red.
/// </summary>
public sealed class TheOrdersTabEditsTests
{
    // the condition JSON names its component by type name — resolved through the registry, as a production world registers it
    static TheOrdersTabEditsTests() { using var w = new Fdp.Core.EntityRepository(); w.RegisterComponent<MissionPlanQueue>(); }

    [Fact]
    public void P7b_AfterNIsTheRuntimesOwnTranslation_WhenDoneWritesNothing()
    {
        var after = OrdersPlanEdits.Then(ThenPreset.After, 120)!;
        var runtime = Fdp.Toolkit.Behavior.Systems.PlanConditionCache.Translate(Fdp.Toolkit.Behavior.Components.MissionTrigger.TimerElapsed, 120);
        Assert.Equal(ConditionCache.ToJson(runtime), ConditionCache.ToJson(after));     // one condition, authored or translated

        var t = OrdersPlanEdits.NewOrder("Move");
        OrdersPlanEdits.WithThen(t, OrdersPlanEdits.Then(ThenPreset.After, 120));
        Assert.Single(t.Triggers);
        OrdersPlanEdits.WithThen(t, OrdersPlanEdits.Then(ThenPreset.WhenDone));
        Assert.Empty(t.Triggers);                                                         // P-2: nothing written for "when done"
        Assert.Equal("Move, then when done", OrdersPlanModel.SentenceOf(t));
    }

    [Theory]
    [InlineData(ThenPreset.After, 300)]
    [InlineData(ThenPreset.UnderFire, 0.5)]
    [InlineData(ThenPreset.StrengthBelow, 0.25)]
    public void P7b_EachPresetReadsBackAsItself_WithNoOnEntityWrapper(ThenPreset preset, double number)
    {
        var t = OrdersPlanEdits.WithThen(OrdersPlanEdits.NewOrder("Hold"), OrdersPlanEdits.Then(preset, number));
        Assert.Equal(MissionTriggerHelper.ThenType, t.Triggers[0].Type);
        Assert.DoesNotContain("OnEntity", t.Triggers[0].Params);                          // E5: it runs on the unit
        Assert.Equal((preset, number), OrdersPlanEdits.ThenOf(t));
        Assert.Equal(ThenPreset.After, OrdersPlanEdits.ThenOf(new MissionTask { Triggers = { new MissionTrigger { Type = "TimerElapsed", Params = "45" } } }).Preset);   // a legacy entry reads as its preset
    }

    [Fact]
    public void P7b_GiveUpIsTheSecondEntry_ReorderKeepsIds_AnEmptyWaitIsRefused()
    {
        var t = OrdersPlanEdits.WithGiveUp(OrdersPlanEdits.NewOrder("Attack"), 600);
        Assert.Equal(600, OrdersPlanEdits.GiveUpOf(t));
        Assert.Contains(" · give up ", OrdersPlanModel.SentenceOf(t));
        Assert.Null(OrdersPlanEdits.GiveUpOf(OrdersPlanEdits.WithGiveUp(t, null)));

        var wait = OrdersPlanEdits.NewWait();
        Assert.Equal(ThenPreset.After, OrdersPlanEdits.ThenOf(wait).Preset);              // E9: a new Wait is never empty
        var plan = new MissionPlan { Tasks = { OrdersPlanEdits.NewOrder("Move"), wait } };
        var ids = new[] { plan.Tasks[0].TaskId, plan.Tasks[1].TaskId };
        OrdersPlanEdits.Move(plan, 1, -1);
        Assert.Equal(new[] { ids[1], ids[0] }, new[] { plan.Tasks[0].TaskId, plan.Tasks[1].TaskId });
        Assert.Null(OrdersPlanEdits.RefusalOf(plan));

        OrdersPlanEdits.WithThen(wait, null);                                              // a Wait with nothing to end it
        Assert.Contains("a Wait needs a condition", OrdersPlanEdits.RefusalOf(plan));
    }

    private sealed class FakeMissions : Hrot.UI.Common.Facades.IMissionEditorService
    {
        public MissionPlan? Plan; public long Version = 7;
        public readonly List<(int Net, MissionPlan Plan, long Base)> Commits = new();
        public IReadOnlyList<string> GetAvailableBehaviors(int entityId) => Array.Empty<string>();
        public (MissionPlan? Plan, long Version) GetMissionSnapshot(int entityId) => (Plan, Version);
        public System.Threading.Tasks.Task<Hrot.UI.Common.Models.MissionCommitResult> CommitMissionAsync(int entityId, MissionPlan plan, long baseVersion)
        { Commits.Add((entityId, plan, baseVersion)); return System.Threading.Tasks.Task.FromResult(new Hrot.UI.Common.Models.MissionCommitResult(true, baseVersion + 1)); }
        public System.Threading.Tasks.Task<Hrot.UI.Common.Models.MissionCommitResult> SendControlCommandAsync(int entityId, eMissionCommandType type, Guid taskId)
            => System.Threading.Tasks.Task.FromResult(new Hrot.UI.Common.Models.MissionCommitResult(true, Version));
    }

    [Fact]
    public void P7b_ApplySendsTheWholePlanWithItsBaseVersion_AndRefusesAnEmptyWait()
    {
        var keep = Guid.NewGuid();
        var svc = new FakeMissions { Plan = new MissionPlan { Tasks = { new MissionTask { TaskId = keep, BehaviorId = "Order.Move" } } } };
        var tab = new OrdersTab(() => svc, () => null);
        var draft = tab.EnsureDraft(42);
        Assert.NotSame(svc.Plan, draft);                                                   // a copy: the snapshot is never edited in place
        draft.Tasks.Add(OrdersPlanEdits.NewWait()); tab.Touch();

        Assert.Null(tab.Apply());
        var (net, sent, @base) = Assert.Single(svc.Commits);
        Assert.Equal((42, 7L), (net, @base));
        Assert.Equal(2, sent.Tasks.Count);
        Assert.Equal(keep, sent.Tasks[0].TaskId);                                          // P-4: progress is carried by task id
        tab.PollCommit();
        Assert.Null(tab.Draft);                                                            // re-read from the new snapshot

        var d2 = tab.EnsureDraft(42);
        d2.Tasks.Add(OrdersPlanEdits.WithThen(OrdersPlanEdits.NewWait(), null)); tab.Touch();
        Assert.NotNull(tab.Apply());                                                       // refused: nothing sent
        Assert.Single(svc.Commits);
    }
}
