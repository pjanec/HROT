using Fdp.Toolkit.Traffic;
using Sim.LiveCity;
using Sim.Replication;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 EM-1/EM-5 (R-4045) — one running SUMO: SumoSharp's <see cref="LiveCitySim"/> (its proven engine + closed-loop car demand
/// + DR-gated replication publisher + in-memory wire) over the terrain's net, stepped to a target time OFF the main thread.
/// </summary>
/// <remarks>
/// <para>⭐ <b>Why <see cref="LiveCitySim"/> and not a new driver</b> (the seam law): it already is "an engine, random routable trips at a
/// density, the publisher with the built-in DR-error policy, an in-memory source" — the demo city's live traffic, tuned. Configured
/// here for HROT: <see cref="PedNavMode.RouteGraph"/> (the WHOLE net, no demo crop — R-4040 background traffic), pedestrians off
/// (slice 1), and <see cref="LiveCitySim.SelfPumpVehicleBus"/> false so the main thread alone drains the wire.</para>
/// <para>⛔ <b>Threads.</b> <see cref="StepTo"/> runs on the stepping module's thread; <see cref="Source"/> is pumped and read on the
/// main thread. The bus between them is SumoSharp's thread-safe queue. <see cref="Dispose"/> and <see cref="StepTo"/> share a gate so a
/// session swapped out mid-step is never disposed under its stepper.</para>
/// </remarks>
public sealed class TrafficSession : IDisposable
{
    /// <summary>The most SUMO steps one call catches up (a long hitch is not replayed in one go; the rest follows next tick).</summary>
    public const int MaxStepsPerCall = 40;

    private readonly LiveCitySim _sim;
    private readonly object _gate = new();
    private bool _disposed;

    public TrafficSession(TrafficSessionKey key)
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
        };
        _sim = new LiveCitySim(cfg) { SelfPumpVehicleBus = false };
        _sim.SetPedDensity(0, 0.0);
    }

    /// <summary>What this session was built from; a different key means a new session.</summary>
    public TrafficSessionKey Key { get; }

    /// <summary>The car wire. ⛔ Main thread only: <see cref="IReplicationSource.Pump"/> and every read.</summary>
    public IReplicationSource Source => _sim.VehicleSource;

    /// <summary>The net's lane shapes, for dead reckoning on the receiving side.</summary>
    public Sim.Core.NetworkLaneSource Lanes => _sim.LocalLanes;

    /// <summary>SUMO's own clock, seconds since the session started.</summary>
    public double Time => Volatile.Read(ref _time);
    private double _time;

    /// <summary>SUMO's step length (seconds).</summary>
    public double StepSeconds => _sim.Dt;

    /// <summary>Steps SUMO until its clock reaches <paramref name="sumoTime"/> (at most <see cref="MaxStepsPerCall"/> steps).
    /// A target that does not move — the editor paused — steps nothing.</summary>
    /// <returns>The steps taken.</returns>
    public int StepTo(double sumoTime)
    {
        lock (_gate)
        {
            if (_disposed) return 0;
            int steps = 0;
            while (steps < MaxStepsPerCall && _sim.Time + _sim.Dt <= sumoTime + 1e-9)
            {
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
        }
    }
}

/// <summary>What a <see cref="TrafficSession"/> is built from: the terrain's net (path + content hash, so a re-loaded terrain with
/// another net restarts SUMO), the scenario's demand, and a generation that a
/// rewind bumps (SUMO cannot run backwards, so a clock that jumps back restarts it).</summary>
public sealed record TrafficSessionKey(string NetFile, string Folder, string NetHash, TrafficDemand Demand, int Generation);
