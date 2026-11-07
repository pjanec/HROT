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
[Collection(PanelSnapshotTestCollection.Name)]   // one ImGui context: the frame rails of this assembly run serially
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

    private static void Pos(ImGuiNET.ImGuiIOPtr io, float x, float y) { io.AddMousePosEvent(x, y); Raylib_cs.Raylib.SetMousePosition((int)Math.Round(x), (int)Math.Round(y)); }

    /// <summary>⭐ CE-4254 step 1: the REAL rename gesture (double-click the name, type, Enter) keeps the new name.</summary>
    [SkippableFact]
    public void ARenameTypedInTheNameCell_AndEnter_Sticks() => Gesture("faction", expectRenamed: true);

    /// <summary>CE-4254 measured cause: a name the validator refuses used to vanish without a word; now the reason is drawn.</summary>
    [SkippableFact]
    public void ARefusedRename_SaysWhy_InsteadOfSilentlyReverting() => Gesture("my side", expectRenamed: false);

    private static void Gesture(string typed, bool expectRenamed)
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, src, drawer, view) = Fixture();
        using var _r = repo;
        using var frame = UiFrameHarness.Begin();
        src.AddVariable(new BlackboardVariableEntry("side", typeof(ForceId), null));
        drawer.RequestOpen();
        var ctx = DetailsContext.Empty("Scenario");
        void Step(Action<ImGuiNET.ImGuiIOPtr>? inject, int times = 1)
        {
            for (int i = 0; i < times; i++)
                frame.Step(() =>
                {
                    inject?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
                    ImGui.SetNextWindowSize(new Vector2(520, 360), ImGuiNET.ImGuiCond.Always);
                    ImGui.Begin("Scenario (CE-4253)");
                    view.Draw(ctx, "t");
                    ImGui.End();
                });
        }
        Step(null, 4);
        var cell = drawer.NameCells.Single(c => c.Name == "side");
        var at = (cell.Min + cell.Max) / 2f;
        Step(io => Pos(io, at.X, at.Y), 2);
        for (int k = 0; k < 2; k++)                                   // the double click
        {
            Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); });
            Step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); });
        }
        Step(null, 2);
        // select-all then type the new name
        Step(io => { io.AddKeyEvent(ImGuiNET.ImGuiKey.ModCtrl, true); io.AddKeyEvent(ImGuiNET.ImGuiKey.A, true); });
        Step(io => { io.AddKeyEvent(ImGuiNET.ImGuiKey.A, false); io.AddKeyEvent(ImGuiNET.ImGuiKey.ModCtrl, false); });
        Step(io => { foreach (var ch in typed) io.AddInputCharacter(ch); });
        Step(null, 2);
        Step(io => io.AddKeyEvent(ImGuiNET.ImGuiKey.Enter, true));
        Step(io => io.AddKeyEvent(ImGuiNET.ImGuiKey.Enter, false));
        Step(null, 4);
        var names = string.Join(",", src.Variables.Select(v => v.Name));
        if (expectRenamed) Assert.True(src.Variables.Any(v => v.Name == typed), $"names now: [{names}] refusal: '{src.LastRefusal}'");
        else
        {
            Assert.Equal("side", Assert.Single(src.Variables).Name);
            Assert.False(string.IsNullOrEmpty(drawer.RenameRefusal), "a refused rename must say why");
        }
    }

    private static Action<Action<ImGuiNET.ImGuiIOPtr>?, int> Stepper(UiFrameSession frame, ScenarioEnvironmentView view)
    {
        var ctx = DetailsContext.Empty("Scenario");
        return (inject, times) =>
        {
            for (int i = 0; i < times; i++)
                frame.Step(() =>
                {
                    inject?.Invoke(ImGui.GetIO());
                    ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiNET.ImGuiCond.Always);
                    ImGui.SetNextWindowSize(new Vector2(520, 360), ImGuiNET.ImGuiCond.Always);
                    ImGui.Begin("Scenario (CE-4253)");
                    view.Draw(ctx, "t");
                    ImGui.End();
                });
        };
    }

    private static string? Default(HeaderParamsSchemaSource src) => src.Variables.Single().DefaultValueJson;

    /// <summary>⭐ CE-4254 — the parameters host: no byte size, no memory line, no Remove unused (though the param IS unused); an editable Default and a Used by count (a bound param shows 1).</summary>
    [SkippableFact]
    public void TheParametersHost_DrawsADefaultAndUsedBy_AndNoBlackboardAffordances()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, src, drawer, view) = Fixture();
        using var _r = repo;
        using var frame = UiFrameHarness.Begin();
        var e = repo.CreateEntity();
        new Fdp.Presentation.Adapters.RepositoryAdapter(repo).SetComponent(e, typeof(ParamBindings),
            new ParamBindings { Bindings = new System.Collections.Generic.Dictionary<string, string> { ["EntityInfo.ForceId"] = "side" } });
        src.AddVariable(new BlackboardVariableEntry("side", typeof(ForceId), null));
        src.AddVariable(new BlackboardVariableEntry("idle", typeof(ForceId), null));
        drawer.RequestOpen();
        Stepper(frame, view)(null, 4);
        // N-6: the Define table draws exactly the columns the parameters settings name — no byte size, no memory line, no Remove unused exist in it at all
        Assert.Equal(new[] { "Name", "Type", "Default", "Used by" }, drawer.Table.ColumnHeaders);
        Assert.Equal(1, drawer.Table.UsedByDrawn["side"]);
        Assert.Equal(0, drawer.Table.UsedByDrawn["idle"]);
    }

    /// <summary>⭐ CE-4254 → N-6 — the default is edited IN PLACE by the type's own cell (typed into an int parameter's box), lands on the source as the member JSON and is undoable.</summary>
    [SkippableFact]
    public void TheDefaultCell_EditsTheSourcesDefault_AndItIsUndoable()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, src, drawer, view) = Fixture();
        using var _r = repo;
        using var frame = UiFrameHarness.Begin();
        src.AddVariable(new BlackboardVariableEntry("count", typeof(int), null));
        drawer.RequestOpen();
        var step = Stepper(frame, view);
        step(null, 4);
        Assert.Null(Default(src));
        var shot = System.Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS");
        if (!string.IsNullOrEmpty(shot)) frame.Screenshot(System.IO.Path.Combine(shot, "params-default-cell.png"));
        var nm = drawer.NameCells.Single(); var tc = drawer.Table.TypeCell("count");
        var at = new Vector2(tc.Max.X + 60f, (nm.Min.Y + nm.Max.Y) / 2f);           // inside the Default column of that row
        step(io => Pos(io, at.X, at.Y), 2);
        step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, true); }, 1);
        step(io => { Pos(io, at.X, at.Y); io.AddMouseButtonEvent(0, false); }, 1);
        step(null, 2);
        step(io => { io.AddKeyEvent(ImGuiNET.ImGuiKey.ModCtrl, true); io.AddKeyEvent(ImGuiNET.ImGuiKey.A, true); }, 1);
        step(io => { io.AddKeyEvent(ImGuiNET.ImGuiKey.A, false); io.AddKeyEvent(ImGuiNET.ImGuiKey.ModCtrl, false); }, 1);
        step(io => io.AddInputCharacter('7'), 1);
        step(null, 3);
        Assert.Equal("7", Default(src));
        step(io => io.AddKeyEvent(ImGuiNET.ImGuiKey.Enter, true), 1);                // Enter leaves the box (an undo from the shell's menu comes with the focus elsewhere; an ACTIVE ImGui text box keeps its own copy)
        step(io => io.AddKeyEvent(ImGuiNET.ImGuiKey.Enter, false), 1);
        step(null, 3);
        Assert.True(src.History.CanUndo);
        src.History.Undo();
        step(null, 3);
        Assert.Null(Default(src));
        src.History.Redo();
        step(null, 3);
        Assert.Equal("7", Default(src));
    }

    /// <summary>⭐ CE-4254 → ⭐ B-14 (<c>a916d6e2f</c>) — the drawer's own Ctrl+Z / Ctrl+Y chord was REMOVED on purpose: Edit ▸ Undo / Redo (the shell's commands, Ctrl+Z /
    /// Ctrl+Y / Ctrl+Shift+Z) undo the editor's ONE <see cref="Hrot.Presentation.Edits.EditHistory"/>, and a second chord on the panel would undo twice. What stays true here: an edit
    /// made through the drawer is ONE step of that history, so the shell's Undo / Redo take it back and forth. (Was: chords on the focused panel — superseded by B-14.)</summary>
    [SkippableFact]
    public void AnEditMadeInThePanel_IsOneStepOfTheOneHistory_TheShellsUndoAndRedoTakeIt()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var (repo, src, drawer, view) = Fixture();
        using var _r = repo;
        using var frame = UiFrameHarness.Begin();
        src.AddVariable(new BlackboardVariableEntry("side", typeof(ForceId), null));
        drawer.RequestOpen();
        var step = Stepper(frame, view);
        step(null, 4);
        Assert.Single(drawer.NameCells);                              // the row is drawn
        Assert.True(src.History.Undo());
        step(null, 2);
        Assert.Empty(src.Variables);                                  // the add was undone
        Assert.True(src.History.Redo());
        step(null, 2);
        Assert.Single(src.Variables);                                 // and redone
        Assert.Single(drawer.NameCells);
    }
}
