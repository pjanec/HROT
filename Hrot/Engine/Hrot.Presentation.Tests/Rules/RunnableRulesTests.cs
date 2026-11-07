using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.Presentation.Windows;
using Hrot.UI.Common.Adapters;
using Hrot.UI.Common.Map3D;
using Hrot.UI.Common.Rules;
using Xunit;

namespace Hrot.Presentation.Tests.Rules;

/// <summary>
/// ⭐⭐ CE-4239 / CE-4240 / CE-4241 (handoff Rules_Runnable ⑧ ② ③; docs/DESIGN_Rules_View_UI.md §7.10, docs/DESIGN_Entity_Hierarchy_Views.md §8) — the instructor's End, what went to whom, and the ONE "seeable on the map" rule, on a plain world.
/// </summary>
public sealed class RunnableRulesTests : IDisposable
{
    private readonly EntityRepository _world = new();
    private int _next = 100;

    public RunnableRulesTests()
    {
        _ = typeof(Hrot.AI.Behaviors.Brains.Objectives.ObjectiveStateKeepers).Assembly;   // the shipped actions (SetSkillAmplifier …) are in the catalogue's scan only once their assembly is loaded: ⚠ the palette rail was order-dependent without this
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<ObjectiveStatus>();
        _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterComponent<MacroOrigin>();
        _world.RegisterComponent<MacroExpansionStatus>();
        _world.RegisterManagedComponent<MacroInstance>();
        _world.RegisterComponent<EntityInfo>();
        _world.RegisterComponent<SimTransform>();
        _world.RegisterComponent<RefKey>();
        _world.RegisterComponent<ExerciseResult>();
        _world.RegisterComponent<TkbIdentity>();
    }

    public void Dispose() => _world.Dispose();

    private Entity Unit(string name, ForceId side = ForceId.Friend)
    {
        var e = _world.CreateEntity(); int id = ++_next;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64(name), ForceId = side });
        _world.AddComponent(e, new SimTransform());
        return e;
    }

    private Entity Rule(string title, ObjectiveRole role = ObjectiveRole.Objective, TriggerState trig = TriggerState.Armed, bool withInfo = true)
    {
        var e = _world.CreateEntity(); int id = ++_next;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new ObjectiveStatus { Title = new FixedString64(title), State = ObjectiveState.Active, Role = role, TriggerState = trig, Owner = ForceId.Friend });
        if (withInfo) _world.AddComponent(e, new EntityInfo { Name = new FixedString64(title) });
        return e;
    }

    private Entity Instance(MacroExpansionState state = MacroExpansionState.Done, bool positioned = true)
    {
        var e = _world.CreateEntity(); int id = ++_next;
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.SetManagedComponent(e, new MacroInstance { Macro = "game/score/Default" });
        _world.AddComponent(e, new MacroExpansionStatus { State = state });
        _world.AddComponent(e, new EntityInfo { Name = new FixedString64("") });
        if (positioned) _world.AddComponent(e, new SimTransform());
        return e;
    }

    private void Member(Entity member, Entity instance)
        => _world.AddComponent(member, new MacroOrigin { Instance = new EntityRef(_world.GetComponentRO<NetworkIdentity>(instance).Value) });

    private void Advance() { _world.Tick(); }   // the per-world cache is keyed on the world version

    // ───────────────────────────────────────────────── ⑧ the ONE seeable rule

    /// <summary>⭐ CE-2394 — <c>Seeable.OnMap</c> is answered once per entity per frame (the map's five projectors each asked it): the same answer
    /// on a repeat ask, a changed entity answered anew the next frame, a reused slot not mistaken for the old entity, and no allocation in a
    /// steady frame. ✅ Red-proof: a memo that ignores the world version ⇒ the rule-made unit stays seeable.</summary>
    [Fact]
    public void CE2394_SeeableIsAnsweredOncePerFrame_AndFollowsTheNextFrame()
    {
        var a = Unit("Alpha"); var b = Unit("Bravo"); var r = Rule("Hold");
        Assert.True(Seeable.OnMap(_world, a)); Assert.True(Seeable.OnMap(_world, a));
        Assert.False(Seeable.OnMap(_world, r));
        Assert.True(Seeable.OnMap(_world, b));                        // cached as seeable this frame
        _world.Tick();
        _world.AddComponent(b, new ObjectiveStatus { Title = new FixedString64("now a rule"), Role = ObjectiveRole.Objective });
        _world.Tick();
        Assert.False(Seeable.OnMap(_world, b));                       // the next frame sees the change
        _world.DestroyEntity(a); _world.Tick();
        var reused = Unit("Charlie");                                  // may take a's slot: a new generation, a fresh answer
        Assert.True(Seeable.OnMap(_world, reused));
        _world.Tick();
        for (int k = 0; k < 3; k++) { Seeable.OnMap(_world, reused); Seeable.OnMap(_world, r); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 100; k++) { Seeable.OnMap(_world, reused); Seeable.OnMap(_world, r); Seeable.OnMap(_world, b); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ARuleIsNotSeeable_AUnitIs()
    {
        Assert.True(Seeable.OnMap(_world, Unit("Alpha")));
        Assert.False(Seeable.OnMap(_world, Rule("Hold the bridge")));
        Assert.False(Seeable.OnMap(_world, Rule("Blue wins", ObjectiveRole.Trigger)));
    }

    [Fact]
    public void AMacroInstanceIsSeeableOnlyThroughASeeableMember_NestedToo()
    {
        var evaluation = Instance();                 // only objectives inside: an evaluation macro (CE-3365)
        Member(Rule("Points"), evaluation); Member(Rule("Kills"), evaluation);
        var forces = Instance();                     // a unit inside: a forces macro
        Member(Unit("Alpha"), forces);
        var outer = Instance(); var inner = Instance();   // outer holds ONLY an inner instance which holds only rules
        Member(inner, outer); Member(Rule("x"), inner);
        var outer2 = Instance(); var inner2 = Instance(); // outer2 holds an inner instance which holds a unit
        Member(inner2, outer2); Member(Unit("Bravo"), inner2);
        Advance();

        Assert.False(Seeable.OnMap(_world, evaluation));
        Assert.True(Seeable.OnMap(_world, forces));
        Assert.False(Seeable.OnMap(_world, outer));
        Assert.True(Seeable.OnMap(_world, outer2));
        Assert.True(Seeable.OnMap(_world, inner2));
    }

    [Fact]
    public void AnInstanceStillExpandingOrFailedStaysSeeable_SoItIsFoundAndItsReasonShows()
    {
        Assert.True(Seeable.OnMap(_world, Instance(MacroExpansionState.Expanding)));
        Assert.True(Seeable.OnMap(_world, Instance(MacroExpansionState.Failed)));
    }

    [Fact]
    public void TheCommandAndStructureOrbat_AskTheSameRuleAsTheMap_TheScenarioViewStillListsTheVirtualOnes()
    {
        var unit = Unit("Alpha"); var rule = Rule("Hold the bridge"); var evaluation = Instance(); Member(Rule("Points"), evaluation); Advance();
        foreach (Fdp.Toolkit.Vis2D.Abstractions.IHierarchyAdapter a in new Fdp.Toolkit.Vis2D.Abstractions.IHierarchyAdapter[] { new CommandHierarchyAdapter(), new StructureHierarchyAdapter() })
        {
            Assert.True(a.Includes(_world, unit));
            Assert.False(a.Includes(_world, rule));
            Assert.False(a.Includes(_world, evaluation));
        }
        Fdp.Toolkit.Vis2D.Abstractions.IHierarchyAdapter scenario = new ScenarioHierarchyAdapter();
        Assert.True(scenario.Includes(_world, rule));          // the Scenario view lists the virtual ones (HV-3)
        Assert.True(scenario.Includes(_world, evaluation));
    }

    // ───────────────────────────────────────────────── ② the instructor's End

    private Entity EndTrigger(TriggerState trig = TriggerState.Armed, string key = InstructorEnd.Key)
    {
        var e = Rule("Instructor end", ObjectiveRole.Trigger, trig);
        _world.AddComponent(e, new RefKey(key));
        return e;
    }

    [Fact]
    public void TheEndButton_IsDisabledSayingWhy_WhenThereIsNoInstructorEndTrigger()
    {
        Rule("End", ObjectiveRole.Trigger);          // a trigger by TITLE is not it: the KEY is
        EndTrigger(key: "something-else");
        var offer = InstructorEnd.Of(_world, null);
        Assert.False(offer.Available);
        Assert.Equal(InstructorEnd.NoTrigger, offer.DisabledReason);
        Assert.Contains("no instructor-end trigger", new InstructorEndControl(() => _world, () => null).Offer.DisabledReason);
    }

    [Fact]
    public void TheEndButton_FindsTheTriggerByItsKey_AndIsAvailableWhileItIsArmed()
    {
        Rule("Instructor end", ObjectiveRole.Trigger);   // same title, no key: ignored
        var end = EndTrigger();
        var offer = InstructorEnd.Of(_world, null);
        Assert.True(offer.Available);
        Assert.Equal(end, offer.Trigger);
    }

    [Fact]
    public void TheEndButton_RefusesWithTheOwnersReason_AfterTheEnd_OnARecording_AndWhileTheTriggerIsNotRunning()
    {
        var end = EndTrigger();
        Assert.Equal("a recording is read-only", InstructorEnd.Of(_world, "a recording is read-only").DisabledReason);

        var done = _world.CreateEntity(); _world.AddComponent(done, new ExerciseResult { Phase = ExercisePhase.Ended });
        Assert.Equal(InstructorEnd.Ended, InstructorEnd.Of(_world, null).DisabledReason);
        _world.RemoveComponent<ExerciseResult>(done);

        _world.SetComponent(end, new ObjectiveStatus { Role = ObjectiveRole.Trigger, State = ObjectiveState.Active, TriggerState = TriggerState.Fired });
        Assert.Equal("it is fired", InstructorEnd.Of(_world, null).DisabledReason);   // RuleCommands.DisabledReason is the one source of the words
    }

    [Fact]
    public void AnAuthoredTriggerNotYetRunning_SaysSo()
    {
        var e = _world.CreateEntity(); _world.AddComponent(e, new NetworkIdentity(++_next)); _world.AddComponent(e, new RefKey(InstructorEnd.Key));
        Assert.Equal(RulesModel.NotRunning, InstructorEnd.Of(_world, null).DisabledReason);
    }

    [Fact]
    public void TheEndControl_AsksBeforeFiring_CancelWritesNothing()
    {
        EndTrigger();
        var c = new InstructorEndControl(() => _world, () => null);
        Assert.True(c.Ask());
        Assert.True(c.Confirming);
        c.Cancel();
        Assert.False(c.Confirming);
        Assert.Null(c.Message);
        var none = new InstructorEndControl(() => _world, () => "a recording is read-only");
        Assert.False(none.Ask());
        Assert.False(none.Confirming);
    }

    /// <summary>⭐ Replay Browser UX ⑤ / Q-B4 (R-4156) — on a RECORDING End exercise does not exist: the control says so by the world (the Replay Browser and an AAR
    /// alike) and its Draw draws nothing. ✅ Red-proof: ShowsARecording false ⇒ red.</summary>
    [Fact]
    public void QB4_OnARecording_TheEndControlIsNotDrawn()
    {
        EndTrigger();
        var c = new InstructorEndControl(() => _world, () => null);
        Assert.False(c.ShowsARecording);
        Fdp.Toolkit.Replay.ReplayState.Enter(_world);
        try { Assert.True(c.ShowsARecording); }
        finally { Fdp.Toolkit.Replay.ReplayState.Leave(_world); }
    }

    /// <summary>⭐ R-4156 — the Mission summary on a recording says where the playhead is: "at 0:39 of 1:06".</summary>
    [Fact]
    public void QB4_TheSummary_SaysWhereInTheRecording()
    {
        long s = TimeSpan.TicksPerSecond, from = 1000 * s;
        Assert.Equal("at 0:39 of 1:06", MissionSummaryWindow.RecordingPosition(from, from + 66 * s, from + 39 * s));
        Assert.Equal("at 0:00 of 1:06", MissionSummaryWindow.RecordingPosition(from, from + 66 * s, from - 5 * s));      // clamped
        Assert.Equal("at 1:02:05 of 2:00:00", MissionSummaryWindow.RecordingPosition(0, 7200 * s, 3725 * s));
    }

    [Fact]
    public void TheRulesAndSummaryWindows_BothCarryTheSameEndControl()
    {
        EndTrigger();
        var rules = new RulesWindow(() => _world, () => null, null, "t_rules", "Scenario", System.Numerics.Vector4.One, _ => { }, _ => { }, now: () => 0d) { ListView = false };   // U-18: these rails drive the Timeline
        var summary = new MissionSummaryWindow(() => _world, () => null, null, "t_sum", "Scenario", System.Numerics.Vector4.One, _ => { }, now: () => 0d);
        Assert.True(rules.End.Offer.Available);
        Assert.True(summary.End.Offer.Available);
        var rec = new MissionSummaryWindow(() => _world, () => null, null, "t_sum2", "Scenario", System.Numerics.Vector4.One, _ => { }, now: () => 0d, blocked: () => "a recording is read-only");
        Assert.Equal("a recording is read-only", rec.End.Offer.DisabledReason);
    }

    // ───────────────────────────────────────────────── ③ what went to whom

    private static PlayerMessageLike M(int id, string kind, int value, string text, double at, int source) => new(id, kind, value, text, at, source);
    private readonly record struct PlayerMessageLike(int Id, string Kind, int Value, string Text, double At, int Source);

    private void Sent(PlayerMessageLike m)
        => PlayerMessages.In(_world).Record(new PlayerMessageEvent { MessageId = m.Id, ToKind = m.Kind, ToValue = m.Value, Text = m.Text, SentAt = (SimInstant)m.At, Source = m.Source });

    [Fact]
    public void ABriefingToAll_NamesItsRecipientsAndWhen()
    {
        var briefing = Rule("Briefing", ObjectiveRole.Trigger);
        int net = _world.GetComponentRO<NetworkIdentity>(briefing).Value;
        Sent(M(1, "All", 0, "Seize the crossroads", 5, net));
        Sent(M(2, "All", 0, "someone else's", 9, net + 99));
        var lines = MessageDelivery.LinesOf(_world, net);
        Assert.Single(lines);
        Assert.Equal("0:05  to everyone (every side, unit and player): \"Seize the crossroads\"", lines[0]);
    }

    [Fact]
    public void Recipients_ResolveSideUnitStationAndInstructor()
    {
        var unit = Unit("Alpha 1-1", ForceId.Hostile); int unitNet = _world.GetComponentRO<NetworkIdentity>(unit).Value;
        Assert.Equal("the Blue side (its units and their players)", MessageDelivery.Recipients(_world, "Side", (int)ForceId.Friend));
        Assert.Equal("the Red side (its units and their players)", MessageDelivery.Recipients(_world, "Side", (int)ForceId.Hostile));
        Assert.Equal($"unit \"Alpha 1-1\" (#{unitNet}, Red) and its player", MessageDelivery.Recipients(_world, "Unit", unitNet));
        Assert.Equal("unit #999999 and its player", MessageDelivery.Recipients(_world, "Unit", 999999));
        Assert.Equal("the player at station 3", MessageDelivery.Recipients(_world, "Station", 3));
        Assert.Contains("instructor only", MessageDelivery.Recipients(_world, "Instructor", 0));
    }

    [Fact]
    public void AMessageStep_ShowsItsAuthoredAddress_NotSentYet_ThenWhenItWasSent_ASubjectTemplateMatches()
    {
        var trig = Rule("Ease off", ObjectiveRole.Trigger); int net = _world.GetComponentRO<NetworkIdentity>(trig).Value;
        var step = new ActionStep { Act = "Message", Args = new JsonObject { ["toKind"] = "Side", ["toValue"] = (int)ForceId.Friend, ["text"] = "{subject} is down" } };
        Assert.Equal("to the Blue side (its units and their players) - not sent yet", MessageDelivery.StepNote(_world, net, step));
        Sent(M(7, "Side", (int)ForceId.Friend, "Kral is down", 61, net));
        Assert.Equal("to the Blue side (its units and their players) - sent 1x, at 1:01", MessageDelivery.StepNote(_world, net, step));
        Assert.Null(MessageDelivery.StepNote(_world, net, new ActionStep { Act = "SetObjectiveActive", Args = new JsonObject() }));
    }

    [Fact]
    public void ThePlayerMessageStore_KeepsTheSource_AndStillAddressesByViewer()
    {
        Sent(M(1, "Side", (int)ForceId.Hostile, "to red", 1, 55));
        var store = PlayerMessages.Of(_world)!;
        Assert.Equal(55, store.All.Single().Source);
        Assert.Empty(store.Log(new MessageViewer(0, ForceId.Friend, 0)));
        Assert.Single(store.Log(new MessageViewer(0, ForceId.Hostile, 0)));
    }

    [Fact]
    public void ANeverTrueClockPlaceholder_IsNotAClockPin_SoItIsNeverComingUp()
    {
        var e = Rule("Instructor end", ObjectiveRole.Trigger);
        _world.RegisterManagedComponent<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>();
        _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { InstanceId = 1, BehaviorName = "Trigger", JsonParams = "{\"when\":{\"$type\":\"ScenarioTime\",\"fromSeconds\":1000000000}}" });
        var s = _world.GetComponentRO<ObjectiveStatus>(e);
        Assert.Empty(RulesModel.ClockPins(_world, e, s));
        _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { InstanceId = 1, BehaviorName = "Trigger", JsonParams = "{\"when\":{\"$type\":\"ScenarioTime\",\"fromSeconds\":1200}}" });
        Assert.Single(RulesModel.ClockPins(_world, e, s));
    }

    // ───────────────────────────────────────────────── ⑥ the game-master palette (CE-4243)

    [Fact]
    public void AHandRunAction_IsATransientOneShotTrigger_WithTheStepInItsDoList()
    {
        var step = GmOneShot.SkillStep(ForceId.Hostile, -1);
        var json = JsonNode.Parse(GmOneShot.TriggerParamsJson(step))!.AsObject();
        Assert.Equal("GM · SetSkillAmplifier", (string?)json["common"]!["title"]);
        Assert.Equal("ScenarioTime", (string?)json["when"]!["$type"]);
        Assert.Equal(0, (int)json["when"]!["fromSeconds"]!);
        Assert.Equal(1, (int)json["maxFires"]!);
        Assert.True((bool)json["fireIfTrueWhenArmed"]!);
        var act = json["do"]!.AsArray().Single()!.AsObject();
        Assert.Equal("SetSkillAmplifier", (string?)act["act"]);
        Assert.Equal("Hostile", (string?)act["args"]!["side"]);
        Assert.Equal(-1, (int)act["args"]!["tiers"]!);
        Assert.Equal(2, (int)GmOneShot.SkillStep(ForceId.Friend, 9).Args!["tiers"]!);   // clamped to the verb's own range
    }

    [Fact]
    public void ThePalette_RunsTheStepThroughTheOneCreationPath_AndIsRefusedInEditModeWithTheReason()
    {
        var made = new List<string>();
        var w = new GameMasterWindow(() => _world, "t_gm", "Scenario", System.Numerics.Vector4.One,
            new RuleEditorServices(() => null, () => null, (_, _, _) => true, new Hrot.Presentation.Edits.EditHistory(), _ => Array.Empty<RuleJournalEntry>()),
            createOneShot: json => { made.Add(json); return true; });
        Assert.Equal(GmOneShot.EditModeRefusal, w.Refusal());               // no rule runs: edit mode
        Assert.False(w.SetSkill(ForceId.Hostile, -1));
        Assert.Empty(made);

        Rule("Mission");                                                    // a running exercise has statuses
        Assert.Null(w.Refusal());
        Assert.True(w.SetSkill(ForceId.Hostile, -1));
        Assert.True(w.Run(new ActionStep { Act = "Message", Args = new JsonObject { ["toKind"] = "Side", ["toValue"] = 1, ["text"] = "Hold the line" } }));
        Assert.Equal(2, made.Count);
        Assert.Contains("\"toKind\":\"Side\"", made[1]);
        Assert.Equal(new[] { "GM · SetSkillAmplifier", "GM · Message" }, w.Made);

        var off = new GameMasterWindow(() => _world, "t_gm2", "Scenario", System.Numerics.Vector4.One,
            new RuleEditorServices(() => null, () => null, (_, _, _) => true, new Hrot.Presentation.Edits.EditHistory(), _ => Array.Empty<RuleJournalEntry>()),
            createOneShot: _ => true, blocked: () => "a recording is read-only", enabled: () => true);
        Assert.Equal("a recording is read-only", off.Refusal());
        var disabled = new GameMasterWindow(() => _world, "t_gm3", "Scenario", System.Numerics.Vector4.One,
            new RuleEditorServices(() => null, () => null, (_, _, _) => true, new Hrot.Presentation.Edits.EditHistory(), _ => Array.Empty<RuleJournalEntry>()),
            createOneShot: _ => true, enabled: () => false);
        Assert.Contains("gm.palette", disabled.Refusal());
    }

    [Fact]
    public void ThePalette_ShowsTheCurrentSkillOfEachSide_AndPrefillsTheSelection()
    {
        _world.RegisterComponent<Fdp.Toolkit.Weather.ScenarioClock>();
        _world.RegisterComponent<Fdp.Toolkit.Behavior.Components.SkillAmplifier>();
        var env = _world.CreateEntity(); _world.AddComponent(env, new Fdp.Toolkit.Weather.ScenarioClock()); _world.AddComponent(env, new Fdp.Toolkit.Behavior.Components.SkillAmplifier { Hostile = -1 });
        var w = new GameMasterWindow(() => _world, "t_gm4", "Scenario", System.Numerics.Vector4.One,
            new RuleEditorServices(() => null, () => null, (_, _, _) => true, new Hrot.Presentation.Edits.EditHistory(), _ => Array.Empty<RuleJournalEntry>()),
            createOneShot: _ => true, selection: () => (4242, ForceId.Hostile));
        Assert.Equal(-1, w.SkillOf(ForceId.Hostile));
        Assert.Equal(0, w.SkillOf(ForceId.Friend));
        w.Rebuild();
        Assert.Equal(new[] { "Blue 0", "Red -1" }, w.Probe.Skill);

        var set = w.Prefill(new ActionStep { Act = "SetSkillAmplifier", Args = new JsonObject() });
        Assert.Equal("Hostile", (string?)set.Args!["side"]);                // GM-2: the selection's side
        var kept = w.Prefill(new ActionStep { Act = "SetSkillAmplifier", Args = new JsonObject { ["side"] = "Friend" } });
        Assert.Equal("Friend", (string?)kept.Args!["side"]);                // an authored arg is never overwritten
    }

    [Fact]
    public void TheRulesView_GroupsTheGameMastersRulesUnderOneSection_LastOfTheMacros()
    {
        Rule("Mission");
        Rule("GM · Message", ObjectiveRole.Trigger);
        var view = RulesModel.Build(_world, new RulesOptions(RulesGroupBy.Macro), null, 10);
        Assert.Equal(new[] { RulesModel.ScenarioGroup, GmOneShot.GroupName }, view.Groups.Select(g => g.Name).ToArray());
        Assert.Equal("GM · Message", view.Groups[1].Rows.Single().Title);
    }

    [Fact]
    public void TheGamePaletteFeature_IsDeclaredOnEveryHost_OnForTheServerUi_OffForThePlayer()
    {
        var server = Hrot.Common.Infrastructure.HudFeatures.Declarations(playerDefault: false).Single(d => d.Id == Hrot.Common.Infrastructure.HudFeatures.GamePalette);
        var player = Hrot.Common.Infrastructure.HudFeatures.Declarations(playerDefault: true).Single(d => d.Id == Hrot.Common.Infrastructure.HudFeatures.GamePalette);
        Assert.Equal("gm.palette", server.Id);
        var gateS = new Hrot.Common.Infrastructure.NodeFeatures(new Hrot.Common.Infrastructure.FeatureRegistry(new[] { server }), Fdp.Core.NodeRole.Map2D);
        var gateP = new Hrot.Common.Infrastructure.NodeFeatures(new Hrot.Common.Infrastructure.FeatureRegistry(new[] { player }), Fdp.Core.NodeRole.Map2D);
        Assert.True(Hrot.Common.Infrastructure.HudFeatures.IsOn(gateS, "gm.palette"));
        Assert.False(Hrot.Common.Infrastructure.HudFeatures.IsOn(gateP, "gm.palette"));
    }

    /// <summary>
    /// ⭐ CE-4229 (⑨a) — the bounty marker of the 2-D / 3-D map: an Active <c>Bounty</c> status draws a gold <i>BOUNTY</i> line into its SUBJECT's card (Labels layer); a Done one, another kind, or one with no subject draws nothing.
    /// ✅ Red-proof: the Active test dropped ⇒ the finished bounty draws ⇒ red.
    /// </summary>
    [Fact]
    public void TheBountyMarker_IsAGoldLineOnTheSubjectsCard_WhileTheBountyIsActive()
    {
        var man = Unit("Kral"); int manNet = _world.GetComponentRO<NetworkIdentity>(man).Value;
        Entity Bounty(string kind, ObjectiveState state, int subject)
        {
            var e = Rule("Bounty", ObjectiveRole.Objective); _world.GetComponentRW<ObjectiveStatus>(e).Kind = new FixedString32(kind); _world.GetComponentRW<ObjectiveStatus>(e).State = state;
            _world.AddComponent(e, new ObjectiveLinks { Subject = new EntityRef(subject) });
            return e;
        }
        string[] Drawn(Entity rule)
        {
            var d = new Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveBuffer();
            new BountyMarkerGizmo().Draw(_world, rule, d);
            return d.GetFrame().ToArray().Where(p => p.Shape == Fdp.Toolkit.Diagnostics.Gizmos.DebugPrimitiveShape.Text).Select(p => $"{p.AnchorIndex}:{p.TextContent}").ToArray();
        }
        Assert.Equal(new[] { $"{manNet}:BOUNTY" }, Drawn(Bounty("Bounty", ObjectiveState.Active, manNet)));
        Assert.Empty(Drawn(Bounty("Bounty", ObjectiveState.Achieved, manNet)));
        Assert.Empty(Drawn(Bounty("Count", ObjectiveState.Active, manNet)));
        Assert.Empty(Drawn(Bounty("Bounty", ObjectiveState.Active, 0)));
    }
}
