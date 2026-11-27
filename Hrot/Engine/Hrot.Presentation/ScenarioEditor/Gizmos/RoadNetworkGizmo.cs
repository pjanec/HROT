using System.Numerics;
using CarKinem.Road;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-3124</c> (R-228) — the loaded terrain's ROAD NETWORK, from the <see cref="ZoneEnvironmentData"/> world singleton that
/// terrain residency writes when a terrain with roads is committed (<c>TerrainResidency</c>, the one source; <c>RoadNetworkHolder</c>
/// is only its thread-safe projection). Each segment's curve as a grey band of its full lane width with a yellow centre line, each node
/// as a small blue dot — the drawing <c>SimHostRoadLayer</c> did with raylib on SimHost alone. Has data = can draw: it draws on
/// every host whose world holds a road network (SimHost, Editor, CGF, IG today; the Replay Browser once it loads the terrain,
/// <c>CE-3118</c>). Read on the main thread, where the singleton is also swapped. Toggled by the <c>Roads</c> layer.
/// 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
/// </summary>
[GizmoProjector]
public sealed class RoadNetworkGizmo : IGlobalStatelessGizmo
{
    public static readonly Rgba32 RoadColor   = new(130, 130, 130, 160);
    public static readonly Rgba32 CentreColor = new(253, 249, 0, 220);
    public static readonly Rgba32 NodeColor   = new(0, 121, 241, 230);

    /// <summary>Pieces a segment's curve is drawn in — a segment is a cubic Hermite (P0,T0 → P1,T1), evaluated by the same <see cref="RoadGraphNavigator.EvaluateHermite"/> the navigator uses;
    /// the old raylib layer drew the chord, which cut every bend.</summary>
    public const int CurveSamples = 8;


    /// <summary>⭐ CE-11046 (HL-2) — the net's curve samples WITH their ground heights, rebuilt only when the net or the terrain is replaced (a steady frame: 0 B, no height lookup).</summary>
    private readonly RoadPolylineCache _cache = new();

    /// <summary>⭐ CE-11046 — the cache a rail reads (rebuild count).</summary>
    public RoadPolylineCache Cache => _cache;

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.HasSingleton<ZoneEnvironmentData>()) return;
        if (!draw.IsLayerActive(DebugTraceLayers.Roads)) return;   // a hidden layer costs no work
        var network = repo.GetSingleton<ZoneEnvironmentData>().RoadNetwork;
        var terrain = repo.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() ? repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() : null;
        _cache.Draw(in network, draw, terrain);
    }

    /// <summary>⭐ CE-4401 — how far above the ground a road is drawn (m): on the surface, never inside it.</summary>
    public const float GroundLift = 0.05f;

    /// <summary>Draws <paramref name="network"/>; nothing when it holds no roads.</summary>
    public static void Draw(in RoadNetworkBlob network, IDebugDrawBuilder draw) => Draw(network, draw, null);

    /// <summary>
    /// Draws <paramref name="network"/> laid ON <paramref name="terrain"/>'s ground (<see cref="Fdp.Toolkit.Terrain.TerrainWorld.GroundHeightAt"/>), at z 0 with no terrain.
    /// ⭐ CE-4401 — the road graph is 2-D (SUMO's net, <see cref="RoadNetworkBlob"/>); 📐 before, every point was drawn at z 0, so on a terrain at ≈ 500 m
    /// (the scale rig's <c>scale-1</c>) the roads hung 500 m under the ground (user finding). Per frame: an index loop and a height lookup per point, 0 B.
    /// </summary>
    public static void Draw(in RoadNetworkBlob network, IDebugDrawBuilder draw, Fdp.Toolkit.Terrain.TerrainWorld? terrain)
    {
        if (!network.Segments.IsCreated || !network.Nodes.IsCreated) return;
        for (int i = 0; i < network.Segments.Length; i++)
        {
            var seg = network.Segments[i];
            float width = seg.LaneWidth * seg.LaneCount;
            var a = On(terrain, seg.P0);
            for (int k = 1; k <= CurveSamples; k++)
            {
                var b = On(terrain, RoadGraphNavigator.EvaluateHermite(k / (float)CurveSamples, seg.P0, seg.T0, seg.P1, seg.T1));
                draw.DrawLine(a, b, RoadColor, width, SizeMode.WorldMeters, layer: DebugTraceLayers.Roads);
                draw.DrawLine(a, b, CentreColor, 1f, SizeMode.ScreenPixels, layer: DebugTraceLayers.Roads);
                a = b;
            }
        }
        if (!draw.IsLayerActive(DebugTraceLayers.RoadNodes)) return;   // ⭐ CE-6504
        for (int i = 0; i < network.Nodes.Length; i++)
            draw.DrawSphere(On(terrain, network.Nodes[i].Position), 2f, NodeColor, layer: DebugTraceLayers.RoadNodes, fillColor: NodeColor);
    }

    /// <summary>A road point on the ground (z 0 with no terrain).</summary>
    public static Vector3 On(Fdp.Toolkit.Terrain.TerrainWorld? terrain, Vector2 p)
        => new(p, terrain == null ? 0f : terrain.GroundHeightAt(p.X, p.Y) + GroundLift);
}
