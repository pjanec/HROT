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
    // ⭐⭐ CE-4590 (P-2; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4 G-10, §9.2 V-19, V-20, V-24) — THE ROAD-BLOCKAGE STEP. The name is the
    //   first blocker it handled (a fallen bridge); it now takes EVERY static blocker: a fallen bridge's segments (by the overlay's row), and every road a
    //   static obstacle (a roadblock, a wreck) spans across its full width. ⭐ Rebuilt FROM THE BASE whenever the blocked set changes (V-19): a cleared
    //   roadblock RE-OPENS its road, and a seek, a reload or a bake cannot leave a stale cut — the published net is always Without(base, blocked now).
    //   SUMO: the edges a blocker closes (in SUMO's own lanes) + a fallen bridge's go into the closed set; a PARTLY covered road is the H12 body feed's
    //   (TrafficBodyFeedSystem tells SUMO each obstacle as a body at rest — a lane obstacle).
    private readonly TrafficRuntime? _runtime;
    private readonly Func<TrajectoryPoolManager?> _pool;

    private TerrainBridges? _bridges;
    private TerrainWorld? _terrain;
    private BuildingDamageTable? _table;
    private bool _seenTable;
    private ulong _obstacleSig = ulong.MaxValue;
    private CraterTable? _craters;
    private readonly Hrot.Map.Common.Services.ObstacleSignatureProbe _probe = new();
    private readonly HashSet<string> _fallen = new(StringComparer.Ordinal);

    private RoadNetworkBlob _base;          // ⭐ OUR copy of the base graph (the holder frees a retired one)
    private bool _haveBase;
    private int _publishedVersion = -1;     // the holder version of our last publish
    private readonly SortedSet<int> _blocked = new();      // ORIGINAL (base) indices dropped now
    private readonly HashSet<int> _scratch = new();
    private readonly List<System.Numerics.Vector2[]> _blockers = new();
    private readonly List<string> _sumoScratch = new();
    private string[] _closed = Array.Empty<string>();

    /// <summary>Bridges this system has treated as fallen (rails / diagnostics).</summary>
    public int Fallen => _fallen.Count;

    /// <summary>⭐ CE-4590 — road segments (base indices) cut right now, by any blocker (rails / diagnostics).</summary>
    public int Blocked => _blocked.Count;

    /// <summary>Vehicles it asked to re-plan (rails / diagnostics).</summary>
    public int Replans { get; private set; }

    /// <summary>⭐ CE-4590 — times the net was republished (rails / diagnostics).</summary>
    public int Republished { get; private set; }

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
        if (!repo.HasSingletonManaged<TerrainWorld>()) return;
        var terrain = repo.GetSingletonManaged<TerrainWorld>()!;
        var bridges = repo.HasSingletonManaged<TerrainDefinition>() ? repo.GetSingletonManaged<TerrainDefinition>()?.ResolvedBridges : null;
        var holder = repo.HasSingletonManaged<RoadNetworkHolder>() ? repo.GetSingletonManaged<RoadNetworkHolder>() : null;

        bool changed = false;
        if (!ReferenceEquals(bridges, _bridges)) { _bridges = bridges; _fallen.Clear(); _seenTable = false; changed = true; }
        if (!ReferenceEquals(terrain, _terrain)) { _terrain = terrain; _seenTable = false; }   // a bake's new world: re-judge the collapse table (the fallen set is kept by key)
        // ⭐ a NEW base: the residency published a graph we did not (a load, a reload) — copy it, judge everything again
        if (holder != null && holder.Version != _publishedVersion && holder.Current.Segments.IsCreated)
        {
            if (_haveBase) _base.Dispose();
            _base = RoadNetworkEdit.Without(holder.Current, static _ => false);
            _haveBase = true; _blocked.Clear(); _publishedVersion = holder.Version;
            changed = true;
        }

        // the fallen bridges (monotonic per terrain definition: a bridge stays fallen)
        if (bridges != null && bridges.Items.Count > 0)
        {
            var table = BuildingDamageTable.Of(repo, terrain);
            if (!_seenTable || !ReferenceEquals(table, _table))
            {
                _seenTable = true; _table = table;
                if (table != null && table.CollapsedCount > 0)
                {
                    var collapsed = table.CollapsedBuildings;
                    for (int i = 0; i < collapsed.Count; i++)
                    {
                        int b = collapsed[i];
                        if ((uint)b >= (uint)terrain.Buildings.Count) continue;
                        string label = terrain.Buildings[b].Label;
                        for (int k = 0; k < bridges.Items.Count; k++)
                            if (string.Equals(bridges.Items[k].Key, label, StringComparison.Ordinal) && _fallen.Add(bridges.Items[k].Key)) changed = true;
                    }
                }
            }
        }

        // the static obstacles (allocation-free hash: the same walk the obstacle bake uses)
        ulong sig = Hrot.Map.Common.Services.StaticObstacleBakeSystem.SignatureOfWorld(repo, _probe);
        if (sig != _obstacleSig) { _obstacleSig = sig; changed = true; }
        // ⭐ CE-4591 (P-4, G-10) — the craters: a crater across a road is a blocker like a roadblock (the view's table: the same instance while unchanged)
        var craters = repo.IsComponentTypeRegistered<GroundDamage>() ? DoorStates.Of(repo, terrain).Craters : null;
        if (!ReferenceEquals(craters, _craters)) { _craters = craters; changed = true; }

        if (!changed) return;
        Recompute(repo, holder);
    }

    private void Recompute(EntityRepository repo, RoadNetworkHolder? holder)
    {
        RoadBlockage.Blockers(repo, _blockers);
        RoadBlockage.AddCraters(_craters, _blockers);   // ⭐ CE-4591 — the craters' bowls

        // the set of base segments to drop now
        _scratch.Clear();
        if (_haveBase)
        {
            RoadBlockage.ClosedSegments(in _base, _blockers, _scratch);
            if (_bridges != null)
                foreach (var b in _bridges.Items)
                    if (_fallen.Contains(b.Key)) foreach (int s in b.Segments) if ((uint)s < (uint)_base.Segments.Length) _scratch.Add(s);
        }

        // SUMO — where this node runs it: a fallen bridge's edges + every edge a blocker closes
        if (_runtime != null)
        {
            var edges = new List<string>();
            if (_bridges != null) foreach (var b in _bridges.Items) if (_fallen.Contains(b.Key)) foreach (var e in b.SumoEdges) if (!edges.Contains(e)) edges.Add(e);
            if (TrafficNetCache.Of(repo) is { } net)
            {
                RoadBlockage.ClosedEdges(net.Model, net.Placement.ToLocal, _blockers, _sumoScratch);
                foreach (var e in _sumoScratch) if (!edges.Contains(e)) edges.Add(e);
            }
            edges.Sort(StringComparer.Ordinal);
            if (!edges.SequenceEqual(_closed)) { _closed = edges.ToArray(); _runtime.SetClosedEdges(_closed); }
        }

        if (!_haveBase || holder == null || _scratch.SetEquals(_blocked)) return;

        // corridors of the NEWLY dropped segments: vehicles whose way crosses them re-plan (a re-opened one needs nobody to re-plan)
        var corridors = new List<(System.Numerics.Vector2 A, System.Numerics.Vector2 B, float Half)>();
        foreach (int s in _scratch)
        {
            if (_blocked.Contains(s)) continue;
            ref readonly var seg = ref _base.Segments[s];
            corridors.Add((seg.P0, seg.P1, seg.LaneWidth * Math.Max(1, seg.LaneCount) * 0.5f + 1.5f));
        }
        _blocked.Clear(); foreach (int s in _scratch) _blocked.Add(s);

        var next = RoadNetworkEdit.Without(in _base, i => _blocked.Contains(i));
        holder.Publish(next);
        _publishedVersion = holder.Version;
        Republished++;
        if (repo.HasSingleton<ZoneEnvironmentData>()) repo.SetSingleton(new ZoneEnvironmentData { RoadNetwork = next });

        // ⛔ never in a replay: the recording owns every vehicle (the net itself follows the replayed blockers)
        if (!Fdp.Toolkit.Replay.ReplayState.IsActive(repo)) ReplanCrossing(repo, corridors);
    }

    /// <summary>The share of a bridge segment at each end that does not count as "on the bridge": the junction where the base road meets it.</summary>
    private const float EndShare = 0.15f;

    private void ReplanCrossing(EntityRepository repo, List<(Vector2 A, Vector2 B, float Half)> corridors)
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
            bool crosses = nav.Mode == KinematicsMode.RoadGraph;   // ⭐ CE-4590 — the net was rebuilt: a road-graph follower's segment indices may have moved
            if (!crosses && corridors.Count > 0 && nav.Mode == KinematicsMode.CustomTrajectory && pool != null && pool.TryGetTrajectory(nav.TrajectoryId, out var path))
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
