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
            Title = new FixedString64(title), State = state, Role = role, Owner = ForceId.Friend, ChangedAt = (SimInstant)changedAt, TriggerState = trig,
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
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules", "Scenario", Vector4.One, picked.Add, centred.Add, now: () => 120d) { ListView = false };   // U-18: these rails drive the Timeline
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
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_rec", "Scenario", Vector4.One, _ => { }, _ => { }, blocked: () => "a recording is read-only", now: () => 5d) { ListView = false };   // U-18: these rails drive the Timeline
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
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_edit", "Scenario", Vector4.One, picked.Add, _ => { }) { ListView = false };   // U-18: these rails drive the Timeline
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
    /// ⭐ U-18 / RX3 (docs/UX/DESIGN_Rule_Editor_UX.md §2.1) — in EDIT MODE the Rules window opens on the LIST: one sentence per rule, an incomplete rule says so;
    /// the Timeline is one toggle away and the pick sticks.
    /// 🔴 Red-proof (compiles): <c>ListView</c>'s default <c>false</c> ⇒ the timeline draws ⇒ <c>ListView</c> false ⇒ red.
    /// </summary>
    [SkippableFact]
    public void U18_InEditMode_TheListReadsOneSentencePerRule_AndTheTimelineIsAToggleAway()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        void Rule(int id, string kind, string json)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new NetworkIdentity(id));
            _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 });
        }
        Rule(10, "WeightedMean", """{"common":{"title":"Mission","subject":900}}""");
        Rule(11, "Trigger", """{"common":{"title":"Counter-attack","parent":10,"subject":900}}""");

        var picked = new List<int>();
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_list", "Scenario", Vector4.One, picked.Add, _ => { });
        using var frame = UiFrameHarness.Begin(1280, 300);
        Action draw = () =>   // ONE delegate: a lambda made per Step would itself allocate under the 0 B check
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 300));
            ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
            window.DrawContent();
            ImGui.End();
        };
        void Step(int n = 1) { for (int i = 0; i < n; i++) frame.Step(draw); }
        Step(5);
        Assert.True(window.ListView, "edit mode (every row authored) opens on the List");
        Assert.Equal(new[] { "Weighted mean: Mission", "When (no condition yet) → (nothing to do yet)" }, window.ListRows);
        Assert.True(window.IncompleteOf(11));
        Assert.False(window.IncompleteOf(10));
        Assert.Equal(0, window.Panel.LastTrackHits.Count);   // the timeline did not draw
        // ⭐ CE-1465 — a steady frame (nothing changed) rebuilds nothing: no model, tracks or probe — 0 B over three frames
        Step(2);   // warm: the first steady frames settle caches
        long before = GC.GetAllocatedBytesForCurrentThread();
        Step(3);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        // ... and a change to a rule's params shows after ONE step (the table's version moved with the tick)
        Entity mission = default;
        foreach (var e in _world.Query().WithManaged<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>().Build()) if (_world.GetComponentRO<NetworkIdentity>(e).Value == 10) mission = e;
        _world.Tick();
        _world.SetManagedComponent(mission, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = "WeightedMean", InstanceId = 1, JsonParams = """{"common":{"title":"Mission renamed","subject":900}}""" });
        _world.Tick();
        Step();
        Assert.Contains("Weighted mean: Mission renamed", window.ListRows);
        // ... and a write stamped with the SAME version as the last build (no tick in between — an Apply inside the frame's draw) is still seen once the world ticks
        _world.SetManagedComponent(mission, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = "WeightedMean", InstanceId = 1, JsonParams = """{"common":{"title":"Mission again","subject":900}}""" });
        _world.Tick();
        Step();
        Assert.Contains("Weighted mean: Mission again", window.ListRows);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 }) frame.Screenshot(ShotPath("rules-list.png"));

        window.ListView = false;
        Step(3);
        Assert.False(window.ListView);   // the pick sticks
        Assert.Equal(2, window.Panel.LastTrackHits.Count);   // each row's anchor key — the Timeline drew

        // ⭐ U-18 / RX2 — attached to the Details panel: opening a rule picks the RULE (Details shows what is selected), not its subject; the pane is not drawn
        Assert.Empty(picked);
        window.AttachToDetailsPanel();
        window.SelectNetworkId(11);
        Step(2);
        Assert.Equal(new[] { 11 }, picked);
        Assert.True(window.DetailsInPanel);
    }

    /// <summary>
    /// ⭐ CE-4308 (RX6) fidelity (docs/UX/rule-list-visual.svg, "New rule — choose a starting point") — <b>+ Add</b> opens the GALLERY in a real frame: the mockup's groups and
    /// starting points, a search, the engine's kinds under <i>More…</i>; a unit's menu opens it with the unit filled in. Renders <c>rules-gallery.png</c> under <c>HROT_RAIL_SHOTS</c>.
    /// </summary>
    [SkippableFact]
    public void CE4308_TheGalleryFrame_ShowsTheMockupsStartingPoints()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(30));
        _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = "WeightedMean", JsonParams = """{"common":{"title":"Mission"}}""", InstanceId = 1 });
        var svc = new RuleEditorServices(() => Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build(), () => null, (x, n, j) => true, new Hrot.Presentation.Edits.EditHistory(), _ => Array.Empty<RuleJournalEntry>(),
            CreateRule: (tkb, kind, json) => true);
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_gallery", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 0d, editor: svc);
        using var frame = UiFrameHarness.Begin(900, 640);
        Action draw = () =>
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(900, 640));
            ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
            window.DrawContent();
            ImGui.End();
        };
        for (int i = 0; i < 3; i++) frame.Step(draw);
        window.OpenGallery(subjectNetworkId: 30);
        for (int i = 0; i < 4; i++) frame.Step(draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 }) frame.Screenshot(ShotPath("rules-gallery.png"));
        Assert.Equal(RuleGallery.All.Select(t => t.Label), window.GalleryRows);                       // every starting point drawn, in the mockup's order
        Assert.Equal(30, window.GalleryContext.Subject.Ref.NetworkId);                                  // the unit is filled in while it is open
    }

    /// <summary>
    /// ⭐ U-18 fidelity (docs/UX/rule-list-visual.svg, R-4138 / R-4146) — the List view reads like its mockup: a coloured KIND glyph first (trigger bolt amber · objective bullseye green · measure flag amber), the group as the shared header bar,
    /// <i>required</i> muted on the right, the incomplete ✖ on the RIGHT of the text, the Font Awesome arrow (the font has no U+2192). 🔴 Red-proof (compiles): <c>KindGlyph</c> giving a trigger the objective's glyph ⇒ red.
    /// </summary>
    [SkippableFact]
    public void U18_TheListFrame_MatchesTheMockup_KindGlyphs_RequiredMark_IncompleteOnTheRight()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        void Rule(int id, string kind, string json)
        {
            var e = _world.CreateEntity();
            _world.AddComponent(e, new NetworkIdentity(id));
            _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = kind, JsonParams = json, InstanceId = 1 });
        }
        Rule(20, "Trigger", """{"common":{"title":"Push","subject":900},"holdFor":30,"when":{"$type":"ScenarioTime","FromSeconds":60},"do":[{"act":"Message","args":{"Text":"Phase 2"}}]}""");
        Rule(21, "Protect", """{"common":{"title":"OBJ BLUE","subject":900,"required":true}}""");
        Rule(22, "Trigger", """{"common":{"title":"Bravo","subject":900},"do":[{"act":"Message"}]}""");
        Rule(23, "HitRatio", """{"common":{"title":"Blue hit ratio","subject":900}}""");
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_fidelity", "Scenario", Vector4.One, _ => { }, _ => { });
        using var frame = UiFrameHarness.Begin(1280, 300);
        Action draw = () =>
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1280, 300));
            ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
            window.DrawContent();
            ImGui.End();
        };
        for (int i = 0; i < 6; i++) frame.Step(draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 }) frame.Screenshot(ShotPath("rules-list-fidelity.png"));
        var marks = new Dictionary<int, RulesWindow.ListMark>();
        foreach (var m in window.ListMarks) marks[m.NetworkId] = m;
        Assert.Equal(4, marks.Count);
        Assert.Equal("\uf0e7", marks[20].Glyph); Assert.Equal(NodeEditor.Core.Widgets.IconPalette.Amber, marks[20].Colour);   // trigger: bolt, amber
        Assert.Equal("\uf140", marks[21].Glyph); Assert.Equal(NodeEditor.Core.Widgets.IconPalette.Green, marks[21].Colour);   // objective: bullseye, green
        Assert.Equal("\uf11e", marks[23].Glyph); Assert.Equal(NodeEditor.Core.Widgets.IconPalette.Amber, marks[23].Colour);   // measure: flag, amber
        Assert.True(marks[21].Required); Assert.False(marks[20].Required);                                                      // *required* on the objective only
        Assert.True(marks[22].Incomplete); Assert.False(marks[20].Incomplete);
        Assert.True(marks[22].MarkX > marks[22].TextX + 100f, "the incomplete mark sits on the RIGHT of the text");
        Assert.Equal(0f, marks[20].MarkX);                                                                                      // a complete rule draws no mark
        Assert.Contains("for 30 s → Message everyone: \"Phase 2\"", window.ListRows[0]);   // ⭐ CE-4307 — the step as a sentence                                                              // the text keeps the arrow; the frame draws the Font Awesome one
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
        // ⭐ U-8 (§8.2 ④) — the objective is a named rule entity: the When tab's link to it is a chip "Objective: Hold the bridge", through the editor's ONE scope
        _world.RegisterComponent<EntityInfo>(); _world.RegisterComponent<TkbIdentity>();
        foreach (var e in _world.Query().With<NetworkIdentity>().Build())
            if (_world.GetComponent<NetworkIdentity>(e).Value == 10)
            {
                _world.AddComponent(e, new EntityInfo { Name = new FixedString64("Hold the bridge") });
                _world.AddComponent(e, new TkbIdentity { TkbType = (TkbTypeId)AuthoredRules.TkbObjective });
            }
        var picks = new Hrot.Presentation.Facades.MapPickBroker(() => null) { References = AuthoredRules.ScopeOf(_world), Preview = new Fdp.Presentation.Editing.MapPreview() };   // ⭐ U-10 — the Thing row draws its ⌖
        Assert.Equal("Objective: Hold the bridge", AuthoredRules.ScopeOf(_world).ChipLabelOf(new EntityRef(10)));
        Assert.Equal("Objective: Hold the bridge", Fdp.Presentation.Editing.ReferenceLeafDrawers.ChipLabel(new EntityLink(new EntityRef(10), default), picks.References));
        var history = new Hrot.Presentation.Edits.EditHistory();
        var svc = new RuleEditorServices(() => Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build(), () => null,
            (e, n, j) => true, history, _ => Array.Empty<RuleJournalEntry>(), Picks: () => picks);
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_ed", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 5d, editor: svc) { ListView = false };   // U-18: these rails drive the Timeline
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
        Assert.Equal(new[] { "Overview", "When", "Do", "Settings", "Bindings", "History" }, ed.Tabs);   // ⭐ U-9 (§8.2 ⑧)
        Assert.True(ed.Editable);
        window.Details!.RequestTab(RuleTab.When); Step(null, 4);
        Assert.Equal("When", window.Probe.Editor!.Tab);
        Assert.Equal(new[] { "When", "HoldFor", "Delay", "Cooldown", "Repeat", "MaxFires", "FireIfTrueWhenArmed", "WhileTrue" }, window.Probe.Editor.Fields.ToArray());   // ⭐ U-9 — the condition and its timing, no header
        // ⭐ U-15 / C3 — the When tab reads as SENTENCES (the friendly view): the objective by its kind and name, no component path; nothing blank ⇒ Apply may run
        Assert.Equal(new[] { "Objective: Hold the bridge is achieved" }, window.Details!.ConditionRows);
        Assert.Null(window.Details.ConditionProblem);
        // ⭐ C4 — the timing is the END OF THE SENTENCE (cooldown 10 on a once-trigger: "once")
        Assert.Equal("...and it stays true for 0 s, then wait 0 s. Fire once", window.Details.TimingSentence);
        frame.Screenshot(ShotPath("rules-4-when.png"));
        {   // ⭐ CE-4286 / U-2 — the rule editor's tabs are a real tab bar (the neutral palette: the selected tab #3A3A48 with the blue overline), not a strip of buttons
            var shot = Raylib.LoadImageFromScreen();
            try
            {
                int selected = 0, overline = 0;
                for (int x = 0; x < shot.Width; x += 2)
                    for (int y = 0; y < shot.Height; y += 2)
                    {
                        var p = Raylib.GetImageColor(shot, x, y);
                        if (Math.Abs(p.R - 0x3A) <= 3 && Math.Abs(p.G - 0x3A) <= 3 && Math.Abs(p.B - 0x48) <= 3) selected++;
                        else if (Math.Abs(p.R - 0x4D) <= 6 && Math.Abs(p.G - 0x99) <= 6 && p.B > 0xF0) overline++;
                    }
                Assert.True(selected > 40, $"the rule editor draws a tab bar: the selected tab's neutral fill is on screen ({selected} samples)");
                Assert.True(overline > 5, $"...with the blue top line ({overline} samples)");
            }
            finally { Raylib.UnloadImage(shot); }
        }
        // ⭐ U-9 (§8.2 ⑧) — the When tab holds the condition and its timing, no header; the header is on Settings alone
        window.Details.RequestTab(RuleTab.When); Step(null, 4);
        Assert.Equal(new[] { "When", "HoldFor", "Delay", "Cooldown", "Repeat", "MaxFires", "FireIfTrueWhenArmed", "WhileTrue" }.OrderBy(x => x), window.Probe.Editor!.Fields.OrderBy(x => x).ToArray());
        frame.Screenshot(ShotPath("rules-5-when.png"));
        window.Details.RequestTab(RuleTab.Settings); Step(null, 3);
        Assert.Equal(new[] { "Common" }, window.Probe.Editor!.Fields.ToArray());
        window.Details.RequestTab(RuleTab.Do); Step(null, 3);
        Assert.Equal(new[] { "Do" }, window.Probe.Editor!.Fields.ToArray());   // the tab OWNS `do`; its list editor is R5b, so the tab draws a placeholder

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
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_do", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 5d, editor: svc) { ListView = false };   // U-18: these rails drive the Timeline
        window.Details!.LiveWriter = (EntityRepository w, Entity e, string j, int o, ref int last, out int seq, out Guid rid) => { seq = ++last; rid = Guid.NewGuid(); written.Add(j); return Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct; };
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
        (Vector2 Min, Vector2 Max) Slot(int slot) { NodeEditor.Core.Widgets.KitProbe.TryGet(slot, out var a, out var b); return (a, b); }
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
        Assert.Equal(new[] { "1. Message everyone: \"one\"", "> 2. Message everyone: \"two\"", "3. Message everyone: \"three\"" }, ed.Steps);                       // the running step is marked
        Assert.Equal("step 2 / 3", ed.StepMarker);
        Assert.True(window.Details.Steps.Probe.AddEnabled);
        frame.Screenshot(ShotPath("rules-7-do.png"));

        Click(window.Details.Steps.AddRect);                                                                // + Add step ⇒ the generic picker answers Message
        Assert.Equal("rules.add-step", asked!.ContextKey);
        Assert.Equal(4, window.Probe.Editor!.Steps!.Count);
        Assert.True(window.Probe.Editor.Dirty);
        Click(window.Details.Steps.MenuRects[3]);                                                           // ⭐ CE-3395 — the new step's ⋮ menu: Move up
        Click(Slot(NodeEditor.Core.Widgets.KitProbe.MenuRow + 3));
        Click(window.Details.Steps.MenuRects[0]);                                                           // the first one's ⋮ menu: Delete
        Click(Slot(NodeEditor.Core.Widgets.KitProbe.MenuRow + 8));
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
        Assert.Equal(new string?[] { "yes 2" }, window.Details.ConditionLive);   // ⭐ C4 — the row's own live column (✓ + the number), not a strip under the form
        frame.Screenshot(ShotPath("rules-9-when-live.png"));
        window.Details.RequestTab(RuleTab.Settings); Step(null, 3);
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
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_conn", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 0d, editor: svc) { ListView = false };   // U-18: these rails drive the Timeline
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

    /// <summary>
    /// ⭐ U-15 / C3 (DESIGN_Condition_Editor §4) — an unfilled blank in the friendly view: the row says what is missing (<i>Choose an area …</i>) and Apply stays
    /// disabled until it is filled; the Expert view (the struct editor, as before) is one tick away and lifts the friendly view's hold.
    /// 🔴 Red-proof (compiles): <c>ConditionEditor.ProblemOf</c> returning null ⇒ red.
    /// </summary>
    [SkippableFact]
    public void U15_TheWhenTab_ReadsAsSentences_AnUnfilledBlankHoldsApply()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        _world.RegisterManagedComponent<Fdp.Toolkit.Behavior.Components.BehaviorStartRecord>();
        var e = _world.CreateEntity();
        _world.AddComponent(e, new NetworkIdentity(30));
        _world.SetManagedComponent(e, new Fdp.Toolkit.Behavior.Components.BehaviorStartRecord { BehaviorName = "Trigger", InstanceId = 1,
            JsonParams = """{"common":{"title":"Reach"},"when":{"$type":"OnEntity","ref":{"NetworkId":12},"predicate":{"$type":"SpatialBounding"}}}""" });
        var svc = new RuleEditorServices(() => Fdp.Presentation.Editing.ComponentEditServices.CreateBuilder().Build(), () => null,
            (x, n, j) => true, new Hrot.Presentation.Edits.EditHistory(), _ => Array.Empty<RuleJournalEntry>());
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_u15", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 5d, editor: svc) { ListView = false };   // U-18: these rails drive the Timeline
        using var frame = UiFrameHarness.Begin(1400, 700);
        void Step(int n = 1)
        {
            for (int i = 0; i < n; i++)
                frame.Step(() =>
                {
                    ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1400, 700));
                    ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
                    window.DrawContent();
                    ImGui.End();
                });
        }
        window.SelectNetworkId(30);
        Step(4);
        window.Details!.RequestTab(RuleTab.When); Step(4);
        Assert.Single(window.Details.ConditionRows);
        Assert.Contains("is inside the area", window.Details.ConditionRows[0]);
        Assert.StartsWith("Choose an area", window.Details.ConditionProblem);
        frame.Screenshot(ShotPath("rules-u15-blank.png"));
    }

    /// <summary>
    /// ⭐ W-1 (P-8, mission-plan-detail-visual.svg B) — the Phases group in a run: the violet flag, the number and title, how it begins, <i>begun 0:00</i>
    /// for a phase that fired, <i>▶ Begin now</i> + <i>waiting</i> for one that has not; first, before the other rules. Renders <c>rules-phases.png</c>.
    /// </summary>
    [SkippableFact]
    public void W1_ThePhasesGroup_OnARealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        Add(30, "Approach", 0, 0, ObjectiveState.Active, ObjectiveRole.Trigger, trig: TriggerState.Done, fires: 1, lastFired: 0);
        Add(31, "Assault", 0, 0, ObjectiveState.Active, ObjectiveRole.Trigger, trig: TriggerState.Armed);
        Add(32, "Hold the bridge", 0, 0, ObjectiveState.Active);
        foreach (var (id, phase) in new[] { (30, (byte)1), (31, (byte)2) })
            foreach (var e in _world.Query().With<NetworkIdentity>().With<ObjectiveStatus>().Build())
                if (_world.GetComponentRO<NetworkIdentity>(e).Value == id) { var st = _world.GetComponentRO<ObjectiveStatus>(e); st.Phase = phase; _world.SetComponent(e, st); }
        var window = new RulesWindow(() => _world, () => null, null, "rail_rules_phases", "Scenario", Vector4.One, _ => { }, _ => { }, now: () => 300d) { ListView = true };
        using var frame = UiFrameHarness.Begin(1100, 220);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        Action draw = () =>
        {
            ImGui.SetNextWindowPos(new Vector2(0, 0)); ImGui.SetNextWindowSize(new Vector2(1100, 220));
            ImGui.Begin("Rules", ImGuiWindowFlags.NoCollapse);
            window.DrawContent();
            ImGui.End();
        };
        for (int i = 0; i < 6; i++) frame.Step(draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 }) frame.Screenshot(ShotPath("rules-phases.png"));
        var marks = window.ListMarks;
        Assert.Equal(new[] { 30, 31 }, marks.Take(2).Select(m => m.NetworkId));                       // the phases first, by number
        var flag = NodeEditor.Core.Widgets.Icons.Get("rule/phase");
        Assert.All(marks.Take(2), m => { Assert.Equal(flag.Glyph, m.Glyph); Assert.Equal(NodeEditor.Core.Widgets.IconPalette.Violet, m.Colour); });
        Assert.Equal(32, marks[2].NetworkId);
    }
}
