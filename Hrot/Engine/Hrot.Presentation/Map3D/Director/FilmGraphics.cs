using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>⭐ <c>CE-1305</c> P6 — TE-9 (docs/DESIGN_Director_Timeline_Editor.md §2, §11t G1): the kinds of film graphic — a ring on a unit, an arrow, a route
/// line, an area, a label.</summary>
public enum GraphicKind : byte { Ring, Arrow, Route, Area, Label }

/// <summary>
/// ⭐⭐ <c>CE-1305</c> P6 — TE-9 / R-4070 (§5, §11t G1): one film graphic — FILM-ONLY (stored in the take, drawn by the film, never written to a recording or a
/// scenario). On screen from <see cref="At"/> until <see cref="Until"/> (SOURCE sim seconds, a time or an anchor + offset, like a subtitle), fading in and
/// out over <see cref="Fade"/>. WHERE is the world: a unit by name (<see cref="Who"/>; an arrow's head <see cref="Toward"/>) — read every frame, so it
/// moves with the unit — or fixed <see cref="Points"/> (a route, an area, a label or an arrow on the ground).
/// </summary>
public sealed record FilmGraphic
{
    public AtExpr At { get; init; }
    public AtExpr Until { get; init; }
    public GraphicKind Kind { get; init; } = GraphicKind.Ring;
    /// <summary>The unit a ring circles, a label names, an arrow starts from.</summary>
    public string? Who { get; init; }
    /// <summary>The unit an arrow points at.</summary>
    public string? Toward { get; init; }
    /// <summary>World points (HROT: x east, y north, z up): a route's and an area's corners; a label's place, an arrow's tail / head when no unit is named.</summary>
    public IReadOnlyList<Vector3> Points { get; init; } = Array.Empty<Vector3>();
    /// <summary>A label's text; on a ring, the caption above it.</summary>
    public string? Text { get; init; }
    /// <summary>A ring's radius in metres.</summary>
    public float Radius { get; init; } = DefaultRadius;
    /// <summary>0xRRGGBB.</summary>
    public uint Colour { get; init; } = DefaultColour;
    public double Fade { get; init; }

    public const float DefaultRadius = 8f;
    public const uint DefaultColour = 0xFFD23C;
}

/// <summary>⭐ CE-1305 — a graphic on screen now: its kind, its WORLD points resolved this frame (a unit's position read now), text, radius, colour, opacity.</summary>
public sealed record GraphicFrame(GraphicKind Kind, IReadOnlyList<Vector3> Points, float Radius, string? Text, uint Colour, float Alpha);

/// <summary>
/// ⭐ <c>CE-1305</c> — what a captured frame burns into the picture besides the scene: the subtitle (CE-1370) and the film graphics. ONE value handed to
/// <see cref="IFrameSink"/>, so a still, a movie frame and the screen draw the same things.
/// </summary>
public sealed record FilmOverlay(SubtitleFrame? Subtitle, IReadOnlyList<GraphicFrame> Graphics)
{
    public static readonly FilmOverlay None = new(null, Array.Empty<GraphicFrame>());
}

/// <summary>⭐ CE-1305 — the ONE fade rule of a timed overlay (a subtitle, a graphic): full between, rising over <paramref name="fade"/> after its start, falling before its end.</summary>
public static class FilmFade
{
    public static float Alpha(double from, double until, double fade, double t)
    {
        if (!(fade > 0d)) return 1f;
        return (float)Math.Clamp(Math.Min(Math.Min((t - from) / fade, (until - t) / fade), 1d), 0d, 1d);
    }
}

/// <summary>
/// ⭐⭐ <c>CE-1305</c> P6 (§11t G2) — the take's graphics at one source second: every graphic whose [at, until) holds it, in list order (later drawn on top),
/// with its world points resolved through the run's units (<see cref="IShotWorld"/> — the one name lookup the camera uses). A graphic whose unit is not
/// here this frame is not drawn.
/// </summary>
public static class FilmGraphics
{
    /// <summary>How far above a unit's position its label and an arrow's ends sit (metres).</summary>
    public const float UnitLift = 1.5f;

    public static IReadOnlyList<GraphicFrame> At(IReadOnlyList<FilmGraphic> graphics, Func<string, double?> anchorTime, double sourceSim, IShotWorld? world)
    {
        if (graphics.Count == 0) return Array.Empty<GraphicFrame>();
        var list = new List<GraphicFrame>();
        foreach (var g in graphics)
        {
            if (g.At.Resolve(anchorTime) is not { } from || g.Until.Resolve(anchorTime) is not { } until) continue;
            if (sourceSim < from - 1e-9 || sourceSim >= until - 1e-9) continue;
            if (Points(g, world) is not { } points) continue;
            list.Add(new GraphicFrame(g.Kind, points, g.Radius, g.Text, g.Colour, FilmFade.Alpha(from, until, g.Fade, sourceSim)));
        }
        return list;
    }

    /// <summary>The graphic's world points now; null when it cannot be drawn (a named unit not here, too few points).</summary>
    public static IReadOnlyList<Vector3>? Points(FilmGraphic g, IShotWorld? world)
    {
        Vector3? Unit(string? who) => who != null && world?.Resolve(who) is { } id ? world.PositionOf(id) : null;
        switch (g.Kind)
        {
            case GraphicKind.Ring:
            case GraphicKind.Label:
                if (g.Who != null) return Unit(g.Who) is { } at ? new[] { g.Kind == GraphicKind.Label ? at + new Vector3(0, 0, UnitLift) : at } : null;
                return g.Points.Count > 0 ? new[] { g.Points[0] } : null;
            case GraphicKind.Arrow:
            {
                Vector3? tail = g.Who != null ? Unit(g.Who) : g.Points.Count > 0 ? g.Points[0] : null;
                Vector3? head = g.Toward != null ? Unit(g.Toward) : g.Points.Count > 1 ? g.Points[^1] : null;
                if (tail is not { } t || head is not { } h) return null;
                var lift = new Vector3(0, 0, UnitLift);
                return new[] { g.Who != null ? t + lift : t, g.Toward != null ? h + lift : h };
            }
            case GraphicKind.Route: return g.Points.Count >= 2 ? g.Points : null;
            case GraphicKind.Area: return g.Points.Count >= 3 ? g.Points : null;
            default: return null;
        }
    }

    /// <summary>The track's label of a graphic: its kind, and what it is on.</summary>
    public static string Describe(FilmGraphic g)
    {
        string kind = g.Kind.ToString().ToLowerInvariant();
        string on = g.Kind == GraphicKind.Arrow && g.Who != null && g.Toward != null ? $" {g.Who} -> {g.Toward}"
                  : g.Who != null ? " " + g.Who
                  : g.Points.Count > 0 ? $" ({g.Points.Count} pt)" : "";
        return kind + on + (string.IsNullOrEmpty(g.Text) ? "" : " \"" + g.Text + "\"");
    }
}

/// <summary>⭐ CE-1305 — one graphic laid out on a picture, in PIXELS: a line (open or closed), a filled area (triangles), an arrow head, a text.</summary>
public sealed record GraphicPlot(
    IReadOnlyList<Vector2> Line, bool Closed, IReadOnlyList<Vector2> Fill, IReadOnlyList<Vector2> Head,
    Vector2? TextAt, string? Text, float TextSize, float Thickness, uint Colour, byte Alpha);

/// <summary>
/// ⭐⭐ <c>CE-1305</c> P6 (§11t G3) — the ONE layout of a film graphic on a picture (the <see cref="SubtitleBand"/> pattern, CE-1370): the world points
/// projected through the camera that draws the picture, line widths and text sizes from the picture's height. The map's screen pass and a captured frame
/// both paint THIS (<c>FilmText.Graphics</c>), so a still shows what the screen showed. Pure: a rail lays a graphic out with a stated projection.
/// </summary>
public static class GraphicLayout
{
    /// <summary>Segments of a ring's circle.</summary>
    public const int RingSegments = 48;

    /// <param name="project">World → pixel; null when the point cannot be shown (behind the camera).</param>
    /// <returns>Null when nothing of it is on the picture.</returns>
    public static GraphicPlot? Of(GraphicFrame g, Func<Vector3, Vector2?> project, float frameHeight)
    {
        float thick = MathF.Max(2f, frameHeight / 270f);
        float textSize = MathF.Max(12f, SubtitleBand.TextSizeFor(frameHeight) * 0.8f);
        byte alpha = (byte)Math.Round(Math.Clamp(g.Alpha, 0f, 1f) * 255f);
        var none = Array.Empty<Vector2>();
        List<Vector2>? Project(IEnumerable<Vector3> points)
        {
            var list = new List<Vector2>();
            foreach (var p in points) { if (project(p) is not { } s) return null; list.Add(s); }
            return list;
        }
        switch (g.Kind)
        {
            case GraphicKind.Ring:
            {
                var c = g.Points[0];
                var ring = Project(Enumerable.Range(0, RingSegments).Select(i =>
                {
                    float a = i * 2f * MathF.PI / RingSegments;
                    return c + new Vector3(MathF.Cos(a) * g.Radius, MathF.Sin(a) * g.Radius, 0f);
                }));
                if (ring is null) return null;
                Vector2? text = string.IsNullOrEmpty(g.Text) ? null : new Vector2(ring.Average(p => p.X), ring.Min(p => p.Y) - textSize * 1.2f);
                return new GraphicPlot(ring, true, none, none, text, g.Text, textSize, thick, g.Colour, alpha);
            }
            case GraphicKind.Label:
            {
                if (project(g.Points[0]) is not { } at) return null;
                return new GraphicPlot(none, false, none, none, at, g.Text ?? "", textSize, thick, g.Colour, alpha);
            }
            case GraphicKind.Arrow:
            case GraphicKind.Route:
            {
                var line = Project(g.Points);
                if (line is null || line.Count < 2) return null;
                return new GraphicPlot(line, false, none, Head(line[^2], line[^1], thick * 5f), null, null, textSize, thick, g.Colour, alpha);
            }
            case GraphicKind.Area:
            {
                var poly = Project(g.Points);
                if (poly is null || poly.Count < 3) return null;
                var centre = new Vector2(poly.Average(p => p.X), poly.Average(p => p.Y));
                var fill = new List<Vector2>(poly.Count * 3);
                for (int i = 0; i < poly.Count; i++) { fill.Add(centre); fill.Add(poly[i]); fill.Add(poly[(i + 1) % poly.Count]); }   // a fan from the centre
                Vector2? text = string.IsNullOrEmpty(g.Text) ? null : centre;
                return new GraphicPlot(poly, true, fill, none, text, g.Text, textSize, thick, g.Colour, alpha);
            }
            default: return null;
        }
    }

    /// <summary>An arrow head at <paramref name="tip"/>, pointing from <paramref name="from"/>: a triangle <paramref name="size"/> pixels long.</summary>
    public static IReadOnlyList<Vector2> Head(Vector2 from, Vector2 tip, float size)
    {
        var d = tip - from;
        if (!(d.Length() > 1e-3f)) return Array.Empty<Vector2>();
        d = Vector2.Normalize(d);
        var n = new Vector2(-d.Y, d.X);
        var back = tip - d * size;
        return new[] { tip, back + n * size * 0.5f, back - n * size * 0.5f };
    }
}
