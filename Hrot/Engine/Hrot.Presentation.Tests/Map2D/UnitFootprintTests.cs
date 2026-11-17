using System;
using System.Numerics;
using Hrot.Presentation.Map2D;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-8602 (exp/agg-ui, docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-2 / UI-4) — a memberless unit's footprint GEOMETRY: the deployed
/// rectangle is width ACROSS the heading and depth ALONG it, centred, corners counter-clockwise; the arrow leaves the front edge's middle; a march column is
/// the last Depth metres of its path up to the head, bends included; 0 B. 🔴 Red-proof (compiles): <c>Forward</c> with sin / cos swapped ⇒ a unit heading
/// east draws heading north ⇒ red.
/// </summary>
public sealed class UnitFootprintTests
{
    private static void Near(Vector2 want, Vector2 got) { Assert.InRange(got.X, want.X - 1e-3f, want.X + 1e-3f); Assert.InRange(got.Y, want.Y - 1e-3f, want.Y + 1e-3f); }

    [Fact]
    public void AUnitHeadingEast_HasItsFrontageAcrossTheHeading_AndItsArrowOffTheFront()
    {
        Span<Vector2> c = stackalloc Vector2[4];
        UnitFootprint.Corners(new Vector2(100, 50), width: 200, depth: 80, headingDeg: 90, c);
        // heading east: depth runs along x (±40), the frontage along y (±100); rear-left is west-north, then CCW
        Near(new Vector2(60, 150), c[0]); Near(new Vector2(60, -50), c[1]); Near(new Vector2(140, -50), c[2]); Near(new Vector2(140, 150), c[3]);
        float area2 = 0; for (int i = 0; i < 4; i++) { var a = c[i]; var b = c[(i + 1) % 4]; area2 += (a.X * b.Y) - (b.X * a.Y); }
        Assert.True(area2 > 0, "counter-clockwise");
        Assert.Equal(2f * 200 * 80, area2, 1);
        var (b0, tip) = UnitFootprint.FrontArrow(new Vector2(100, 50), 80, 90, 25);
        Near(new Vector2(140, 50), b0); Near(new Vector2(165, 50), tip);
        // north is +y
        Near(new Vector2(0, 1), UnitFootprint.Forward(0));
    }

    [Fact]
    public void AMarchColumn_IsTheLastDepthMetresOfItsPath_BendsIncluded()
    {
        // an L-shaped road: 100 m east, then 100 m north
        ReadOnlySpan<Vector2> road = stackalloc Vector2[] { new(0, 0), new(100, 0), new(100, 100) };
        Span<Vector2> pts = stackalloc Vector2[8];
        int n = UnitFootprint.ColumnSegment(road, headAlong: 150, depth: 80, pts);
        Assert.Equal(3, n);                 // the tail cut, the corner, the head
        Near(new Vector2(70, 0), pts[0]);   // tail 80 m behind the head, round the bend
        Near(new Vector2(100, 0), pts[1]);
        Near(new Vector2(100, 50), pts[2]); // head 150 m along
        // a column longer than the road travelled is clamped to the road's start
        n = UnitFootprint.ColumnSegment(road, headAlong: 30, depth: 80, pts);
        Assert.Equal(2, n); Near(new Vector2(0, 0), pts[0]); Near(new Vector2(30, 0), pts[1]);
        // allocation-free
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { UnitFootprint.ColumnSegment(road, 150, 80, pts); UnitFootprint.Corners(default, 10, 10, i, pts); }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
