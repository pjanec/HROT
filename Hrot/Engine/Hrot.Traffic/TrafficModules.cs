using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Traffic;
using NetworkLaneSource = Sim.Core.NetworkLaneSource;
using ILaneShapeSource = Sim.Core.ILaneShapeSource;
using Sim.Replication;
using Sim.Replication.Dds;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 EM-5 (R-4045) — steps SUMO OFF the main thread: a <c>SlowBackground</c> module whose tick is
/// <see cref="TrafficRuntime.SteppingTick"/>. It reads nothing from the world (the main thread hands it the target), so its snapshot
/// is filtered to one tiny component.
/// </summary>
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class TrafficSteppingModule : IEcsModule
{
    private readonly TrafficRuntime _runtime;

    public TrafficSteppingModule(TrafficRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public string Name => "TrafficStepping";

    /// <summary>20 Hz: a 0.5 s SUMO step is never more than a tick late; the target, not this rate, paces SUMO.</summary>
    /// <remarks>
    /// ⭐ CE-4020 — the time budget is the SESSION BUILD, not one step. 📌 Measured 2026-10-17 on the five-process cluster: the default
    /// <c>SlowBackground</c> budget (100 ms) timed out the tick that builds the session (SUMO parses the net: 2.5 s on demo-city) and
    /// every 40-step catch-up after it; five timeouts open the circuit for 10 s, the next catch-up is 40 steps again, and SUMO crawled.
    /// ⛔ An abandoned tick also keeps running while the next one starts, and <see cref="TrafficRuntime.SteppingTick"/> is not
    /// re-entrant. ⇒ <see cref="MaxTickMs"/> covers a build; a hang is still caught.
    /// </remarks>
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(20).WithTimeout(MaxTickMs);

    /// <summary>The longest one stepping tick may take before the host calls it hung: a session build plus a full catch-up.</summary>
    public const int MaxTickMs = 30_000;

    public IEnumerable<Type>? GetRequiredComponents() => new[] { typeof(TrafficDemand) };

    public void Tick(ISimulationView view, float deltaTime) => _runtime.SteppingTick();
}

/// <summary>
/// ⭐ AQ93 — each frame on the main thread, in the INPUT phase (cars are in place before the simulation reads the world, like any
/// network ingress), on EVERY node that composes traffic (§12 N6): mirror every SUMO wire this node can see — its
/// own session's in-memory wire when it SERVES traffic (N7), and every other traffic node's DDS wire, one per sender (H8, R-4055) — at
/// the SAME cluster instant (N3); keep the vicinity replicas (R-4041), each owned by its sender (N5); publish the frame for the maps.
/// </summary>
/// <remarks>
/// <para>⭐ <b>Serving</b> (a node built with a <see cref="TrafficRuntime"/>): it runs SUMO for a traffic-settings entity whose
/// <c>dtTrafficDemand</c> it OWNS — the Traffic ownership group its creating Brain granted it (S2, push-only G-6), or the whole entity on
/// a single-node host (the editor) — and only while the terrain has a SUMO net (🔒 R-4047). The target is the cluster clock: a paused
/// cluster holds SUMO. A clock that jumps BACK (a rewind) restarts SUMO: it cannot run backwards.</para>
/// <para>⭐ Cars are resolved one SUMO step behind the clock (the playout delay, the settings' <c>StepSeconds</c>), so the dead reckoning
/// interpolates between the two samples that bracket the instant.</para>
/// </remarks>
[UpdateInPhase(Fdp.ModuleHost.Abstractions.SystemPhase.Input)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class TrafficMirrorSystem : IEcsModuleSystem, Fdp.Interfaces.IReplayRestoreGated
{
    /// <summary>⭐ AQ93 §12 N4 — <c>EDescriptorType.dtTrafficDemand</c> (Hrot.Network.NED); a rail pins the two equal.</summary>
    public const long TrafficDemandDescriptor = 160;

    private readonly TrafficRuntime? _runtime;
    private readonly DdsParticipant? _participant;
    private readonly NetworkEntityMap? _entityMap;
    private readonly int _localNodeId;
    private readonly List<TrafficCarPose> _cars = new();
    private readonly List<TrafficPedPose> _peds = new();
    private readonly Dictionary<(int Owner, uint Source), TrafficPedMirror> _pedMirrors = new();
    private readonly List<(int, uint)> _stalePedMirrors = new();
    private readonly HashSet<(int, uint)> _livePedMirrors = new();
    private DdsMultiSourcePedSubscriber? _pedDds;
    private readonly Dictionary<(int Owner, uint Source), TrafficMirror> _mirrors = new();
    private readonly HashSet<(int, uint)> _liveMirrors = new();
    private readonly List<(int, uint)> _staleMirrors = new();
    private TrafficReplicas? _replicas;
    private EntityRepository? _replicaWorld;
    private DdsMultiSourceSubscriber? _dds;
    private double _wireNow;
    private SilenceClock _silence;
    private readonly System.Diagnostics.Stopwatch _wall = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>
    /// ⭐ CE-4021 (AQ93 E10, R-4027) — a traffic node that falls silent for this long, on the <see cref="SilenceClock"/>, is gone:
    /// its cars go. 📌 Measured 2026-10-17: the subscriber had no timeout, so after the traffic node was killed every mirror kept its
    /// cars, frozen, for good. ⭐ Not wall time: a paused cluster steps no SUMO and sends nothing, and must keep its cars.
    /// </summary>
    public const double SilentSourceSeconds = 5.0;

    /// <summary>The DDS reader of every traffic node's wire: samples stamped with SUMO sim time (E9), a source silent for
    /// <see cref="SilentSourceSeconds"/> of <paramref name="clusterSeconds"/> removed (E10).</summary>
    public static DdsMultiSourceSubscriber NewSubscriber(DdsParticipant participant, Func<double> clusterSeconds)
        => new(participant, stampWithSimTime: true, sourceTimeoutSeconds: SilentSourceSeconds, clockSeconds: clusterSeconds);
    private TrafficNetCache? _net;
    private NetworkLaneSource? _netLanes;
    private TrafficSession? _adopted;
    private IReadOnlyDictionary<int, byte> _lights = EmptyLights;
    private double _lastResolved = double.NaN;
    private int _generation;
    private TrafficFrame? _frame;
    private static readonly Dictionary<int, byte> EmptyLights = new();

    /// <param name="runtime">Non-null on a node that SERVES traffic (the traffic node, the editor).</param>
    /// <param name="participant">The node's DDS participant: null ⇒ no other node's traffic is mirrored (the networkless editor).</param>
    /// <param name="entityMap">The node's network entity map, so a replica answers to its network id.</param>
    /// <summary>
    /// ⭐ CE-4048 (AQ93 E5, R-4027: the mirror is gated in replay LIKE INGRESS, R-4012) — true while this node restores its own
    /// recording; set by <c>EcsRecordReplayController.PrepareReplayAsync</c> on every <see cref="Fdp.Interfaces.IReplayRestoreGated"/>
    /// system. ⛔ Ungated, a replay kept moving the recorded replicas with the LIVE wire, made new ones, and drew live cars over it.
    /// </summary>
    public Func<bool>? IsReplayRestoring { get; set; }

    public TrafficMirrorSystem(TrafficRuntime? runtime, DdsParticipant? participant, NetworkEntityMap? entityMap, int localNodeId)
    {
        _runtime = runtime;
        _participant = participant;
        _entityMap = entityMap;
        _localNodeId = localNodeId;
    }

    /// <summary>The cars of the last frame, every sender's, in terrain-local metres (for the car layer and rails).</summary>
    public IReadOnlyList<TrafficCarPose> Cars => _cars;

    /// <summary>⭐ AQ93 §15 K3 — the pedestrians of the last frame, every sender's, terrain-local.</summary>
    public IReadOnlyList<TrafficPedPose> Pedestrians => _peds;

    /// <summary>The replica book (null until the first frame).</summary>
    public TrafficReplicas? Replicas => _replicas;

    /// <summary>The senders mirrored last frame: (owner node, wire source).</summary>
    public IReadOnlyCollection<(int Owner, uint Source)> Senders => _mirrors.Keys;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world)
            throw new InvalidOperationException($"{nameof(TrafficMirrorSystem)} needs the live EntityRepository, not {view.GetType().Name}.");
        // ⭐ CE-4048 — the recording owns the world while this node restores it: no entity is touched (the replica bookkeeping is kept,
        //   so the next live frame reconciles the same entities), and the live frame the map draws is withdrawn (it is NoReplay).
        if (IsReplayRestoring?.Invoke() == true)
        {
            if (_frame is not null && world.HasSingletonManaged<TrafficFrame>()) world.SetSingletonManaged<TrafficFrame>(null!);
            _frame = null;
            return;
        }
        if (!ReferenceEquals(world, _replicaWorld))
        {
            _replicas?.Clear();
            _replicas = new TrafficReplicas(world, _entityMap, _localNodeId);
            _replicaWorld = world;
        }

        var net = TrafficNetCache.Of(world);
        if (net is null) { Stop(world); return; }
        if (!ReferenceEquals(net, _net)) { Stop(world); _net = net; _netLanes = new NetworkLaneSource(net.Model); }
        double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0.0;
        float step = TrafficDemand.Default.StepSeconds;

        // ── serve (N1/N7) ─────────────────────────────────────────────────────────────────────────
        TrafficSession? own = null;
        if (_runtime is not null)
        {
            if (TryServedDemand(world, out var demand))
            {
                step = demand.StepSeconds > 0f ? demand.StepSeconds : step;
                var key = new TrafficSessionKey(net.Path, Path.GetDirectoryName(net.Path) ?? ".", net.NetHash, demand, _generation);
                if (!Equals(_runtime.Requested, key)) _runtime.Request(key, TrafficVehicleCatalog.From(TkbOf(world)));
                _runtime.TopUpIds(demand.TargetCars + Math.Max(0, demand.TargetPedestrians));   // N2/K2 — the allocator is main-thread only
                _runtime.SetTarget(key, now);
                own = _runtime.Current;
                if (own is not null && now < own.Time - 2.0 * own.StepSeconds - 1.0)   // the clock jumped back: restart SUMO
                {
                    _generation++;
                    own = null;
                }
            }
            else _runtime.Request(null);
        }
        if (!ReferenceEquals(own, _adopted)) { _adopted = own; _lastResolved = double.NaN; }
        if (step == TrafficDemand.Default.StepSeconds && TryAnyDemand(world, out var anyDemand) && anyDemand.StepSeconds > 0f)
            step = anyDemand.StepSeconds;

        // ── mirror every wire (N5/N6) ─────────────────────────────────────────────────────────────
        double at = now - step;
        bool resolve = at != _lastResolved;   // paused ⇒ the same instant ⇒ the same poses; the reconstructor is never fed a zero step
        if (resolve) { _cars.Clear(); _peds.Clear(); }
        _liveMirrors.Clear();
        _livePedMirrors.Clear();
        _lights = EmptyLights;
        if (own is not null)
        {
            own.Source.Pump();
            Mirror(_localNodeId, (uint)_localNodeId, own.Source, own.Lanes, at, resolve);
            own.Pedestrians.Pump(now);
            MirrorPeds(_localNodeId, (uint)_localNodeId, own.Pedestrians, at, resolve);
        }
        if (_participant is not null)
        {
            _wireNow = _silence.Tick(now, _wall.Elapsed.TotalSeconds);
            _dds ??= NewSubscriber(_participant, () => _wireNow);
            _dds.Pump();
            foreach (var (src, source) in _dds.Sources)
            {
                if (_runtime is not null && src == (uint)_localNodeId) continue;   // our own steps, already read in memory (N7)
                int owner = _dds.SenderAppInstanceOf(src) ?? (int)src;
                Mirror(owner, src, source, _netLanes!, at, resolve);
            }
            // ⭐ §15 K3 — every traffic node's pedestrians (SumoSharp H14: one reconstructor per sender, sim-time poses)
            _pedDds ??= new DdsMultiSourcePedSubscriber(_participant, SilentSourceSeconds, () => _wireNow);
            _pedDds.Pump();
            foreach (var (src, peds) in _pedDds.Sources)
            {
                if (_runtime is not null && src == (uint)_localNodeId) continue;
                MirrorPeds(_pedDds.SenderAppInstanceOf(src) ?? (int)src, src, peds, at, resolve);
            }
        }
        _staleMirrors.Clear();
        foreach (var k in _mirrors.Keys) if (!_liveMirrors.Contains(k)) _staleMirrors.Add(k);
        foreach (var k in _staleMirrors) _mirrors.Remove(k);
        _stalePedMirrors.Clear();
        foreach (var k in _pedMirrors.Keys) if (!_livePedMirrors.Contains(k)) _stalePedMirrors.Add(k);
        foreach (var k in _stalePedMirrors) _pedMirrors.Remove(k);
        if (resolve) _lastResolved = at;

        _replicas!.Update(_cars, _peds);
        Publish(world);
    }

    private void Mirror(int owner, uint src, IReplicationSource source, ILaneShapeSource lanes, double at, bool resolve)
    {
        var k = (owner, src);
        _liveMirrors.Add(k);
        if (!_mirrors.TryGetValue(k, out var mirror)) _mirrors[k] = mirror = new TrafficMirror(_net!.Placement, owner);
        if (resolve) mirror.Resolve(source, lanes, at, _cars);
        if (_lights.Count == 0) _lights = source.TlStateByLane;
    }

    private void MirrorPeds(int owner, uint src, Sim.Pedestrians.Lod.PedRemoteReconstructor peds, double at, bool resolve)
    {
        var k = (owner, src);
        _livePedMirrors.Add(k);
        if (!_pedMirrors.TryGetValue(k, out var mirror)) _pedMirrors[k] = mirror = new TrafficPedMirror(_net!.Placement, owner);
        if (resolve) mirror.Resolve(peds, at, _peds);
    }

    private void Stop(EntityRepository world)
    {
        _runtime?.Request(null);
        _adopted = null;
        _mirrors.Clear();
        _pedMirrors.Clear();
        _cars.Clear();
        _peds.Clear();
        _replicas?.Clear();
        _lastResolved = double.NaN;
        _net = null;
        _netLanes = null;
        if (world.HasSingletonManaged<TrafficFrame>()) world.SetSingletonManaged<TrafficFrame>(null!);
        _frame = null;
    }

    // ⭐ One frame object: the car list is the live collection, re-published when the light table changes.
    private void Publish(EntityRepository world)
    {
        if (_frame is null || !ReferenceEquals(_frame.Lights, _lights))
        {
            var replicas = _replicas!;
            _frame = new TrafficFrame(_cars, replicas.IsEntity, _lights, _peds, replicas.IsPedReplica);
            world.RegisterManagedComponent<TrafficFrame>();
            world.SetSingletonManaged(_frame);
        }
    }

    /// <summary>S2 — a traffic-settings entity this node SERVES: it owns the entity's <c>dtTrafficDemand</c>.</summary>
    private static bool TryServedDemand(EntityRepository world, out TrafficDemand demand)
    {
        demand = default;
        if (!world.IsComponentTypeRegistered<TrafficDemand>()) return false;
        long key = OwnershipExtensions.PackKey(TrafficDemandDescriptor, 0);
        foreach (var e in world.Query().With<TrafficDemand>().Build())
        {
            if (!world.HasAuthority(e, key)) continue;
            demand = world.GetComponentRO<TrafficDemand>(e);
            return true;
        }
        return false;
    }

    private static bool TryAnyDemand(EntityRepository world, out TrafficDemand demand)
    {
        demand = default;
        if (!world.IsComponentTypeRegistered<TrafficDemand>()) return false;
        foreach (var e in world.Query().With<TrafficDemand>().Build()) { demand = world.GetComponentRO<TrafficDemand>(e); return true; }
        return false;
    }

    private static ITkbDatabase? TkbOf(EntityRepository world)
        => world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
}

/// <summary>
/// ⭐ CE-4021 — the clock a silent traffic node is measured on: WALL seconds that pass only while the cluster clock advances.
/// </summary>
/// <remarks>
/// 📌 Measured 2026-10-17: on cluster time itself IG lost every car for over 100 s — IG's slave clock discards sync results whose round
/// trip exceeds 200 ms (a loaded box) and corrects in JUMPS, and a jump of more than 5 s read as 5 s of silence, every time.
/// ⭐ So: paused (the cluster clock holds) ⇒ this clock holds; running ⇒ it runs at wall rate, at most one second a frame, whatever
/// the cluster clock jumps by.
/// </remarks>
public struct SilenceClock
{
    private double _lastCluster, _lastWall, _seconds;
    private bool _started;

    /// <summary>The clock after one frame: <paramref name="clusterSeconds"/> is the node's cluster time, <paramref name="wallSeconds"/>
    /// any monotonic wall clock.</summary>
    public double Tick(double clusterSeconds, double wallSeconds)
    {
        if (_started && clusterSeconds > _lastCluster) _seconds += Math.Clamp(wallSeconds - _lastWall, 0.0, 1.0);
        _started = true;
        _lastCluster = clusterSeconds;
        _lastWall = wallSeconds;
        return _seconds;
    }
}
