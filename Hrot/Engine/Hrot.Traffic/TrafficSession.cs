using CycloneDDS.Runtime;
using Fdp.Toolkit.Traffic;
using Sim.LiveCity;
using Sim.Replication;
using Sim.Replication.Dds;

namespace Hrot.Traffic;

/// <summary>What a node that SERVES traffic gives its sessions: who it is on the wire and where car ids come from.</summary>
/// <param name="NextNetworkId">⭐ N2 (R-4054) — the cluster's id allocator: a car's network id IS its SUMO vehicle id.</param>
/// <param name="Participant">⭐ N7 — the node's DDS participant; null (the networkless editor) ⇒ no DDS tee.</param>
/// <param name="Source">⭐ H8 — this node's key on SUMO's topics (its node id), so several traffic nodes never collide (R-4055).</param>
public sealed record TrafficServeOptions(Func<long> NextNetworkId, DdsParticipant? Participant, uint Source);

/// <summary>
/// ⭐ AQ93 EM-1/EM-5 (R-4045), §12 N2/N3/N7 (R-4054, R-4056) — one running SUMO: SumoSharp's <see cref="LiveCitySim"/> (its proven engine +
/// closed-loop car demand + DR-gated publisher + in-memory wire) over the terrain's net, stepped to a target cluster time OFF the main thread.
/// </summary>
/// <remarks>
/// <para>⭐ The network's rules, met in ONE place so the editor obeys them too (R-4053): every car's id is a network id from the cluster's
/// allocator (H5); SUMO's clock STARTS at the cluster time the session starts (H6), so every sample on every wire is stamped in cluster
/// time; the car types are the TKB's (H9, each carrying its TKB type on the spawn record, H7); and when the node has a participant the
/// same steps are TEED to SumoSharp's DDS sink keyed by this node (H8) while its own mirror reads the in-memory wire (N7).</para>
/// <para>⭐ <see cref="LiveCitySim"/> in <see cref="PedNavMode.RouteGraph"/> mode (the whole net, R-4040), pedestrians off (their slice), and
/// <see cref="LiveCitySim.SelfPumpVehicleBus"/> false so the main thread alone drains the in-memory wire.</para>
/// <para>⛔ <b>Threads.</b> <see cref="StepTo"/> runs on the stepping module's thread; <see cref="Source"/> is pumped and read on the main
/// thread. <see cref="Dispose"/> and <see cref="StepTo"/> share a gate.</para>
/// </remarks>
public sealed class TrafficSession : IDisposable
{
    /// <summary>The most SUMO steps one call catches up (a long hitch is not replayed in one go; the rest follows next tick).</summary>
    public const int MaxStepsPerCall = 40;

    private readonly LiveCitySim _sim;
    private readonly DdsReplicationSink? _dds;
    private readonly object _gate = new();
    private bool _disposed;

    /// <param name="types">⭐ T1 — the TKB's car types; empty ⇒ SumoSharp's single default type.</param>
    /// <param name="startTime">⭐ N3 — the cluster sim time SUMO's clock starts at.</param>
    public TrafficSession(TrafficSessionKey key, TrafficServeOptions options, IReadOnlyList<TrafficVehicleType> types, double startTime)
    {
        Key = key;
        var demand = key.Demand;
        var cfg = new LiveCityConfig
        {
            DatasetDir = key.Folder,
            NetPath = key.NetFile,
            NavMode = PedNavMode.RouteGraph,
            CarTargetConcurrent = Math.Max(0, demand.TargetCars),
            CarRngSeed = 0x243F6A8885A308D3UL ^ demand.Seed,
            Dt = demand.StepSeconds > 0f ? demand.StepSeconds : TrafficDemand.Default.StepSeconds,
            PedPopulationCap = 0,
            PedSpawnRatePerSecond = 0.0,
            StartTime = startTime,
            VehicleIdProvider = () => options.NextNetworkId().ToString(System.Globalization.CultureInfo.InvariantCulture),
            VehicleTypes = types.Count == 0 ? null : types.Select(t => new LiveCityVehicleType(
                t.TkbType.ToString(System.Globalization.CultureInfo.InvariantCulture), t.VClass, t.Length, t.Width, t.MaxSpeed, t.Weight,
                ExternalType: t.TkbType, Color: 0u)).ToList(),
        };
        _dds = options.Participant is { } p ? new DdsReplicationSink(p, options.Source) : null;
        _sim = new LiveCitySim(cfg, recordVehSink: _dds) { SelfPumpVehicleBus = false };
        _sim.SetPedDensity(0, 0.0);
        StartTime = startTime;
        Volatile.Write(ref _time, startTime);
    }

    /// <summary>What this session was built from; a different key means a new session.</summary>
    public TrafficSessionKey Key { get; }

    /// <summary>The cluster time SUMO's clock started at.</summary>
    public double StartTime { get; }

    /// <summary>The car wire, in cluster time. ⛔ Main thread only: <see cref="IReplicationSource.Pump"/> and every read.</summary>
    public IReplicationSource Source => _sim.VehicleSource;

    /// <summary>The net's lane shapes, for dead reckoning on the receiving side.</summary>
    public Sim.Core.NetworkLaneSource Lanes => _sim.LocalLanes;

    /// <summary>SUMO's own clock — cluster sim time (N3).</summary>
    public double Time => Volatile.Read(ref _time);
    private double _time;

    /// <summary>SUMO's step length (seconds).</summary>
    public double StepSeconds => _sim.Dt;

    /// <summary>⭐ AQ93 §13 — our bodies for SUMO's next step (stepping thread, between steps).</summary>
    public void SetBodies(TrafficBody[] bodies) => _bodies = bodies ?? Array.Empty<TrafficBody>();
    private TrafficBody[] _bodies = Array.Empty<TrafficBody>();

    /// <summary>The bodies SUMO was last told about.</summary>
    public IReadOnlyList<TrafficBody> Bodies => _bodies;

    /// <summary>What SUMO made of our bodies (SumoSharp H12): lane obstacles and crowd discs. Null until bodies were first told.</summary>
    public Sim.Core.Bridge.ExternalBodyFeed? Feed => _sim.ExternalBodies;

    /// <summary>⭐ AQ93 §14 G3 — the vehicles handed to SUMO, for the next step (stepping thread).</summary>
    public void SetHandovers(TrafficHandover[] handovers) => _handovers = handovers ?? Array.Empty<TrafficHandover>();
    private TrafficHandover[] _handovers = Array.Empty<TrafficHandover>();
    private TrafficHandover[]? _appliedHandovers;

    // id → the intent its shadow serves; a shadow that ARRIVED stays here (not respawned) until the intent changes.
    private readonly Dictionary<long, uint> _shadows = new();
    private readonly Dictionary<long, double> _refused = new();   // id → when SUMO last refused it (retried after RetrySeconds)
    private readonly HashSet<long> _wanted = new();
    private readonly List<long> _drop = new();

    /// <summary>A refused hand-over (no lane near the vehicle, no route) is retried after this long, in SUMO time.</summary>
    public const double RetrySeconds = 2.0;

    /// <summary>The shadows SUMO is driving now (or has just arrived), by network id — rails and diagnostics.</summary>
    public IReadOnlyCollection<long> Shadows => _shadows.Keys;

    // ⭐ SumoSharp H13 (f252ddc): SpawnExternalVehicle routes "hrot:<id>" from the vehicle's lane to the destination's lane offset;
    //   DespawnExternalVehicle takes it back. Ownership never moves — the body's owner follows the shadow (TrafficShadowFollowSystem).
    private void ApplyHandovers()
    {
        var want = _handovers;
        if (ReferenceEquals(want, _appliedHandovers) && _refused.Count == 0) return;
        _appliedHandovers = want;
        _wanted.Clear();
        foreach (var h in want)
        {
            _wanted.Add(h.NetworkId);
            if (_shadows.TryGetValue(h.NetworkId, out var intent) && intent == h.IntentId) continue;   // driving, or arrived for this intent
            if (_shadows.ContainsKey(h.NetworkId))
            {
                _sim.DespawnExternalVehicle(TrafficShadowFollowSystem.ShadowIdOf(h.NetworkId));   // a NEW Traffic intent: a new route
                _shadows.Remove(h.NetworkId);
            }
            if (_refused.TryGetValue(h.NetworkId, out var at) && _sim.Time - at < RetrySeconds) continue;
            string id = TrafficShadowFollowSystem.ShadowIdOf(h.NetworkId);
            bool ok = h.VType is { } t && _sim.SpawnExternalVehicle(id, h.X, h.Y, h.Heading, h.Speed, h.DestX, h.DestY, t)
                      || _sim.SpawnExternalVehicle(id, h.X, h.Y, h.Heading, h.Speed, h.DestX, h.DestY);   // an unmapped type: SUMO's default car
            if (ok) { _shadows[h.NetworkId] = h.IntentId; _refused.Remove(h.NetworkId); }
            else _refused[h.NetworkId] = _sim.Time;
        }
        _drop.Clear();
        foreach (var id in _shadows.Keys) if (!_wanted.Contains(id)) _drop.Add(id);
        foreach (var id in _refused.Keys) if (!_wanted.Contains(id)) _drop.Add(id);
        foreach (var id in _drop)
        {
            if (_shadows.Remove(id)) _sim.DespawnExternalVehicle(TrafficShadowFollowSystem.ShadowIdOf(id));   // the intent changed: take it back
            _refused.Remove(id);
        }
    }

    private TrafficBody[]? _applied;
    private Sim.Core.Bridge.ExternalBody[] _external = Array.Empty<Sim.Core.Bridge.ExternalBody>();

    // ⭐ SumoSharp H12 (f91c0f4): a vehicle becomes a moving obstacle on every vehicle lane its footprint overlaps, anything else a
    //   crowd disc that cars brake for. Told only when the snapshot changed (the main thread writes a new array each frame).
    private void ApplyBodies()
    {
        var bodies = _bodies;
        if (ReferenceEquals(bodies, _applied)) return;
        _applied = bodies;
        if (_external.Length != bodies.Length) _external = new Sim.Core.Bridge.ExternalBody[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            var b = bodies[i];
            _external[i] = new Sim.Core.Bridge.ExternalBody(b.NetworkId,
                b.Kind == TrafficBodyKind.Vehicle ? Sim.Core.Bridge.ExternalBodyKind.Vehicle : Sim.Core.Bridge.ExternalBodyKind.Person,
                b.X, b.Y, b.Vx, b.Vy, b.Length, b.Width);
        }
        _sim.SetExternalBodies(_external);
    }

    /// <summary>Steps SUMO until its clock reaches <paramref name="clusterTime"/> (at most <see cref="MaxStepsPerCall"/> steps). A target
    /// that does not move — the cluster paused — steps nothing.</summary>
    /// <returns>The steps taken.</returns>
    public int StepTo(double clusterTime)
    {
        lock (_gate)
        {
            if (_disposed) return 0;
            int steps = 0;
            while (steps < MaxStepsPerCall && _sim.Time + _sim.Dt <= clusterTime + 1e-9)
            {
                if (steps == 0) { ApplyBodies(); ApplyHandovers(); }   // ⭐ AQ93 §13/§14 — before SUMO moves its cars
                _sim.Step();
                steps++;
            }
            Volatile.Write(ref _time, _sim.Time);
            return steps;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _sim.Dispose();
            _dds?.Dispose();
        }
    }
}

/// <summary>What a <see cref="TrafficSession"/> is built from: the terrain's net (path + content hash), the scenario's demand, and a
/// generation that a rewind bumps (SUMO cannot run backwards, so a clock that jumps back restarts it).</summary>
public sealed record TrafficSessionKey(string NetFile, string Folder, string NetHash, TrafficDemand Demand, int Generation);
