using System.IO;
using Hrot.Presentation.Edits;
using Hrot.UI.Common.Map3D.Director;
using Xunit;

namespace Hrot.Presentation.Tests.Map3D;

/// <summary>
/// ⭐ U4 (docs/UX/DESIGN_Edit_Mode_Undo.md §3 U4, Q3 / Q6; docs/DESIGN_Film_Director.md take editor) — the director's take editor rides the editor's ONE
/// <see cref="EditHistory"/> instead of its own <c>GizmoUndoStack</c>: one take edit = ONE named step there (so Edit ▸ Undo / Redo / History show it),
/// interleaved with world steps in one order; the take window's Undo acts on a take step only; a closed take's steps stay, greyed.
/// </summary>
public sealed class TakeEditsOnTheOneHistoryTests : System.IDisposable
{
    private const string V2 = """{"segments":[{"from":0,"to":4}],"keys":[{"at":2,"lookAt":[1,2,3],"distance":60},{"at":5,"lookAt":[4,5,6],"distance":30}]}""";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "u4-" + System.Guid.NewGuid().ToString("N"));

    public TakeEditsOnTheOneHistoryTests()
    {
        var path = new TakeStore(() => _root, () => null).TakePath("sc", "main");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, V2);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private DirectorTakeSession Session(EditHistory h)
    {
        var store = new TakeStore(() => _root, () => null);
        return new DirectorTakeSession(() => store, () => null, () => null, history: h);
    }

    /// <summary>🔴 Mutation (compiles): <c>TakeEditor.Apply</c> not pushing to the history (the removed <c>_history.Push</c>) ⇒ Count 0 ⇒ red.</summary>
    [Fact]
    public void U4_ATakeEditIsOneNamedStepOnTheSharedHistory_UndoneAndRedoneThroughIt()
    {
        var h = new EditHistory();
        var session = Session(h);
        Assert.Empty(session.Open("sc", "main"));
        var camera = session.Director!.Camera;

        camera.Move("key:0", 3.5, commit: true);
        Assert.Equal(1, h.Count);
        Assert.Equal("Move camera key", h.UndoLabel);
        Assert.False(h.IsDirty);                          // a take is its own file: the scenario stays clean

        Assert.True(h.Undo());                            // what Edit ▸ Undo calls
        Assert.Equal(2d, camera.Items[0].From);
        Assert.True(h.Redo());
        Assert.Equal(3.5, camera.Items[0].From);
    }

    /// <summary>🔴 Mutation (compiles): the session's <c>CanUndo</c> without the owner check ⇒ the window's Undo undoes the world step on top ⇒ red.</summary>
    [Fact]
    public void U4_TheTakeWindowsUndo_ActsOnATakeStepOnly_WorldStepsAreEditUndos()
    {
        var h = new EditHistory();
        var session = Session(h);
        session.Open("sc", "main");
        session.Director!.Camera.Move("key:0", 3.5, commit: true);
        int world = 0;
        h.Push("Move Alpha 1-1", () => world = 1, () => world = 0);   // a world step after it

        Assert.False(session.CanUndo);                    // the newest step is not the take's
        session.UndoLast();
        Assert.Equal(2, h.Position);                      // untouched
        h.Undo();                                         // Edit ▸ Undo takes the world step
        Assert.True(session.CanUndo);
        session.UndoLast();
        Assert.Equal(2d, session.Director.Camera.Items[0].From);
        Assert.True(session.CanRedo);
    }

    /// <summary>🔴 Mutation (compiles): <c>Close</c> not retiring the editor ⇒ the step stays available and would restore a closed take ⇒ red.</summary>
    [Fact]
    public void U4_ClosingTheTake_LeavesItsStepsOnTheHistory_Greyed()
    {
        var h = new EditHistory();
        var session = Session(h);
        session.Open("sc", "main");
        session.Director!.Camera.Move("key:0", 3.5, commit: true);
        session.Close();

        Assert.Equal(1, h.Count);
        Assert.False(h.StepAvailable(0));
        Assert.False(h.CanUndo);
        Assert.False(session.CanUndo);
        Assert.False(h.GoTo(0));
    }

    /// <summary>A host with no shared history (the Replay Browser) gets a private one, and the pack wires the panel's own chord.</summary>
    [Fact]
    public void U4_WithoutASharedHistory_TheSessionKeepsAPrivateOne()
    {
        var store = new TakeStore(() => _root, () => null);
        var session = new DirectorTakeSession(() => store, () => null, () => null);
        Assert.False(session.SharedHistory);
        session.Open("sc", "main");
        session.Director!.Camera.Move("key:0", 3.5, commit: true);
        Assert.True(session.CanUndo);
        session.UndoLast();
        Assert.Equal(2d, session.Director.Camera.Items[0].From);
    }
}
