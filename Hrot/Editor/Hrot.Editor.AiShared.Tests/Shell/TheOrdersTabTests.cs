using System;
using System.Collections.Generic;
using System.Linq;
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

    private sealed class FixedSelection(params Fdp.Core.Entity[] e) : IEntitySelectionSource
    {
        public IReadOnlyList<Fdp.Core.Entity> Selected() => e;
    }

    /// <summary>⭐ L-14 / CE-4314 (LC-6) — <i>Copy plan</i> / <i>Paste plan</i>, through the host's actions: a paste on a unit inside the selection
    /// gives EACH selected unit its own copy (new task ids, all planned, the active one mapped), at its own base version; outside it, that unit alone.
    /// 🔴 Red-proof (compiles): <c>Paste</c> sending <c>Copy</c> (the same ids) instead of <c>CopyAsNew</c> ⇒ red.</summary>
    [Fact]
    public void L14_PastePlan_GivesEachSelectedUnitItsOwnCopy_NewTaskIds_AllPlanned()
    {
        using var world = new Fdp.Core.EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        Fdp.Core.Entity Unit(int net) { var e = world.CreateEntity(); world.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(net)); return e; }
        var (a, b, c, d) = (Unit(1), Unit(2), Unit(3), Unit(4));

        var t1 = Guid.NewGuid(); var t2 = Guid.NewGuid();
        var svc = new FakeMissions { Plan = new MissionPlan { ActiveTaskId = t2, Tasks =
        {
            new MissionTask { TaskId = t1, BehaviorId = "Order.Move", State = eTaskState.TASK_DONE },
            new MissionTask { TaskId = t2, BehaviorId = "Patrol", State = eTaskState.TASK_ACTIVE, Triggers = { new MissionTrigger { Type = "TimerElapsed", Params = "30" } } },
        } } };
        var host = new Hrot.Common.Interactions.HostActions();
        var plans = new PlanClipboard(() => svc);
        Hrot.Editor.AiShared.Windows.EntityDetailsBundle.RegisterPlanClipboard(host, plans, new FixedSelection(b, c));
        Assert.True(host.Actions.TryGetHandler(Hrot.Common.Constants.GlobalActionIds.CopyPlan, out var copy));
        Assert.True(host.Actions.TryGetHandler(Hrot.Common.Constants.GlobalActionIds.PastePlan, out var paste));

        paste(world, b);
        Assert.Empty(svc.Commits);                                                         // nothing copied yet ⇒ nothing sent
        copy(world, a);
        Assert.Equal(2, plans.TaskCount);

        paste(world, c);                                                                   // c is in the selection ⇒ b AND c
        Assert.Equal(new[] { 2, 3 }, svc.Commits.ConvertAll(x => x.Net));
        Assert.All(svc.Commits, x => Assert.Equal(7L, x.Base));
        var sb = svc.Commits[0].Plan; var sc = svc.Commits[1].Plan;
        Assert.NotSame(sb, sc);
        Assert.All(new[] { sb, sc }, p =>
        {
            Assert.Equal(new[] { "Order.Move", "Patrol" }, p.Tasks.ConvertAll(t => t.BehaviorId));
            Assert.DoesNotContain(p.Tasks, t => t.TaskId == t1 || t.TaskId == t2);       // each its own task ids
            Assert.All(p.Tasks, t => Assert.Equal(eTaskState.TASK_PLANNED, t.State));
            Assert.Equal(p.Tasks[1].TaskId, p.ActiveTaskId);                               // the active task, mapped
            Assert.Equal("30", p.Tasks[1].Triggers[0].Params);
        });
        Assert.NotEqual(sb.Tasks[0].TaskId, sc.Tasks[0].TaskId);

        paste(world, d);                                                                   // d is not selected ⇒ d alone
        Assert.Equal(4, svc.Commits[^1].Net);
        Assert.Equal(3, svc.Commits.Count);
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

    /// <summary>⭐ 7b rest — an order task's fields are its order DTO (the ONE order registry G1 resolves by); a behaviour's the host's lookup; a Wait none.
    /// An advanced condition reads back as itself. 🔴 Red-proof: <c>ParamsTypeOfTask</c> not special-casing orders ⇒ the Move row ⇒ red.</summary>
    [Fact]
    public void P7b_AnOrdersFieldsAreItsDto_AWaitHasNone_AnAdvancedConditionReadsBack()
    {
        var tab = new OrdersTab(() => null, () => null, paramsTypeOf: name => name == "Patrol" ? typeof(string) : null);
        Assert.True(Hrot.Common.Orders.OrderMenu.TryGetByName("Move", out var move));
        Assert.Equal(move.Dto, tab.ParamsTypeOfTask("Order.Move"));
        Assert.Equal(typeof(string), tab.ParamsTypeOfTask("Patrol"));           // the host's lookup is used (the silent-default rule)
        Assert.Null(tab.ParamsTypeOfTask(""));                                  // a Wait has no fields

        var advanced = new CompoundPredicateDto { Operator = LogicalOperator.Or, Conditions = { OrdersPlanEdits.Then(ThenPreset.UnderFire)!, OrdersPlanEdits.Then(ThenPreset.After, 60)! } };
        var t = OrdersPlanEdits.WithThen(OrdersPlanEdits.NewOrder("Hold"), advanced);
        Assert.Equal(ThenPreset.Advanced, OrdersPlanEdits.ThenOf(t).Preset);
        Assert.Equal(ConditionCache.ToJson(advanced), ConditionCache.ToJson(OrdersPlanEdits.ConditionOf(t)!));
    }

    // ── ⭐ W-1 / P-8 8b (Mission plan §5.5 M5 / M6, §5.6) — the picker's phase, rule and unit entries ─────────────────────────

    /// <summary>
    /// ⭐ <i>when Phase N begins</i> / <i>when a rule fires</i> store the ONE shape (the trigger fired — <c>PlanConditionCache.PhaseBegins</c>) and read back as
    /// <see cref="ThenPreset.RuleFires"/> with the trigger; <i>when &lt;unit&gt; finishes task N</i> stores <c>TaskFinished</c> and reads back with its number.
    /// 🔴 Red-proof: <c>TargetIn</c> dropped from <c>ThenOf</c> ⇒ both read as Advanced ⇒ red.
    /// </summary>
    [Fact]
    public void P8b_ARuleOrPhaseAndAUnitsTask_ReadBackAsThemselves_WithTheirTarget()
    {
        var trigger = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(77));
        var t = new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "Order.Hold" };
        OrdersPlanEdits.WithThen(t, OrdersPlanEdits.ThenRuleFires(trigger));
        Assert.Equal(ThenPreset.RuleFires, OrdersPlanEdits.ThenOf(t).Preset);
        Assert.Equal(77, OrdersPlanEdits.TargetOf(t).Ref.NetworkId);
        Assert.Equal(ConditionCache.ToJson(Fdp.Toolkit.Behavior.Systems.PlanConditionCache.PhaseBegins(trigger)), Assert.Single(t.Triggers).Params);   // the runtime's own preset

        var unit = new Fdp.Toolkit.Replication.EntityLink(new Fdp.Toolkit.Replication.EntityRef(42));
        OrdersPlanEdits.WithThen(t, OrdersPlanEdits.ThenUnitFinishes(unit, 3));
        Assert.Equal((ThenPreset.UnitFinishesTask, 3d), OrdersPlanEdits.ThenOf(t));
        Assert.Equal(42, OrdersPlanEdits.TargetOf(t).Ref.NetworkId);
        Assert.IsType<TaskFinishedPredicateDto>(ConditionCache.Parse(Assert.Single(t.Triggers).Params));
        Assert.Equal(1, ((TaskFinishedPredicateDto)OrdersPlanEdits.ThenUnitFinishes(unit, 0)).Task);   // a task number is 1-based
    }

    /// <summary>⭐ The picker's rules: the phases first by number, then the other triggers by title — authored (Edit) rules too; objectives are not offered.</summary>
    [Fact]
    public void P8b_ThePickerOffersThePhasesFirst_ThenTheOtherTriggers_AuthoredToo()
    {
        using var w = new Fdp.Core.EntityRepository();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        w.RegisterManagedComponent<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>();
        void Rule(int id, string kind, string json)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(id));
            w.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 });
        }
        Rule(1, "Trigger", """{"common":{"title":"Assault","phase":2}}""");
        Rule(2, "Trigger", """{"common":{"title":"Brief Blue"}}""");
        Rule(3, "Trigger", """{"common":{"title":"Approach","phase":1}}""");
        Rule(4, "ReachArea", """{"common":{"title":"Reach the bridge"}}""");
        var tab = new OrdersTab(() => null, () => w);
        Assert.Equal(new[] { "Approach", "Assault", "Brief Blue" }, tab.RuleChoicesOf(w).Select(r => r.Title));
    }

    /// <summary>⭐ The silent-default rule — the bundle HAS the host's actions and passes them: the constructed tab offers <i>New rule…</i>. 🔴 Red-proof: the
    /// descriptor not forwarding <c>host</c> ⇒ red.</summary>
    [Fact]
    public void P8b_TheDescriptorForwardsTheHost_SoNewRuleCanAsk()
    {
        var host = new Hrot.Common.Interactions.HostActions();
        var tab = (OrdersTab)OrdersTabDescriptor.For(() => null, () => null, host: () => host).Create();
        Assert.True(tab.OffersNewRule);
        Assert.False(((OrdersTab)OrdersTabDescriptor.For(() => null, () => null).Create()).OffersNewRule);
    }
}
