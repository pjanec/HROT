using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Hrot.Editor.UiFrameRail;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.UI.Common.Map2D;
using Raylib_cs;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ CE-4392 (handoff D-3 / D-4; docs/DESIGN_Map_3D_Mode.md §6aj X-5) — the 2-D plan draws the replicated damage: a breach is a gap in the wall line, a collapsed building is
/// rubble (a grey-brown fill, dashed outline) and its walls and label are gone. ⭐ Never the whole plan rebuilt: the damaged building's own fills and strokes are hidden and the
/// building drawn again, damaged, in a small overlay; an unchanged table rebuilds nothing. The frame rail runs the PRODUCTION attach (<c>MapInteractionPack.AttachMapLayers</c>)
/// on a view that only received a <see cref="BuildingDamage"/> entity (a display node: its terrain never changed). 🔴 Red-proof (compiles): <c>ApplyDamage</c> never called ⇒ the
/// breach frame still shows the wall.
/// </summary>
[Collection(Windows.PanelSnapshotTestCollection.Name)]
public sealed class TheBuildingDamagePlanTests
{
    private readonly ITestOutputHelper _out;
    public TheBuildingDamagePlanTests(ITestOutputHelper output) => _out = output;

    private const string House = """
        { "type": "FeatureCollection", "features": [ { "type": "Feature",
            "properties": { "kind": "building", "label": "H",
              "building": { "footprint": [[0,0],[10,0],[10,8],[0,8]],
                "storeys": [ { "height": 3, "walls": [
                  { "from": [0,0], "to": [10,0], "thickness": 0.3, "material": "brick" },
                  { "from": [10,0], "to": [10,8], "thickness": 0.3, "material": "brick" },
                  { "from": [10,8], "to": [0,8], "thickness": 0.3, "material": "brick" },
                  { "from": [0,8], "to": [0,0], "thickness": 0.3, "material": "brick" } ] } ] } },
            "geometry": { "type": "Point", "coordinates": [20, 20] } } ] }
        """;

    private static (EntityRepository Repo, TerrainWorld World) World()
    {
        var w = TerrainWorldParser.Parse(House, "house");
        var repo = new EntityRepository();
        repo.RegisterComponent<BuildingDamage>();
        repo.RegisterManagedComponent<TerrainObjectKey>();
        repo.SetSingletonManaged(w);
        return (repo, w);
    }

    private static void Receive(EntityRepository repo, TerrainWorld w, bool collapsed, params WallBreach[] breaches)
    {
        var d = new BuildingDamage { Building = 0, Collapsed = collapsed ? (byte)1 : (byte)0 };
        foreach (var b in breaches) BuildingDamages.Add(ref d, in b, null);
        var e = repo.CreateEntity();
        repo.AddComponent(e, d);
        repo.SetManagedComponent(e, new TerrainObjectKey { Key = BuildingDamages.KeyOf(w, 0) });
    }

    private static int SouthWall(TerrainWorld w)
    {
        for (int i = 0; i < w.Panels.Count; i++)
            if (MathF.Abs(w.Panels[i].A.Y - 20f) < 1e-3f && MathF.Abs(w.Panels[i].B.Y - 20f) < 1e-3f && w.Panels[i].B.X > w.Panels[i].A.X) return i;
        return -1;
    }

    // does any fill of the plan (not hidden) in colour <c> cover the point?
    private static bool Covers(TerrainLayer2D.Plan plan, Vector2 at, Func<Rgba32, bool> colour, bool[]? hidden = null)
    {
        for (int t = 0; t < plan.FillTriangles; t++)
        {
            int b = plan.FillBuilding[t];
            if (hidden != null && (uint)b < (uint)hidden.Length && hidden[b]) continue;
            if (!colour(plan.FillColors[t])) continue;
            var a = plan.FillVertices[3 * t]; var p = plan.FillVertices[3 * t + 1]; var c = plan.FillVertices[3 * t + 2];
            float d1 = Cross(a, p, at), d2 = Cross(p, c, at), d3 = Cross(c, a, at);
            if (!((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))) return true;
        }
        return false;
        static float Cross(Vector2 a, Vector2 b, Vector2 p) => ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));
    }

    private static readonly Rgba32 Brick = TerrainPlan2D.MaterialColor("brick");
    private static bool IsBrick(Rgba32 c) => c.R == Brick.R && c.G == Brick.G && c.B == Brick.B;
    private static bool IsRubble(Rgba32 c) => c.R == TerrainPlan2D.RubbleFill.R && c.G == TerrainPlan2D.RubbleFill.G && c.B == TerrainPlan2D.RubbleFill.B;

    [Fact]
    public void ThePlan_KnowsEachPiecesBuilding_AndTheDamagedBuildingIsReEmittedDamaged()
    {
        var (repo, w) = World();
        var plain = TerrainLayer2D.PlanOf(w);
        Assert.Contains(plain.FillBuilding, b => b == 0);
        Assert.Contains(plain.StrokeBuilding, b => b == 0);
        var wallMid = new Vector2(25f, 20f);
        Assert.True(Covers(plain, wallMid, IsBrick), "the plain plan draws the south wall");

        Receive(repo, w, false, new WallBreach(SouthWall(w), 3.5f, 6.5f, 0.3f, 2.6f, BreachKind.Hole));
        var damage = DoorStates.Of(repo, w).Damage!;
        var overlay = TerrainLayer2D.PlanOf(w, damage, b => b == 0);
        Assert.False(Covers(overlay, wallMid, IsBrick), "the breach is a gap in the wall line");
        Assert.True(Covers(overlay, new Vector2(21f, 20f), IsBrick), "the wall beside it is drawn");
        Assert.True(overlay.Strokes.Length < plain.Strokes.Length + 50 && overlay.FillTriangles < plain.FillTriangles + 50, "the overlay holds the one building, not the terrain");
        var hidden = new[] { true };
        Assert.False(Covers(plain, wallMid, IsBrick, hidden), "the plan's own copy of the building is hidden");
        var view = (new Vector2(0, 0), new Vector2(60, 60));
        var into = new DebugPrimitive[plain.Strokes.Length];
        Assert.True(TerrainLayer2D.Cull(plain, view, 20f, into, hidden) < TerrainLayer2D.Cull(plain, view, 20f, into), "the hidden building's strokes are not culled in");

        var (repo2, w2) = World();
        Receive(repo2, w2, true);
        var collapse = TerrainLayer2D.PlanOf(w2, DoorStates.Of(repo2, w2).Damage!, b => b == 0);
        Assert.DoesNotContain(collapse.FillColors, IsBrick);
        Assert.True(Covers(collapse, new Vector2(25f, 24f), IsRubble), "the rubble covers the footprint");
        Assert.Contains(collapse.Strokes, s => s.Shape == DebugPrimitiveShape.Text && s.TextContent.ToString().Contains("rubble"));
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4599</c> (B-2, V-16) — a GARDEN WALL breach draws on every node from the replicated record, like a building's: the plan tags the free wall's pieces with
    /// its wall GROUP (1 — after the house), and the damaged overlay of that group draws the wall with the gap and its other segment whole. 🔴 Red-proof: tag a prism by
    /// <c>p.Building</c> (−1 for a free wall) in <c>TerrainPlan2D.Emit</c> ⇒ the overlay holds no wall at all and the plan cannot hide its own copy.
    /// </summary>
    [Fact]
    public void CE4599_AGardenWallBreach_IsDrawnAsAGap_ByItsWallGroup()
    {
        var w = TerrainWorldParser.Parse(House.Replace("\"features\": [", """
            "features": [ { "type": "Feature", "properties": { "kind": "wall", "label": "Garden", "height": 1.0, "thickness": 0.3, "material": "brick" },
              "geometry": { "type": "LineString", "coordinates": [[10,10],[30,10],[50,10]] } },
            """), "house");
        var repo = new EntityRepository();
        repo.RegisterComponent<BuildingDamage>();
        repo.RegisterManagedComponent<TerrainObjectKey>();
        repo.SetSingletonManaged(w);
        int group = BuildingDamages.IndexOfKey(w, "house/wall:Garden");
        Assert.Equal(1, group);
        var plain = TerrainLayer2D.PlanOf(w);
        Assert.Contains(plain.FillBuilding, b => b == group);
        Assert.True(Covers(plain, new Vector2(20f, 10f), IsBrick), "the plain plan draws the garden wall");

        int panel = BuildingPanelIndex.ToGlobal(w, group, 0);
        var d = new BuildingDamage { Building = group };
        BuildingDamages.Add(ref d, new WallBreach(panel, 8f, 12f, 0f, 1f, BreachKind.Passage), null);
        var e = repo.CreateEntity(); repo.AddComponent(e, d); repo.SetManagedComponent(e, new TerrainObjectKey { Key = "house/wall:Garden" });
        var damage = DoorStates.Of(repo, w).Damage!;
        var touched = new HashSet<int>();
        DamagedBuildings.Touched(w, null, damage, touched);
        Assert.Contains(group, touched);
        var overlay = TerrainLayer2D.PlanOf(w, damage, b => b == group);
        Assert.False(Covers(overlay, new Vector2(20f, 10f), IsBrick), "the breach is a gap in the garden wall");
        Assert.True(Covers(overlay, new Vector2(14f, 10f), IsBrick), "beside the gap the wall stands");
        Assert.True(Covers(overlay, new Vector2(40f, 10f), IsBrick), "its other segment stands");
        Assert.False(Covers(plain, new Vector2(40f, 10f), IsBrick, new[] { false, true }), "the plan's own copy of the line is hidden");
    }

    // the wall's own colour (its brick fill or its darker outline) in a 9 × 9 box round the point
    private static int WallPixels(string png, Vector2 screen)
    {
        var img = Raylib.LoadImage(png);
        try
        {
            var f = TerrainPlan2D.MaterialColor("brick"); var e = TerrainPlan2D.WallEdge(f);
            int n = 0;
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    var c = Raylib.GetImageColor(img, (int)screen.X + dx, (int)screen.Y + dy);
                    if ((Math.Abs(c.R - f.R) + Math.Abs(c.G - f.G) + Math.Abs(c.B - f.B) < 40) || (Math.Abs(c.R - e.R) + Math.Abs(c.G - e.G) + Math.Abs(c.B - e.B) < 40)) n++;
                }
            return n;
        }
        finally { Raylib.UnloadImage(img); }
    }

    private static Color At(string png, Vector2 screen)
    {
        var img = Raylib.LoadImage(png);
        try { return Raylib.GetImageColor(img, (int)screen.X, (int)screen.Y); }
        finally { Raylib.UnloadImage(img); }
    }

    [SkippableFact]
    public void TheProductionMap_DrawsTheBreachAndTheRubble_OnADisplayNode()
    {
        Skip.IfNot(UiFrameHarness.IsAvailable(), UiFrameHarness.UnavailableReason);
        string shots = Environment.GetEnvironmentVariable("HROT_RAIL_SHOTS") is { Length: > 0 } d ? d : Path.Combine(Path.GetTempPath(), "hrot-frames");
        Directory.CreateDirectory(shots);
        var (repo, w) = World();
        using var frame = UiFrameHarness.Begin(800, 500);
        LitShader.Reset();
        var canvas = new MapCanvas(input: null);
        var cam = new MapCamera();
        canvas.Camera = cam;
        float zoom = 40f;
        var centre = new Vector2(25f, 22f);
        var target = centre + new Vector2(-800f / 2f / zoom, 500f / 2f / zoom);
        cam.ApplyCameraView(new MapCameraView { Target = target, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom });
        var buffer = new DebugPrimitiveBuffer(Hrot.ScenarioEditor.Map.MapInteractionContext.DefaultBufferCapacity);
        buffer.SetActiveLayers(new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Map = { Grid = false } }.ToMask());
        var layers = Hrot.ScenarioEditor.Map.MapInteractionPack.AttachMapLayers(canvas, buffer, new FdpEventBus(), () => repo);
        var terrain = layers.Terrain2D!;

        string Shot(string name) { frame.StepN(3, canvas.Draw); var p = Path.Combine(shots, name); frame.Screenshot(p); return p; }

        var before = Shot("damage2d-1-house.png");
        var wall = cam.WorldToScreen(new Vector2(25f, 20f));     // after a frame: the camera as the canvas drew it
        var inside = cam.WorldToScreen(new Vector2(25f, 24f));
        _out.WriteLine($"south wall's middle at {wall}, the inside at {inside}");
        int builds = terrain.DamageBuilds;
        Shot("damage2d-1b-house-again.png");
        Assert.Equal(builds, terrain.DamageBuilds);   // unchanged ⇒ nothing rebuilt
        Assert.Equal(1, terrain.Builds);

        Receive(repo, w, false, new WallBreach(SouthWall(w), 3.5f, 6.5f, 0.3f, 2.6f, BreachKind.Hole));
        var breach = Shot("damage2d-2-breach.png");
        Assert.Equal(1, terrain.Builds);              // the plan was not rebuilt — only the overlay
        Assert.Equal(1, terrain.DamagedBuildingsDrawn);
        int wallBefore = WallPixels(before, wall), wallAfter = WallPixels(breach, wall);
        _out.WriteLine($"wall-coloured pixels round the breach: {wallBefore} → {wallAfter}");
        Assert.True(wallBefore >= 6, "the wall is drawn there before");
        Assert.True(wallAfter <= 1, "the breach is a gap in the drawn wall");

        Receive(repo, w, true);
        var rubble = Shot("damage2d-3-rubble.png");
        var cr = At(rubble, inside);
        _out.WriteLine($"inside pixel {cr.R},{cr.G},{cr.B}");
        Assert.InRange(cr.R, TerrainPlan2D.RubbleFill.R - 20, TerrainPlan2D.RubbleFill.R + 20);
        Assert.True(cr.R > cr.G && cr.G > cr.B, "the rubble's warm grey");
        Assert.Same(w, repo.GetSingletonManaged<TerrainWorld>());   // R-4196: the display node's terrain is not changed
    }
}
