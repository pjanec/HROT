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
        Assert.Empty(window.Panel.LastTrackHits);                                    // no spans: nothing to hit, the headers are the rows
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
}
