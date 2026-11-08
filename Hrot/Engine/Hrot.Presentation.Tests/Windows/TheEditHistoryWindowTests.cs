using System;
using System.Linq;
using System.Numerics;
using Hrot.Editor.UiFrameRail;
using Hrot.Presentation.Edits;
using Hrot.Presentation.Windows;
using ImGuiNET;
using Xunit;

namespace Hrot.Presentation.Tests.Windows;

/// <summary>
/// ⭐ U5 (docs/UX/DESIGN_Edit_Mode_Undo.md §3 U5; Z-2) — <b>Edit ▸ History…</b>: the editor's ONE history as a list, the state before the first step on top, the current
/// position marked, a click going there by repeated Undo / Redo, a row greyed WITH ITS REASON where a step on the way is no longer available, everything greyed
/// outside Edit mode, and a steady frame that allocates nothing.
/// </summary>
public sealed class TheEditHistoryWindowTests
{
    private sealed class Rig
    {
        public readonly EditHistory History = new();
        public bool Edit = true;
        public bool EvictFirst;
        public int Value;
        public readonly EditHistoryWindow Window;

        public Rig(bool threeSteps = true)
        {
            Window = new EditHistoryWindow(History, () => Edit, "t_history", "Scenario", Vector4.One);
            if (threeSteps) { Step("Move Alpha 1-1", 1, () => !EvictFirst); Step("Set side of Bravo 1", 2); Step("Delete Charlie 2", 3); }
        }

        public void Step(string label, int to, Func<bool>? avail = null)
        {
            int from = Value; Value = to;
            History.Push(label, () => Value = to, () => Value = from, avail);
        }
    }

    /// <summary>🔴 Mutation (compiles): <c>Click</c> calling <c>GoTo(position + 1)</c> ⇒ it lands one step off ⇒ the value assertion fails.</summary>
    [Fact]
    public void U5_TheRowsAreTheStepsOldestFirst_TheCurrentOneIsMarked_AClickGoesThere()
    {
        var r = new Rig();
        Assert.Equal(new[] { EditHistoryWindow.OriginalLabel, "Move Alpha 1-1", "Set side of Bravo 1", "Delete Charlie 2" }, r.Window.Rows);
        Assert.True(r.Window.IsCurrent(3)); Assert.False(r.Window.IsCurrent(1));

        Assert.True(r.Window.Click(1));
        Assert.Equal(1, r.Value);
        Assert.True(r.Window.IsCurrent(1));              // the mark follows
        Assert.True(r.Window.Click(3));                  // and forward again
        Assert.Equal(3, r.Value);
        Assert.True(r.Window.Click(0));
        Assert.Equal(0, r.Value);
    }

    /// <summary>🔴 Mutation (compiles): the row builder dropping the reason (<c>display = label</c> only) ⇒ red; <c>CanGoTo</c> ignoring availability ⇒ the reachable assertion fails.</summary>
    [Fact]
    public void U5_AStepNoLongerAvailable_GreysTheRowsThroughIt_WithTheReason_AndTheClickDoesNothing()
    {
        var r = new Rig();
        r.EvictFirst = true;                              // the ring evicted the first step's snapshot
        var rows = r.Window.Rows;
        Assert.False(r.Window.IsReachable(0));
        Assert.Contains("'Move Alpha 1-1' can no longer be undone", rows[0]);
        Assert.True(r.Window.IsReachable(1)); Assert.True(r.Window.IsReachable(2));
        Assert.DoesNotContain("no longer", rows[1]);

        Assert.False(r.Window.Click(0));
        Assert.Equal(3, r.Value);                         // nothing moved
        Assert.True(r.Window.Click(1)); Assert.Equal(1, r.Value);
    }

    /// <summary>🔴 Mutation (compiles): <c>Click</c> without the <c>_inEdit()</c> guard ⇒ a click outside Edit mode restores the world ⇒ red.</summary>
    [Fact]
    public void U5_OutsideEditMode_EveryRowIsGreyed_AndAClickDoesNothing()
    {
        var r = new Rig();
        r.Edit = false;
        for (int i = 0; i < 4; i++) Assert.False(r.Window.IsReachable(i));
        Assert.False(r.Window.Click(1));
        Assert.Equal(3, r.Value);
        r.Edit = true;
        Assert.True(r.Window.IsReachable(1));             // the rows follow the mode
    }

    /// <summary>The rows follow the history (a step pushed, an undo) without a stale cache.</summary>
    [Fact]
    public void U5_TheRowsFollowTheHistory()
    {
        var r = new Rig(threeSteps: false);
        Assert.Single(r.Window.Rows);
        r.Step("Add Bravo 2", 1);
        Assert.Equal(2, r.Window.Rows.Count);
        r.History.Undo();
        Assert.True(r.Window.IsCurrent(0));
        r.History.Clear();
        Assert.Single(r.Window.Rows);
    }

    /// <summary>
    /// On a real frame: every row's bar at ONE x and ONE height, the rows stacked (R-4138 measured layout), a steady frame allocating 0 B (HOT PATH), and the picture
    /// (<c>HROT_RAIL_SHOTS</c> ⇒ <c>edit-history.png</c>). A skip is not a pass: run under <c>xvfb-run -a</c>.
    /// 🔴 Mutation (compiles): a row's Selectable preceded by <c>ImGui.SameLine()</c> ⇒ the rows run on one line ⇒ the one-x assertion fails; a per-frame <c>string.Format</c> in <c>DrawInto</c> ⇒ the 0 B assertion fails.
    /// </summary>
    [SkippableFact]
    public void U5_OnARealFrame_TheRowsAlign_AndASteadyFrameAllocatesNothing()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        var r = new Rig();
        r.EvictFirst = true;
        r.History.Undo();                                 // the current position is in the middle: one dimmed (undone) row at the end
        r.Window.IsOpen = true;
        using var frame = UiFrameHarness.Begin();
        new Fdp.Presentation.Fonts.EditorFontService().Initialize(1f, 1f);
        long bytes = -1;
        bool measure = false;
        void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(520, 230));
            ImGui.Begin("History##rail");
            long before = GC.GetAllocatedBytesForCurrentThread();
            r.Window.DrawInto();
            if (measure) bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ImGui.End();
        }
        for (int i = 0; i < 4; i++) frame.Step(Draw);

        var rects = r.Window.LastRowRects;
        Assert.Equal(4, rects.Count);
        float x = rects[0].Min.X, h = rects[0].Max.Y - rects[0].Min.Y;
        Assert.True(h > 8f);
        for (int i = 0; i < rects.Count; i++)
        {
            Assert.Equal(x, rects[i].Min.X, 0.5f);                                   // one x
            Assert.Equal(h, rects[i].Max.Y - rects[i].Min.Y, 0.5f);                  // one height
            if (i > 0) Assert.True(rects[i].Min.Y >= rects[i - 1].Max.Y - 0.5f);     // stacked, never overlapping
        }

        for (int i = 0; i < 2; i++) frame.Step(Draw);                                // warm
        measure = true;
        frame.Step(Draw);                                                             // the content alone: a steady frame builds no string
        Assert.True(bytes <= 0, $"a steady History frame allocated {bytes} B");

        if (Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } dir) frame.Screenshot(System.IO.Path.Combine(dir, "edit-history.png"));
    }
}
