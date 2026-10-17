using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Sim.Scene;

namespace Hrot.Traffic.Tests;

/// <summary>AQ93 V7/V8 (R-4049) — the demo city's road scene, as the terrain load builds it.</summary>
public sealed class TrafficRoadSceneTests
{
    [Fact]
    public void V7_TheDemoCityScene_HasEveryKind_AndLightsAtItsSignalisedJunctions()
    {
        var roads = Load().Roads;

        var kinds = roads.Meshes.Select(m => m.Kind).ToHashSet();
        foreach (var k in new[] { SceneKind.Asphalt, SceneKind.Junction, SceneKind.LaneMarking, SceneKind.Crosswalk, SceneKind.AmbientGround })
            Assert.Contains(k, kinds);
        Assert.True(roads.Signals.Count > 0);
        // junction d_4_4 (net 2600, 2600) is a traffic light at local (400, 400): a lamp stands within 30 m of it.
        Assert.Contains(roads.Signals, s => Vector2.Distance(new Vector2(s.Head.X, s.Head.Y), new Vector2(400f, 400f)) < 30f);
    }

    /// <summary>V8 — the ambient ground stops at OUR terrain: no ground cell is centred inside the 800 m demo-city box, and the roads
    /// themselves run through it (placed by the anchor: local = net − (2200, 2200)).</summary>
    [Fact]
    public void V8_TheAmbientGround_LeavesOurTerrainOut_TheRoadsDoNot()
    {
        var roads = Load().Roads;
        var ground = roads.Meshes.Single(m => m.Kind == SceneKind.AmbientGround);
        for (int t = 0; t + 2 < ground.Indices.Length; t += 6)   // each cell is two triangles
        {
            var c = (ground.Vertices[ground.Indices[t]] + ground.Vertices[ground.Indices[t + 1]] + ground.Vertices[ground.Indices[t + 2]]) / 3f;
            Assert.False(c.X > 20f && c.X < 780f && c.Y > 20f && c.Y < 780f, $"a ground cell sits at ({c.X}, {c.Y}), inside our terrain");
        }

        var asphalt = roads.Meshes.Single(m => m.Kind == SceneKind.Asphalt);
        Assert.Contains(asphalt.Vertices, v => v.X > 0f && v.X < 800f && v.Y > 0f && v.Y < 800f);
        Assert.Contains(asphalt.Vertices, v => v.X < -500f);   // and beyond it: the net is 4.75 km
    }

    internal static TrafficNetCache Load()
    {
        using var world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(world, residency.Prepare("demo-city"));
        return TrafficNetCache.Of(world)!;
    }

    internal static string TerrainRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Hrot", "Subsystems")))
            dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(dir!, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Terrain");
    }
}
