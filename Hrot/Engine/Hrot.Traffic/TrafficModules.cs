using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Traffic;
using Fdp.Toolkit.NetworkSpawning;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 EM-5 (R-4045) — steps SUMO OFF the main thread: a <c>SlowBackground</c> module whose tick is
/// <see cref="TrafficRuntime.SteppingTick"/>. It reads nothing from the world (the main thread hands it the target), so its snapshot
/// is filtered to one tiny component.
/// </summary>
public sealed class TrafficSteppingModule : IEcsModule
{
    private readonly TrafficRuntime _runtime;

    public TrafficSteppingModule(TrafficRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public string Name => "TrafficStepping";

    /// <summary>20 Hz: a 0.5 s SUMO step is never more than a tick late; the target, not this rate, paces SUMO.</summary>
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(20);

    public IEnumerable<Type>? GetRequiredComponents() => new[] { typeof(TrafficDemand) };

    public void Tick(ISimulationView view, float deltaTime) => _runtime.SteppingTick();
}

/// <summary>
/// ⭐ AQ93 R-4020 / R-4038 — the MAIN-thread half: <see cref="TrafficMirrorSystem"/> in the Input phase (cars are in place before the
/// simulation reads the world, like any network ingress).
/// </summary>
public sealed class TrafficMirrorModule : IEcsModule
{
    public TrafficMirrorModule(TrafficMirrorSystem system) => System = system ?? throw new ArgumentNullException(nameof(system));

    public TrafficMirrorSystem System { get; }

    public string Name => "TrafficMirror";

    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void RegisterSystems(ISystemRegistry registry) => registry.RegisterSystem(System);

    public void Tick(ISimulationView view, float deltaTime) { }
}

/// <summary>
/// ⭐ AQ93 — each frame on the main thread: decide whether SUMO runs (🔒 R-4047: only when the terrain has a net AND the scenario has
/// a <see cref="TrafficDemand"/>), hand the stepping thread its target on the editor's clock (pause ⇒ the target holds ⇒ SUMO
/// holds), pump SUMO's wire, dead-reckon every car (<see cref="TrafficMirror"/>) and keep the vicinity replicas
/// (<see cref="TrafficReplicas"/>).
/// </summary>
/// <remarks>
/// ⭐ SUMO time = the HROT clock minus the clock reading when this session was adopted. A clock that jumps BACK (a rewind) restarts
/// SUMO: it cannot run backwards, and fresh background traffic is the honest answer. ⭐ Cars are resolved one SUMO step behind the
/// newest sample (the playout delay), so the dead reckoning interpolates between the two samples that bracket the instant.
/// </remarks>
[UpdateInPhase(SystemPhase.Input)]
public sealed class TrafficMirrorSystem : IEcsModuleSystem
{
    private readonly TrafficRuntime _runtime;
    private readonly INetworkIdAllocator? _ids;
    private readonly int _localNodeId;
    private readonly List<TrafficCarPose> _cars = new();
    private TrafficReplicas? _replicas;
    private EntityRepository? _replicaWorld;
    private TrafficMirror? _mirror;
    private TrafficSession? _adopted;
    private double _origin;
    private double _lastResolved = double.NaN;
    private int _generation;

    public TrafficMirrorSystem(TrafficRuntime runtime, INetworkIdAllocator? ids, int localNodeId)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _ids = ids;
        _localNodeId = localNodeId;
    }

    /// <summary>The cars of the last frame, in terrain-local metres (for the car layer and rails).</summary>
    public IReadOnlyList<TrafficCarPose> Cars => _cars;

    /// <summary>The replica book (null until the first frame).</summary>
    public TrafficReplicas? Replicas => _replicas;

    /// <summary>SUMO's traffic-light state per lane handle, from the wire (empty before SUMO runs).</summary>
    public IReadOnlyDictionary<int, byte> TlStateByLane
        => (IReadOnlyDictionary<int, byte>?)_adopted?.Source.TlStateByLane ?? EmptyTl;
    private static readonly Dictionary<int, byte> EmptyTl = new();

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world)
            throw new InvalidOperationException($"{nameof(TrafficMirrorSystem)} needs the live EntityRepository, not {view.GetType().Name}.");
        if (!ReferenceEquals(world, _replicaWorld))
        {
            _replicas?.Clear();
            _replicas = new TrafficReplicas(world, _ids, _localNodeId);
            _replicaWorld = world;
        }

        var net = TrafficNetCache.Of(world);
        if (net is null || !TryDemand(world, out var demand))
        {
            Stop();
            return;
        }

        var key = new TrafficSessionKey(net.Path, Path.GetDirectoryName(net.Path) ?? ".", net.NetHash, demand, _generation);
        _runtime.Request(key);
        double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0.0;

        var session = _runtime.Current;
        if (session is null) { _cars.Clear(); _replicas!.Update(_cars); return; }   // the stepping thread is still building it
        if (!ReferenceEquals(session, _adopted))
        {
            _adopted = session;
            _origin = now;
            _mirror = new TrafficMirror(net.Placement);
            _lastResolved = double.NaN;
            _replicas!.Clear();
            _frame = null;
        }

        double sumoNow = now - _origin;
        if (sumoNow < session.Time - 2.0 * session.StepSeconds - 1.0)   // the clock jumped back: restart SUMO
        {
            _generation++;
            _cars.Clear();
            _replicas!.Clear();
            return;
        }
        _runtime.SetTarget(session.Key, sumoNow);

        session.Source.Pump();
        double at = sumoNow - session.StepSeconds;
        if (at != _lastResolved)   // paused ⇒ the same instant ⇒ the same poses; the reconstructor is never fed a zero step
        {
            _mirror!.Resolve(session.Source, session.Lanes, at, _cars);
            _lastResolved = at;
        }
        _replicas!.Update(_cars);
        Publish(world, session.Source.TlStateByLane);
    }

    private void Stop()
    {
        _runtime.Request(null);
        _adopted = null;
        _mirror = null;
        _cars.Clear();
        _replicas?.Clear();
        if (_replicaWorld is { } w && w.HasSingletonManaged<TrafficFrame>()) w.SetSingletonManaged<TrafficFrame>(null!);
        _frame = null;
    }

    private TrafficFrame? _frame;

    // ⭐ One frame object per session: the car list and the light table are the live collections, so a map reads THIS frame's.
    private void Publish(EntityRepository world, IReadOnlyDictionary<int, byte> lights)
    {
        if (_frame is null || !ReferenceEquals(_frame.Lights, lights))
        {
            var replicas = _replicas!;
            _frame = new TrafficFrame(_cars, replicas.IsReplica, lights);
            world.RegisterManagedComponent<TrafficFrame>();
            world.SetSingletonManaged(_frame);
        }
    }

    private static bool TryDemand(EntityRepository world, out TrafficDemand demand)
    {
        demand = default;
        if (!world.IsComponentTypeRegistered<TrafficDemand>()) return false;
        foreach (var e in world.Query().With<TrafficDemand>().Build())
        {
            demand = world.GetComponentRO<TrafficDemand>(e);
            return true;
        }
        return false;
    }
}
