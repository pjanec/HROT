using System;
using System.Numerics;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.Canvas;

/// <summary>
/// CE-1000 — the one link geometry (docs/blueprints/DESIGN_Hsm_Canvas_Authoring.md §6).
/// </summary>
public sealed class LinkPathBuilderTests
{
    private static readonly RectF A = new(new Vector2(0, 0), new Vector2(100, 40));
    private static readonly RectF B = new(new Vector2(300, 100), new Vector2(100, 40));

    private static bool OnBorder(RectF r, Vector2 p, float eps = 0.01f)
    {
        bool insideX = p.X >= r.Min.X - eps && p.X <= r.Max.X + eps;
        bool insideY = p.Y >= r.Min.Y - eps && p.Y <= r.Max.Y + eps;
        bool onX = MathF.Abs(p.X - r.Min.X) <= eps || MathF.Abs(p.X - r.Max.X) <= eps;
        bool onY = MathF.Abs(p.Y - r.Min.Y) <= eps || MathF.Abs(p.Y - r.Max.Y) <= eps;
        return insideX && insideY && (onX || onY);
    }

    [Fact]
    public void NodeToNode_EndpointsLieOnTheTwoBorders()
    {
        var path = LinkPathBuilder.NodeToNode(A, B);
        Assert.True(OnBorder(A, path.Start), $"start {path.Start} not on A's border");
        Assert.True(OnBorder(B, path.End), $"end {path.End} not on B's border");
        Assert.True(path.HasArrowhead);
        Assert.False(path.IsSelfLoop);
    }

    [Fact]
    public void NodeToNode_ArrowheadPointsIntoTheTarget()
    {
        var path = LinkPathBuilder.NodeToNode(A, B);
        var dir = Vector2.Normalize(path.Tangent(1f));
        var toCentre = Vector2.Normalize(B.Center - path.End);
        Assert.True(Vector2.Dot(dir, toCentre) > 0.3f, "the end tangent must head into the target state");
    }

    [Fact]
    public void NodeToNode_APair_BendsToOppositeSides_AndNeverTouches()
    {
        var ab = LinkPathBuilder.NodeToNode(A, B);
        var ba = LinkPathBuilder.NodeToNode(B, A);
        float closest = float.MaxValue;
        for (int i = 0; i <= 40; i++)
            closest = MathF.Min(closest, ba.DistanceTo(ab.Point(i / 40f)));
        Assert.True(closest > 6f, $"A→B and B→A come within {closest:F1}px of each other");
    }

    [Fact]
    public void NodeToNode_ParallelLinks_FanOutByLane()
    {
        var lane0 = LinkPathBuilder.NodeToNode(A, B, lane: 0);
        var lane1 = LinkPathBuilder.NodeToNode(A, B, lane: 1);
        Assert.True(Vector2.Distance(lane0.Point(0.5f), lane1.Point(0.5f)) > 10f);
    }

    [Fact]
    public void NodeToNode_IntoANestedChild_StartsOnTheContainerEdge()
    {
        var container = new RectF(new Vector2(0, 0), new Vector2(400, 300));
        var child = new RectF(new Vector2(150, 200), new Vector2(100, 40));
        var path = LinkPathBuilder.NodeToNode(container, child);
        Assert.True(OnBorder(container, path.Start));
        Assert.True(OnBorder(child, path.End));
    }

    [Fact]
    public void SameRect_IsASelfLoopOnTheTopRightCorner()
    {
        var path = LinkPathBuilder.NodeToNode(A, A);
        Assert.True(path.IsSelfLoop);
        Assert.True(OnBorder(A, path.Start) && OnBorder(A, path.End));
        Assert.True(path.Point(0.5f).Y < A.Min.Y || path.Point(0.5f).X > A.Max.X, "the loop must bulge outside the node");
    }

    [Fact]
    public void PinWire_KeepsTheBlueprintTangents()
    {
        var a = new Vector2(10, 10);
        var b = new Vector2(200, 80);
        var path = LinkPathBuilder.PinWire(a, b, null, PinOrientation.Horizontal, 1f);
        var (c1, c2) = LinkPathBuilder.PinWireTangents(a, b, PinOrientation.Horizontal, 1f);
        Assert.Single(path.Segments);
        Assert.Equal(new BezierSegment(a, c1, c2, b), path.Segments[0]);
        Assert.False(path.HasArrowhead);
    }

    [Fact]
    public void PinWire_WithWaypoints_HasOneSegmentPerHop()
    {
        var path = LinkPathBuilder.PinWire(Vector2.Zero, new Vector2(300, 0),
            new[] { new Vector2(100, 50), new Vector2(200, -50) });
        Assert.Equal(3, path.Segments.Count);
        Assert.Equal(new Vector2(100, 50), path.Segments[0].P3);
    }

    [Fact]
    public void DistanceTo_IsSmallOnTheCurve_AndLargeAway()
    {
        var path = LinkPathBuilder.NodeToNode(A, B);
        Assert.True(path.DistanceTo(path.Point(0.37f)) < 2f);
        Assert.True(path.DistanceTo(new Vector2(-500, -500)) > 100f);
    }
}
