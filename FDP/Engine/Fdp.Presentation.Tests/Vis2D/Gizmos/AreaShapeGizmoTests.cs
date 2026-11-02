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

    [Fact]
    public void Escape_and_a_right_click_CANCEL_like_the_location_gizmo_and_the_Done_button_commits()
    {
        AreaLink? done = null; int cancelled = 0, removed = 0;
        var esc = new AreaShapeGizmo(AreaLink.Circle(Origin, 20f), a => done = a, () => removed++, () => cancelled++);
        esc.OnKeyEvent(MapKeyboardKey.Escape, true);
        Assert.Equal((1, 1), (cancelled, removed)); Assert.Null(done);

        var right = new AreaShapeGizmo(AreaLink.Circle(Origin, 20f), a => done = a, () => removed++, () => cancelled++);
        right.OnMouseEvent(MapMouseButton.Right, true, Vector3.Zero);
        Assert.Equal(2, cancelled); Assert.Null(done);                       // a right-click never commits

        var shape = AreaLink.Circle(Origin, 20f);
        var button = new AreaShapeGizmo(shape, a => done = a, () => removed++, () => cancelled++);
        var at = AreaShapeHandles.DonePosition(shape);
        Assert.Null(AreaShapeHandles.HitHandle(shape, at, AreaShapeHandles.HitRadiusFor(shape)));   // the button is clear of every handle
        Press(button, at);
        Assert.Equal(shape, done);
        Assert.Equal(2, cancelled);
    }

    [Fact]
    public void With_no_start_the_first_click_places_a_50m_circle_there_and_nothing_placed_cancels_on_Enter()
    {
        AreaLink? done = null; bool cancelled = false;
        var gizmo = new AreaShapeGizmo(default, a => done = a, () => { }, () => cancelled = true);
        Press(gizmo, new Vector2(300f, 400f));
        Assert.Equal(AreaLink.Circle(new WorldPoint(300f, 400f, 0f), 50f), gizmo.Shape);
        gizmo.OnKeyEvent(MapKeyboardKey.Enter, true);
        Assert.Equal(50f, done!.Value.Size.X);

        var empty = new AreaShapeGizmo(default, a => done = null, () => { }, () => cancelled = true);
        empty.OnKeyEvent(MapKeyboardKey.Enter, true);
        Assert.True(cancelled);                                               // nothing was placed ⇒ no value invented
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
