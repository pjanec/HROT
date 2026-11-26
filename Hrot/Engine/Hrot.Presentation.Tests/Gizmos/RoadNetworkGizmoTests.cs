using System;
using System.Linq;
using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.ScenarioEditor.Gizmos;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ <c>CE-3124</c> (R-228) — <see cref="RoadNetworkGizmo"/> draws the road network the loaded terrain put in the world
/// (<see cref="ZoneEnvironmentData"/>), on its own <c>Roads</c> layer, following each segment's curve; nothing without one.
/// 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
/// </summary>
public sealed class RoadNetworkGizmoTests : IDisposable
{
    private readonly EntityRepository _w = new();
    private readonly DebugPrimitiveBuffer _draw = new();
    private RoadNetworkBlob _blob;

    public void Dispose()
    {
        if (_blob.Nodes.IsCreated) _blob.Dispose();
        _w.Dispose();
    }

    private DebugPrimitive[] Roads() => _draw.GetFrame().ToArray().Where(p => p.DebugLayer == DebugTraceLayers.Roads).ToArray();

    [Fact]
    public void CE3124_TheTerrainsRoads_AreDrawnAsCurves_OnTheirOwnLayer()
    {
        var b = new RoadNetworkBuilder();
        b.AddNode(new Vector2(0, 0));
        b.AddNode(new Vector2(100, 0));
        // a bend: leaves heading north-east, arrives heading south-east
        b.AddSegment(new Vector2(0, 0), new Vector2(100, 100), new Vector2(100, 0), new Vector2(100, -100),
            laneWidth: 3.5f, laneCount: 2, startNodeIdx: 0, endNodeIdx: 1);
        _blob = b.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = _blob });

        new RoadNetworkGizmo().Draw(_w, _draw);

        var roads = Roads();
        var bands = roads.Where(p => p.Shape == DebugPrimitiveShape.Line && p.Color.Equals(RoadNetworkGizmo.RoadColor)).ToArray();
        Assert.Equal(RoadNetworkGizmo.CurveSamples, bands.Length);
        Assert.Equal(RoadNetworkGizmo.CurveSamples,
            roads.Count(p => p.Shape == DebugPrimitiveShape.Line && p.Color.Equals(RoadNetworkGizmo.CentreColor)));
        Assert.Equal(0, roads.Count(p => p.Shape == DebugPrimitiveShape.Sphere));   // ⭐ CE-6504 — the nodes are on their own layer
        Assert.Equal(2, _draw.GetFrame().ToArray().Count(p => p.DebugLayer == DebugTraceLayers.RoadNodes && p.Shape == DebugPrimitiveShape.Sphere));
        Assert.Equal(new Vector3(0, 0, 0), bands[0].LineStart);
        Assert.Equal(new Vector3(100, 0, 0), bands[^1].LineEnd);
        Assert.True(bands.Max(p => p.LineEnd.Y) > 5f, "the bend is drawn, not the chord");
    }

    /// <summary>
    /// ⭐⭐ CE-4401 — on a terrain whose ground is at 500 m (the scale rig's <c>scale-1</c> stands at ≈ 500 m) every road band, centre line and node is drawn ON the
    /// ground (its height + <see cref="RoadNetworkGizmo.GroundLift"/>), never at z 0, 500 m under it (user finding). 🔴 Red-proof: draw at z 0 again (<c>On</c>
    /// ignoring the terrain) ⇒ red.
    /// </summary>
    [Fact]
    public void CE4401_OnATerrainAt500m_TheRoadsLieOnTheGround()
    {
        var b = new RoadNetworkBuilder();
        b.AddNode(new Vector2(0, 0));
        b.AddNode(new Vector2(100, 0));
        b.AddSegment(new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 0), new Vector2(100, 0),
            laneWidth: 3.5f, laneCount: 2, startNodeIdx: 0, endNodeIdx: 1);
        _blob = b.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = _blob });
        var terrain = Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
            "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-50,-50,150,50],\"groundZ\":500},\"features\":[]}", "ce4401");
        Assert.Equal(500f, terrain.GroundHeightAt(50f, 0f), 3);
        _w.SetSingletonManaged(terrain);

        new RoadNetworkGizmo().Draw(_w, _draw);

        var roads = Roads();
        Assert.NotEmpty(roads);
        foreach (var p in roads)
        {
            float z = p.Shape == DebugPrimitiveShape.Line ? MathF.Min(p.LineStart.Z, p.LineEnd.Z) : p.SphereCenter.Z;
            Assert.Equal(500f + RoadNetworkGizmo.GroundLift, z, 3);
        }
    }

    /// <summary>
    /// ⭐⭐ CE-6504 — the road NODES (blue spheres every few metres of a bend) are their own row, <i>Road nodes</i>, off by default: with the default layer set
    /// the bands and centre lines draw and not one node sphere; ticked, every node does — cached and uncached alike. 🔴 Red-proof: the nodes back on
    /// <see cref="DebugTraceLayers.Roads"/> ⇒ spheres with the default set ⇒ red.
    /// </summary>
    [Fact]
    public void CE6504_TheRoadNodes_AreTheirOwnRow_OffByDefault()
    {
        _blob = SeededNet(5, 12);
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = _blob });
        int Spheres(DebugPrimitiveBuffer d) => d.GetFrame().ToArray().Count(p => p.Shape == DebugPrimitiveShape.Sphere);
        int Lines(DebugPrimitiveBuffer d) => d.GetFrame().ToArray().Count(p => p.Shape == DebugPrimitiveShape.Line);
        var off = new Hrot.Common.Diagnostics.Gizmos.LayerControlDto();
        Assert.False(off.Map.RoadNodes);
        var on = new Hrot.Common.Diagnostics.Gizmos.LayerControlDto { Map = { RoadNodes = true } };
        foreach (bool cached in new[] { true, false })
        {
            var hidden = new DebugPrimitiveBuffer(1 << 14); hidden.SetActiveLayers(off.ToMask());
            var shown = new DebugPrimitiveBuffer(1 << 14); shown.SetActiveLayers(on.ToMask());
            if (cached) { new RoadNetworkGizmo().Draw(_w, hidden); new RoadNetworkGizmo().Draw(_w, shown); }
            else { RoadNetworkGizmo.Draw(_blob, hidden, null); RoadNetworkGizmo.Draw(_blob, shown, null); }
            Assert.True(Lines(hidden) > 0, "the roads themselves still draw");
            Assert.Equal(0, Spheres(hidden));
            Assert.Equal(_blob.Nodes.Length, Spheres(shown));
            Assert.Equal(Lines(hidden), Lines(shown));
        }
    }

    [Fact]
    public void CE3124_NoTerrainRoads_DrawNothing()
    {
        new RoadNetworkGizmo().Draw(_w, _draw);                                  // no singleton
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = default });      // a terrain with no roads
        new RoadNetworkGizmo().Draw(_w, _draw);
        Assert.Empty(Roads());
    }

    // ── ⭐⭐ CE-11046 (HL-2) — the cached polyline ─────────────────────────────────────────────────────────────

    private RoadNetworkBlob SeededNet(int seed, int segments)
    {
        var rnd = new Random(seed);
        var b = new RoadNetworkBuilder();
        for (int i = 0; i <= segments; i++) b.AddNode(new Vector2(rnd.Next(-400, 400), rnd.Next(-400, 400)));
        for (int i = 0; i < segments; i++)
            b.AddSegment(new Vector2(rnd.Next(-400, 400), rnd.Next(-400, 400)), new Vector2(rnd.Next(-80, 80), rnd.Next(-80, 80)),
                new Vector2(rnd.Next(-400, 400), rnd.Next(-400, 400)), new Vector2(rnd.Next(-80, 80), rnd.Next(-80, 80)),
                laneWidth: 3f + rnd.Next(0, 3), laneCount: 1 + rnd.Next(0, 3), startNodeIdx: i, endNodeIdx: i + 1);
        return b.Build(cellSize: 20f, gridWidth: 50, gridHeight: 50);
    }

    private static Fdp.Toolkit.Terrain.TerrainWorld Terrain(float z, string name)
        => Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(
            "{\"type\":\"FeatureCollection\",\"hrot\":{\"schemaVersion\":1,\"bounds\":[-500,-500,500,500],\"groundZ\":" + z.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},\"features\":[]}", name);

    private static byte[] Bytes(DebugPrimitiveBuffer d)
    {
        var f = d.GetFrame();
        return System.Runtime.InteropServices.MemoryMarshal.AsBytes(f).ToArray();
    }

    /// <summary>
    /// ⭐ CE-11046 — the cached draw emits EXACTLY the primitives the uncached reference does (same order, same bytes), on seeded nets, with and without a terrain.
    /// 🔴 Red-proof: the cache storing a segment's points off by one sample ⇒ the bytes differ.
    /// </summary>
    [Theory]
    [InlineData(1, 12, false)]
    [InlineData(7, 40, true)]
    [InlineData(99, 3, true)]
    public void CE11046_TheCachedDraw_EqualsTheUncachedOne_OnSeededNets(int seed, int segments, bool withTerrain)
    {
        _blob = SeededNet(seed, segments);
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = _blob });
        var terrain = withTerrain ? Terrain(123f, "ce11046-eq") : null;
        if (terrain != null) _w.SetSingletonManaged(terrain);

        var reference = new DebugPrimitiveBuffer(1 << 14);
        RoadNetworkGizmo.Draw(_blob, reference, terrain);
        var gizmo = new RoadNetworkGizmo();
        gizmo.Draw(_w, _draw);                    // the build frame
        Assert.True(reference.Count > 0);
        Assert.Equal(Bytes(reference), Bytes(_draw));
        _draw.Clear(); gizmo.Draw(_w, _draw);     // a cached frame
        Assert.Equal(Bytes(reference), Bytes(_draw));
        Assert.Equal(1, gizmo.Cache.Rebuilds);
    }

    /// <summary>
    /// ⭐⭐ CE-11046 — a STEADY frame allocates nothing and looks up no ground height: after N frames the cache rebuilt ONCE (the heights are read only in a rebuild —
    /// <c>TerrainWorld</c> is sealed, so the count of height lookups IS the rebuild count) and <c>GC.GetAllocatedBytesForCurrentThread</c> across 200 frames is 0 B.
    /// 🔴 Red-proof: <c>IsCurrent</c> always false ⇒ Rebuilds = 201 and bytes &gt; 0; the cache not stored ⇒ the same.
    /// </summary>
    [Fact]
    public void CE11046_ASteadyFrame_IsZeroBytes_AndReadsNoGroundHeight()
    {
        _blob = SeededNet(5, 60);
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = _blob });
        _w.SetSingletonManaged(Terrain(40f, "ce11046-steady"));
        var gizmo = new RoadNetworkGizmo();
        var buf = new DebugPrimitiveBuffer(1 << 15);
        for (int i = 0; i < 5; i++) { buf.Clear(); gizmo.Draw(_w, buf); }
        Assert.Equal(1, gizmo.Cache.Rebuilds);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) { buf.Clear(); gizmo.Draw(_w, buf); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes);
        Assert.Equal(1, gizmo.Cache.Rebuilds);   // 205 frames, one set of height lookups
    }

    /// <summary>⭐ CE-11046 — a road change (a new blob) and a terrain change (a new terrain world) each REBUILD, and the new heights are drawn.
    /// 🔴 Red-proof: the key ignoring the terrain ⇒ the terrain row draws the old 10 m ground.</summary>
    [Fact]
    public void CE11046_ARoadChange_AndATerrainChange_Rebuild()
    {
        var gizmo = new RoadNetworkGizmo();
        _blob = SeededNet(3, 5);
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = _blob });
        _w.SetSingletonManaged(Terrain(10f, "ce11046-a"));
        gizmo.Draw(_w, _draw); _draw.Clear(); gizmo.Draw(_w, _draw);
        Assert.Equal(1, gizmo.Cache.Rebuilds);
        Assert.Equal(10f + RoadNetworkGizmo.GroundLift, Roads()[0].Shape == DebugPrimitiveShape.Line ? Roads()[0].LineStart.Z : 0f, 3);

        _w.SetSingletonManaged(Terrain(77f, "ce11046-b"));          // the terrain is replaced (a reload, a bake)
        _draw.Clear(); gizmo.Draw(_w, _draw);
        Assert.Equal(2, gizmo.Cache.Rebuilds);
        Assert.Equal(77f + RoadNetworkGizmo.GroundLift, Roads()[0].LineStart.Z, 3);

        var old = _blob;
        _blob = SeededNet(4, 9);                                     // the net is replaced (an edit, a bridge closure republish)
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = _blob });
        old.Dispose();
        _draw.Clear(); gizmo.Draw(_w, _draw);
        Assert.Equal(3, gizmo.Cache.Rebuilds);
        Assert.Equal(9 * RoadNetworkGizmo.CurveSamples, Roads().Count(p => p.Shape == DebugPrimitiveShape.Line && p.Color.Equals(RoadNetworkGizmo.RoadColor)));
    }

    /// <summary>
    /// ⭐ CE-11046 — the ground changes at RUNTIME (a crater bake, a ground patch, a breach, an obstacle bake): each publishes a NEW <c>TerrainWorld</c> over a NEW immutable
    /// <c>TerrainHeightGrid</c> (<c>Patched</c> is copy-on-write; <c>TerrainResidency.CommitObstacles</c> swaps the singleton). The cache keys on both references ⇒ the roads follow
    /// the patched ground. 🔴 Red-proof: key on the net alone ⇒ the roads stay at the old height.
    /// </summary>
    [Fact]
    public void CE11046_APatchedGroundGrid_RebuildsAndTheRoadsFollowIt()
    {
        var b = new RoadNetworkBuilder();
        b.AddNode(new Vector2(0, 0)); b.AddNode(new Vector2(100, 0));
        b.AddSegment(new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 0), new Vector2(100, 0), laneWidth: 3.5f, laneCount: 2, startNodeIdx: 0, endNodeIdx: 1);
        _blob = b.Build(cellSize: 20f, gridWidth: 10, gridHeight: 10);
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = _blob });

        int cols = 41, rows = 41; var z = new float[cols * rows]; Array.Fill(z, 10f);
        var grid = new Fdp.Toolkit.Terrain.TerrainHeightGrid(new Vector2(-50, -50), 5f, cols, rows, z);
        Fdp.Toolkit.Terrain.TerrainWorld World(Fdp.Toolkit.Terrain.TerrainHeightGrid g) => new() { Height = g, BoundsMin = new Vector2(-50, -50), BoundsMax = new Vector2(150, 150), GroundZ = 10f };
        _w.SetSingletonManaged(World(grid));
        var gizmo = new RoadNetworkGizmo();
        gizmo.Draw(_w, _draw);
        float zBefore = Roads()[0].LineStart.Z;
        Assert.Equal(10f + RoadNetworkGizmo.GroundLift, zBefore, 2);

        var patched = grid.Patched(0, 0, cols - 1, rows - 1, (c, r, h) => h + 30f);   // a dozer / crater bake: a new grid
        _w.SetSingletonManaged(World(patched));
        _draw.Clear(); gizmo.Draw(_w, _draw);
        Assert.Equal(2, gizmo.Cache.Rebuilds);
        Assert.Equal(40f + RoadNetworkGizmo.GroundLift, Roads()[0].LineStart.Z, 2);
    }

    private sealed class BoxView : Fdp.Toolkit.Diagnostics.Gizmos.IGizmoViewFilter
    {
        public Vector2 Min, Max;
        public bool Active => true; public float MetresPerPixel => 1f; public bool Is3D => false;
        public bool InView(ISimulationView view, Entity entity) => true;
        public bool Overlaps(Vector2 min, Vector2 max) => max.X >= Min.X && min.X <= Max.X && max.Y >= Min.Y && min.Y <= Max.Y;
    }

    /// <summary>⭐ CE-11046 — culled to the map's view: segments (and nodes) outside it are not emitted, those inside are. 🔴 Red-proof: no <c>Overlaps</c> test ⇒ every segment drawn.</summary>
    [Fact]
    public void CE11046_TheOverlay_IsCulledToTheView()
    {
        _blob = SeededNet(11, 40);
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = _blob });
        var gizmo = new RoadNetworkGizmo();
        gizmo.Draw(_w, _draw);
        int all = gizmo.Cache.LastSegmentsDrawn;
        Assert.Equal(40, all);

        var view = new BoxView { Min = new Vector2(-1000, -1000), Max = new Vector2(-300, -300) };   // a corner of the 800 m square
        Fdp.Toolkit.Diagnostics.Gizmos.GizmoViews.Register(_draw, view);
        _draw.Clear(); gizmo.Draw(_w, _draw);
        Assert.InRange(gizmo.Cache.LastSegmentsDrawn, 0, all - 1);
        Assert.Equal(1, gizmo.Cache.Rebuilds);                       // culling never rebuilds
    }
}
