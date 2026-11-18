using System;
using System.Linq;
using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
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
        Assert.Equal(2, roads.Count(p => p.Shape == DebugPrimitiveShape.Sphere));
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

    [Fact]
    public void CE3124_NoTerrainRoads_DrawNothing()
    {
        new RoadNetworkGizmo().Draw(_w, _draw);                                  // no singleton
        _w.SetSingleton(new ZoneEnvironmentData { RoadNetwork = default });      // a terrain with no roads
        new RoadNetworkGizmo().Draw(_w, _draw);
        Assert.Empty(Roads());
    }
}
