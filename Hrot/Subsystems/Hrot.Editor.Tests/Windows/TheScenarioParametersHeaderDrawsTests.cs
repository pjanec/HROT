using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.Scenario;
using Hrot.Editor.UiFrameRail;
using Xunit;
using ImGui = ImGuiNET.ImGui;

namespace Hrot.Editor.Tests.Windows;

/// <summary>
/// ⭐ CE-4253 (user, 2026-11-03: <i>"a collapsible header, by default collapsed, called parameters … the same editor that now opens in a separate panel"</i>) — the Details ▸ Scenario view's
/// <b>Parameters</b> header, in a REAL ImGui frame (under <c>xvfb-run -a</c>; a skip is not a pass): CLOSED by default (the drawer's body is not drawn), opened when something asks
/// (<see cref="ParametersDrawer.RequestOpen"/> — the bind popup's <i>New parameter…</i>), and what it edits is the SAME <see cref="HeaderParamsSchemaSource"/> with its ONE undo history.
/// 🔴 Red-proof (compiles): the header made <c>DefaultOpen</c> ⇒ the first rail goes red.
/// </summary>
public sealed class TheScenarioParametersHeaderDrawsTests
{
    private static (EntityRepository Repo, HeaderParamsSchemaSource Src, ParametersDrawer Drawer, ScenarioEnvironmentView View) Fixture()
    {
        var repo = new EntityRepository();
        repo.RegisterManagedComponent<ParamBindings>();
        ScenarioDescription.Stamp(repo, "desc", "scenario", "h", null, "");
        var src = new HeaderParamsSchemaSource(() => repo, new Hrot.Presentation.Edits.EditHistory());
        var drawer = new ParametersDrawer(src);
        return (repo, src, drawer, new ScenarioEnvironmentView(() => repo, () => null, (_, _) => { }, drawer));
    }

    private static void Frames(UiFrameSession frame, ScenarioEnvironmentView view, int n)
    {
        var ctx = DetailsContext.Empty("Scenario");
        frame.StepN(n, () =>
        {
            ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(520, 360), ImGuiNET.ImGuiCond.Always);
            ImGui.Begin("Scenario (CE-4253)");
            view.Draw(ctx, "t");
            ImGui.End();
        });
    }

    [SkippableFact]
    public void TheParametersHeader_IsClosedByDefault_OpensWhenAsked_AndEditsTheSameSourceWithUndo()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, src, drawer, view) = Fixture();
        using var _r = repo;
        using var frame = UiFrameHarness.Begin();

        Frames(frame, view, 3);
        Assert.False(view.ParametersHeaderOpen, "collapsed by default");
        Assert.Equal(0, drawer.DrawCount);                       // the editor body is not even drawn

        drawer.RequestOpen();                                     // what the bind popup's New parameter… does
        Frames(frame, view, 2);
        Assert.True(view.ParametersHeaderOpen, "opened on request");
        Assert.True(drawer.DrawCount > 0, "the parameter editor is the header's content");

        // an edit goes through the ONE source, with the ONE undo history
        src.AddVariable(new BlackboardVariableEntry("side", typeof(ForceId), null));
        Assert.Equal("side", Assert.Single(src.Variables).Name);
        Assert.True(src.History.CanUndo);
        src.History.Undo();
        Assert.Empty(src.Variables);
    }
}
