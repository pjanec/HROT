using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Terrain;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐⭐ <b>The 2-D map's PLAN of the terrain WORLD</b> (docs/DESIGN_Terrain_World.md §7.1 W2/W3, §10) — ground cover, floor
/// slabs and ramps, then buildings and walls, filled and shaded by height, outlined, and labelled.
///
/// <para>⭐⭐ <c>CE-1227</c> (§10, R-1013) — NO LONGER A PROJECTOR. It was the <c>TerrainWorldGizmo</c>, emitting the whole world into
/// the map's gizmo buffer every frame: 📐 73 188 primitives on demo-city against a 16 384 buffer, so the entity symbols and the
/// marquee were dropped. Now <see cref="Hrot.UI.Common.Map2D.TerrainLayer2D"/> calls <see cref="Emit"/> ONCE per loaded terrain into a
/// private buffer, keeps the fills as one mesh and draws the outlines and labels in view. The look is unchanged.</para>
/// <para>⭐ Fills use <see cref="IDebugDrawBuilder.DrawFilledTriangle"/> over the triangles the parser computed ONCE; the plan does
/// no geometry of its own.</para>
/// </summary>
public sealed class TerrainPlan2D
{
    /// <summary>⭐ CE-1227 — the layer every primitive of the plan carries: the "Terrain (2-D)" checkbox (§5e FL-1).</summary>
    public const byte TerrainLayer = DebugTraceLayers.Terrain2D;

    private static readonly Rgba32 ForestFill  = new(40, 110, 50, 110);
    private static readonly Rgba32 WaterFill   = new(50, 110, 200, 150);
    private static readonly Rgba32 SlabFill    = new(90, 150, 220, 90);
    private static readonly Rgba32 RampFill    = new(130, 180, 230, 110);
    // ⭐ CE-1021 — a plain wall is a light concrete grey: the old (70,70,70) under a near-black outline read as black on the map.
    private static readonly Rgba32 WallFill    = new(150, 150, 145, 235);
    private static readonly Rgba32 Outline     = new(30, 30, 30, 220);
    private static readonly Rgba32 SlabOutline = new(40, 90, 170, 220);
    private static readonly Rgba32 LabelColor  = new(20, 20, 20, 255);

    /// <summary>⭐ <c>CE-1420</c> (docs/DESIGN_Director_Timeline_Editor.md §11t "CE-1381 sample") — how much of the 3-D ground colour the 2-D ground keeps (0 = black,
    /// 1 = the 3-D ground): dark enough that the grid, the roads, the units' symbols and the graphics keep their contrast. A SAMPLE value awaiting the user's pick.</summary>
    public const float DefaultGroundFillFactor = 0.35f;

    /// <summary>⭐ CE-1420 — the 2-D open ground: <paramref name="ground3D"/> (the 3-D map's ground colour — ONE ground colour for both maps) darkened to
    /// <paramref name="factor"/> of its brightness, opaque.</summary>
    public static Rgba32 MutedGround(Rgba32 ground3D, float factor = DefaultGroundFillFactor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        return new Rgba32((byte)(ground3D.R * factor + 0.5f), (byte)(ground3D.G * factor + 0.5f), (byte)(ground3D.B * factor + 0.5f), 255);
    }

    /// <summary>Emits the plan of the terrain <paramref name="view"/> holds; nothing when it holds none.</summary>
    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.HasSingletonManaged<TerrainWorld>()) return;
        var world = repo.GetSingletonManaged<TerrainWorld>();
        if (world != null) Emit(world, draw);
    }

    /// <summary>⭐ CE-1227 — emits <paramref name="world"/>'s plan: fills first (painter's order), then outlines and labels.</summary>
    public static void Emit(TerrainWorld world, IDebugDrawBuilder draw)
    {
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
            if (Fdp.Toolkit.Terrain.TerrainWorldMesh.IsVolumeEffect(p)) continue;   // ⭐ CE-1165 — a gas / smoke cloud is drawn by its own layer
            // ⭐ Stage 1 — a wall/fence piece is coloured by its MATERIAL (the "materials" layer of
            //   DESIGN_Terrain_Combat_Tuning.md §5); fences are dashed. Openings are gaps because a panel's pieces leave them.
            var fill = p.Kind == TerrainPrismKind.Wall ? MaterialColor(p.Material?.Name) : HeightColor(p.Height);
            FillPolygon(draw, p.Footprint, p.Triangles, fill);
            bool fence = p.Material?.Name?.StartsWith("fence", StringComparison.Ordinal) == true || p.Material?.Name == "hedge";
            // ⭐ CE-1021 — a wall is thinner than a pixel at most zooms, so its FILL vanishes and only the outline shows: the outline
            //   is drawn in the wall's own (slightly darker) colour and thicker, so a thin wall reads as a coloured line, never black.
            bool wall = p.Kind == TerrainPrismKind.Wall;
            Outline2(draw, p.Footprint, wall ? WallEdge(fill) : Outline, fence ? LineStyle.Dashed : LineStyle.Solid,
                     wall ? WallEdgePixels : 1.5f);
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

    /// <summary>⭐ CE-1021 — a wall's outline width in screen pixels: it is what remains visible of a sub-pixel wall.</summary>
    public const float WallEdgePixels = 2.5f;

    /// <summary>⭐ CE-1021 — a wall's outline: its fill, a little darker and opaque (never the near-black building outline).</summary>
    public static Rgba32 WallEdge(Rgba32 fill) => new((byte)(fill.R * 0.8f), (byte)(fill.G * 0.8f), (byte)(fill.B * 0.8f), 255);

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

    private static void Outline2(IDebugDrawBuilder draw, IReadOnlyList<Vector2> poly, Rgba32 color, LineStyle style, float pixels = 1.5f)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            draw.DrawLine(new Vector3(a, 0f), new Vector3(b, 0f), color, pixels, SizeMode.ScreenPixels,
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
