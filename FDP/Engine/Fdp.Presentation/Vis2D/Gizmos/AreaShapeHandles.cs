using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>What a handle of an <see cref="AreaLink"/> shape edits.</summary>
public enum AreaHandleKind
{
    /// <summary>The shape's centre — drag moves it.</summary>
    Center,
    /// <summary>A circle's radius — a handle on the rim.</summary>
    Radius,
    /// <summary>A rectangle's width — a handle on the edge across the heading.</summary>
    Width,
    /// <summary>A rectangle's length — a handle on the front edge.</summary>
    Length,
    /// <summary>A rectangle's heading — a knob beyond the front edge.</summary>
    Heading,
}

/// <summary>One handle: what it edits and where it is (world X, Y).</summary>
public readonly record struct AreaHandle(AreaHandleKind Kind, Vector2 Position);

/// <summary>
/// ⭐ CE-4206 R2 (DESIGN_Map_Picking_Unification §6.3 F6) — the PURE geometry of <see cref="AreaShapeGizmo"/>: where an <see cref="AreaLink"/> shape's handles
/// are, which one a cursor hits, and the new shape a drag produces. No input, no drawing — the rails call it directly.
/// Frame: world X east, Y north; a heading is a COMPASS bearing (0 = +Y, 90 = +X) exactly as <see cref="AreaLink.Contains"/> reads it, so
/// <c>along = (sin h, cos h)</c> and <c>across = (cos h, −sin h)</c>.
/// </summary>
public static class AreaShapeHandles
{
    /// <summary>The smallest radius / width / length a drag may produce (m).</summary>
    public const float MinExtent = 1f;

    /// <summary>The circle a new area starts as (m) — "a new area with no start value defaults to a circle of radius 50 m at the clicked point".</summary>
    public const float DefaultRadius = 50f;

    private static Vector2 Centre(AreaLink a) => new(a.Center.X, a.Center.Y);

    /// <summary>The unit vector along a rectangle's length for <paramref name="headingDegrees"/> (compass: 0 = north).</summary>
    public static Vector2 Along(float headingDegrees)
    {
        float rad = headingDegrees * (MathF.PI / 180f);
        return new Vector2(MathF.Sin(rad), MathF.Cos(rad));
    }

    /// <summary>The unit vector across a rectangle (to its right when facing along the heading).</summary>
    public static Vector2 Across(float headingDegrees)
    {
        var along = Along(headingDegrees);
        return new Vector2(along.Y, -along.X);
    }

    /// <summary>How far beyond the front edge the heading knob sits (m): a quarter of the length, at least 3 m.</summary>
    public static float KnobOffset(AreaLink rect) => MathF.Max(3f, rect.Size.Y * 0.25f);

    /// <summary>The compass bearing (degrees, [0, 360)) from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static float BearingTo(Vector2 from, Vector2 to)
    {
        var d = to - from;
        float deg = MathF.Atan2(d.X, d.Y) * (180f / MathF.PI);   // compass: atan2(east, north)
        return ((deg % 360f) + 360f) % 360f;
    }

    /// <summary>A new circle of <see cref="DefaultRadius"/> at the clicked point.</summary>
    public static AreaLink NewAt(Vector2 clicked) => AreaLink.Circle(new WorldPoint(clicked.X, clicked.Y, 0f), DefaultRadius);

    /// <summary>The handles of a SHAPE (none for an area entity or none).</summary>
    public static IReadOnlyList<AreaHandle> HandlesOf(AreaLink area)
    {
        if (!area.IsShape) return Array.Empty<AreaHandle>();
        var c = Centre(area);
        var list = new List<AreaHandle> { new(AreaHandleKind.Center, c) };
        if (area.Spec.Form == RefForm.Circle)
        {
            list.Add(new AreaHandle(AreaHandleKind.Radius, c + new Vector2(area.Size.X, 0f)));
            return list;
        }
        var along = Along(area.Heading.Degrees);
        var across = Across(area.Heading.Degrees);
        list.Add(new AreaHandle(AreaHandleKind.Width,   c + across * (area.Size.X * 0.5f)));
        list.Add(new AreaHandle(AreaHandleKind.Length,  c + along * (area.Size.Y * 0.5f)));
        list.Add(new AreaHandle(AreaHandleKind.Heading, c + along * (area.Size.Y * 0.5f + KnobOffset(area))));
        return list;
    }

    /// <summary>The distance within which a cursor grabs a handle (m): a tenth of the shape's size, between 2 and 40 m.</summary>
    public static float HitRadiusFor(AreaLink area)
    {
        float extent = area.Spec.Form == RefForm.Circle ? area.Size.X : MathF.Max(area.Size.X, area.Size.Y) * 0.5f;
        return Math.Clamp(extent * 0.1f, 2f, 40f);
    }

    /// <summary>The handle under <paramref name="position"/> within <paramref name="hitRadius"/>, the nearest first with the knob / edge handles winning
    /// over the centre they may overlap; <c>null</c> when none.</summary>
    public static AreaHandle? HitHandle(AreaLink area, Vector2 position, float hitRadius)
    {
        AreaHandle? best = null;
        float bestScore = float.MaxValue;
        foreach (var h in HandlesOf(area))
        {
            float d = Vector2.Distance(h.Position, position);
            if (d > hitRadius) continue;
            float score = h.Kind == AreaHandleKind.Center ? d + hitRadius : d;   // a centre grab only when no edge handle is in reach
            if (score < bestScore) { bestScore = score; best = h; }
        }
        return best;
    }

    /// <summary>The shape after dragging <paramref name="handle"/> to <paramref name="world"/>.</summary>
    public static AreaLink Drag(AreaLink area, AreaHandleKind handle, Vector2 world)
    {
        if (!area.IsShape) return area;
        var c = Centre(area);
        switch (handle)
        {
            case AreaHandleKind.Center:
                return area.WithPlacement(new WorldPoint(world.X, world.Y, area.Center.Z), area.Heading);
            case AreaHandleKind.Radius when area.Spec.Form == RefForm.Circle:
                return AreaLink.Circle(area.Center, MathF.Max(MinExtent, Vector2.Distance(c, world)));
            case AreaHandleKind.Width when area.Spec.Form == RefForm.Rectangle:
                return AreaLink.Rectangle(area.Center, MathF.Max(MinExtent, 2f * MathF.Abs(Vector2.Dot(world - c, Across(area.Heading.Degrees)))),
                                          area.Size.Y, area.Heading);
            case AreaHandleKind.Length when area.Spec.Form == RefForm.Rectangle:
                return AreaLink.Rectangle(area.Center, area.Size.X,
                                          MathF.Max(MinExtent, 2f * MathF.Abs(Vector2.Dot(world - c, Along(area.Heading.Degrees)))), area.Heading);
            case AreaHandleKind.Heading when area.Spec.Form == RefForm.Rectangle:
                return area.WithPlacement(area.Center, new Bearing(BearingTo(c, world)));
            default:
                return area;
        }
    }

    /// <summary>The outline of a shape as a closed polyline (circle: 48 segments; rectangle: 4 corners), for drawing.</summary>
    public static IReadOnlyList<Vector2> Outline(AreaLink area)
    {
        if (!area.IsShape) return Array.Empty<Vector2>();
        var c = Centre(area);
        var pts = new List<Vector2>();
        if (area.Spec.Form == RefForm.Circle)
        {
            const int n = 48;
            for (int i = 0; i < n; i++)
            {
                float a = i * (2f * MathF.PI / n);
                pts.Add(c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * area.Size.X);
            }
            return pts;
        }
        var along = Along(area.Heading.Degrees) * (area.Size.Y * 0.5f);
        var across = Across(area.Heading.Degrees) * (area.Size.X * 0.5f);
        pts.Add(c + along + across); pts.Add(c + along - across); pts.Add(c - along - across); pts.Add(c - along + across);
        return pts;
    }
}
