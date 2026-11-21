using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Documents;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Edits;
using Hrot.Presentation.Windows;
using ImGuiNET;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Documents;

/// <summary>
/// ⭐ U5 (docs/UX/DESIGN_Edit_Mode_Undo.md §2.9 / §3, Z-2) — the Edit menu: <i>Undo &lt;step&gt;</i>, <i>Redo &lt;step&gt;</i> and, right under Redo, <i>History…</i> that opens the list of steps.
/// All three are greyed outside Edit mode with the reason (<c>only in Edit mode</c>), and the take editor's step (U4) is named like any other.
/// </summary>
[Collection(Hrot.Editor.AiShared.Tests.PanelSnapshotTestCollection.Name)]
public sealed class TheEditMenuHasHistoryTests
{
    private sealed class Rig
    {
        public readonly EditHistory History = new();
        public readonly ShellEditorCommands Commands = new();
        public readonly GlobalMenuRegistry Menu = new();
        public bool Edit = true;
        public int Opened;
        public int Value;

        public Rig()
        {
            EditUndoCommands.Register(Commands.Register, History, () => Edit, showHistory: () => Opened++);
            MenuCommandAdapter.Register(Menu, Commands, EditUndoCommands.UndoId, "Edit/Undo");
            MenuCommandAdapter.Register(Menu, Commands, EditUndoCommands.RedoId, "Edit/Redo");
            MenuCommandAdapter.Register(Menu, Commands, EditUndoCommands.HistoryId, "Edit/History…");
        }

        public void Step(string label, int to) { int from = Value; Value = to; History.Push(label, () => Value = to, () => Value = from); }
        public MenuItemNode Item(string name) => Menu.Root.Children["Edit"].Children[name];
    }

    /// <summary>🔴 Mutation (compiles): <c>HistoryLabel</c> without the reason ⇒ the greyed-label assertion fails; the History descriptor's <c>IsEnabled</c> always true ⇒ the greyed assertion fails. (The item's place under Redo is the editor's registration order, <c>EditorSubsystem</c> — this rig mirrors it.)</summary>
    [Fact]
    public void U5_TheEditMenu_NamesTheSteps_HistoryIsRightUnderRedo_AllGreyedOutsideEditMode()
    {
        var r = new Rig();
        Assert.Equal(new[] { "Undo", "Redo", "History…" }, r.Menu.Root.Children["Edit"].OrderedChildren().Select(c => c.Name));

        Assert.Equal("Undo (nothing to undo)", r.Item("Undo").ResolveLabel());
        Assert.Equal("History…", r.Item("History…").ResolveLabel());
        r.Step("Move Alpha 1-1", 1); r.Step("Move camera key", 2);   // a world step, then a take step (U4): both are just steps
        Assert.Equal("Undo Move camera key", r.Item("Undo").ResolveLabel());
        Assert.Equal("Ctrl+Z", r.Item("Undo").Shortcut);
        r.History.Undo();
        Assert.Equal("Redo Move camera key", r.Item("Redo").ResolveLabel());
        Assert.Equal("Ctrl+Y", r.Item("Redo").Shortcut);

        r.Edit = false;
        Assert.Equal("Undo (only in Edit mode)", r.Item("Undo").ResolveLabel());
        Assert.Equal("Redo (only in Edit mode)", r.Item("Redo").ResolveLabel());
        Assert.Equal("History… (only in Edit mode)", r.Item("History…").ResolveLabel());
        foreach (var n in new[] { "Undo", "Redo", "History…" }) Assert.False(r.Item(n).GetEnabled!());
    }

    /// <summary>🔴 Mutation (compiles): the History command's action without the <c>inEdit()</c> guard ⇒ it opens outside Edit mode ⇒ red.</summary>
    [Fact]
    public void U5_HistoryOpensTheList_InEditModeOnly()
    {
        var r = new Rig();
        Assert.True(r.Commands.Get(EditUndoCommands.HistoryId)!.IsEnabled());
        r.Commands.Invoke(EditUndoCommands.HistoryId);
        Assert.Equal(1, r.Opened);
        r.Edit = false;
        r.Commands.Invoke(EditUndoCommands.HistoryId);
        Assert.Equal(1, r.Opened);
    }

    /// <summary>A host that gives no list (the History window is the editor's) registers no History command.</summary>
    [Fact]
    public void U5_WithoutAWindow_NoHistoryCommand()
    {
        var commands = new ShellEditorCommands();
        EditUndoCommands.Register(commands.Register, new EditHistory(), () => true);
        Assert.Null(commands.Get(EditUndoCommands.HistoryId));
    }

    /// <summary>
    /// Fidelity (docs/UX/menus-visual.svg panel C + E): the REAL Edit rows (ImGui menu items with the descriptors' own labels and shortcuts) and the REAL History list on one frame, in Edit
    /// mode, written to <c>HROT_RAIL_SHOTS</c> as <c>edit-menu-history.png</c> for the side-by-side. A skip is not a pass: run under <c>xvfb-run -a</c>.
    /// </summary>
    [SkippableFact]
    public void U5_TheRealEditMenuRows_AndTheHistoryList_OnOneFrame()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var r = new Rig();
        r.Step("Move Alpha 1-1", 1); r.Step("Set side of Bravo 1", 2); r.Step("Move camera key", 3);
        r.History.Undo();
        var window = new EditHistoryWindow(r.History, () => r.Edit, "t_hist", "Scenario", Vector4.One) { IsOpen = true };
        using var frame = UiFrameHarness.Begin();
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        var rows = new System.Collections.Generic.List<(string Label, string? Shortcut, bool Enabled)>();
        void Draw()
        {
            rows.Clear();
            ImGui.SetNextWindowPos(new Vector2(10, 10)); ImGui.SetNextWindowSize(new Vector2(330, 150));
            ImGui.Begin("Edit##menu", ImGuiWindowFlags.NoCollapse);
            foreach (var n in r.Menu.Root.Children["Edit"].OrderedChildren())
            {
                bool enabled = n.GetEnabled?.Invoke() ?? true;
                rows.Add((n.ResolveLabel(), n.Shortcut, enabled));
                ImGui.MenuItem(n.ResolveLabel(), n.Shortcut ?? string.Empty, false, enabled);
            }
            ImGui.End();
            ImGui.SetNextWindowPos(new Vector2(360, 10)); ImGui.SetNextWindowSize(new Vector2(520, 200));
            ImGui.Begin("History##rail");
            window.DrawInto();
            ImGui.End();
        }
        for (int i = 0; i < 4; i++) frame.Step(Draw);
        Assert.Equal(new[] { "Undo Set side of Bravo 1", "Redo Move camera key", "History…" }, rows.Select(x => x.Label));
        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); frame.Screenshot(Path.Combine(dir, "edit-menu-history.png")); }
    }
}
