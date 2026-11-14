using System.Numerics;
using Sim.Ingest;
using Sim.Scene;

namespace Hrot.Traffic;

/// <summary>One kind's triangles, terrain-local metres, Z up, counter-clockwise from above.</summary>
/// <param name="Sidewalk">⭐ <c>CE-1197</c> — the pedestrian-only lanes (sidewalks and the walking areas at the corners), split out of
/// <see cref="SceneKind.Asphalt"/> here because <c>Sim.Scene</c> has no sidewalk kind (it is read-only in HROT, R-4046): their
/// <see cref="Kind"/> stays <see cref="SceneKind.Asphalt"/>, so "the road surface" is <c>Kind == Asphalt</c> and "the carriageway" is
/// <c>Kind == Asphalt &amp;&amp; !Sidewalk</c>.</param>
public sealed record TrafficRoadMesh(SceneKind Kind, Vector3[] Vertices, int[] Indices, bool Sidewalk = false)
{
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>A traffic-light head: the approach lane it shows (its state comes from SUMO's wire), its pole foot and its lamp.</summary>
public readonly record struct TrafficSignal(int LaneHandle, Vector3 Pole, Vector3 Head);

/// <summary>
/// ⭐ AQ93 V7/V8 (R-4049) — the SUMO net's static road scene for HROT's maps: SumoSharp's <c>Sim.Scene</c> geometry (the CityLib
/// builders, engine-agnostic: asphalt, junction fills, lane markings, crosswalks, signal heads) plus the AMBIENT GROUND — the
/// background floor under the net — with the cells over OUR terrain left out (V8), all in terrain-local metres.
/// </summary>
/// <remarks>
/// <para>⭐ Built once per terrain load, off the main thread (<see cref="SumoNetTerrainLoader"/>), and read by every map host.</para>
/// <para>⭐ Placement is per VERTEX through <see cref="SumoNetPlacement.ToLocal"/> in double precision: the scene is built in a frame
/// centred on the net (small floats, Sim.Scene's own precision rule) and every vertex goes frame → net → terrain-local, so a UTM net
/// placed through WGS84 (E1) lands exactly where the cars do — never a translation-only approximation.</para>
/// <para>⭐ Heights are SUMO's (R-4025) — ⭐ EXCEPT a FLAT net (no lane z), which is laid on OUR ground (R-4030 / E2b, <see cref="TrafficDrape"/>). ⚠ The seam blend between the ambient ground and our terrain (V8 "blended") is not built: the
/// ground simply stops at our footprint.</para>
/// </remarks>
public sealed class TrafficRoadScene
{
    public static readonly TrafficRoadScene Empty = new(Array.Empty<TrafficRoadMesh>(), Array.Empty<TrafficSignal>());

    public TrafficRoadScene(IReadOnlyList<TrafficRoadMesh> meshes, IReadOnlyList<TrafficSignal> signals, TrafficRoadPlan? plan = null)
    {
        Meshes = meshes;
        Signals = signals;
        Plan = plan ?? TrafficRoadPlan.Empty;
    }

    /// <summary>One mesh per kind present: Asphalt (the carriageway), the sidewalks (<see cref="TrafficRoadMesh.Sidewalk"/>), Junction,
    /// LaneMarking, Crosswalk, then AmbientGround.</summary>
    public IReadOnlyList<TrafficRoadMesh> Meshes { get; }

    public IReadOnlyList<TrafficSignal> Signals { get; }

    /// <summary>The same roads as the 2-D map draws them — a symbol (centrelines, light dots), never the meshes: <see cref="TrafficRoadPlan"/>.</summary>
    public TrafficRoadPlan Plan { get; }

    public int TriangleCount => Meshes.Sum(m => m.TriangleCount);

    /// <param name="terrainMin">Our terrain's footprint (local metres); a degenerate box ⇒ no footprint, the ground is everywhere.</param>
    /// <param name="ground">⭐ R-4030 (E2b) — our ground at a terrain-local (x, y), e.g. <c>TerrainWorld.GroundHeightAt</c>. Used ONLY when the net is flat
    /// (<see cref="TrafficDrape.IsFlat"/>): every vertex is then laid on it (long triangles subdivided so they follow the hills). A net
    /// with z, or no function, is placed exactly as the net says (R-4025).</param>
    public static TrafficRoadScene Build(NetworkModel net, SumoNetPlacement placement, Vector2 terrainMin, Vector2 terrainMax,
                                         Func<float, float, float>? ground = null)
    {
        if (net is null) throw new ArgumentNullException(nameof(net));
        if (placement is null) throw new ArgumentNullException(nameof(placement));

        var (minX, minY, maxX, maxY) = Bounds(net);
        // ⭐ a net WITH z (netconvert's own, or ours from NetElevation): the junction fills, walking areas and ambient ground have no z of their own (their outlines are 2-D
        // in the model) and take it from the field baked from the lanes' z — never a flat 0 under roads at 350 m. A flat net is laid on the ground below instead (R-4030).
        bool flatNet = TrafficDrape.IsFlat(net);
        var frame = new SceneFrame((minX + maxX) / 2.0, (minY + maxY) / 2.0, 0.0, flatNet ? null : TerrainField.FromNetwork(net, 10.0));
        var scene = RoadSceneBuilder.Build(net, frame);
        var drape = ground is not null && flatNet ? ground : null;

        var meshes = new List<TrafficRoadMesh>(scene.Meshes.Count + 2);
        foreach (var (kind, mesh) in scene.Meshes)
        {
            if (kind != SceneKind.Asphalt) { meshes.Add(ToLocal(kind, mesh, frame, placement)); continue; }
            // ⭐ CE-1197 — the same ribbons Sim.Scene merged into Asphalt (RoadMeshBuilder.BuildAll, internal lanes included), split by
            // who may use the lane: a pedestrian-only lane that is not a crossing (crossings stay road, under their zebra) is a sidewalk.
            var lanes = net.LanesByHandle.ToDictionary(l => l.Handle);
            var road = new List<SceneMesh>();
            var walk = new List<SceneMesh>();
            foreach (var (handle, ribbon) in RoadMeshBuilder.BuildAll(frame, net, includeInternal: true))
            {
                if (ribbon.IsEmpty) continue;
                var lane = lanes[handle];
                if (!IsSidewalk(lane)) { road.Add(ribbon); continue; }
                // a WALKING AREA (a junction-interior pedestrian lane, ':…_w…') is a corner's outline, not a centreline: filled as the
                // polygon it is (the junction fill's own triangulation), never a ribbon running round its edge
                var corner = lane.Id.StartsWith(':') ? JunctionMeshBuilder.Build(frame, lane.Shape) : SceneMesh.Empty;
                walk.Add(corner.IsEmpty ? ribbon : corner);
            }
            if (road.Count > 0) meshes.Add(ToLocal(SceneKind.Asphalt, SceneMesh.Merge(road), frame, placement));
            if (walk.Count > 0) meshes.Add(ToLocal(SceneKind.Asphalt, SceneMesh.Merge(walk), frame, placement) with { Sidewalk = true });
        }

        bool hasFootprint = terrainMax.X > terrainMin.X && terrainMax.Y > terrainMin.Y;
        var floor = AmbientGroundBuilder.Build(frame, minX, minY, maxX, maxY,
            excludeCell: hasFootprint
                ? (cx, cy) =>
                {
                    var (lx, ly) = placement.ToLocal(cx, cy);
                    return lx >= terrainMin.X && lx <= terrainMax.X && ly >= terrainMin.Y && ly <= terrainMax.Y;
                }
                : null);
        if (!floor.IsEmpty) meshes.Add(ToLocal(SceneKind.AmbientGround, floor, frame, placement));

        if (drape is not null)
            for (int i = 0; i < meshes.Count; i++)
                meshes[i] = TrafficDrape.Mesh(meshes[i], drape, meshes[i].Kind == SceneKind.AmbientGround ? TrafficDrape.GroundStep : TrafficDrape.RoadStep);

        var signals = new List<TrafficSignal>(scene.SignalHeads.Count);
        foreach (var h in scene.SignalHeads)
        {
            var pole = Local(frame, placement, h.PoleX, h.PoleY, h.PoleZ);
            var head = Local(frame, placement, h.HeadX, h.HeadY, h.HeadZ);
            if (drape is not null) { pole = TrafficDrape.Point(pole, drape); head = TrafficDrape.Point(head, drape); }   // ⭐ R-4030 — poles and heads stand on the ground
            signals.Add(new TrafficSignal(h.LaneHandle, pole, head));
        }

        return new TrafficRoadScene(meshes, signals, TrafficRoadPlan.For(net, placement, signals.Count));
    }

    /// <summary>⭐ <c>CE-1197</c> — a lane only pedestrians may use (<c>allow="pedestrian"</c>: a sidewalk or a walking area), not a crossing.</summary>
    public static bool IsSidewalk(Lane lane) => !lane.AllowsRoadVehicle && !CrosswalkBuilder.IsCrossingLaneId(lane.Id);

    private static TrafficRoadMesh ToLocal(SceneKind kind, SceneMesh mesh, SceneFrame frame, SumoNetPlacement placement)
    {
        var v = mesh.Vertices;
        var verts = new Vector3[v.Length / 3];
        for (int i = 0; i < verts.Length; i++)
            verts[i] = Local(frame, placement, v[3 * i], v[3 * i + 1], v[3 * i + 2]);
        return new TrafficRoadMesh(kind, verts, mesh.Indices);
    }

    private static Vector3 Local(SceneFrame frame, SumoNetPlacement placement, float fx, float fy, float fz)
    {
        var (x, y) = placement.ToLocal(fx + frame.OriginX, fy + frame.OriginY);
        return new Vector3((float)x, (float)y, (float)(fz + frame.OriginZ));
    }

    /// <summary>⭐ CE-4122 — the net's bounds in NET metres: the rectangle the SUMO ambient ground covers (and so the footprint of the ground beyond our terrain).</summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) NetBounds(NetworkModel net) => Bounds(net);

    private static (double MinX, double MinY, double MaxX, double MaxY) Bounds(NetworkModel net)
    {
        if (net.Location is { } loc && loc.ConvBoundary.MaxX > loc.ConvBoundary.MinX)
            return loc.ConvBoundary;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var lane in net.LanesByHandle)
            foreach (var p in lane.Shape)
            {
                minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
            }
        return minX <= maxX ? (minX, minY, maxX, maxY) : (0, 0, 0, 0);
    }
}
