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

    /// <summary>
    /// ⭐ P-7 7d (U-3, docs/UX/DESIGN_Colour_Icons.md §3) — re-homed from the retired Mission panel's glyph rail: each task's STATE has its colour — running blue, done green,
    /// failed red, halted amber, planned grey — and no two states share one. 🔴 Red-proof (compiles): <c>ColourOf</c> answering the grey for <c>Done</c> ⇒ red.
    /// ⚠ The retired rail also read the glyph pixels of a real frame; the Orders tab draws the number in this colour (no glyph) and has no frame rail yet.
    /// </summary>
    [Fact]
    public void U3_EachTaskStateHasItsColour_RunningBlue_DoneGreen_FailedRed_HaltedAmber_PlannedGrey()
    {
        var running = OrdersTab.ColourOf(OrdersTaskState.Running); var done = OrdersTab.ColourOf(OrdersTaskState.Done);
        var failed = OrdersTab.ColourOf(OrdersTaskState.Failed); var halted = OrdersTab.ColourOf(OrdersTaskState.Halted); var planned = OrdersTab.ColourOf(OrdersTaskState.Planned);

        Assert.True(running.Z > running.X && running.Z > running.Y * 0.99f, "running is blue");
        Assert.True(done.Y > done.X && done.Y > done.Z, "done is green");
        Assert.True(failed.X > failed.Y && failed.X > failed.Z, "failed is red");
        Assert.True(halted.X > halted.Z && halted.Y > halted.Z, "halted is amber");
        Assert.True(planned.X == planned.Y && planned.Y == planned.Z, "planned is neutral grey");
        Assert.Equal(5, new[] { running, done, failed, halted, planned }.Distinct().Count());
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
        public bool ConflictNext;   // ⭐ 7c — the next commit answers CONFLICT (the plan moved since it was opened)
        public readonly List<(int Net, MissionPlan Plan, long Base)> Commits = new();
        public IReadOnlyList<string> GetAvailableBehaviors(int entityId) => Array.Empty<string>();
        public readonly List<int> AskedAbout = new();
        public (MissionPlan? Plan, long Version) GetMissionSnapshot(int entityId) { AskedAbout.Add(entityId); return (Plan, Version); }
        public System.Threading.Tasks.Task<Hrot.UI.Common.Models.MissionCommitResult> CommitMissionAsync(int entityId, MissionPlan plan, long baseVersion)
        {
            Commits.Add((entityId, plan, baseVersion));
            if (ConflictNext) { ConflictNext = false; return System.Threading.Tasks.Task.FromResult(new Hrot.UI.Common.Models.MissionCommitResult(false, 0, "changed", Conflict: true)); }
            return System.Threading.Tasks.Task.FromResult(new Hrot.UI.Common.Models.MissionCommitResult(true, baseVersion + 1));
        }
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

    /// <summary>⭐ P-7 7c (D9) — a CONFLICT keeps the draft and offers the choice: <i>Overwrite</i> sends it again over the plan's CURRENT version;
    /// <i>Reload</i> drops it. 🔴 Red-proof (compiles): <c>Overwrite</c> keeping the stale base ⇒ the second commit's base is 7 ⇒ red.</summary>
    [Fact]
    public void P7c_AConflict_KeepsTheDraft_OverwriteResendsOverTheCurrentVersion_ReloadDropsIt()
    {
        var svc = new FakeMissions { Plan = new MissionPlan { Tasks = { new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "Order.Move" } } }, ConflictNext = true };
        var tab = new OrdersTab(() => svc, () => null);
        var draft = tab.EnsureDraft(42);
        draft.Tasks.Add(OrdersPlanEdits.NewWait()); tab.Touch();
        Assert.Null(tab.Apply());
        tab.PollCommit();
        Assert.True(tab.Conflicted);
        Assert.Same(draft, tab.Draft);                                                     // mine is kept

        svc.Version = 9;                                                                   // someone else committed meanwhile
        Assert.Null(tab.Overwrite());
        Assert.Equal(9L, svc.Commits[^1].Base);                                            // over the plan as it is NOW
        Assert.Equal(2, svc.Commits[^1].Plan.Tasks.Count);
        tab.PollCommit();
        Assert.False(tab.Conflicted);

        svc.ConflictNext = true;
        tab.EnsureDraft(42).Tasks.Add(OrdersPlanEdits.NewWait()); tab.Touch();
        tab.Apply(); tab.PollCommit();
        Assert.True(tab.Conflicted);
        tab.Reload();
        Assert.Null(tab.Draft);                                                            // theirs: re-read on the next frame
        Assert.False(tab.Conflicted);
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

    // ── ⭐ P-7 7d — the OFFERING rules, re-homed from the retired Mission view's rails (TheScenarioMissionViewTests) onto the Orders tab ───────────────────────────

    private const ulong GroundUnitDis = (3UL << 56) | (1UL << 48) | (225UL << 32) | (1UL << 24);   // a unit with orders offered (DebugApiOrdersTests)
    private const ulong MunitionDis   = 2UL << 56;                                                    // no class of a munition has an order behaviour

    private static Fdp.Core.Entity Networked(Fdp.Core.EntityRepository w, int net, ulong dis)
    {
        var e = w.CreateEntity();
        w.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity(net));
        w.SetDisType(e, new Fdp.Core.DISEntityType { Value = dis });
        return e;
    }

    private static DetailsContext Ctx(params Fdp.Core.Entity[] e)
        => DetailsContextBuilder.Build(new Hrot.Editor.AiShared.Selection.EditorSelectionStore(), "Scenario", Hrot.Editor.AiShared.Variables.VariableRunState.Planning, new FixedSelection(e));

    private static DetailsViewRegistry Catalogue(Func<Hrot.UI.Common.Facades.IMissionEditorService?> svc, Fdp.Core.EntityRepository w)
    {
        var views = new DetailsViewRegistry();
        views.Add(OrdersTabDescriptor.For(svc, () => w));
        return views;
    }

    /// <summary>⭐⭐ THE GATE (was "only a brain-equipped entity offers Mission"): a unit that can be ordered — or already has a plan — OFFERS Orders; one that can neither does not.
    /// 🔴 Red-proof (compiles): <c>AppliesTo</c> answering true ⇒ the munition row ⇒ red.</summary>
    [Fact]
    public void P7d_OnlyAUnitThatCanBeOrderedOrHasAPlan_OffersTheOrdersTab()
    {
        using var w = new Fdp.Core.EntityRepository();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        var unit = Networked(w, 11, GroundUnitDis); var munition = Networked(w, 12, MunitionDis);
        var svc = new FakeMissions();
        var views = Catalogue(() => svc, w);

        Assert.Contains(OrdersTabDescriptor.Id, views.OfferSet(Ctx(unit)).Select(d => d.Id));
        Assert.DoesNotContain(OrdersTabDescriptor.Id, views.OfferSet(Ctx(munition)).Select(d => d.Id));

        svc.Plan = new MissionPlan { Tasks = { new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "Patrol" } } };   // …but a munition WITH a plan shows it
        Assert.Contains(OrdersTabDescriptor.Id, views.OfferSet(Ctx(munition)).Select(d => d.Id));
    }

    /// <summary>⛔⛔ The ENTITY half still binds — none and two both offer nothing (the tab edits ONE unit's plan; offering on a multi-selection would silently drop the rest).</summary>
    [Fact]
    public void P7d_NoEntityAndTwoEntities_OfferNothing()
    {
        using var w = new Fdp.Core.EntityRepository();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        var (a, b) = (Networked(w, 11, GroundUnitDis), Networked(w, 12, GroundUnitDis));
        var views = Catalogue(() => new FakeMissions(), w);

        Assert.Empty(views.OfferSet(Ctx()));
        Assert.Empty(views.OfferSet(Ctx(a, b)));
    }

    /// <summary>⭐⭐ No mission service ⇒ no offer — a host without the facade is not told it has orders (R-117: declining hands the answer to the shell's one grey line).</summary>
    [Fact]
    public void P7d_WithNoMissionServiceAtAll_ItNeverOffers()
    {
        using var w = new Fdp.Core.EntityRepository();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        var unit = Networked(w, 11, GroundUnitDis);

        Assert.Empty(Catalogue(() => null, w).OfferSet(Ctx(unit)));
    }

    /// <summary>⭐⭐ Rank: Orders outranks the raw Components tab and sits below Runtime (R-98: rank is the default pick only) — an entity with orders is a more specific fact than "it has components".</summary>
    [Fact]
    public void P7d_ItsRank_SitsBetweenComponentsAndRuntime()
    {
        int rank = OrdersTabDescriptor.For(() => null, () => null).Rank;
        Assert.True(rank > Hrot.Editor.Scenario.ScenarioComponentsViewDescriptor.Rank);
        Assert.True(rank < RuntimeDetailsViewDescriptor.Rank);
    }

    /// <summary>⭐⭐⭐ THE SEAM (was "the panel is pointed at the entity's NETWORK id"): the tab asks the service about <c>NetworkIdentity.Value</c>, never <c>Entity.Index</c> — the two coincide in
    /// single-entity scenarios and diverge everywhere else. 🔴 Red-proof (compiles): <c>RowsFor</c> passing <c>entity.Index</c> ⇒ red.</summary>
    [Fact]
    public void P7d_TheTabAsksTheServiceAboutTheSelectedUnitsNetworkId_NotItsIndex()
    {
        using var w = new Fdp.Core.EntityRepository();
        w.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        for (int i = 0; i < 3; i++) w.CreateEntity();   // push the unit's index away from its network id
        var unit = Networked(w, 4242, GroundUnitDis);
        var svc = new FakeMissions();
        var tab = new OrdersTab(() => svc, () => w);

        tab.RowsFor(unit);

        Assert.NotEqual(4242, unit.Index);
        Assert.Contains(4242, svc.AskedAbout);
        Assert.DoesNotContain(unit.Index, svc.AskedAbout);
    }

    /// <summary>⭐ P-7 7d — re-homed from <c>MissionPanelRegistryTests.CE1023_…</c>: a task whose behaviour has a params type gets THE form; one with no known type gets none (the raw JSON);
    /// a host's lookup wins over the declared contracts, and with no lookup the declared contracts answer (what a host with no registry can know).
    /// 🔴 Red-proof (compiles): <c>ParamsTypeOfTask</c> ignoring the host's lookup ⇒ the first row ⇒ red.</summary>
    [Fact]
    public void P7d_ATasksFormComesFromTheHostsLookup_AnUnknownBehaviourGetsNone_TheDefaultIsTheDeclaredContracts()
    {
        var tab = new OrdersTab(() => null, () => null, paramsTypeOf: name => name == "Mine" ? typeof(string) : null);
        Assert.Equal(typeof(string), tab.ParamsTypeOfTask("Mine"));
        Assert.Null(tab.ParamsTypeOfTask("NoSuchBehaviour"));

        var declared = Hrot.Map.Definitions.Behavior.ParamsDtoRegistry.Default.Behaviors;
        Assert.NotEmpty(declared);
        var plain = new OrdersTab(() => null, () => null);
        Assert.All(declared, d => Assert.Equal(d.ParamsType, plain.ParamsTypeOfTask(d.Name)));
    }

    /// <summary>⭐ P-7 7d (CE-4303; re-homes the retired <c>MissionPanelRegistryTests</c> P4 + CE-1059) — every task's form picks through the host's ONE broker, SCOPED to that
    /// task (a pick one task asked for is not another's), stable across frames (no allocation per frame), with the host's searchable entity LIST.
    /// 🔴 Red-proof (compiles): <c>PickFor</c> returning the unscoped context ⇒ task B consumes task A's pick ⇒ red.</summary>
    [Fact]
    public void P7d_EachTasksPickIsScopedToThatTask_TheEntityListReachesIt_TheScopeIsCached()
    {
        string[]? asked = null;
        var broker = new Hrot.Presentation.Facades.MapPickBroker(() => null) { EntityList = (presets, chosen) => { asked = presets; chosen(42); return true; } };
        var tab = new OrdersTab(() => null, () => null, pick: () => broker);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        var pa = tab.PickFor(a, then: false)!;
        Assert.Same(pa, tab.PickFor(a, then: false));                                // cached: a frame builds nothing
        Assert.True(pa.CanListEntities);                                              // CE-1059 — the list reaches a task's form
        pa.RequestEntityListPick("$.TargetNetworkId", new[] { "units_ground" });
        Assert.Equal(new[] { "units_ground" }, asked);
        Assert.False(tab.PickFor(b, then: false)!.TryConsumeEntityPick("$.TargetNetworkId", out _));   // another task's field: not its pick
        Assert.False(tab.PickFor(a, then: true)!.TryConsumeEntityPick("$.TargetNetworkId", out _));    // nor the same task's then-condition
        Assert.True(pa.TryConsumeEntityPick("$.TargetNetworkId", out var picked));
        Assert.Equal(42L, picked.NetworkId);

        Assert.Null(new OrdersTab(() => null, () => null).PickFor(a, then: false));  // no host pick ⇒ no pick
    }

    /// <summary>⭐ CE-3395 — the tasks LIST's ops (kit ⋮ menu / drag) go to the draft's EXISTING edits: Move reorders by single steps, Delete removes, and both mark the plan dirty.</summary>
    [Fact]
    public void TheTaskListOps_MoveAndDelete_ActOnTheDraft()
    {
        var tab = new OrdersTab(() => null, () => null);
        var plan = new MissionPlan { Tasks = { new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "Order.Move" }, new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "Order.Attack" }, new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "Order.Hold" } } };
        tab.ApplyTaskOp(plan, new NodeEditor.Core.Widgets.ListOp(NodeEditor.Core.Widgets.ListOpKind.Move, 0, 2));
        Assert.Equal(new[] { "Order.Attack", "Order.Hold", "Order.Move" }, plan.Tasks.Select(t => t.BehaviorId).ToArray());
        tab.ApplyTaskOp(plan, new NodeEditor.Core.Widgets.ListOp(NodeEditor.Core.Widgets.ListOpKind.Delete, 1));
        Assert.Equal(new[] { "Order.Attack", "Order.Move" }, plan.Tasks.Select(t => t.BehaviorId).ToArray());
    }

    /// <summary>⭐ CE-3403 (Y-9; DESIGN_Struct_Editor §10.2 / §12 N-9) — the Orders tab in a REAL frame: its plan is the kit's list (one summary row per task, ⋮ per row, Add at the end) and a steady frame allocates nothing (R-2084; it had no frame harness, so it was
    /// unmeasured). Renders <c>orders-tab.png</c> under <c>HROT_RAIL_SHOTS</c>. 🔴 Red-proof (compiles): a per-frame string built in <c>Draw</c> ⇒ the byte assert fails.</summary>
    [SkippableFact]
    public void CE3403_TheOrdersTab_DrawsThePlanAsAList_AndASteadyFrameAllocatesNothing()
    {
        Skip.IfNot(Hrot.Editor.UiFrameRail.UiFrameHarness.IsAvailable(), Hrot.Editor.UiFrameRail.UiFrameHarness.UnavailableReason);
        using var world = new Fdp.Core.EntityRepository();
        world.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
        world.RegisterComponent<MissionPlanQueue>();
        var unit = world.CreateEntity(); world.AddComponent(unit, new Fdp.Toolkit.Replication.Components.NetworkIdentity(5));
        var svc = new FakeMissions { Plan = new MissionPlan { Tasks =
        {
            new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "Order.Move", State = eTaskState.TASK_DONE },
            new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "Order.Hold", State = eTaskState.TASK_ACTIVE, Triggers = { new MissionTrigger { Type = "TimerElapsed", Params = "30" } } },
            new MissionTask { TaskId = Guid.NewGuid(), BehaviorId = "Order.Move", State = eTaskState.TASK_PLANNED },
        } } };
        var tab = new OrdersTab(() => svc, () => world);
        var ctx = new DetailsContext(Hrot.Editor.AiShared.Selection.SelectionOrigin.Unknown, Array.Empty<Hrot.Editor.AiShared.Selection.IAssetSubSelection>(), new[] { unit }, null, "Scenario", Hrot.Editor.AiShared.Variables.VariableRunState.Planning);
        using var frame = Hrot.Editor.UiFrameRail.UiFrameHarness.Begin(700, 420);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        long bytes = -1;
        void Draw()
        {
            ImGuiNET.ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10)); ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(680, 400));
            ImGuiNET.ImGui.Begin("##orders", ImGuiNET.ImGuiWindowFlags.NoDecoration | ImGuiNET.ImGuiWindowFlags.NoMove | ImGuiNET.ImGuiWindowFlags.NoSavedSettings);
            long before = GC.GetAllocatedBytesForCurrentThread();
            tab.Draw(ctx, "o");
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGuiNET.ImGui.End();
        }
        for (int k = 0; k < 3; k++) { frame.StepN(15, Draw); System.Threading.Thread.Sleep(220); }
        frame.StepN(5, Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "orders-tab.png"));
        Assert.Equal(3, tab.LastRows.Count);
        Assert.True(NodeEditor.Core.Widgets.KitProbe.TryGet(NodeEditor.Core.Widgets.KitProbe.ListMenuButton, out _, out _), "the tasks are the kit list (a menu button per row)");
        Assert.True(bytes == 0, $"a steady Orders tab allocates nothing ({bytes} bytes)");
    }
}
