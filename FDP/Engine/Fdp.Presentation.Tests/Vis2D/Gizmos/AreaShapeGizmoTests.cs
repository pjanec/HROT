using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Vis2D.Gizmos;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos;

/// <summary>
/// ⭐ CE-4206 R2 (DESIGN_Map_Picking_Unification §6.3 F6) — <see cref="AreaShapeHandles"/> (the pure geometry) and <see cref="AreaShapeGizmo"/> (the input state machine).
/// Red-proof (compiles): swap X/Y in <c>AreaShapeHandles.BearingTo</c> (<c>atan2(d.Y, d.X)</c>) ⇒ the heading rails fail. No screenshot rail: the repo has no 2-D gizmo frame rail to extend
/// (the frame rails are 3-D film scenes), so the draw is asserted on the primitive buffer.
/// </summary>
public class AreaShapeGizmoTests
{
    private static readonly WorldPoint Origin = new(100f, 200f, 0f);

    // ── the pure maths ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_centre_drag_moves_Center_and_keeps_the_size_and_heading()
    {
        var rect = AreaLink.Rectangle(Origin, 20f, 60f, new Bearing(30f));
        var moved = AreaShapeHandles.Drag(rect, AreaHandleKind.Center, new Vector2(150f, 260f));
        Assert.Equal(new WorldPoint(150f, 260f, 0f), moved.Center);
        Assert.Equal(rect.Size, moved.Size);
        Assert.Equal(30f, moved.Heading.Degrees);
        Assert.Equal(RefForm.Rectangle, moved.Spec.Form);
    }

    [Fact]
    public void A_radius_drag_sets_the_radius_to_the_distance_from_the_centre()
    {
        var circle = AreaLink.Circle(Origin, 10f);
        var grown = AreaShapeHandles.Drag(circle, AreaHandleKind.Radius, new Vector2(100f + 30f, 200f + 40f));
        Assert.Equal(50f, grown.Size.X, 3);                                   // 3-4-5
        Assert.Equal(RefForm.Circle, grown.Spec.Form);
        Assert.Equal(1f, AreaShapeHandles.Drag(circle, AreaHandleKind.Radius, new Vector2(100f, 200f)).Size.X);   // never below the minimum
    }

    [Fact]
    public void Edge_drags_set_width_and_length_symmetric_about_the_centre()
    {
        var rect = AreaLink.Rectangle(Origin, 20f, 60f, default);              // heading north: length along +Y, width along +X
        var wider = AreaShapeHandles.Drag(rect, AreaHandleKind.Width, new Vector2(100f + 25f, 200f));
        Assert.Equal(50f, wider.Size.X, 3);                                   // half-width 25 ⇒ width 50
        Assert.Equal(60f, wider.Size.Y);
        var longer = AreaShapeHandles.Drag(rect, AreaHandleKind.Length, new Vector2(100f, 200f + 45f));
        Assert.Equal(90f, longer.Size.Y, 3);
        Assert.Equal(20f, longer.Size.X);
        // symmetric: dragging to the opposite side gives the same extent
        Assert.Equal(90f, AreaShapeHandles.Drag(rect, AreaHandleKind.Length, new Vector2(100f, 200f - 45f)).Size.Y, 3);

        // on a TURNED rectangle the edge handle follows the heading (east-pointing length)
        var east = AreaLink.Rectangle(Origin, 20f, 60f, new Bearing(90f));
        Assert.Equal(80f, AreaShapeHandles.Drag(east, AreaHandleKind.Length, new Vector2(100f + 40f, 200f)).Size.Y, 3);
    }

    [Fact]
    public void A_heading_drag_sets_the_compass_bearing_from_the_centre()
    {
        var rect = AreaLink.Rectangle(Origin, 20f, 60f, default);
        Assert.Equal(0f,   AreaShapeHandles.Drag(rect, AreaHandleKind.Heading, new Vector2(100f, 300f)).Heading.Degrees, 3);          // north
        Assert.Equal(90f,  AreaShapeHandles.Drag(rect, AreaHandleKind.Heading, new Vector2(200f, 200f)).Heading.Degrees, 3);          // east
        Assert.Equal(180f, AreaShapeHandles.Drag(rect, AreaHandleKind.Heading, new Vector2(100f, 100f)).Heading.Degrees, 3);          // south
        Assert.Equal(270f, AreaShapeHandles.Drag(rect, AreaHandleKind.Heading, new Vector2(0f, 200f)).Heading.Degrees, 3);            // west
        Assert.Equal(45f,  AreaShapeHandles.Drag(rect, AreaHandleKind.Heading, new Vector2(150f, 250f)).Heading.Degrees, 3);
        // and it is the SAME convention Contains() reads: a point along the new heading is inside the long thin rectangle
        var turned = AreaShapeHandles.Drag(AreaLink.Rectangle(Origin, 4f, 100f, default), AreaHandleKind.Heading, new Vector2(200f, 200f));
        Assert.True(turned.Contains(null, new Vector3(140f, 200f, 0f)));
        Assert.False(turned.Contains(null, new Vector3(100f, 240f, 0f)));
    }

    [Fact]
    public void Handles_are_where_the_shape_says_and_a_hit_picks_the_nearest_with_the_centre_last()
    {
        var circle = AreaLink.Circle(Origin, 40f);
        var hs = AreaShapeHandles.HandlesOf(circle);
        Assert.Equal(new[] { AreaHandleKind.Center, AreaHandleKind.Radius }, hs.Select(h => h.Kind).ToArray());
        Assert.Equal(new Vector2(140f, 200f), hs.Single(h => h.Kind == AreaHandleKind.Radius).Position);

        var rect = AreaLink.Rectangle(Origin, 20f, 60f, default);
        var kinds = AreaShapeHandles.HandlesOf(rect).ToDictionary(h => h.Kind, h => h.Position);
        Assert.Equal(new Vector2(110f, 200f), kinds[AreaHandleKind.Width]);
        Assert.Equal(new Vector2(100f, 230f), kinds[AreaHandleKind.Length]);
        Assert.True(kinds[AreaHandleKind.Heading].Y > kinds[AreaHandleKind.Length].Y);   // the knob hangs beyond the front edge

        float reach = AreaShapeHandles.HitRadiusFor(rect);
        Assert.Equal(AreaHandleKind.Center,  AreaShapeHandles.HitHandle(rect, new Vector2(100.5f, 200.5f), reach)!.Value.Kind);
        Assert.Equal(AreaHandleKind.Length,  AreaShapeHandles.HitHandle(rect, new Vector2(100f, 230.5f), reach)!.Value.Kind);
        Assert.Equal(AreaHandleKind.Heading, AreaShapeHandles.HitHandle(rect, kinds[AreaHandleKind.Heading] + new Vector2(0.5f, 0f), reach)!.Value.Kind);
        Assert.Null(AreaShapeHandles.HitHandle(rect, new Vector2(500f, 500f), reach));
        Assert.Empty(AreaShapeHandles.HandlesOf(AreaLink.OfEntity(new EntityRef(5))));   // an area entity has no handles
    }

    // ── the gizmo ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static void Press(AreaShapeGizmo g, Vector2 at) { g.OnDragUpdate(new Vector3(at, 0f)); g.OnMouseEvent(MapMouseButton.Left, true, new Vector3(at, 0f)); }
    private static void Release(AreaShapeGizmo g, Vector2 at) => g.OnMouseEvent(MapMouseButton.Left, false, new Vector3(at, 0f));

    [Fact]
    public void Grab_drag_release_edits_the_shape_and_Enter_commits_it_after_removing_the_gizmo()
    {
        AreaLink? done = null; int removed = 0; bool cancelled = false; bool removedBeforeDone = false;
        AreaShapeGizmo? gizmo = null;
        gizmo = new AreaShapeGizmo(AreaLink.Circle(Origin, 20f), a => { removedBeforeDone = removed == 1; done = a; }, () => removed++, () => cancelled = true);

        Press(gizmo, new Vector2(120f, 200f));                              // the radius handle
        Assert.Equal(AreaHandleKind.Radius, gizmo.Grabbed);
        gizmo.OnDragUpdate(new Vector3(160f, 200f, 0f));
        Release(gizmo, new Vector2(160f, 200f));
        Assert.Null(gizmo.Grabbed);
        Assert.Equal(60f, gizmo.Shape.Size.X, 3);

        gizmo.OnKeyEvent(MapKeyboardKey.Enter, true);
        Assert.Equal(60f, done!.Value.Size.X, 3);
        Assert.True(removedBeforeDone);
        Assert.False(cancelled);
    }

    private static int Finish() => Hrot_Gizmo_Ids.Finish;
    private static class Hrot_Gizmo_Ids { public const int Finish = 2410, RemoveLast = 2411, Cancel = 2412; }

    private static ShapeMenu TheMenu() => new("[]", id => id switch { 2410 => ShapeCommand.Finish, 2411 => ShapeCommand.RemoveLast, 2412 => ShapeCommand.Cancel, _ => null });

    /// <summary>
    /// ⭐ B-17 P2 (D11) — every door reaches the ONE session: Enter, the Done tick and the menu's Finish all commit; Esc and the menu's Cancel both cancel.
    /// Red-proof (compiles): make <c>Resolve</c> ignore <c>ShapeOutcome.Finished</c> ⇒ all three finish doors fail together.
    /// </summary>
    [Fact]
    public void D11_Enter_the_Done_tick_and_the_menu_Finish_all_commit_and_Esc_and_the_menu_Cancel_both_cancel()
    {
        var shape = AreaLink.Circle(Origin, 20f);
        AreaLink? done = null; int cancelled = 0, removed = 0;
        AreaShapeGizmo Make() { var g = new AreaShapeGizmo(shape, a => done = a, () => removed++, () => cancelled++); g.Session.Menu = TheMenu(); return g; }

        done = null; var viaEnter = Make(); viaEnter.OnKeyEvent(MapKeyboardKey.Enter, true);
        Assert.Equal(shape, done);

        done = null; var viaTick = Make(); Press(viaTick, AreaShapeHandles.DonePosition(shape));
        Assert.Equal(shape, done);

        done = null; var viaMenu = Make(); viaMenu.OnMenuAction(Finish());
        Assert.Equal(shape, done);
        Assert.Equal(3, removed);
        Assert.Equal(0, cancelled);

        done = null; Make().OnKeyEvent(MapKeyboardKey.Escape, true);
        Make().OnMenuAction(Hrot_Gizmo_Ids.Cancel);
        Assert.Null(done);
        Assert.Equal(2, cancelled);
    }

    /// <summary>⭐ B-17 P7 ⑥c — the area gizmo's Done button is the SAME rectangle <see cref="ShapeDoneMark"/> draws: a press at the button's far corner finishes, one just outside does not.</summary>
    [Fact]
    public void P7_TheDoneButtonOfAnArea_IsHitOverItsWholeRectangle()
    {
        var shape = AreaLink.Circle(Origin, 20f);
        var c = AreaShapeHandles.DonePosition(shape); float d = AreaShapeHandles.DoneHalfSize(shape);
        Assert.True(AreaShapeHandles.HitDone(shape, c + new Vector2(ShapeDoneMark.HalfWidth(d) - 0.01f, ShapeDoneMark.HalfHeight(d) - 0.01f)));
        Assert.False(AreaShapeHandles.HitDone(shape, c + new Vector2(ShapeDoneMark.HalfWidth(d) + 0.01f, 0f)));
        Assert.False(AreaShapeHandles.HitDone(shape, c + new Vector2(0f, ShapeDoneMark.HalfHeight(d) + 0.01f)));
        AreaLink? done = null;
        var g = new AreaShapeGizmo(shape, a => done = a, () => { }, () => { });
        Press(g, c + new Vector2(ShapeDoneMark.HalfWidth(d) * 0.9f, 0f));   // on the button's right end, where the old square was not
        Assert.Equal(shape, done);
    }

    /// <summary>⭐ B-17 P2 (D11) — right-click is the MENU: it ends NOTHING (it used to cancel). The gizmo advertises its menu for the layer to open.</summary>
    [Fact]
    public void D11_A_right_click_ends_nothing_and_the_gizmo_has_a_menu_to_open()
    {
        AreaLink? done = null; int cancelled = 0, removed = 0;
        var g = new AreaShapeGizmo(AreaLink.Circle(Origin, 20f), a => done = a, () => removed++, () => cancelled++);
        g.Session.Menu = new ShapeMenu("[{\"id\":2410}]", _ => null);
        g.OnMouseEvent(MapMouseButton.Right, true, Vector3.Zero);
        g.OnMouseEvent(MapMouseButton.Right, false, Vector3.Zero);
        Assert.Equal((0, 0), (cancelled, removed));
        Assert.Null(done);
        Assert.Equal("[{\"id\":2410}]", g.ToolMenuJson);
    }

    [Fact]
    public void With_no_start_the_first_click_places_a_50m_circle_there_and_Finish_with_nothing_placed_says_so_and_stays_armed()
    {
        AreaLink? done = null; bool cancelled = false; int removed = 0;
        var gizmo = new AreaShapeGizmo(default, a => done = a, () => removed++, () => cancelled = true);
        Press(gizmo, new Vector2(300f, 400f));
        Assert.Equal(AreaLink.Circle(new WorldPoint(300f, 400f, 0f), 50f), gizmo.Shape);
        gizmo.OnKeyEvent(MapKeyboardKey.Enter, true);
        Assert.Equal(50f, done!.Value.Size.X);

        done = null; removed = 0;
        var empty = new AreaShapeGizmo(default, a => done = a, () => removed++, () => cancelled = true);
        string? reason = null; empty.Session.ReasonChanged += r => reason = r;
        empty.OnKeyEvent(MapKeyboardKey.Enter, true);
        Assert.False(cancelled);                                              // ⭐ D11: NOT a silent cancel …
        Assert.Equal(0, removed);                                             // … the tool stays armed …
        Assert.Null(done);                                                    // … no value invented …
        Assert.Contains("click to place", reason);                            // … and it says why
        Press(empty, new Vector2(1f, 1f));
        Assert.Null(empty.Session.Reason);                                    // the next click clears the reason
    }

    /// <summary>⭐ B-17 P2 — Backspace un-places the centre (back to "click to place"); the centre handle drags the WHOLE shape through the session's MoveAll.</summary>
    [Fact]
    public void D11_Backspace_unplaces_and_the_centre_handle_is_the_sessions_MoveAll()
    {
        var gizmo = new AreaShapeGizmo(default, _ => { }, () => { });
        Press(gizmo, new Vector2(300f, 400f));
        Release(gizmo, new Vector2(300f, 400f));
        Assert.Single(gizmo.Session.Points);

        Press(gizmo, new Vector2(300f, 400f));                               // grab the centre handle
        Assert.Equal(AreaHandleKind.Center, gizmo.Grabbed);
        gizmo.OnDragUpdate(new Vector3(350f, 420f, 0f));
        Release(gizmo, new Vector2(350f, 420f));
        Assert.Equal(new Vector2(350f, 420f), gizmo.Session.Points[0]);      // the session's point moved with the shape's centre
        Assert.Equal(new WorldPoint(350f, 420f, 0f), gizmo.Shape.Center);

        gizmo.OnKeyEvent(MapKeyboardKey.Backspace, true);
        Assert.False(gizmo.Shape.IsShape);                                    // un-placed
        Assert.Empty(gizmo.Session.Points);
    }

    [Fact]
    public void The_gizmo_draws_the_outline_and_its_handles()
    {
        var buffer = new DebugPrimitiveBuffer();
        new AreaShapeGizmo(AreaLink.Rectangle(Origin, 20f, 60f, new Bearing(40f)), _ => { }, () => { })
            .UpdateAndDraw(new Moq.Mock<ISimulationView>().Object, 0f, buffer);
        var frame = buffer.GetFrame().ToArray();
        Assert.True(frame.Length >= 4 + 4 + 1, $"4 outline edges + 4 handles + the knob stem; saw {frame.Length}");
    }
}
