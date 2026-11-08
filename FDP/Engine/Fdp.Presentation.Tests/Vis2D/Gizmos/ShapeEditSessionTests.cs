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
}
