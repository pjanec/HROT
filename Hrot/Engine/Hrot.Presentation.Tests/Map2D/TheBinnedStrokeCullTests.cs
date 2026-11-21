using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Terrain;
using Hrot.UI.Common.Map2D;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-4376 (handoff D-2) — the 2-D plan's stroke cull walks only the cells in view (<see cref="TerrainLayer2D.StrokeIndex"/>) and keeps EXACTLY the strokes the linear
/// rule kept (re-stated here as the reference): in view with the margin, a line at least <see cref="TerrainLayer2D.MinStrokePixels"/> long, labels from
/// <see cref="TerrainLayer2D.LabelMinZoom"/> — on the shipped terrains, whole and close, with a long wall. Order is not kept (a set). 🔴 Red-proof (compiles): the
/// one-cell slack dropped ⇒ a line whose midpoint is just outside the view is lost.
/// </summary>
public sealed class TheBinnedStrokeCullTests
{
    private static TerrainWorld Load(string name)
    {
        var folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), name);
        return TerrainWorldParser.Parse(File.ReadAllText(Directory.GetFiles(folder, "*.world.geojson").Single()), name, TerrainAssets.ForFolder(folder));
    }

    private static List<int> Linear(TerrainLayer2D.Plan plan, (Vector2 Min, Vector2 Max) view, float zoom)
    {
        var pad = new Vector2(TerrainLayer2D.CullMarginPixels / zoom);
        var min = view.Min - pad; var max = view.Max + pad;
        float minLength = TerrainLayer2D.MinStrokePixels / zoom;
        var kept = new List<int>();
        for (int i = 0; i < plan.Strokes.Length; i++)
        {
            var p = plan.Strokes[i];
            if (p.Shape == DebugPrimitiveShape.Line)
            {
                var a = new Vector2(p.LineStart.X, p.LineStart.Y); var b = new Vector2(p.LineEnd.X, p.LineEnd.Y);
                if (Vector2.DistanceSquared(a, b) < minLength * minLength) continue;
                if (1f / zoom > TerrainLayer2D.BuildingStrokesMaxMetresPerPixel && plan.StrokeBuilding[i] >= 0) continue;   // CE-4389
                if (zoom < TerrainLayer2D.InteriorMinZoom && plan.IsHouse(plan.StrokeBuilding[i])) continue;                   // U-4
                if (Math.Max(a.X, b.X) < min.X || Math.Min(a.X, b.X) > max.X || Math.Max(a.Y, b.Y) < min.Y || Math.Min(a.Y, b.Y) > max.Y) continue;
            }
            else if (p.Shape == DebugPrimitiveShape.Text)
            {
                if (zoom < TerrainLayer2D.LabelMinZoom) continue;
                if (zoom < TerrainLayer2D.InteriorMinZoom && plan.IsHouse(plan.StrokeBuilding[i])) continue;
                if (p.TextX < min.X || p.TextX > max.X || p.TextY < min.Y || p.TextY > max.Y) continue;
            }
            kept.Add(i);
        }
        return kept;
    }

    private static string Key(DebugPrimitive p) => p.Shape == DebugPrimitiveShape.Text
        ? $"T{p.TextX:F2},{p.TextY:F2}"
        : $"{p.Shape}{p.LineStart.X:F2},{p.LineStart.Y:F2},{p.LineEnd.X:F2},{p.LineEnd.Y:F2},{p.Color.R},{p.Color.G},{p.Color.B}";

    /// <summary>The one-cell slack: a 200 m wall whose MIDPOINT is in the cell west of the view still crosses it and is kept.</summary>
    [Fact]
    public void ALongLine_WhoseMidpointIsInTheNextCell_IsKept()
    {
        var c = new Rgba32(255, 255, 255, 255);
        var strokes = new[]
        {
            DebugPrimitive.MakeLine(new Vector3(0, 0, 0), new Vector3(1, 0, 0), c),
            DebugPrimitive.MakeLine(new Vector3(100, 0, 0), new Vector3(300, 0, 0), c),   // midpoint x = 200: cell 0
            DebugPrimitive.MakeLine(new Vector3(600, 0, 0), new Vector3(601, 0, 0), c),
        };
        var plan = new TerrainLayer2D.Plan(Array.Empty<Vector2>(), Array.Empty<Rgba32>(), strokes);
        var into = new DebugPrimitive[3];
        var view = (new Vector2(262, -5), new Vector2(270, 5));                                    // cell 1, x = 262…270
        int n = TerrainLayer2D.Cull(plan, view, 100f, into);
        Assert.Equal(Linear(plan, view, 100f).Count, n);
        Assert.Contains(into.Take(n), p => p.LineStart.X == 100f);
    }

    /// <summary>⭐ CE-4389 — coarser than <see cref="TerrainLayer2D.BuildingStrokesMaxMetresPerPixel"/> a building is its fill only: none of its strokes drawn, every
    /// other stroke as before; at the limit and closer, its strokes again. 🔴 Red-proof (compiles): the limit raised to 100 m/px ⇒ the far frame draws them.</summary>
    [Fact]
    public void FarOut_ABuildingIsItsFillOnly_CloseIn_ItsOutlinesToo()
    {
        var t = Load("demo-city");
        var plan = TerrainLayer2D.PlanOf(t);
        int inBuildings = plan.StrokeBuilding.Count(b => b >= 0);
        Assert.True(inBuildings > 0, "demo-city's plan has building strokes");
        Assert.NotEmpty(plan.FillColors);
        var view = (t.BoundsMin, t.BoundsMax);
        var into = new DebugPrimitive[plan.Strokes.Length];
        var far = into.Take(TerrainLayer2D.Cull(plan, view, 1f / (TerrainLayer2D.BuildingStrokesMaxMetresPerPixel * 2f), into)).Select(Key).ToHashSet();
        int buildingsFar = Enumerable.Range(0, plan.Strokes.Length).Count(i => plan.StrokeBuilding[i] >= 0 && far.Contains(Key(plan.Strokes[i])));
        Assert.Equal(0, buildingsFar);
        var close = into.Take(TerrainLayer2D.Cull(plan, view, Math.Max(1f / TerrainLayer2D.BuildingStrokesMaxMetresPerPixel, TerrainLayer2D.InteriorMinZoom), into)).Select(Key).ToHashSet();
        int buildingsClose = Enumerable.Range(0, plan.Strokes.Length).Count(i => plan.StrokeBuilding[i] >= 0 && close.Contains(Key(plan.Strokes[i])));
        Assert.True(buildingsClose > 0, "at the limit the building outlines are drawn");
    }

    [Theory]
    [InlineData("demo-city")]
    [InlineData("highway-rest")]
    [InlineData("test-town")]
    public void TheBinnedCull_KeepsExactlyTheLinearRulesStrokes(string terrain)
    {
        var t = Load(terrain);
        var plan = TerrainLayer2D.PlanOf(t);
        Assert.NotEmpty(plan.Strokes);
        var lo = t.BoundsMin; var hi = t.BoundsMax; var c = (lo + hi) * 0.5f;
        var views = new (Vector2, Vector2, float)[]
        {
            (lo, hi, 1280f / (hi.X - lo.X)),                                       // the whole terrain
            (c - new Vector2(150), c + new Vector2(150), 1280f / 300f),           // close in (labels)
            (c + new Vector2(170, -40), c + new Vector2(330, 60), 8f),            // off-centre, very close
            (lo - new Vector2(400), lo + new Vector2(100), 0.5f),                  // the corner, far
        };
        var into = new DebugPrimitive[plan.Strokes.Length];
        foreach (var (a, b, zoom) in views)
        {
            int n = TerrainLayer2D.Cull(plan, (a, b), zoom, into);
            var binned = into.Take(n).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var linear = Linear(plan, (a, b), zoom).Select(i => Key(plan.Strokes[i])).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(linear.Count, binned.Count);
            Assert.Equal(linear, binned);
        }
    }
}
