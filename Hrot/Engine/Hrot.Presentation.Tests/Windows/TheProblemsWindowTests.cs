using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Windows;
using ImGuiNET;
using Xunit;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐ CE-4318 (L-6, docs/UX/DESIGN_Scenario_Lifecycle_Review.md LC-13 / §2.7) — the ONE Problems window over backend's <c>ScenarioProblems</c>: a row per
/// problem in its words, a click selects the culprit, the checks run on a timer (not per frame) and the rows are kept while nothing changed.
/// </summary>
public sealed class TheProblemsWindowTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly List<int> _selected = new();

    public TheProblemsWindowTests()
    {
        _w.RegisterComponent<EntityInfo>();
        _w.RegisterComponent<NetworkIdentity>();
        _w.RegisterComponent<ObjectiveLinks>();
        _w.RegisterComponent<ObjectiveStatus>();
        _w.RegisterComponent<Fdp.Core.CommandHierarchy.UnitRoster>();
    }

    public void Dispose() => _w.Dispose();

    private Entity RuleOnAGoneSubject(string title, int net)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new ObjectiveStatus { Role = ObjectiveRole.Objective, Title = new FixedString64(title), VisibleSides = 0xFF });
        _w.AddComponent(e, new ObjectiveLinks { Subject = new Fdp.Toolkit.Replication.EntityRef(999) });
        _w.AddComponent(e, new NetworkIdentity(net));
        return e;
    }

    private ProblemsWindow Window() => new(() => _w, n => _selected.Add(n), "t_problems", "Scenario", Vector4.One);

    /// <summary>✅ Red-proof: <c>Select</c> passing the problem's <c>Other</c> (999) ⇒ the selected id differs ⇒ red.</summary>
    [Fact]
    public void CE4318_ARowSaysTheProblem_AndAClickSelectsTheCulprit()
    {
        RuleOnAGoneSubject("Hold the bridge", 42);
        var window = Window();
        window.Refresh(force: true);
        Assert.Equal(1, window.Count);
        Assert.Contains("is gone", window.Rows[0]);
        Assert.Contains("Hold the bridge", window.Rows[0]);
        Assert.True(window.Select(0));
        Assert.Equal(new[] { 42 }, _selected);
        Assert.False(window.Select(5));
    }

    /// <summary>✅ Red-proof: <c>Refresh</c> ignoring <c>RefreshSeconds</c> ⇒ the second problem shows at once ⇒ red.</summary>
    [Fact]
    public void CE4318_TheChecksRunOnATimer_AndTheRowsAreKeptWhileNothingChanged()
    {
        RuleOnAGoneSubject("Hold the bridge", 42);
        var window = Window();
        window.Refresh(force: true);
        var row = window.Rows[0];
        RuleOnAGoneSubject("Reach the ford", 43);
        window.Refresh();                                       // within RefreshSeconds: no new scan
        Assert.Equal(1, window.Count);
        window.Refresh(force: true);
        Assert.Equal(2, window.Count);
        Assert.NotSame(row, window.Rows[0]);                    // the list changed ⇒ the rows were rebuilt
        row = window.Rows[0];
        window.Refresh(force: true);
        Assert.Same(row, window.Rows[0]);                       // unchanged ⇒ kept (no new strings on a steady scan)
    }

    /// <summary>On a real frame: the toolbar count and the window (rendered under <c>HROT_RAIL_SHOTS</c> as <c>problems.png</c>).</summary>
    [SkippableFact]
    public void CE4318_TheCountAndTheWindow_OnARealFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        RuleOnAGoneSubject("Hold the bridge", 42);
        RuleOnAGoneSubject("Reach the ford", 43);
        var window = Window();
        window.IsOpen = true;
        using var frame = UiFrameHarness.Begin(640, 260);
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(640, 50));
            ImGui.Begin("##toolbar", ImGuiWindowFlags.NoDecoration);
            window.DrawToolbarCount();
            ImGui.End();
            ImGui.SetNextWindowPos(new Vector2(0, 60)); ImGui.SetNextWindowSize(new Vector2(640, 190));
            ImGui.Begin("Problems##rail");
            window.DrawInto();
            ImGui.End();
        }
        for (int i = 0; i < 4; i++) frame.Step(Draw);
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "problems.png"));
        Assert.Equal(2, window.Count);
    }
}
