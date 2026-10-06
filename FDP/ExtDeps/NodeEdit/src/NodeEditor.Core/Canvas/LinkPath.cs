using System;
using System.Collections.Generic;
using System.Numerics;

namespace NodeEditor.Core.Canvas;

/// <summary>One cubic Bezier segment of a link, in whatever space it was built in (the canvas uses screen space).</summary>
public readonly record struct BezierSegment(Vector2 P0, Vector2 C1, Vector2 C2, Vector2 P3)
{
    public Vector2 Point(float t)
    {
        float u = 1f - t;
        return u * u * u * P0
             + 3f * u * u * t * C1
             + 3f * u * t * t * C2
             + t * t * t * P3;
    }

    public Vector2 Tangent(float t)
    {
        float u = 1f - t;
        return 3f * u * u * (C1 - P0)
             + 6f * u * t * (C2 - C1)
             + 3f * t * t * (P3 - C2);
    }
}

/// <summary>
/// The drawn shape of one link: one or more cubic segments (more than one when the link has reroute waypoints).
/// <para>⭐ CE-1000: there is ONE producer — <see cref="LinkPathBuilder"/>, called once per frame by the canvas
/// layout — and every consumer (wire renderer, hit-tester, custom renderers via
/// <c>ICanvasRenderContext.TryGetLinkScreenPath</c>) reads the same instance, so a label, an arrowhead and the
/// clickable band can never disagree with the drawn curve. 📄 docs/blueprints/DESIGN_Hsm_Canvas_Authoring.md §3.</para>
/// </summary>
public sealed class LinkPath
{
    public LinkPath(IReadOnlyList<BezierSegment> segments, bool isSelfLoop = false, bool hasArrowhead = false)
    {
        if (segments is null || segments.Count == 0)
            throw new ArgumentException("A link path needs at least one segment.", nameof(segments));
        Segments = segments;
        IsSelfLoop = isSelfLoop;
        HasArrowhead = hasArrowhead;
    }

    public IReadOnlyList<BezierSegment> Segments { get; }

    /// <summary>True when source and target are the same node (drawn as a loop on the node's corner).</summary>
    public bool IsSelfLoop { get; }

    /// <summary>True when the path should be drawn with an arrowhead at <see cref="End"/> (node-to-node routing).</summary>
    public bool HasArrowhead { get; }

    public Vector2 Start => Segments[0].P0;
    public Vector2 End => Segments[Segments.Count - 1].P3;

    /// <summary>A point along the whole path, <paramref name="t"/> in [0,1], each segment getting an equal share.</summary>
    public Vector2 Point(float t)
    {
        var (seg, local) = Locate(t);
        return Segments[seg].Point(local);
    }

    /// <summary>The (unnormalised) direction of travel at <paramref name="t"/>.</summary>
    public Vector2 Tangent(float t)
    {
        var (seg, local) = Locate(t);
        return Segments[seg].Tangent(local);
    }

    /// <summary>Shortest sampled distance from <paramref name="p"/> to the path.</summary>
    public float DistanceTo(Vector2 p, int samplesPerSegment = 24)
    {
        float best = float.MaxValue;
        foreach (var s in Segments)
        {
            var prev = s.P0;
            for (int i = 1; i <= samplesPerSegment; i++)
            {
                var cur = s.Point(i / (float)samplesPerSegment);
                best = MathF.Min(best, DistanceToSegment(p, prev, cur));
                prev = cur;
            }
        }
        return best;
    }

    private (int seg, float local) Locate(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        int n = Segments.Count;
        float scaled = t * n;
        int seg = Math.Min((int)scaled, n - 1);
        return (seg, scaled - seg);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-6f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}
