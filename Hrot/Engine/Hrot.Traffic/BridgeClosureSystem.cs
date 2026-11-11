using System.Numerics;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Terrain;
using NavMode = Fdp.Toolkit.Navigation.NavigationMode;

namespace Hrot.Traffic;

/// <summary>
/// ⭐⭐ T-7b (CE-4423, docs/DESIGN_Terrain_Authoring.md §18k) — a FALLEN BRIDGE stops being road, in both nets and for every vehicle on its way.
/// <para>ONE per node (<see cref="TrafficPack.PopulateSystems"/>: every node composes the pack, so every node that holds the roads holds this). Each frame it asks <see cref="BuildingDamageTable.Of"/> — the table the damage
/// system already caches per frame stamp, so a steady frame is one reference compare and allocates nothing — and, when the collapsed set moved, finds the structures the resident overlay's <see cref="TerrainBridges"/> (<c>TerrainDefinition.ResolvedBridges</c>) names:</para>
/// <list type="number">
///   <item><b>the vehicles' net</b> — the road network is republished WITHOUT the bridge's segments (<see cref="RoadNetworkEdit.Without"/>, into the node's <see cref="RoadNetworkHolder"/> AND the <see cref="ZoneEnvironmentData"/>
///     singleton, as <c>TerrainResidency</c> commits them): a route planned from now on cannot cross it. Every vehicle whose baked trajectory runs over the bridge, or that holds one of its segments, is RE-PLANNED through the one
///     replan (<see cref="NavigationExecutionSystem.RequestReplan"/>) — needs the node's trajectory pool, set by <c>RoleCapabilityTable.Plan</c> where the node has vehicles;</item>
///   <item><b>SUMO's net</b> — where this node RUNS SUMO (<see cref="TrafficRuntime"/> non-null), the bridge's SUMO edges are put in the runtime's closed set; the stepping thread tells the session (<c>Engine.CloseEdge</c>, which
///     re-routes the cars crossing it).</item>
/// </list>
/// A node with no <see cref="TerrainBridges"/> row, or no collapse, does nothing. Monotonic: a bridge stays fallen until the terrain reloads (a new <see cref="TerrainBridges"/>).
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class BridgeClosureSystem : IEcsModuleSystem
{
    private readonly TrafficRuntime? _runtime;
    private readonly Func<TrajectoryPoolManager?> _pool;

    private TerrainBridges? _bridges;
    private TerrainWorld? _terrain;
    private BuildingDamageTable? _table;
    private bool _seen;                                         // a table (or the lack of one) has been judged for _bridges / _terrain
    private readonly HashSet<string> _fallen = new(StringComparer.Ordinal);
    private readonly List<int> _dropped = new();                // the ORIGINAL indices of the road segments already removed, ascending
    private string[] _closed = Array.Empty<string>();

    /// <summary>Bridges this system has treated as fallen (rails / diagnostics).</summary>
    public int Fallen => _fallen.Count;

    /// <summary>Vehicles it asked to re-plan (rails / diagnostics).</summary>
    public int Replans { get; private set; }

    /// <param name="runtime">Where this node runs SUMO, else null.</param>
    /// <param name="trajectoryPool">The node's trajectory pool (null = no vehicle here to re-plan).</param>
    public BridgeClosureSystem(TrafficRuntime? runtime, Func<TrajectoryPoolManager?>? trajectoryPool = null)
    {
        _runtime = runtime;
        _pool = trajectoryPool ?? (() => null);
    }

    /// <inheritdoc/>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo) return;
        if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // a replay: the recording owns the world
        if (!repo.HasSingletonManaged<TerrainDefinition>() || !repo.HasSingletonManaged<TerrainWorld>()) return;
        var bridges = repo.GetSingletonManaged<TerrainDefinition>()?.ResolvedBridges;
        if (bridges == null) return;
        var terrain = repo.GetSingletonManaged<TerrainWorld>()!;
        if (!ReferenceEquals(bridges, _bridges) || !ReferenceEquals(terrain, _terrain)) Reset(bridges, terrain);
        if (bridges.Items.Count == 0) return;

        var table = BuildingDamageTable.Of(repo, terrain);
        if (_seen && ReferenceEquals(table, _table)) return;   // steady: the table is the same instance while nothing of it was written
        _seen = true; _table = table;
        if (table == null || table.CollapsedCount == 0) return;

        List<OverlayBridge>? fell = null;
        var collapsed = table.CollapsedBuildings;
        for (int i = 0; i < collapsed.Count; i++)
        {
            int b = collapsed[i];
            if ((uint)b >= (uint)terrain.Buildings.Count) continue;
            string label = terrain.Buildings[b].Label;
            for (int k = 0; k < bridges.Items.Count; k++)
            {
                var bridge = bridges.Items[k];
                if (!string.Equals(bridge.Key, label, StringComparison.Ordinal) || !_fallen.Add(bridge.Key)) continue;
                (fell ??= new()).Add(bridge);
            }
        }
        if (fell == null) return;
        Fall(repo, fell);
    }

    private void Reset(TerrainBridges bridges, TerrainWorld terrain)
    {
        _bridges = bridges; _terrain = terrain; _table = null; _seen = false;
        _fallen.Clear(); _dropped.Clear();
        if (_closed.Length > 0) { _closed = Array.Empty<string>(); _runtime?.SetClosedEdges(_closed); }
    }

    private int Current(int original)
    {
        int shift = 0;
        foreach (int d in _dropped) if (d < original) shift++;
        return original - shift;
    }

    private void Fall(EntityRepository repo, List<OverlayBridge> fell)
    {
        // SUMO — where this node runs it
        if (_runtime != null)
        {
            var edges = new List<string>(_closed);
            foreach (var b in fell) foreach (var e in b.SumoEdges) if (!edges.Contains(e)) edges.Add(e);
            _closed = edges.ToArray();
            _runtime.SetClosedEdges(_closed);
        }

        // the vehicles' net
        if (!repo.HasSingletonManaged<RoadNetworkHolder>()) return;
        var holder = repo.GetSingletonManaged<RoadNetworkHolder>()!;
        var blob = holder.Current;
        if (!blob.Segments.IsCreated || blob.Segments.Length == 0) return;

        var gone = new List<int>();   // in the CURRENT blob
        var corridors = new List<(Vector2 A, Vector2 B, float Half)>();
        int firstGone = int.MaxValue;
        foreach (var b in fell)
            foreach (int original in b.Segments)
            {
                int now = Current(original);
                if ((uint)now >= (uint)blob.Segments.Length || gone.Contains(now)) continue;
                gone.Add(now); firstGone = Math.Min(firstGone, now);
                ref readonly var s = ref blob.Segments[now];
                corridors.Add((s.P0, s.P1, s.LaneWidth * Math.Max(1, s.LaneCount) * 0.5f + 1.5f));
                _dropped.Add(original);
            }
        if (gone.Count == 0) return;
        _dropped.Sort();

        ReplanCrossing(repo, corridors, firstGone);

        var next = RoadNetworkEdit.Without(in blob, i => gone.Contains(i));
        holder.Publish(next);
        if (repo.HasSingleton<ZoneEnvironmentData>()) repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = next });
    }

    /// <summary>The share of a bridge segment at each end that does not count as "on the bridge": the junction where the base road meets it.</summary>
    private const float EndShare = 0.15f;

    private void ReplanCrossing(EntityRepository repo, List<(Vector2 A, Vector2 B, float Half)> corridors, int firstGoneSegment)
    {
        if (!repo.IsComponentTypeRegistered<NavigationIntent>() || !repo.IsComponentTypeRegistered<NavigationStatus>() || !repo.IsComponentTypeRegistered<NavState>()
            || !repo.IsComponentTypeRegistered<SimTransform>()) return;
        var pool = _pool();
        var query = repo.Query().With<NavigationIntent>().With<NavigationStatus>().With<NavState>().With<SimTransform>().Build();
        foreach (var entity in query)
        {
            var intent = repo.GetComponent<NavigationIntent>(entity);
            if (intent.Mode == NavMode.None) continue;
            var status = repo.GetComponent<NavigationStatus>(entity);
            if (status.Result != NavigationResult.InProgress) continue;
            var nav = repo.GetComponentRO<NavState>(entity);
            bool crosses = nav.Mode == KinematicsMode.RoadGraph && nav.CurrentSegmentId >= firstGoneSegment;   // a segment index at or past the first dropped one moves
            if (!crosses && nav.Mode == KinematicsMode.CustomTrajectory && pool != null && pool.TryGetTrajectory(nav.TrajectoryId, out var path))
                crosses = Crosses(in path, nav.ProgressS, corridors);
            if (!crosses) continue;
            var at = repo.GetComponentRO<SimTransform>(entity).Position;
            NavigationExecutionSystem.RequestReplan(repo, entity, in intent, ref status, at);
            Replans++;
        }
    }

    /// <summary>True when the REMAINING polyline of <paramref name="path"/> (from <paramref name="progress"/>) runs over the middle of a bridge segment: within its half-width, away from its two ends.</summary>
    public static bool Crosses(in CustomTrajectory path, float progress, List<(Vector2 A, Vector2 B, float Half)> corridors)
    {
        int n = path.Waypoints.Length;
        if (n < 2) return false;
        int i0 = 0;
        while (i0 + 1 < n && path.Waypoints[i0 + 1].CumulativeDistance < progress) i0++;
        for (int i = i0; i + 1 < n; i++)
        {
            var a = new Vector2(path.Waypoints[i].Position.X, path.Waypoints[i].Position.Y); var b = new Vector2(path.Waypoints[i + 1].Position.X, path.Waypoints[i + 1].Position.Y);
            float len = Vector2.Distance(a, b);
            int steps = Math.Max(1, (int)MathF.Ceiling(len / 2f));
            for (int s = 0; s <= steps; s++)
            {
                var p = a + (b - a) * ((float)s / steps);
                foreach (var (ca, cb, half) in corridors)
                {
                    var d = cb - ca; float l2 = d.LengthSquared();
                    if (l2 <= 0f) continue;
                    float t = Vector2.Dot(p - ca, d) / l2;
                    if (t < EndShare || t > 1f - EndShare) continue;
                    if (Vector2.Distance(p, ca + d * t) <= half) return true;
                }
            }
        }
        return false;
    }
}
