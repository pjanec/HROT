using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.Panels.Timeline;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Fdp.Toolkit.ScenarioRules.Actions;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Tests.Map3D;
using Hrot.Presentation.Windows;
using Hrot.UI.Common.Rules;
using ImGuiNET;
using Raylib_cs;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐⭐ CE-4216 (rules view R2; docs/DESIGN_Rules_View_UI.md RV-3 / RV-5 / RV-6) — <b>Window ▸ Rules in a REAL ImGui frame</b>: the rows drawn with their headers, a click on a row (an item, and the header) selects the row AND its subject, and a disabled action
/// button carries its reason. A small tree — a mission with a reach objective, a trigger that is acting, a failed objective — over a live world, no recording (the source seam: the same window opens on one). ⛔ Screenshots are evidence (<c>HROT_RULES_SHOTS</c>), never compared.
/// </summary>
[Collection(Map3DFrameRail.Serial)]
public sealed class RulesWindowFrameRail : IDisposable
{
    private readonly EntityRepository _world = new();

    public RulesWindowFrameRail()
    {
        _world.RegisterComponent<NetworkIdentity>();
        _world.RegisterComponent<ObjectiveStatus>();
        _world.RegisterComponent<ObjectiveLinks>();
        _world.RegisterManagedComponent<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>();
    }

    public void Dispose() => _world.Dispose();

    private void Add(int id, string title, int parent, int subject, ObjectiveState state, ObjectiveRole role = ObjectiveRole.Objective, double changedAt = 0,
                     TriggerState trig = TriggerState.Disarmed, int fires = 0, double lastFired = 0, byte stepCur = 0, byte stepCount = 0)
    {
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(id));
        _world.AddComponent(e, new ObjectiveStatus
        {
            Title = new FixedString64(title), State = state, Role = role, Owner = ForceId.Friend, ChangedAt = changedAt, TriggerState = trig,
            FireCount = fires, LastFired = lastFired, StepCur = stepCur, StepCount = stepCount,
        });
        _world.AddComponent(e, new ObjectiveLinks { Parent = new EntityRef(parent), Subject = new EntityRef(subject) });
    }

    private static string ShotPath(string name)
    {
        string dir = Environment.GetEnvironmentVariable("HROT_RULES_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-rules-rail");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private static void Pos(ImGuiIOPtr io, float x, float y) { io.AddMousePosEvent(x, y); Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    [SkippableFact]
    public void TheRulesWindow_DrawsTheRowsWithHeaders_AClickSelectsTheRowAndItsSubject_ADisabledButtonCarriesItsReason()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Add(10, "Mission", 0, 900, ObjectiveState.Active, changedAt: 0);
        Add(11, "Reach the bridge", 10, 901, ObjectiveState.Active, changedAt: 20);
        Add(12, "Fire at target", 10, 900, ObjectiveState.Active, ObjectiveRole.Trigger, changedAt: 40, trig: TriggerState.Acting, fires: 1, lastFired: 60, stepCur: 2, stepCount: 3);
        Add(13, "Hold the ridge", 10, 0, ObjectiveState.Failed, changedAt: 30);

        var picked = new List<int>(); var centred = new List<int>();
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules", "Scenario", Vector4.One, picked.Add, centred.Add, now: () => 120d);
        using var frame = UiFrameHarness.Begin(1280, 420);

        void Step(Action<ImGuiIOPtr>? input = null, int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    input?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 420));
                    ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
                    window.DrawContent();
                    ImGui.End();
                });
        }
        void Click(float x, float y)
        {
            Step(io => Pos(io, x, y), 2);
            Step(io => { Pos(io, x, y); io.AddMouseButtonEvent(0, true); });
            Step(io => Pos(io, x, y));
            Step(io => { Pos(io, x, y); io.AddMouseButtonEvent(0, false); });
            Step(null, 2);
        }

        // ── the rows, drawn: a group header and the four rules in tree order, each with its state in words
        Step(null, 5);
        Assert.Equal(new[] { "Mission | Active 2:00", "  Reach the bridge | Active 1:40", "  Fire at target | Acting 2/3", "  Hold the ridge | Failed" }, window.Probe.Rows);
        var hits = window.Panel.LastTrackHits;
        Assert.Contains(hits, h => h.TrackId == "r:11");                        // the span of a row is drawn (its hit box exists)
        Assert.Contains(hits, h => h.TrackId == "r:12" && h.ItemId.StartsWith("f"));   // and the trigger's fire diamond
        Assert.Null(window.SelectedNetworkId);
        frame.Screenshot(ShotPath("rules-0-rows.png"));

        // ── a click on a row's span selects the row AND its subject
        var reach = hits.First(h => h.TrackId == "r:11");
        Click(reach.X + 4f, reach.Y);
        Assert.Equal(11, window.SelectedNetworkId);
        Assert.Equal(new[] { 901 }, picked);

        // ── a click on the HEADER (label column) of the trigger's row does the same
        hits = window.Panel.LastTrackHits;
        var fire = hits.First(h => h.TrackId == "r:12");
        Click(120f, fire.Y);
        Assert.Equal(12, window.SelectedNetworkId);
        Assert.Equal(new[] { 901, 900 }, picked);
        Assert.Contains("Kind: Trigger", window.Probe.Details);
        Assert.Contains("Fired: 1× · last at 1:00", window.Probe.Details);

        // ── a trigger that is ACTING cannot be fired / re-armed: the button is listed, disabled, WITH the reason
        var fireNow = window.Probe.Buttons.Single(b => b.Label == "Fire now");
        Assert.NotNull(fireNow.Reason);
        Assert.Contains("acting", fireNow.Reason);
        Assert.Null(window.Probe.Buttons.Single(b => b.Label == "Disarm").Reason);   // while Disarm is valid
        frame.Screenshot(ShotPath("rules-1-trigger-selected.png"));

        // ── a failed objective: Activate needs a Reset first; no subject ⇒ Show on map disabled and nothing is selected for it
        var hold = window.Panel.LastTrackHits.First(h => h.TrackId == "r:13");
        Click(120f, hold.Y);
        Assert.Equal(13, window.SelectedNetworkId);
        Assert.Equal(new[] { 901, 900 }, picked);                                      // a rule with no subject selects no unit
        Assert.Contains("Failed", window.Probe.Buttons.Single(b => b.Label == "Activate").Reason);
        Assert.Equal("this rule has no subject", window.Probe.Buttons.Single(b => b.Label == "Show on map").Reason);
        Assert.Null(window.Probe.Buttons.Single(b => b.Label == "Reset").Reason);
        frame.Screenshot(ShotPath("rules-2-failed-selected.png"));
    }

    /// <summary>A recording is read-only: every action is listed, disabled with THAT reason, and asking writes nothing.</summary>
    [Fact]
    public void OnARecording_EveryActionIsDisabledWithTheReason_AndAskingWritesNothing()
    {
        Add(12, "Fire at target", 0, 900, ObjectiveState.Active, ObjectiveRole.Trigger, trig: TriggerState.Armed);
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_rec", "Scenario", Vector4.One, _ => { }, _ => { }, blocked: () => "a recording is read-only", now: () => 5d);
        window.SelectNetworkId(12);
        window.Rebuild();
        Assert.All(window.Probe.Buttons.Where(b => b.Label != "Show on map"), b => Assert.Equal("a recording is read-only", b.Reason));
        var row = Hrot.UI.Common.Rules.RulesModel.Build(_world, new Hrot.UI.Common.Rules.RulesOptions(), null, 5d).Find(12)!;
        window.Ask(row, RuleRequest.Fire);
        Assert.Equal("a recording is read-only", window.Message);          // refused before any write: this world has no write router, so a write would have thrown
    }

    /// <summary>
    /// ⭐ RV-3a — EDIT MODE: the rules exist only as authored params (no <c>ObjectiveStatus</c>, so no spans and no index): the rows are STILL drawn, in the real frame, with the ruler — not "Indexing..." — and a click selects.
    /// ✅ Red-proof: <c>ExtentWithoutItems</c> off ⇒ no extent ⇒ the panel draws nothing ⇒ red.
    /// </summary>
    [SkippableFact]
    public void InEditMode_TheAuthoredRulesAreDrawn_WithNoStatusAndNoIndex()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        void Rule(int id, string kind, string json)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new NetworkIdentity(id));
            _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 });
        }
        Rule(10, "WeightedMean", """{"common":{"title":"Mission","subject":900}}""");
        Rule(11, "Trigger", """{"common":{"title":"Counter-attack","parent":10,"subject":900},"when":{"$type":"OnEntity","ref":{"NetworkId":10}}}""");

        var picked = new List<int>();
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_edit", "Scenario", Vector4.One, picked.Add, _ => { });
        using var frame = UiFrameHarness.Begin(1280, 300);
        void Step(Action<ImGuiIOPtr>? input = null, int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    input?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 300));
                    ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
                    window.DrawContent();
                    ImGui.End();
                });
        }
        Step(null, 5);
        Assert.Equal(new[] { "Mission | Authored", "  Counter-attack | Authored" }, window.Probe.Rows);
        Assert.True(window.Panel.View.HasExtent, "the ruler has an extent although no track holds an item");
        Assert.Equal(new[] { "a", "a" }, window.Panel.LastTrackHits.Select(h => h.ItemId));   // no spans — only each row's ANCHOR key (R4: where an arrow enters and the connect handle sits); the headers are the rows
        frame.Screenshot(ShotPath("rules-3-authored.png"));

        // a click on the header of the trigger picks it: its details say Authored, every button carries the reason
        Step(io => Pos(io, 120f, 138f), 2);
        Step(io => { Pos(io, 120f, 138f); io.AddMouseButtonEvent(0, true); });
        Step(io => Pos(io, 120f, 138f));
        Step(io => { Pos(io, 120f, 138f); io.AddMouseButtonEvent(0, false); });
        Step(null, 2);
        Assert.Equal(11, window.SelectedNetworkId);
        Assert.Equal(new[] { 900 }, picked);
        Assert.Contains("State: Authored", window.Probe.Details);
        Assert.Contains("Waits on: Mission", window.Probe.Details);
        Assert.All(window.Probe.Buttons.Where(b => b.Label != "Show on map"), b => Assert.Equal(RulesModel.NotRunning, b.Reason));
    }

    /// <summary>
    /// ⭐ CE-4218 (R5a) — the RULE EDITOR in a real frame, EDIT MODE: a trigger's tab set (Overview · When · Rules · Do · Bindings · History) and an objective's (Status · Settings · Results · History), each tab's fields drawn from the ONE params form (the When tab through the
    /// standard predicate leaf), the header above them, Apply / Revert / Undo on the bar. ✅ Red-proof: <c>RuleTabs.FieldsOf</c> returning nothing for the Rules tab ⇒ its fields vanish ⇒ red.
    /// </summary>
    [SkippableFact]
    public void TheRuleEditor_DrawsBothTabSets_EachTabsFieldsFromTheOneForm()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        _world.RegisterManagedComponent<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>();
        void Rule(int id, string kind, string json)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new NetworkIdentity(id));
            _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 });
        }
        Rule(10, "ReachArea", """{"common":{"title":"Reach the bridge","subject":901}}""");
        Rule(20, "Trigger", """{"common":{"title":"Counter-attack","subject":900},"when":{"$type":"OnEntity","ref":{"NetworkId":10},"predicate":{"$type":"PropertyMatch","ComponentType":"ObjectiveStatus","PropertyPath":"State","Operator":"Equals","Predicate":{"$type":"Numeric","MinValue":2,"MaxValue":2}}},"cooldown":10}""");
        var history = new Hrot.Presentation.Edits.EditHistory();
        var svc = new RuleEditorServices(() => Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build(), () => null,
            (e, n, j) => true, history, _ => Array.Empty<RuleJournalEntry>());
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_ed", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 5d, editor: svc);
        using var frame = UiFrameHarness.Begin(1400, 700);
        void Step(Action<ImGuiIOPtr>? input = null, int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    input?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1400, 700));
                    ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
                    window.DrawContent();
                    ImGui.End();
                });
        }

        window.SelectNetworkId(20);
        Step(null, 4);
        var ed = window.Probe.Editor!;
        Assert.Equal(new[] { "Overview", "When", "Rules", "Do", "Bindings", "History" }, ed.Tabs);
        Assert.True(ed.Editable);
        window.Details!.RequestTab(RuleTab.When); Step(null, 4);
        Assert.Equal("When", window.Probe.Editor!.Tab);
        Assert.Equal(new[] { "Common", "When" }, window.Probe.Editor.Fields.ToArray());
        frame.Screenshot(ShotPath("rules-4-when.png"));
        window.Details.RequestTab(RuleTab.Rules); Step(null, 4);
        Assert.Equal(new[] { "Common", "HoldFor", "Delay", "Cooldown", "Repeat", "MaxFires", "FireIfTrueWhenArmed", "WhileTrue" }, window.Probe.Editor!.Fields.ToArray());
        frame.Screenshot(ShotPath("rules-5-rules.png"));
        window.Details.RequestTab(RuleTab.Do); Step(null, 3);
        Assert.Equal(new[] { "Common", "Do" }, window.Probe.Editor!.Fields.ToArray());   // the tab OWNS `do`; its list editor is R5b, so the tab draws a placeholder

        // ⭐ found LIVE: clicking Undo changed CanUndo between BeginDisabled and EndDisabled and ImGui asserted (the editor died). Apply, then CLICK Undo in the frame.
        window.SelectNetworkId(20);
        Step(null, 3);
        window.Details!.StagePending("""{"common":{"title":"Counter-attack","subject":900},"when":{"$type":"ScenarioTime","FromSeconds":1,"ToSeconds":2},"cooldown":33}""");   // ⭐ CE-4249: a trigger without a `when` is refused at Apply, as the runtime does
        Step(null, 2);
        Assert.True(window.Details.Apply(Hrot.UI.Common.Rules.RulesModel.Build(_world, new Hrot.UI.Common.Rules.RulesOptions(), null, 5d).Find(20)!));
        Step(null, 3);
        Assert.True(history.CanUndo);
        var r = window.Details.UndoRect; var at = (r.Min + r.Max) / 2f;
        Step(io => Pos(io, at.X, at.Y), 2);
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); });
        Step(io => Pos(io, at.X, at.Y));
        Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); });
        Step(null, 3);
        Assert.False(history.CanUndo);                                   // the click ran Undo and the frame survived

        window.SelectNetworkId(10);
        Step(null, 4);
        Assert.Equal(new[] { "Status", "Settings", "Results", "History" }, window.Probe.Editor!.Tabs);
        window.Details.RequestTab(RuleTab.Settings); Step(null, 4);
        Assert.Equal("Settings", window.Probe.Editor!.Tab);
        Assert.Contains("Common", window.Probe.Editor.Fields);
        Assert.True(window.Probe.Editor.Fields.Count > 1, "a kind's own fields are drawn on Settings");
        frame.Screenshot(ShotPath("rules-6-settings.png"));
    }

    /// <summary>
    /// ⭐ CE-4220 (R5b) — the Do tab in a REAL frame, on a LIVE trigger that is acting at step 2 of 3: the rows, the running-step marker and "step 2 / 3"; CLICKS on <i>+ Add step</i> (the picker's answer comes back through the host's opener), on a row's ^ and x;
    /// then Apply live writes the edited do list as the runtime parses it. The When tab turns the node feature <c>ui.rules-live-values</c> on and draws the Live strip; leaving it turns it off.
    /// ✅ Red-proof: the marker taken from the wrong index (StepCur, not StepCur - 1) ⇒ the marked row moves ⇒ red.
    /// </summary>
    [SkippableFact]
    public void TheDoTab_DrawsTheStepRows_AndTheLiveStrip_InARealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        _world.RegisterComponent<RuleLeaves>();
        _ = typeof(Hrot.AI.Behaviors.Brains.Objectives.ObjectiveStateKeepers).Assembly;
        const string json = """{"common":{"title":"Counter-attack","subject":900},"when":{"$type":"OnEntity","ref":{"NetworkId":10},"predicate":{"$type":"PropertyMatch","ComponentType":"ObjectiveStatus","PropertyPath":"State","Operator":"Equals","Predicate":{"$type":"Numeric","MinValue":2,"MaxValue":2}}},"do":[{"act":"Message","args":{"Text":"one"}},{"act":"Message","args":{"Text":"two"}},{"act":"Message","args":{"Text":"three"}}]}""";
        Add(20, "Counter-attack", 0, 900, ObjectiveState.Active, ObjectiveRole.Trigger, trig: TriggerState.Acting, stepCur: 2, stepCount: 3);
        Entity rule = default; foreach (var re in _world.Query().With<ObjectiveStatus>().Build()) rule = re;
        _world.SetManagedComponent(rule, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = "Trigger", JsonParams = json, InstanceId = 1 });
        _world.AddComponent(rule, new RuleLeaves());
        string label = RuleLeafPlan.LabelOf(((Hrot.Map.Definitions.Behavior.TriggerParamsJsonDto)Hrot.Presentation.Behavior.BehaviorParamsForm.Hydrate(typeof(Hrot.Map.Definitions.Behavior.TriggerParamsJsonDto), json)).When!);
        ref var leaves = ref _world.GetComponentRW<RuleLeaves>(rule);
        leaves.Count = 1; leaves.Leaves[0] = new RuleLeaf { Label = new FixedString32(label), Value = 1, Measured = 2f };

        var message = ActionCatalogue.All.Single(a => a.ShortName == "Message");
        var written = new List<string>(); var liveValues = new List<bool>(); NodeEditor.UI.Picker.PickerRequest? asked = null;
        var svc = new RuleEditorServices(() => Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build(), () => null, (e, n, j) => true, new Hrot.Presentation.Edits.EditHistory(), _ => Array.Empty<RuleJournalEntry>(),
            OpenPicker: () => (req, answer) => { asked = req; answer(new NodeEditor.UI.Picker.PickerResult(new[] { req.ItemsProvider().First(x => x.Name == "Message") })); },
            Live: new RuleLiveServices(k => null, on => liveValues.Add(on)));
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_do", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 5d, editor: svc);
        window.Details!.LiveWriter = (EntityRepository w, Entity e, string j, int o, ref int last, out int seq) => { seq = ++last; written.Add(j); return Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct; };
        float winH = 1900f;
        using var frame = UiFrameHarness.Begin(1400, 1900);
        void Step(Action<ImGuiIOPtr>? input = null, int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    input?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1400, winH));
                    ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
                    window.DrawContent();
                    ImGui.End();
                });
        }
        void Click((Vector2 Min, Vector2 Max) r)
        {
            var at = (r.Min + r.Max) / 2f;
            Step(io => Pos(io, at.X, at.Y), 2);
            Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); });
            Step(io => Pos(io, at.X, at.Y));
            Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); });
            Step(null, 3);
        }

        window.SelectNetworkId(20);
        window.Details!.RequestTab(RuleTab.Do);
        Step(null, 5);
        var ed = window.Probe.Editor!;
        Assert.True(ed.Editable, ed.ReadOnlyReason);
        Assert.Equal(new[] { "1. Message", "> 2. Message", "3. Message" }, ed.Steps);                       // the running step is marked
        Assert.Equal("step 2 / 3", ed.StepMarker);
        Assert.True(window.Details.Steps.Probe.AddEnabled);
        frame.Screenshot(ShotPath("rules-7-do.png"));

        Click(window.Details.Steps.AddRect);                                                                // + Add step ⇒ the generic picker answers Message
        Assert.Equal("rules.add-step", asked!.ContextKey);
        Assert.Equal(4, window.Probe.Editor!.Steps!.Count);
        Assert.True(window.Probe.Editor.Dirty);
        Click(window.Details.Steps.UpRects[3]);                                                             // the new step up one
        Click(window.Details.Steps.RemoveRects[0]);                                                         // the first one out
        Assert.Equal(3, window.Probe.Editor!.Steps!.Count);
        frame.Screenshot(ShotPath("rules-8-do-edited.png"));

        var row = RulesModel.Build(_world, new RulesOptions(), null, 5d).Find(20)!;
        Assert.True(window.Details.Apply(row));                                                             // LIVE: RuleCommands.Edit's complete JSON
        var sent = ActionList.Get(System.Text.Json.Nodes.JsonNode.Parse(written.Single())!["do"]!.ToJsonString());
        Assert.Equal(new[] { "Message", "Message", "Message" }, sent.Select(x => x.Act));
        Assert.Equal(new[] { "two", null, "three" }, sent.Select(x => (string?)System.Text.Json.Nodes.JsonNode.Parse(x.ArgsJson)!["Text"]));   // the first is gone; the new one went up one (above "three") with its args untouched
        Assert.Equal("{}", sent[1].ArgsJson);

        // RE-3 live: the When tab switches the feature on and draws the strip; leaving it switches it off
        window.Details.RequestTab(RuleTab.When); Step(null, 5);
        Assert.Equal(new[] { true }, liveValues);
        Assert.True(window.Probe.Editor!.LiveValuesWanted);
        Assert.Contains(window.Probe.Editor.LiveStrip!, t => t.StartsWith("yes | ") && t.EndsWith("| 2"));
        frame.Screenshot(ShotPath("rules-9-when-live.png"));
        window.Details.RequestTab(RuleTab.Rules); Step(null, 3);
        Assert.Equal(new[] { true, false }, liveValues);

        // Part 0 a — a SHORT pane: the Apply bar (note + verdict + message lines) still ends inside it, never clipped by a fixed reserve
        winH = 560f; window.Details.RequestTab(RuleTab.Do); Step(null, 6);
        Assert.True(window.Details.BarBottom <= winH, $"the Apply bar ends at {window.Details.BarBottom}, the pane at {winH}");
        Assert.NotNull(window.Probe.Editor!.LiveEdit);
    }

    /// <summary>
    /// ⭐ CE-4222 (R5c, RE-9 + A2; R4 X4) — drag to connect in EDIT mode, in a REAL frame: drag a row's handle onto a trigger's row ⇒ the trigger's form shows the new <c>when</c> as a pending edit on the When tab (nothing written yet); Apply writes it as ONE undo entry (And-wrapped,
    /// by the dropped row's kind); Undo restores. A drop on a row that is not a trigger, or a rule from a macro (A1), does nothing. ✅ Red-proof: A2 inverted (<c>DonePredicateOf</c> testing the role the wrong way) ⇒ the unit and this rail go red; reverted.
    /// </summary>
    [SkippableFact]
    public void TheConnectDrag_InEditMode_AddsTheWhenInTheForm_ApplyWritesIt_UndoRestoresIt()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Entity Rule(int id, string kind, string json)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new NetworkIdentity(id));
            _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 });
            return e;
        }
        const string pushJson = """{"common":{"title":"Push harder"},"cooldown":5}""";
        Rule(10, "HoldArea", """{"common":{"title":"Hold the crossroads"}}""");
        Rule(11, "Trigger", """{"common":{"title":"Briefing"}}""");
        var push = Rule(20, "Trigger", pushJson);
        Rule(30, "WeightedMean", """{"common":{"title":"Mission"}}""");
        var history = new Hrot.Presentation.Edits.EditHistory();
        var svc = new RuleEditorServices(() => Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build(), () => null,
            (e, n, j) => { _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = n, JsonParams = j, InstanceId = 1 }); return true; },
            history, _ => Array.Empty<RuleJournalEntry>());
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_conn", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 0d, editor: svc);
        using var frame = UiFrameHarness.Begin(1400, 900);
        void Step(Action<ImGuiIOPtr>? input = null, int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    input?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1400, 900));
                    ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
                    window.DrawContent();
                    ImGui.End();
                });
        }
        void Drag(Vector2 from, Vector2 to)
        {
            Step(io => Pos(io, from.X, from.Y), 2);
            Step(io => { Pos(io, from.X, from.Y); io.AddMouseButtonEvent(0, true); });
            Step(io => Pos(io, (from.X + to.X) / 2, (from.Y + to.Y) / 2));
            Step(io => Pos(io, to.X, to.Y), 2);
            Step(io => { Pos(io, to.X, to.Y); io.AddMouseButtonEvent(0, false); });
            Step(null, 4);
        }
        string Params() => ((Fdp.ModuleHost.Abstractions.ISimulationView)_world).GetManagedComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>(push)!.JsonParams;
        Vector2 Handle(int net) { var h = window.Panel.LastHandles.First(x => x.TrackId == "r:" + net); return (h.Min + h.Max) / 2f; }
        float RowY(int net) { var r = window.Panel.LastRowBands.First(x => x.TrackId == "r:" + net); return (r.Y0 + r.Y1) / 2f; }

        Step(null, 6);
        Assert.Equal(new[] { "r:10", "r:11", "r:20", "r:30" }, window.Panel.LastHandles.Select(h => h.TrackId).OrderBy(x => x));   // every authored row has a handle
        frame.Screenshot(ShotPath("rules-10-edit-handles.png"));

        Drag(Handle(10), new Vector2(Handle(10).X, RowY(30)));                                                // onto a WeightedMean: not a trigger ⇒ nothing
        Assert.Null(window.SelectedNetworkId); Assert.Equal(pushJson, Params());
        Drag(Handle(10), new Vector2(Handle(10).X, RowY(20)));                                                // the objective onto the trigger
        Assert.Equal(20, window.SelectedNetworkId);
        Assert.Equal("When", window.Probe.Editor!.Tab);
        Assert.True(window.Probe.Editor.Dirty);                                                               // pending: nothing written yet
        Assert.Equal(pushJson, Params());
        frame.Screenshot(ShotPath("rules-11-connected-pending.png"));

        var row = RulesModel.Build(_world, new RulesOptions(), null, 0d).Find(20)!;
        Assert.True(window.Details!.Apply(row));                                                              // RE-7
        var when = System.Text.Json.Nodes.JsonNode.Parse(Params())!["when"]!;
        Assert.Equal("OnEntity", when["$type"]!.ToString());                                                  // no `when` before ⇒ the condition itself
        Assert.Equal(10, (int)when["Ref"]!);
        Assert.Equal("State", when["Predicate"]!["PropertyPath"]!.ToString());                                // an objective ⇒ State == Achieved
        Assert.Equal(1, history.Count);
        Drag(Handle(11), new Vector2(Handle(11).X, RowY(20)));                                                // a trigger onto it too ⇒ the condition is AND-wrapped
        Assert.True(window.Details.Apply(RulesModel.Build(_world, new RulesOptions(), null, 0d).Find(20)!));
        var both = System.Text.Json.Nodes.JsonNode.Parse(Params())!["when"]!;
        Assert.Equal(("Compound", "And"), (both["$type"]!.ToString(), both["Operator"]!.ToString()));
        Assert.Equal(new[] { "State", "TriggerState" }, both["Conditions"]!.AsArray().Select(c => c!["Predicate"]!["PropertyPath"]!.ToString()));   // A2: by kind
        Assert.True(window.Details.Undo()); Assert.True(window.Details.Undo());
        Assert.Equal(pushJson, Params());
    }
}
