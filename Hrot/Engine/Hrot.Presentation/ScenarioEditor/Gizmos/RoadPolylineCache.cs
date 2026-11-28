using System;
using System.Numerics;
using CarKinem.Road;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐⭐ CE-11046 (HL-2, docs/DESIGN_Threaded_Simulation_Presentation.md §16) — the road overlay's curve samples WITH their ground heights, kept between frames.
/// 📐 Measured (unreal-ig, Stechovice): <c>RoadNetworkGizmo.Draw</c> was 28.9 % of the headless main thread, 19.3 % of it <c>TerrainWorld.GroundHeightAt</c> — a height
/// per curve sample of the WHOLE net every pass, though neither the net nor the terrain had changed.
/// <para>Rebuilt only when the net or the terrain is REPLACED: the key is the blob's three block addresses and lengths plus the <see cref="Fdp.Toolkit.Terrain.TerrainWorld"/>
/// and its height grid by reference (a terrain world is immutable — a bake or a reload publishes a new instance; residency replaces net and terrain together, so a block
/// address reused by a later net is still a new key). A steady frame is an index loop over arrays: 0 B, no height lookup; segments out of the map's view
/// (<see cref="GizmoViews"/>) are skipped.</para>
/// </summary>
public sealed class RoadPolylineCache
{
    private const int Pts = RoadNetworkGizmo.CurveSamples + 1;

    private nint _segAddr, _nodeAddr;
    private int _segCount, _nodeCount;
    private Fdp.Toolkit.Terrain.TerrainWorld? _terrain;
    private object? _terrainHeight;
    private bool _valid;

    private Vector3[] _pts = Array.Empty<Vector3>();      // segment s: [s * Pts .. s * Pts + Pts)
    private float[] _width = Array.Empty<float>();
    private Vector2[] _min = Array.Empty<Vector2>();      // the segment's ground box, padded by half its width
    private Vector2[] _max = Array.Empty<Vector2>();
    private Vector3[] _nodes = Array.Empty<Vector3>();

    /// <summary>How many times the samples were rebuilt (a rail: 1 after N steady frames; +1 per road or terrain change).</summary>
    public int Rebuilds { get; private set; }

    /// <summary>Segments / nodes the last <see cref="Draw"/> emitted (a rail; culling).</summary>
    public int LastSegmentsDrawn { get; private set; }

    private bool IsCurrent(in RoadNetworkBlob n, Fdp.Toolkit.Terrain.TerrainWorld? t)
        => _valid && _segAddr == n.Segments.Address && _nodeAddr == n.Nodes.Address && _segCount == n.Segments.Length && _nodeCount == n.Nodes.Length
           && ReferenceEquals(_terrain, t) && ReferenceEquals(_terrainHeight, t?.Height);

    /// <summary>Draws <paramref name="network"/> on <paramref name="terrain"/> from the cache (rebuilt first when the net or the terrain changed).</summary>
    public void Draw(in RoadNetworkBlob network, IDebugDrawBuilder draw, Fdp.Toolkit.Terrain.TerrainWorld? terrain)
    {
        LastSegmentsDrawn = 0;
        if (!network.Segments.IsCreated || !network.Nodes.IsCreated) { _valid = false; return; }
        if (!IsCurrent(in network, terrain)) Rebuild(in network, terrain);

        var view = GizmoViews.Of(draw);   // null (a headless host, a rail): everything in view
        var road = RoadNetworkGizmo.RoadColor; var centre = RoadNetworkGizmo.CentreColor;
        for (int s = 0; s < _segCount; s++)
        {
            if (view != null && !view.Overlaps(_min[s], _max[s])) continue;
            LastSegmentsDrawn++;
            float width = _width[s];
            int o = s * Pts;
            var a = _pts[o];
            for (int k = 1; k < Pts; k++)
            {
                var b = _pts[o + k];
                draw.DrawLine(a, b, road, width, SizeMode.WorldMeters, layer: DebugTraceLayers.Roads);
                draw.DrawLine(a, b, centre, 1f, SizeMode.ScreenPixels, layer: DebugTraceLayers.Roads);
                a = b;
            }
        }
        if (!draw.IsLayerActive(DebugTraceLayers.RoadNodes)) return;   // ⭐ CE-6504 — the nodes have their own row, off by default: no work while hidden
        for (int i = 0; i < _nodeCount; i++)
        {
            var p = _nodes[i];
            if (view != null && !view.Overlaps(new Vector2(p.X - 2f, p.Y - 2f), new Vector2(p.X + 2f, p.Y + 2f))) continue;
            draw.DrawSphere(p, 2f, RoadNetworkGizmo.NodeColor, layer: DebugTraceLayers.RoadNodes, fillColor: RoadNetworkGizmo.NodeColor);
        }
    }

    private void Rebuild(in RoadNetworkBlob n, Fdp.Toolkit.Terrain.TerrainWorld? terrain)
    {
        Rebuilds++;
        int sc = n.Segments.Length, nc = n.Nodes.Length;
        if (_pts.Length < sc * Pts) { _pts = new Vector3[sc * Pts]; _width = new float[sc]; _min = new Vector2[sc]; _max = new Vector2[sc]; }
        if (_nodes.Length < nc) _nodes = new Vector3[nc];
        for (int s = 0; s < sc; s++)
        {
            var seg = n.Segments[s];
            _width[s] = seg.LaneWidth * seg.LaneCount;
            int o = s * Pts;
            _pts[o] = RoadNetworkGizmo.On(terrain, seg.P0);
            var min = seg.P0; var max = seg.P0;
            for (int k = 1; k <= RoadNetworkGizmo.CurveSamples; k++)
            {
                var p = RoadGraphNavigator.EvaluateHermite(k / (float)RoadNetworkGizmo.CurveSamples, seg.P0, seg.T0, seg.P1, seg.T1);
                _pts[o + k] = RoadNetworkGizmo.On(terrain, p);
                min = Vector2.Min(min, p); max = Vector2.Max(max, p);
            }
            float pad = _width[s] * 0.5f + 1f;
            _min[s] = min - new Vector2(pad); _max[s] = max + new Vector2(pad);
        }
        for (int i = 0; i < nc; i++) _nodes[i] = RoadNetworkGizmo.On(terrain, n.Nodes[i].Position);
        _segAddr = n.Segments.Address; _nodeAddr = n.Nodes.Address; _segCount = sc; _nodeCount = nc;
        _terrain = terrain; _terrainHeight = terrain?.Height; _valid = true;
    }
}
