using System;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Gizmos;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos;

/// <summary>
/// ⭐ B-17 P7 ② ③ (📄 docs/UX/DESIGN_Map_Interaction.md "P7 — the build"; sheet <c>map-gizmo-look-1.svg</c> rows ② ③) — <see cref="GizmoEditStyle"/>, the ONE look of drawing and editing: what each helper emits, that the hit area of a handle is what it was before P7
/// (an invisible pick box with the ids) and that the drawing gizmos draw only through it. Red-proofs (compile): <c>Handle</c> not emitting the pick box ⇒ <c>P7_AHandle_…</c> red; <c>PointSequenceGizmo</c> still drawing spheres ⇒ the no-sphere assert red.
/// </summary>
public class GizmoEditStyleTests
{
    private static DebugPrimitive[] Draw(Action<DebugPrimitiveBuffer> f)
    {
        var b = new DebugPrimitiveBuffer();
        f(b);
        return b.GetFrame().ToArray();
    }

    private static bool Is(Rgba32 c, Rgba32 want) => c.R == want.R && c.G == want.G && c.B == want.B;

    [Fact]
    public void P7_APoint_IsASmallPixelSquare_TheFirstIsLargerAndWhite_TheOthersDarkWithTheKindOutline()
    {
        var first = Assert.Single(Draw(b => GizmoEditStyle.Point(b, new Vector2(3, 4), ShapeKind.Area, first: true)));
        var next = Assert.Single(Draw(b => GizmoEditStyle.Point(b, new Vector2(3, 4), ShapeKind.Route, first: false)));
        Assert.Equal(SizeMode.ScreenPixels, first.SizeMode);
        Assert.Equal(GizmoEditStyle.FirstHalfPx, first.BoxExtentX);
        Assert.Equal(GizmoEditStyle.PointHalfPx, next.BoxExtentX);
        Assert.Equal((255, 255, 255), (first.FillColor.R, first.FillColor.G, first.FillColor.B));   // the closing target is white
        Assert.True(Is(first.Color, GizmoEditStyle.Teal), "an area's outline is teal");
        Assert.True(Is(next.FillColor, GizmoEditStyle.Dark) && Is(next.Color, GizmoEditStyle.Green), "a route's point is dark with the green outline");
    }

    [Fact]
    public void P7_ASegment_IsSolidWhenPlaced_DashedToTheCursor_DottedWhenClosing()
    {
        LineStyle S(SegmentKind k) => Assert.Single(Draw(b => GizmoEditStyle.Segment(b, Vector3.Zero, Vector3.UnitX, ShapeKind.Area, k))).LineStyle;
        Assert.Equal(LineStyle.Solid, S(SegmentKind.Placed));
        Assert.Equal(LineStyle.Dashed, S(SegmentKind.Next));
        Assert.Equal(LineStyle.Dotted, S(SegmentKind.Closing));
        Assert.Equal(20, Assert.Single(Draw(b => GizmoEditStyle.Segment(b, Vector3.Zero, Vector3.UnitX, ShapeKind.Area, SegmentKind.Placed))).ThicknessU16);   // 2 px
    }

    /// <summary>The hit area of a handle is what the editors always emitted (an 8 px half-extent box carrying the ids) — now invisible (alpha 0) — and the DRAWN square (no ids ⇒ never picked) is the sheet's small one: white idle, amber grabbed.</summary>
    [Fact]
    public void P7_AHandle_KeepsItsPickBox_AndDrawsASmallSquare_WhiteIdle_AmberGrabbed()
    {
        foreach (var state in new[] { HandleState.Idle, HandleState.Grabbed })
        {
            var f = Draw(b => GizmoEditStyle.Handle(b, new Vector2(10, 20), ShapeKind.Route, state, anchorId: 90210L, subElementId: 3));
            Assert.Equal(2, f.Length);
            var pick = Assert.Single(f, p => p.BoxAnchorId != 0);
            var drawn = Assert.Single(f, p => p.BoxAnchorId == 0);
            Assert.Equal((90210L, (ushort)3), (pick.BoxAnchorId, pick.SubElementId));
            Assert.Equal(GizmoEditStyle.PickHalfPx, pick.BoxExtentX);
            Assert.Equal(0, pick.Color.A);                                  // nothing of it is drawn …
            Assert.Equal((ushort)0, drawn.SubElementId);                    // … and the drawn one is not pickable
            Assert.True(drawn.BoxExtentX < pick.BoxExtentX);
            if (state == HandleState.Idle) Assert.Equal((255, 255, 255), (drawn.FillColor.R, drawn.FillColor.G, drawn.FillColor.B));
            else Assert.True(Is(drawn.FillColor, GizmoEditStyle.Amber), "a grabbed handle is amber");
        }
    }

    [Fact]
    public void P7_TheCentre_IsADiamond_WithTheSameIdsAsThePickBox()
    {
        var f = Draw(b => GizmoEditStyle.Centre(b, new Vector2(1, 2), 77L, 9));
        var drawn = Assert.Single(f, p => p.BoxAnchorId == 0);
        Assert.Equal(45f, drawn.BoxAngleDeg);
        Assert.True(Is(drawn.FillColor, GizmoEditStyle.Dark));
        Assert.Equal((77L, (ushort)9), (Assert.Single(f, p => p.BoxAnchorId != 0).BoxAnchorId, Assert.Single(f, p => p.BoxAnchorId != 0).SubElementId));
    }

    [Fact]
    public void P7_AStem_DropsADashedLineToTheGround_AndNothingForAGroundPoint()
    {
        var f = Draw(b => GizmoEditStyle.Stem(b, new Vector2(5, 6), 12f, ShapeKind.Route));
        var line = Assert.Single(f, p => p.Shape == DebugPrimitiveShape.Line);
        Assert.Equal((5f, 6f, 12f, 0f), (line.LineStart.X, line.LineStart.Y, line.LineStart.Z, line.LineEnd.Z));
        Assert.Equal(LineStyle.Dashed, line.LineStyle);
        Assert.Contains(f, p => p.Shape == DebugPrimitiveShape.Sphere && p.SizeMode == SizeMode.ScreenPixels);
        Assert.Empty(Draw(b => GizmoEditStyle.Stem(b, new Vector2(5, 6), 0f, ShapeKind.Route)));
    }

    [Fact]
    public void P7_TheFill_IsAFanOnlyForAConvexArea()
    {
        var square = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
        var dart = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(4, 4), new Vector2(0, 10) };   // concave
        var fan = Draw(b => Assert.True(GizmoEditStyle.FillConvex(b, square, ShapeKind.Area)));
        Assert.Equal(2, fan.Count(p => p.Shape == DebugPrimitiveShape.FilledTriangle));
        Assert.All(fan, p => Assert.Equal(GizmoEditStyle.FillAlpha, p.Color.A));
        Assert.Empty(Draw(b => Assert.False(GizmoEditStyle.FillConvex(b, dart, ShapeKind.Area))));
    }

    /// <summary>The drawn area: kind-coloured segments (placed solid, next dashed, closing dotted), the first point white, no private spheres; a steady frame allocates 0 B.</summary>
    [Fact]
    public void P7_PointSequenceGizmo_DrawsThroughTheStyle_At0BSteady()
    {
        var area = new PointSequenceGizmo(_ => { }, () => { }, minPoints: 3, closed: true, noun: "an area");
        var route = new PointSequenceGizmo(_ => { }, () => { }, minPoints: 2, closed: false, noun: "a route");
        foreach (var g in new[] { area, route })
        {
            g.Session.Add(new Vector2(0, 0)); g.Session.Add(new Vector2(30, 0)); g.Session.Add(new Vector2(30, 20));
            g.OnDragUpdate(new Vector3(5, 25, 0));
        }
        var b = new DebugPrimitiveBuffer();
        area.UpdateAndDraw(new Fdp.Core.EntityRepository(), 0f, b);
        var f = b.GetFrame().ToArray();
        Assert.DoesNotContain(f, p => p.Shape == DebugPrimitiveShape.Sphere);
        Assert.Contains(f, p => p.Shape == DebugPrimitiveShape.Line && p.LineStyle == LineStyle.Dotted && Is(p.Color, GizmoEditStyle.Teal));   // the closing segment
        Assert.Contains(f, p => p.Shape == DebugPrimitiveShape.Line && p.LineStyle == LineStyle.Dashed);                                       // the next segment
        Assert.Equal(2, f.Count(p => p.Shape == DebugPrimitiveShape.Line && p.LineStyle == LineStyle.Solid && Is(p.Color, GizmoEditStyle.Teal)));   // the two placed segments (the Done button's tick is green)
        Assert.True(f.Count(p => p.Shape == DebugPrimitiveShape.FilledTriangle) >= 1, "a convex area is filled");
        Assert.Single(f, p => p.Shape == DebugPrimitiveShape.Box2D && p.FillColor.R == 255 && p.BoxExtentX == GizmoEditStyle.FirstHalfPx);      // the first point, white

        b.Clear(); route.UpdateAndDraw(new Fdp.Core.EntityRepository(), 0f, b);
        Assert.DoesNotContain(b.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.Line && p.LineStyle == LineStyle.Dotted);            // a route has no closing segment
        Assert.Contains(b.GetFrame().ToArray(), p => p.Shape == DebugPrimitiveShape.Line && Is(p.Color, GizmoEditStyle.Green));

        foreach (var g in new[] { area, route })
        {
            var buf = new DebugPrimitiveBuffer();
            var repo = new Fdp.Core.EntityRepository();
            g.UpdateAndDraw(repo, 0f, buf); buf.Clear(); g.UpdateAndDraw(repo, 0f, buf); buf.Clear();
            long before = GC.GetAllocatedBytesForCurrentThread();
            g.UpdateAndDraw(repo, 0f, buf);
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(delta == 0, $"a steady drawing frame allocates {delta} B");
        }
    }
}
