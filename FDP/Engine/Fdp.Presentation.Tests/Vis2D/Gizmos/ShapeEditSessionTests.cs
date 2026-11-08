using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Vis2D.Gizmos;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos;

/// <summary>
/// ⭐ B-17 P2 (📄 docs/UX/DESIGN_Map_Interaction.md §2.5 — D11) — <see cref="ShapeEditSession"/>, the ONE drawing core: the finish / cancel convention the
/// drawing gizmos share. The gizmos' own rails (here, in Hrot.Presentation.Tests) prove each gizmo reaches it; these prove what it does.
/// Red-proof (compiles): make <c>Finish()</c> with too few points return <c>Cancelled</c> ⇒ <c>TooFew_says_why_and_the_session_stays_open</c> fails.
/// </summary>
public class ShapeEditSessionTests
{
    private static ShapeEditSession Area() => new(minPoints: 3, closed: true, noun: "an area");

    private static void Fill(ShapeEditSession s, int n)
    {
        for (int i = 0; i < n; i++) s.Add(new Vector2(i * 100f, (i % 2) * 100f));
    }

    [Fact]
    public void Enter_the_tick_and_the_menu_finish_are_ONE_Finish()
    {
        var viaKey = Area(); Fill(viaKey, 3);
        var viaMenu = Area(); Fill(viaMenu, 3);
        viaMenu.Menu = new ShapeMenu("[]", id => id == 7 ? ShapeCommand.Finish : null);
        var viaTick = Area(); Fill(viaTick, 3);

        Assert.Equal(ShapeOutcome.Finished, viaKey.OnKey(MapKeyboardKey.Enter, true));
        Assert.Equal(ShapeOutcome.Finished, viaMenu.ApplyMenuAction(7));
        Assert.True(viaTick.HitDone(viaTick.DonePosition));          // the ✓ is where the session says it is …
        Assert.Equal(ShapeOutcome.Finished, viaTick.Finish());       // … and its click calls Finish

        Assert.True(viaKey.Ended && viaMenu.Ended && viaTick.Ended);
    }

    [Fact]
    public void TooFew_says_why_and_the_session_stays_open()
    {
        var s = Area(); Fill(s, 2);
        string? seen = null;
        s.ReasonChanged += r => seen = r;

        Assert.Equal(ShapeOutcome.TooFew, s.OnKey(MapKeyboardKey.Enter, true));
        Assert.False(s.Ended);
        Assert.Contains("an area needs 3 points", s.Reason);
        Assert.Equal(s.Reason, seen);                                // the hint line hears it

        s.Add(new Vector2(5f, 5f));                                  // the next click clears it
        Assert.Null(s.Reason);
        Assert.Null(seen);
        Assert.Equal(ShapeOutcome.Finished, s.Finish());
    }

    [Fact]
    public void Esc_and_the_menu_cancel_end_it_and_Backspace_removes_the_last_point()
    {
        var s = Area(); Fill(s, 3);
        s.OnKey(MapKeyboardKey.Backspace, true);
        Assert.Equal(2, s.Points.Count);
        Assert.Equal(2, s.Heights.Count);
        Assert.Equal(ShapeOutcome.None, s.OnKey(MapKeyboardKey.Enter, false));   // a release is not a key press
        Assert.Equal(ShapeOutcome.Cancelled, s.OnKey(MapKeyboardKey.Escape, true));
        Assert.True(s.Ended);
        s.Add(new Vector2(1f, 1f));                                  // an ended session takes no more points
        Assert.Equal(2, s.Points.Count);
        Assert.Equal(ShapeOutcome.Cancelled, s.Finish());            // the first end sticks

        var m = Area(); Fill(m, 3);
        m.Menu = new ShapeMenu("[]", id => id == 1 ? ShapeCommand.RemoveLast : id == 2 ? ShapeCommand.Cancel : null);
        Assert.Equal(ShapeOutcome.None, m.ApplyMenuAction(1));
        Assert.Equal(2, m.Points.Count);
        Assert.Equal(ShapeOutcome.None, m.ApplyMenuAction(99));      // not one of its rows
        Assert.Equal(ShapeOutcome.Cancelled, m.ApplyMenuAction(2));
    }

    [Fact]
    public void Clicking_the_first_point_closes_a_CLOSED_shape_only()
    {
        var area = Area(); Fill(area, 3);
        Assert.True(area.HitsFirstPoint(new Vector2(1f, 1f)));
        Assert.False(area.HitsFirstPoint(new Vector2(150f, 50f)));

        var route = new ShapeEditSession(2, closed: false, noun: "a route"); Fill(route, 3);
        Assert.False(route.HitsFirstPoint(new Vector2(1f, 1f)));
    }

    [Fact]
    public void The_edit_methods_keep_points_and_heights_in_step()
    {
        var s = Area();
        s.Add(new Vector2(0, 0), 5f); s.Add(new Vector2(100, 0)); s.Add(new Vector2(100, 100));
        Assert.Equal(1, s.InsertMidpointAfter(0));                   // between the first two
        Assert.Equal(new Vector2(50, 0), s.Points[1]);
        Assert.Equal(5f, s.Heights[1]);                              // takes the height of the segment's start
        Assert.Equal(1, s.Selected);

        Assert.Equal(1, s.DeleteSelected());
        Assert.Equal(3, s.Points.Count);
        Assert.Equal(-1, s.DeleteSelected());                        // nothing selected now

        s.MoveAll(new Vector2(10, 20));                              // the centre handle: the WHOLE shape
        Assert.Equal(new Vector2(10, 20), s.Points[0]);
        Assert.Equal(new Vector2(110, 120), s.Points[2]);
        Assert.Equal(-1, new ShapeEditSession(2, false).InsertMidpointAfter(0));   // fewer than two points: no segment
    }

    [Fact]
    public void The_tick_sits_clear_of_the_points_and_the_hint_names_the_minimum_and_the_keys()
    {
        var s = Area(); Fill(s, 3);
        var d = s.DonePosition;
        foreach (var p in s.Points) Assert.True(Vector2.Distance(p, d) > s.DoneHalfSize * 2f, "the ✓ never sits on a point");
        Assert.Contains("Enter / ✓: finish (3+)", s.KeysHint);
        Assert.Contains("Backspace: undo point", s.KeysHint);
        Assert.Contains("Esc: cancel", s.KeysHint);
        Assert.False(new ShapeEditSession(1, false).HitDone(Vector2.Zero));       // no points: no ✓ to hit
    }
    // ── ⭐ B-17 P6 — the editing handles (📄 DESIGN_Map_Interaction §2.5 "editing a shape or route") ─────────────────────────────────────

    private static ShapeEditSession Triangle()
    {
        var s = Area();
        s.Add(new Vector2(0f, 0f)); s.Add(new Vector2(60f, 0f)); s.Add(new Vector2(30f, 90f));
        return s;
    }

    /// <summary>The centre is the mean; a centre drag moves EVERY point by the cursor's travel from the grab (idempotent per update), ends as one move, cancels clean.</summary>
    [Fact]
    public void P6_TheCentreDrag_MovesEveryPoint_FromTheOriginals_AndCancelsClean()
    {
        var s = Triangle();
        Assert.Equal(new Vector2(30f, 30f), s.Centre);
        s.BeginCentreDrag(new Vector2(30f, 30f));
        s.UpdateCentreDrag(new Vector2(40f, 35f));
        s.UpdateCentreDrag(new Vector2(50f, 40f));          // from the originals, not cumulative
        Assert.Equal(new Vector2(20f, 10f), s.Points[0]);
        Assert.Equal(new Vector2(80f, 10f), s.Points[1]);
        Assert.Equal(new Vector2(50f, 100f), s.Points[2]);
        Assert.True(s.EndCentreDrag());

        s.BeginCentreDrag(new Vector2(0f, 0f));
        s.UpdateCentreDrag(new Vector2(500f, 500f));
        s.CancelCentreDrag();
        Assert.Equal(new Vector2(20f, 10f), s.Points[0]);
        Assert.False(s.CentreDragging);

        s.BeginCentreDrag(new Vector2(1f, 1f));
        Assert.False(s.EndCentreDrag(), "a grab that moved nothing is no move");
    }

    /// <summary>A click on a segment is a segment's: on a vertex or the centre it is not; the closing segment of a closed shape counts, an open shape's does not exist.</summary>
    [Fact]
    public void P6_SegmentAt_FindsTheSegmentUnderTheClick_NotAVertexNorTheCentre()
    {
        var s = Triangle();
        Assert.Equal(0, s.SegmentAt(new Vector2(30f, 1f), out var on));
        Assert.Equal(new Vector2(30f, 0f), on);
        Assert.Equal(1, s.SegmentAt(new Vector2(46f, 46f), out _));   // (60,0)-(30,90) passes (46.67, 40)… near (45,45)
        Assert.Equal(2, s.SegmentAt(new Vector2(15f, 45f), out _));   // the closing segment (30,90)-(0,0)
        Assert.Equal(-1, s.SegmentAt(new Vector2(1f, 1f), out _));    // on vertex 0
        Assert.Equal(-1, s.SegmentAt(new Vector2(30f, 30f), out _));  // the centre
        Assert.Equal(-1, s.SegmentAt(new Vector2(30f, 60f), out _));  // open ground

        var route = new ShapeEditSession(2, false, "a route");
        route.Add(new Vector2(0f, 0f)); route.Add(new Vector2(60f, 0f)); route.Add(new Vector2(30f, 90f));
        Assert.Equal(2, route.SegmentCount);
        Assert.Equal(-1, route.SegmentAt(new Vector2(15f, 45f), out _));   // no closing segment on a route
        Assert.Equal(2, route.InsertAtClick(new Vector2(46f, 46f)));   // between points 1 and 2
        Assert.Equal(4, route.Points.Count);
        Assert.Equal(2, route.Selected);
    }

    /// <summary>Delete holds the minimum: at it, nothing is removed and the reason is set (TooFew); above it the selected point goes. 🔴 Red-proof (compiles): drop the
    /// minimum check ⇒ the second assert reads 2 points.</summary>
    [Fact]
    public void P6_DeleteSelectedPoint_HoldsTheMinimum_AndSaysWhy()
    {
        var s = Triangle();
        s.Selected = 1;
        Assert.Equal(ShapeOutcome.TooFew, s.DeleteSelectedPoint());
        Assert.Equal(3, s.Points.Count);
        Assert.Contains("an area needs 3 points", s.Reason);
        Assert.Equal(1, s.Selected);

        s.Add(new Vector2(100f, 100f));                 // clears the reason
        Assert.Null(s.Reason);
        Assert.Equal(ShapeOutcome.None, s.DeleteSelectedPoint());
        Assert.Equal(3, s.Points.Count);
        Assert.Equal(-1, s.Selected);
        Assert.Equal(ShapeOutcome.None, s.DeleteSelectedPoint());   // nothing selected: nothing happens
    }

    /// <summary>Delete is a ROW of the keymap, not a key compared in the session: remapping the row moves the key. 🔴 Red-proof (compiles): <c>ShapeEditKeys.Is</c> returning
    /// <c>key == Delete</c> ⇒ the rebound key is not the row ⇒ red.</summary>
    [Fact]
    public void P6_Delete_IsAKeymapRow_ARebindMovesIt()
    {
        var map = new Fdp.Toolkit.Input.Keymap();
        ShapeEditKeys.Declare(map);
        var s = Triangle(); s.Keys = map;
        Assert.True(s.IsDeleteKey(MapKeyboardKey.Delete, false));
        Assert.False(s.IsDeleteKey(MapKeyboardKey.Delete, true), "acts on the release");
        Assert.False(s.IsDeleteKey(MapKeyboardKey.Enter, false));
        Assert.True(s.IsDeleteKey(MapKeyboardKey.Delete | MapKeyboardKey.ShiftMask, false), "the modifier bits ride along");

        var action = Fdp.Toolkit.Input.ActionRef.Command(ShapeEditKeys.DeletePointId);
        map.Unbind(Fdp.Toolkit.Input.InputScope.Map, ShapeEditKeys.Qualifier, action);
        map.Declare(new Fdp.Toolkit.Input.KeymapEntry(new NodeEditor.Core.Action.KeyBinding(NodeEditor.Primitives.EditorKey.X, NodeEditor.Primitives.KeyModifiers.None),
            Fdp.Toolkit.Input.InputScope.Map, ShapeEditKeys.Qualifier, action));
        Assert.False(s.IsDeleteKey(MapKeyboardKey.Delete, false));
        Assert.True(s.IsDeleteKey((MapKeyboardKey)88, false));   // 'X' (GLFW 88)
        Assert.Equal("Delete the selected point", Fdp.Toolkit.Input.Keymap.Process.LabelOf(action));
    }
}
