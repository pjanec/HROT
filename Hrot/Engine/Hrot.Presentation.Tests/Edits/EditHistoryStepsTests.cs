using Hrot.Presentation.Edits;
using Xunit;

namespace Hrot.Presentation.Tests.Edits;

/// <summary>
/// ⭐ U4 + U5 (docs/UX/DESIGN_Edit_Mode_Undo.md §3, Q3) — the ONE <see cref="EditHistory"/> as the History list needs it: the position and the step labels, <c>GoTo</c> by repeated
/// Undo / Redo, a step that is no longer available blocks the way through it, the owner tag of a kind of step, and a step of another document (a take) that does not dirty the scenario.
/// </summary>
public sealed class EditHistoryStepsTests
{
    private int _v;
    private void Step(EditHistory h, string label, int to, Func<bool>? available = null, object? owner = null, bool dirties = true)
    {
        int from = _v; _v = to;
        h.Push(label, () => _v = to, () => _v = from, available, owner, dirties);
    }

    /// <summary>🔴 Mutation (compiles): <c>GoTo</c> stopping one step short (<c>_at &gt; position + 1</c>) ⇒ the value assertion fails.</summary>
    [Fact]
    public void U5_GoTo_MovesBackAndForwardByRepeatedUndoRedo_AndTheLabelsAreTheSteps()
    {
        var h = new EditHistory();
        Step(h, "A", 1); Step(h, "B", 2); Step(h, "C", 3);
        Assert.Equal(3, h.Position);
        Assert.Equal(new[] { "A", "B", "C" }, new[] { h.StepLabel(0), h.StepLabel(1), h.StepLabel(2) });

        Assert.True(h.GoTo(1));
        Assert.Equal(1, _v); Assert.Equal(1, h.Position); Assert.Equal("B", h.RedoLabel);
        Assert.True(h.GoTo(3));
        Assert.Equal(3, _v);
        Assert.True(h.GoTo(0));
        Assert.Equal(0, _v); Assert.False(h.CanUndo);
        Assert.False(h.GoTo(4)); Assert.False(h.GoTo(-1));
    }

    /// <summary>🔴 Mutation (compiles): <c>CanGoTo</c> ignoring <c>Runs(step)</c> ⇒ the blocked jump goes through ⇒ red.</summary>
    [Fact]
    public void U5_AStepNoLongerAvailable_BlocksTheWayThroughIt_AndNothingMoves()
    {
        var h = new EditHistory();
        bool aliveA = true;
        Step(h, "A", 1, () => aliveA); Step(h, "B", 2); Step(h, "C", 3);
        aliveA = false;                                   // A's snapshot aged out

        Assert.True(h.CanGoTo(1));                        // the state after A is still reachable (only B, C are undone)
        Assert.False(h.CanGoTo(0));                       // the state before A needs A undone
        Assert.False(h.GoTo(0));
        Assert.Equal(3, h.Position); Assert.Equal(3, _v);
        Assert.False(h.StepAvailable(0)); Assert.True(h.StepAvailable(1));
        Assert.True(h.GoTo(1)); Assert.Equal(1, _v);
        Assert.False(h.CanGoTo(0));
    }

    /// <summary>🔴 Mutation (compiles): <c>IsDirty</c> back to <c>_at != _savedAt</c> ⇒ a take step dirties the scenario ⇒ red.</summary>
    [Fact]
    public void U4_AStepOfAnotherDocument_DoesNotDirtyTheScenario_ButAWorldStepDoes()
    {
        var h = new EditHistory();
        Step(h, "world", 1);
        h.MarkSaved();
        Assert.False(h.IsDirty);
        Step(h, "take edit", 2, dirties: false);
        Assert.False(h.IsDirty);
        Step(h, "world 2", 3);
        Assert.True(h.IsDirty);
        h.Undo();
        Assert.False(h.IsDirty);                          // back on the take step: the saved world state
        h.GoTo(0);
        Assert.True(h.IsDirty);                           // before the saved world step
    }

    /// <summary>🔴 Mutation (compiles): <c>UndoOwner</c> returning null ⇒ the take window's Undo never sees its own step ⇒ red (and with the world step on top it must see NOT its own).</summary>
    [Fact]
    public void U4_TheOwnerTagsTheNewestStep()
    {
        var h = new EditHistory();
        var take = new object();
        Step(h, "take", 1, owner: take);
        Assert.Same(take, h.UndoOwner);
        Step(h, "world", 2);
        Assert.Null(h.UndoOwner);
        h.Undo();
        Assert.Same(take, h.UndoOwner);
        h.Undo();
        Assert.Same(take, h.RedoOwner);
    }
}
