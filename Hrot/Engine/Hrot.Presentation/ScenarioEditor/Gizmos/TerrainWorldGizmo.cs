using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Terrain;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐⭐ <b>The 2D map layer that draws the terrain WORLD</b> (docs/DESIGN_Terrain_World.md §7.1 W2/W3) — ground
/// cover, floor slabs and ramps, then buildings and walls, filled and shaded by height, outlined, and labelled.
///
/// <para>⭐ A GLOBAL stateless projector on the shared gizmo seam: it reads the <see cref="TerrainWorld"/>
/// singleton every ECS node holds, so every host with a map — editor, CGF, SimHost, IG — draws the same world
/// from this one class (🔒 user: <i>"Cgf must render the map as well"</i>).</para>
/// <para>⭐ Fills use <see cref="IDebugDrawBuilder.DrawFilledTriangle"/> over the triangles the parser computed
/// ONCE; the gizmo does no geometry of its own.</para>
/// <para>⚠ Drawn on <see cref="TerrainLayer"/> 0, the lowest, and emitted before entity gizmos sort above it
/// by the renderer's stable painter's order.</para>
/// </summary>
[GizmoProjector]
public sealed class TerrainWorldGizmo : IGlobalStatelessGizmo
{
    /// <summary>The debug layer terrain draws on — the bottom of the painter's order.</summary>
    public const byte TerrainLayer = 0;

    private static readonly Rgba32 ForestFill  = new(40, 110, 50, 110);
    private static readonly Rgba32 WaterFill   = new(50, 110, 200, 150);
    private static readonly Rgba32 SlabFill    = new(90, 150, 220, 90);
    private static readonly Rgba32 RampFill    = new(130, 180, 230, 110);
    private static readonly Rgba32 WallFill    = new(70, 70, 70, 230);
    private static readonly Rgba32 Outline     = new(30, 30, 30, 220);
    private static readonly Rgba32 SlabOutline = new(40, 90, 170, 220);
    private static readonly Rgba32 LabelColor  = new(20, 20, 20, 255);

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.HasSingletonManaged<TerrainWorld>()) return;
        var world = repo.GetSingletonManaged<TerrainWorld>();
        if (world == null) return;

        foreach (var s in world.Surfaces)
        {
            if (s.Type == TerrainSurfaceType.Open) continue;
            var fill = s.Type switch
            {
                TerrainSurfaceType.Forest => ForestFill,
                _                         => WaterFill,
            };
            FillPolygon(draw, s.Polygon, s.Triangles, fill);
        }

        foreach (var w in world.Walkables)
        {
            var plan = new Vector2[w.Vertices.Length];
            for (int i = 0; i < plan.Length; i++) plan[i] = new Vector2(w.Vertices[i].X, w.Vertices[i].Y);
            FillPolygon(draw, plan, w.Triangles, w.Kind == TerrainWalkableKind.Ramp ? RampFill : SlabFill);
            Outline2(draw, plan, SlabOutline, w.Kind == TerrainWalkableKind.Ramp ? LineStyle.Dashed : LineStyle.Solid);
            var c = Centroid(plan);
            draw.DrawText(c.X, c.Y, new Fdp.Core.FixedString32(
                w.Kind == TerrainWalkableKind.Ramp ? $"ramp {w.MinZ:0.#}-{w.MaxZ:0.#}m" : $"deck {w.MaxZ:0.#}m"),
                LabelColor, layer: TerrainLayer);
        }

        foreach (var p in world.Prisms)
        {
            // ⭐ Stage 1 — a wall/fence piece is coloured by its MATERIAL (the "materials" layer of
            //   DESIGN_Terrain_Combat_Tuning.md §5); fences are dashed. Openings are gaps because a panel's pieces leave them.
            var fill = p.Kind == TerrainPrismKind.Wall ? MaterialColor(p.Material?.Name) : HeightColor(p.Height);
            FillPolygon(draw, p.Footprint, p.Triangles, fill);
            bool fence = p.Material?.Name?.StartsWith("fence", StringComparison.Ordinal) == true || p.Material?.Name == "hedge";
            Outline2(draw, p.Footprint, Outline, fence ? LineStyle.Dashed : LineStyle.Solid);
            if (p.Kind == TerrainPrismKind.Building)
            {
                var c = Centroid(p.Footprint);
                string label = p.Floors > 0 ? $"{p.Height:0.#}m {p.Floors}F" : $"{p.Height:0.#}m";
                draw.DrawText(c.X, c.Y, new Fdp.Core.FixedString32(label), LabelColor, layer: TerrainLayer);
            }
        }

        // ⭐ Stage 1 — an enterable building is named once, with its storeys (its walls and floors are drawn above).
        foreach (var b in world.Buildings)
        {
            if (b.Solid) continue;
            var c = Centroid(b.Footprint);
            draw.DrawText(c.X, c.Y, new Fdp.Core.FixedString32($"{b.Label} {b.Storeys}F"), LabelColor, layer: TerrainLayer);
        }
    }

    /// <summary>⭐ Stage 1 — wall/fence fill by material (unknown or none = the old wall grey).</summary>
    public static Rgba32 MaterialColor(string? material) => material switch
    {
        "brick"             => new Rgba32(160, 70, 50, 230),
        "fence-wood"        => new Rgba32(170, 130, 80, 230),
        "fence-chainlink"   => new Rgba32(170, 170, 170, 160),
        "fence-metal-sheet" => new Rgba32(90, 110, 140, 230),
        "hedge"             => new Rgba32(50, 130, 60, 200),
        "glass"             => new Rgba32(140, 210, 230, 160),
        _                   => WallFill,
    };

    /// <summary>Building fill shaded by height: low = sand, mid = orange, tall = brick, tower = dark red.</summary>
    public static Rgba32 HeightColor(float height) => height switch
    {
        < 5f  => new Rgba32(225, 205, 160, 220),
        < 15f => new Rgba32(225, 160, 90, 220),
        < 30f => new Rgba32(180, 95, 60, 220),
        _     => new Rgba32(120, 40, 40, 230),
    };

    private static void FillPolygon(IDebugDrawBuilder draw, IReadOnlyList<Vector2> poly, int[] tris, Rgba32 fill)
    {
        for (int t = 0; t + 2 < tris.Length; t += 3)
            draw.DrawFilledTriangle(poly[tris[t]], poly[tris[t + 1]], poly[tris[t + 2]], fill, TerrainLayer);
    }

    private static void Outline2(IDebugDrawBuilder draw, IReadOnlyList<Vector2> poly, Rgba32 color, LineStyle style)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            draw.DrawLine(new Vector3(a, 0f), new Vector3(b, 0f), color, 1.5f, SizeMode.ScreenPixels,
                layer: TerrainLayer, style: style);
        }
    }

    private static Vector2 Centroid(IReadOnlyList<Vector2> poly)
    {
        var sum = Vector2.Zero;
        foreach (var p in poly) sum += p;
        return poly.Count == 0 ? sum : sum / poly.Count;
    }
}
