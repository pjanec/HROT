using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Services;
using Sim.Scene;

namespace Hrot.Traffic.Tests;

/// <summary>
/// ⭐ AQ93 slice 1 acceptance — the SUMO road layer leaves room in the 2-D map's gizmo buffer for everything drawn after it.
///
/// <para>📌 Measured 2026-10-17, editor live on demo-city: the layer drew the 3-D meshes (~51.8k triangles a frame) into the map's
/// 4096-primitive buffer, ground first; roads, lights, cars and every entity gizmo were dropped (48.5k a frame) and the map showed
/// nothing. 📄 <c>TrafficRoadPlan</c>.</para>
/// </summary>
public sealed class TheRoadLayerFitsTheMapBufferTests
{
    /// <summary>The map buffer's default (<c>MapInteractionContext.DefaultBufferCapacity</c>, Hrot.Presentation — not referenced here).</summary>
    private const int MapBuffer = 16384;

    [Fact]
    public void DemoCity_TheRoadLayer_TakesAtMostAQuarterOfTheMapBuffer()
    {
        using var world = LoadedWorld();
        var net = TrafficNetCache.Of(world)!;
        world.SetSingletonManaged(new TrafficFrame(Array.Empty<TrafficCarPose>(), _ => false,
            net.Roads.Signals.Select(s => s.LaneHandle).Distinct().ToDictionary(h => h, _ => (byte)'G')));

        var buffer = new DebugPrimitiveBuffer(MapBuffer);
        new TrafficRoadGizmo().Draw(world, buffer);

        Assert.Equal(0, buffer.DroppedCount);
        Assert.True(buffer.Count > 1000, $"the layer drew {buffer.Count} primitives — the roads are missing, the rail proves nothing");
        Assert.True(buffer.Count <= MapBuffer / 4,
            $"the road layer drew {buffer.Count} primitives; terrain and entity gizmos share the {MapBuffer}-primitive map buffer");
    }

    /// <summary>The ground rows cover exactly the cells they merge — no cell lost or doubled by the merge.</summary>
    [Fact]
    public void TheGroundRows_CoverTheSameAreaAsTheGroundCells()
    {
        var roads = TrafficRoadSceneTests.Load().Roads;
        var ground = roads.Meshes.Single(m => m.Kind == SceneKind.AmbientGround);
        double cells = 0;
        for (int t = 0; t + 2 < ground.Indices.Length; t += 3)
            cells += Area(Flat(ground.Vertices[ground.Indices[t]]), Flat(ground.Vertices[ground.Indices[t + 1]]), Flat(ground.Vertices[ground.Indices[t + 2]]));
        double rows = roads.Plan.Ground.Sum(q => Area(q.P00, q.P10, q.P11) + Area(q.P00, q.P11, q.P01));

        Assert.True(roads.Plan.Ground.Count < ground.TriangleCount / 20, $"{roads.Plan.Ground.Count} rows for {ground.TriangleCount / 2} cells — the merge did not merge");
        Assert.InRange(rows / cells, 0.999, 1.001);
    }

    /// <summary>V8 kept by the merge — no ground row reaches over our terrain (the demo-city box is local 0..800).</summary>
    [Fact]
    public void NoGroundRow_CoversOurTerrain()
    {
        var rows = TrafficRoadSceneTests.Load().Roads.Plan.Ground;
        foreach (var q in rows)
        {
            float minX = MathF.Min(MathF.Min(q.P00.X, q.P10.X), MathF.Min(q.P11.X, q.P01.X));
            float maxX = MathF.Max(MathF.Max(q.P00.X, q.P10.X), MathF.Max(q.P11.X, q.P01.X));
            float minY = MathF.Min(MathF.Min(q.P00.Y, q.P10.Y), MathF.Min(q.P11.Y, q.P01.Y));
            float maxY = MathF.Max(MathF.Max(q.P00.Y, q.P10.Y), MathF.Max(q.P11.Y, q.P01.Y));
            bool overlaps = minX < 780f && maxX > 20f && minY < 780f && maxY > 20f;
            Assert.False(overlaps, $"a ground row spans ({minX:0},{minY:0})–({maxX:0},{maxY:0}), over our terrain");
        }
    }

    /// <summary>Every normal lane is on the map — the strips start where the lanes start.</summary>
    [Fact]
    public void EveryNormalLane_HasAStrip()
    {
        var net = TrafficRoadSceneTests.Load();
        int lanes = net.Model.LanesByHandle.Count(l => !l.EdgeId.StartsWith(':') && l.Shape.Count >= 2);
        int segments = net.Model.LanesByHandle.Where(l => !l.EdgeId.StartsWith(':')).Sum(l => Math.Max(0, l.Shape.Count - 1));

        Assert.True(lanes > 0);
        Assert.Equal(segments, net.Roads.Plan.Strips.Count);
        Assert.Contains(net.Roads.Plan.Strips, s => s.Sidewalk);
        Assert.Contains(net.Roads.Plan.Strips, s => !s.Sidewalk);
    }

    private static EntityRepository LoadedWorld()
    {
        var world = new EntityRepository();
        var residency = new TerrainResidency(new TerrainCatalog(new[] { TrafficRoadSceneTests.TerrainRoot() }), new CarKinem.Road.RoadNetworkHolder());
        residency.AttachContent(new SumoNetTerrainLoader());
        residency.Commit(world, residency.Prepare("demo-city"));
        return world;
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    private static double Area(Vector2 a, Vector2 b, Vector2 c) => Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2.0;
}
