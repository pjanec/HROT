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
    [Fdp.Core.WorldDerived("cached collapse table, re-judged when the table instance changes")]
    private BuildingDamageTable? _table;
    [Fdp.Core.WorldDerived("flag: collapse table already judged")]
    private bool _seenTable;
    [Fdp.Core.WorldDerived("static-obstacle signature for change detection")]
    private ulong _obstacleSig = ulong.MaxValue;
    private CraterTable? _craters;
    private readonly Hrot.Map.Common.Services.ObstacleSignatureProbe _probe = new();
    [Fdp.Core.WorldDerived("fallen bridge keys re-derived from the collapsed buildings table (monotonic per definition)")]
    private readonly HashSet<string> _fallen = new(StringComparer.Ordinal);

    [Fdp.Core.WorldDerived("own copy of the base road graph made from the holder (native blob)")]
    private RoadNetworkBlob _base;          // ⭐ OUR copy of the base graph (the holder frees a retired one)
    [Fdp.Core.WorldDerived("flag: a base copy exists")]
    private bool _haveBase;
    [Fdp.Core.WorldDerived("holder version of the last publish: change detection")]
    private int _publishedVersion = -1;     // the holder version of our last publish
    [Fdp.Core.WorldDerived("blocked segment indices recomputed from blockers and fallen bridges")]
    private readonly SortedSet<int> _blocked = new();      // ORIGINAL (base) indices dropped now
    [Fdp.Core.FrameScratch("closed segments set: cleared at 144 each Recompute")]
    private readonly HashSet<int> _scratch = new();
    [Fdp.Core.FrameScratch("blocker polygons list refilled by Blockers/AddCraters/AddMinefields each Recompute")]
    private readonly List<System.Numerics.Vector2[]> _blockers = new();
    [Fdp.Core.FrameScratch("SUMO edges list refilled by ClosedEdges each Recompute")]
    private readonly List<string> _sumoScratch = new();
    [Fdp.Core.ProtocolCache("closed edges last pushed to the SUMO runtime: change test of what was sent")]
    private string[] _closed = Array.Empty<string>();
    private int _worldEpoch;

    // ⭐⭐ CE-4692 BL-7 (📄 docs/designs/engineering/DESIGN_Bridge_Laying.md §3) — the ROAD ADD: the published net is base − blocked + ADDED, the added part one segment per OPEN laid bridge
    //   (a WalkableDeck obstacle that is Active — its bake committed) whose deck ends snap to two base nodes (BridgeRoads.Plan, the authored bridges' own rule, over the run-time net).
    //   Base and added are kept apart: the net is always rebuilt from the base, so a recovered or fallen bridge leaves the base exactly as it was.
    [Fdp.Core.WorldDerived("signature of the open laid bridges: change detection")]
    private ulong _deckSig;
    private EntityRepository? _deckFor;
    private EntityQuery? _deckQuery;
    [Fdp.Core.WorldDerived("the laid bridges' road segments now in the published net, recomputed from the open decks")]
    private List<(int NodeA, int NodeB, float SpeedLimit, float LaneWidth)> _added = new();

    /// <summary>⭐ CE-4692 — laid-bridge road segments in the published net now (rails / diagnostics).</summary>
    public int Added => _added.Count;

    /// <summary>A hash of the OPEN laid bridges (Active WalkableDeck obstacles: index, place, heading, span), allocation-free over a cached query.</summary>
    private ulong DeckSignature(EntityRepository repo)
    {
        ulong h = 1469598103934665603UL;
        if (!repo.IsComponentTypeRegistered<StaticObstacle>() || !repo.IsComponentTypeRegistered<ObstacleShape>()) return h;
        if (!ReferenceEquals(repo, _deckFor) || _deckQuery == null) { _deckQuery = repo.Query().With<StaticObstacle>().With<ObstacleShape>().With<SimTransform>().Build(); _deckFor = repo; }
        foreach (var e in _deckQuery)
        {
            if (repo.GetComponentRO<StaticObstacle>(e).Kind != ObstacleKind.WalkableDeck) continue;
            ref readonly var t = ref repo.GetComponentRO<SimTransform>(e);
            h ^= (ulong)e.Index; h *= 1099511628211UL;
            h ^= (ulong)(long)MathF.Round(t.Position.X * 10f); h *= 1099511628211UL;
            h ^= (ulong)(long)MathF.Round(t.Position.Y * 10f); h *= 1099511628211UL;
            h ^= (ulong)(long)MathF.Round(TerrainObstacles.Yaw(t.Rotation) * 1000f); h *= 1099511628211UL;
            h ^= (ulong)(long)MathF.Round(repo.GetComponentRO<ObstacleShape>(e).Length * 10f); h *= 1099511628211UL;
        }
        return h;
    }

    /// <summary>The road segments of the open laid bridges over <see cref="_base"/>: each one's deck ends snapped to base nodes (<see cref="BridgeRoads.Plan(IEnumerable{ValueTuple{string, Vector2, Vector2}}, int, BridgeRoads.NearestNode?)"/>).</summary>
    private List<(int, int, float, float)> PlanAdded(EntityRepository repo)
    {
        var added = new List<(int, int, float, float)>();
        if (!_haveBase || _deckQuery == null) return added;
        var decks = new List<(string Key, Vector2 A, Vector2 B)>();
        foreach (var e in _deckQuery)
        {
            if (repo.GetComponentRO<StaticObstacle>(e).Kind != ObstacleKind.WalkableDeck) continue;
            ref readonly var t = ref repo.GetComponentRO<SimTransform>(e);
            var (a, b) = DeckGeometry.Ends(new Vector2(t.Position.X, t.Position.Y), TerrainObstacles.Yaw(t.Rotation), repo.GetComponentRO<ObstacleShape>(e).Length);
            decks.Add((e.Index.ToString("D10", System.Globalization.CultureInfo.InvariantCulture), a, b));
        }
        if (decks.Count == 0) return added;
        var nodes = new List<Vector2>(_base.Nodes.Length);
        for (int i = 0; i < _base.Nodes.Length; i++) nodes.Add(_base.Nodes[i].Position);
        foreach (var plan in BridgeRoads.Plan(decks, _base.Segments.Length, (Vector2 p, float r, out int i, out Vector2 at) => BridgeRoads.NearestNodeOf(nodes, p, r, out i, out at)))
            if (plan.Snapped) added.Add((plan.NodeA, plan.NodeB, BridgeRoads.SpeedLimit, BridgeRoads.LaneWidth));
        return added;
    }

    /// <summary>Bridges this system has treated as fallen (rails / diagnostics).</summary>
    public int Fallen => _fallen.Count;

    /// <summary>⭐ CE-4590 — road segments (base indices) cut right now, by any blocker (rails / diagnostics).</summary>
    public int Blocked => _blocked.Count;

    /// <summary>Vehicles it asked to re-plan (rails / diagnostics).</summary>
    [field: Fdp.Core.WorldDerived("diagnostic counter of replans")]
    public int Replans { get; private set; }

    /// <summary>⭐ CE-4590 — times the net was republished (rails / diagnostics).</summary>
    [field: Fdp.Core.WorldDerived("diagnostic counter of republishes")]
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
        if (Fdp.Toolkit.Replication.Services.WorldEpoch.Moved(view, ref _worldEpoch)) _closed = Array.Empty<string>();   // ⭐ R-4297 ④ (CE-1980) — a world replacement forgets the closures last pushed to the traffic (it restarts with the world)
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
            _haveBase = true; _blocked.Clear(); _added = new(); _publishedVersion = holder.Version;
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
        // ⭐ CE-4624 MS-T3 (MR-16) — the minefields: a MARKED field (or one known at start) across a road closes it like a roadblock (the view's table: the same instance while unchanged)
        var knowledge = DoorStates.Of(repo, terrain).Knowledge;
        if (!ReferenceEquals(knowledge, _knowledge)) { _knowledge = knowledge; changed = true; }
        // ⭐ CE-4692 BL-7 — the open laid bridges: their own change test (a bridge OPENS when its bake commits — Constructing → Active, which the obstacle signature does not see)
        ulong deckSig = DeckSignature(repo);
        if (deckSig != _deckSig) { _deckSig = deckSig; changed = true; }

        // ⭐⭐ CE-11070 (DESIGN_Breachable_Obstacle_Walls.md §8 V-1, V-2) — the scenario feature WALLS: a wall across a lane closes it until a gap wide enough to drive through is cut.
        //   Judged from the panels and the REPLICATED breach table (every node the same), re-judged only when the walls' geometry or the Passages on them change — a house hole, a Hole
        //   in a wall or a bake's new world with the same walls leave the signature as it was (the table reference and the world are compared first: a steady frame does nothing).
        var damage = repo.IsComponentTypeRegistered<BuildingDamage>() ? BuildingDamageTable.Of(repo, terrain) : null;
        if (!ReferenceEquals(terrain, _wallWorld) || !ReferenceEquals(damage, _wallTable))
        {
            _wallWorld = terrain; _wallTable = damage;
            ulong wallSig = RoadBlockage.WallSignature(terrain, damage);
            if (wallSig != _wallSig) { _wallSig = wallSig; changed = true; }
        }

        if (!changed) return;
        Recompute(repo, holder);
    }

    [Fdp.Core.WorldDerived("the world the wall signature was taken from: change detection")]
    private TerrainWorld? _wallWorld;
    [Fdp.Core.WorldDerived("the breach table the wall signature was taken from: change detection")]
    private BuildingDamageTable? _wallTable;
    [Fdp.Core.WorldDerived("signature of the scenario wall lines and their Passages: change detection")]
    private ulong _wallSig;
    [Fdp.Core.FrameScratch("scenario wall lines refilled by FeatureWallLines each Recompute")]
    private readonly List<RoadBlockage.WallLine> _walls = new();

    /// <summary>⭐ CE-11070 — scenario feature wall lines judged at the last recompute (rails / diagnostics).</summary>
    public int Walls => _walls.Count;

    [Fdp.Core.WorldDerived("cached knowledge table instance used for change detection")]
    private PlaceKnowledgeTable? _knowledge;

    /// <summary>⭐ CE-11070 — times the blocked set was recomputed (rails: a change that is no change — a house hole — must not recompute).</summary>
    [field: Fdp.Core.WorldDerived("diagnostic counter of recomputes")]
    public int Recomputes { get; private set; }

    private void Recompute(EntityRepository repo, RoadNetworkHolder? holder)
    {
        Recomputes++;
        RoadBlockage.Blockers(repo, _blockers);
        RoadBlockage.AddCraters(_craters, _blockers);   // ⭐ CE-4591 — the craters' bowls
        RoadBlockage.AddMinefields(repo, _knowledge, _blockers);   // ⭐ CE-4624 — the marked minefields' outlines
        if (_wallWorld != null) RoadBlockage.FeatureWallLines(_wallWorld, _wallTable, ContactBreachTable.Shared.MinDriveThroughM, _walls);   // ⭐ CE-11070
        else _walls.Clear();

        // the set of base segments to drop now
        _scratch.Clear();
        if (_haveBase)
        {
            RoadBlockage.ClosedSegments(in _base, _blockers, _scratch);
            RoadBlockage.WallClosedSegments(in _base, _walls, _scratch);   // ⭐ CE-11070 — a segment every lane of which a wall closes
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
                _sumoScratch.Clear();
                RoadBlockage.WallClosedEdges(net.Model, net.Placement.ToLocal, _walls, _sumoScratch);   // ⭐ CE-11070 — per lane: an edge whose every lane a wall closes
                foreach (var e in _sumoScratch) if (!edges.Contains(e)) edges.Add(e);
            }
            edges.Sort(StringComparer.Ordinal);
            if (!edges.SequenceEqual(_closed))
            {
                _closed = edges.ToArray(); _runtime.SetClosedEdges(_closed);
                Fdp.Core.Logging.FdpLog<BridgeClosureSystem>.Info("[Traffic] SUMO closed edges now [{0}] ({1} scenario wall line(s) judged)", string.Join(", ", _closed), _walls.Count);   // ⭐ CE-11072 — cold: on a change only
            }
        }

        var added = PlanAdded(repo);   // ⭐ CE-4692 BL-7
        if (!_haveBase || holder == null || (_scratch.SetEquals(_blocked) && added.SequenceEqual(_added))) return;
        _added = added;

        // corridors of the NEWLY dropped segments: vehicles whose way crosses them re-plan (a re-opened one needs nobody to re-plan)
        var corridors = new List<(System.Numerics.Vector2 A, System.Numerics.Vector2 B, float Half)>();
        foreach (int s in _scratch)
        {
            if (_blocked.Contains(s)) continue;
            ref readonly var seg = ref _base.Segments[s];
            corridors.Add((seg.P0, seg.P1, seg.LaneWidth * Math.Max(1, seg.LaneCount) * 0.5f + 1.5f));
        }
        _blocked.Clear(); foreach (int s in _scratch) _blocked.Add(s);

        var next = RoadNetworkEdit.Edited(in _base, i => _blocked.Contains(i), _added);   // ⭐ CE-4692 — base − blocked + added
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
