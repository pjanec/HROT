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

    public static readonly Rgba32 ForestFill  = new(40, 110, 50, 110);
    public static readonly Rgba32 WaterFill   = new(50, 110, 200, 150);
    private static readonly Rgba32 SlabFill    = new(90, 150, 220, 90);
    private static readonly Rgba32 RampFill    = new(130, 180, 230, 110);
    // ⭐ CE-1021 — a plain wall is a light concrete grey: the old (70,70,70) under a near-black outline read as black on the map.
    public static readonly Rgba32 WallFill    = new(150, 150, 145, 235);
    private static readonly Rgba32 Outline     = new(30, 30, 30, 220);
    private static readonly Rgba32 SlabOutline = new(40, 90, 170, 220);
    private static readonly Rgba32 LabelColor  = new(20, 20, 20, 255);

    /// <summary>Emits the plan of the terrain <paramref name="view"/> holds; nothing when it holds none.</summary>
    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.HasSingletonManaged<TerrainWorld>()) return;
        var world = repo.GetSingletonManaged<TerrainWorld>();
        if (world != null) Emit(world, draw);
    }

    /// <summary>⭐ CE-1227 — emits <paramref name="world"/>'s plan: fills first (painter's order), then outlines and labels.</summary>
    public static void Emit(TerrainWorld world, IDebugDrawBuilder draw) => Emit(world, draw, null, null, null);

    /// <summary>⭐ CE-4392 X-5 — the height over a storey's floor the plan is cut at: a breach open at this height is a gap in the wall line.</summary>
    public const float PlanCutHeight = 1.0f;

    /// <summary>⭐ CE-4392 X-7 — soot each side of a breach on the plan.</summary>
    public static readonly Rgba32 ScorchFill = new(30, 27, 24, 150);
    private static readonly int[] QuadTriangles = { 0, 1, 2, 0, 2, 3 };

    private const int CanopySides = 12;
    private const float CanopyRadius = 2.4f;
    /// <summary>⭐ T-3 — a lone tree's canopy from above.</summary>
    public static readonly Rgba32 CanopyFill = new(52, 128, 60, 200);
    private static readonly int[] CanopyTriangles = BuildFan(CanopySides);
    private static int[] BuildFan(int n) { var t = new int[(n - 2) * 3]; for (int i = 0; i < n - 2; i++) { t[i * 3] = 0; t[i * 3 + 1] = i + 1; t[i * 3 + 2] = i + 2; } return t; }

    /// <summary>⭐ T-3 (CE-4430) — a block's open strip (a lane, a street, a trench): a neutral road grey.</summary>
    public static readonly Rgba32 LaneFill = new(112, 112, 120, 235);
    /// <summary>⭐ T-3 — low scrub: a darker olive than the forest.</summary>
    public static readonly Rgba32 ScrubFill = new(112, 140, 78, 220);

    /// <summary>⭐ CE-4392 X-2 — a collapsed building's rubble on the plan: a dusty grey-brown fill, a dashed outline.</summary>
    public static readonly Rgba32 RubbleFill = new(128, 118, 104, 235);
    private static readonly Rgba32 RubbleOutline = new(90, 82, 70, 230);

    /// <summary>
    /// ⭐ CE-4392 (DESIGN_Map_3D_Mode §6aj X-5) — the plan as <paramref name="damage"/> leaves it (null = none), the same pieces rule as the 3-D chunks
    /// (<see cref="Fdp.Toolkit.Terrain.DamagedBuildings"/>): a collapsed building's pieces go and its rubble is drawn; a breached panel is re-cut, so the breach is a gap in the wall line.
    /// <paramref name="only"/> (non-null) emits only the pieces of the buildings it accepts — the layer's overlay of the damaged ones. <paramref name="mark"/> is told the building
    /// (−1 = none) before each piece's primitives, so the plan knows which building every fill and stroke belongs to.
    /// </summary>
    public static void Emit(TerrainWorld world, IDebugDrawBuilder draw, Fdp.Toolkit.Terrain.BuildingDamageTable? damage, Func<int, bool>? only, Action<int>? mark)
    {
        bool Wanted(int building) => only == null || (building >= 0 && only(building));
        if (only == null)
        {
            mark?.Invoke(-1);
            foreach (var s in world.Surfaces)
            {
                var fill = s.Type switch
                {
                    TerrainSurfaceType.Forest => ForestFill,
                    TerrainSurfaceType.Scrub  => ScrubFill,   // ⭐ T-3 — scrub (a bush, a patch) and an open strip (a block's lane / street / trench) are drawn; plain open ground is not a surface the base writes
                    TerrainSurfaceType.Open   => LaneFill,
                    _                         => WaterFill,
                };
                FillPolygon(draw, s.Polygon, s.Triangles, fill);
            }
        }

        foreach (var w in world.Walkables)
        {
            if (!Wanted(w.Building)) continue;
            if (damage != null && !Fdp.Toolkit.Terrain.DamagedBuildings.Keeps(w, damage)) continue;
            mark?.Invoke(w.Building);
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
            if (!Wanted(p.Building)) continue;
            if (damage != null && !Fdp.Toolkit.Terrain.DamagedBuildings.Keeps(p, damage)) continue;
            mark?.Invoke(p.Building);
            EmitPrism(draw, p);
        }

        if (damage != null)
        {
            var panels = new List<int>();
            Fdp.Toolkit.Terrain.DamagedBuildings.BreachedPanels(damage, panels);
            var pieces = new List<TerrainPrism>();
            foreach (int pi in panels)
            {
                if ((uint)pi >= (uint)world.Panels.Count || !Fdp.Toolkit.Terrain.DamagedBuildings.IsRecut(world, damage, pi) || !Wanted(world.Panels[pi].Building)) continue;
                pieces.Clear();
                Fdp.Toolkit.Terrain.DamagedBuildings.RecutPanel(world, damage, pi, pieces);
                mark?.Invoke(world.Panels[pi].Building);
                var panel = world.Panels[pi];
                var dir = Vector2.Normalize(panel.B - panel.A);
                var across = new Vector2(-dir.Y, dir.X) * ((panel.Thickness * 0.5f) + 0.2f);
                foreach (var b in damage.BreachesOf(pi))
                {
                    // ⭐ X-7 — soot each side of the gap (the 3-D wall's scorch ring seen from above), under the wall pieces
                    float m = MathF.Min(0.9f, 0.25f + (0.25f * (b.To - b.From)));
                    foreach (var (u0, u1) in new[] { (b.From - m, b.From), (b.To, b.To + m) })
                    {
                        var p0 = panel.A + (dir * u0); var p1 = panel.A + (dir * u1);
                        var quad = new[] { p0 - across, p1 - across, p1 + across, p0 + across };
                        FillPolygon(draw, quad, QuadTriangles, ScorchFill);
                    }
                }
                foreach (var piece in pieces)
                {
                    // ⭐ the plan is CUT at PlanCutHeight over the floor: a piece that lies in a breach open at that height (the strip under or over a hole) is not drawn,
                    //   so the breach reads as a gap in the wall line — the drawing convention (a window's sill and head pieces, undamaged, are drawn as before)
                    float along = Vector2.Dot(Centroid(piece.Footprint) - panel.A, dir);
                    if (damage.Opens(pi, along, panel.BaseZ + PlanCutHeight)) continue;
                    EmitPrism(draw, piece);
                }
            }
            foreach (int b in damage.CollapsedBuildings)
            {
                if ((uint)b >= (uint)world.Buildings.Count || !Wanted(b)) continue;
                var r = damage.RubbleOf(world, b);
                mark?.Invoke(b);
                FillPolygon(draw, r.Footprint, r.Triangles, RubbleFill);
                Outline2(draw, r.Footprint, RubbleOutline, LineStyle.Dashed, 2f);
                var c = Centroid(r.Footprint);
                if (!string.IsNullOrEmpty(world.Buildings[b].Name)) draw.DrawText(c.X, c.Y, new Fdp.Core.FixedString32($"{world.Buildings[b].Name} rubble"), LabelColor, layer: TerrainLayer);   // ⭐ O-4 — Name, not the stable-key Label; empty ⇒ no text
            }
        }

        // ⭐ Stage 1 — an enterable building is named once, with its storeys (its walls and floors are drawn above).
        for (int bi = 0; bi < world.Buildings.Count; bi++)
        {
            var b = world.Buildings[bi];
            if (b.Solid || !Wanted(bi) || (damage != null && damage.Collapsed(bi))) continue;
            mark?.Invoke(bi);
            var c = Centroid(b.Footprint);
            if (!string.IsNullOrEmpty(b.Name)) draw.DrawText(c.X, c.Y, new Fdp.Core.FixedString32($"{b.Name} {b.Storeys}F"), LabelColor, layer: TerrainLayer);   // ⭐ O-4 — Name, not the stable-key Label; empty ⇒ no text
        }
    }

    private static void EmitPrism(IDebugDrawBuilder draw, TerrainPrism p)
    {
        if (Fdp.Toolkit.Terrain.TerrainWorldMesh.IsVolumeEffect(p)) return;   // ⭐ CE-1165 — a gas / smoke cloud is drawn by its own layer
        if (p.Kind == TerrainPrismKind.Tree)
        {
            // ⭐ T-3 (CE-4430) — a lone tree is its CANOPY seen from above (a green disc, ~2.4 m), under the trunk's own dot — the mockup's orchard / hedgerow-tree look; a 0.3 m trunk alone was a 2-px speck at plan scales
            var c = Centroid(p.Footprint);
            var disc = new Vector2[CanopySides];
            for (int k = 0; k < disc.Length; k++) disc[k] = c + new Vector2(MathF.Cos(k * MathF.Tau / CanopySides), MathF.Sin(k * MathF.Tau / CanopySides)) * CanopyRadius;
            FillPolygon(draw, disc, CanopyTriangles, CanopyFill);
        }
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
