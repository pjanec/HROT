using Fdp.Toolkit.Traffic;

namespace Hrot.Traffic;

/// <summary>
/// ⭐ AQ93 EM-5 (R-4045) — the hand-off between the MAIN thread (which decides what SUMO should run and how far) and the STEPPING
/// thread (which builds and steps it). One per node that SERVES traffic.
/// </summary>
/// <remarks>
/// ⭐ <b>The one invariant that makes it safe:</b> only the main thread writes the REQUEST; the stepping thread disposes a session
/// only when its key no longer equals the request, and the main thread only touches a session whose key equals it. So no session
/// is ever pumped after it was disposed. The car wire between them is SumoSharp's thread-safe queue.
/// </remarks>
public sealed class TrafficRuntime : IDisposable
{
    private readonly TrafficServeOptions _options;
    private TrafficSessionKey? _requested;
    private IReadOnlyList<TrafficVehicleType> _types = Array.Empty<TrafficVehicleType>();
    private TrafficSession? _session;
    private TrafficSessionKey? _targetKey;
    private double _target;
    private TrafficSessionKey? _failed;
    private readonly object _targetGate = new();

    public TrafficRuntime(TrafficServeOptions options) => _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>The session the stepping thread built last (may lag the request by a tick or two).</summary>
    public TrafficSession? Session => Volatile.Read(ref _session);

    /// <summary>The session the main thread asked for; null ⇒ no SUMO at all.</summary>
    public TrafficSessionKey? Requested => Volatile.Read(ref _requested);

    /// <summary>SUMO steps taken so far, for diagnostics and rails.</summary>
    public long StepsTaken => Interlocked.Read(ref _steps);
    private long _steps;

    // ── main thread ────────────────────────────────────────────────────────────────────────────

    /// <summary>Asks for <paramref name="key"/> (null stops SUMO), its cars drawn from <paramref name="types"/> (T1, read from the TKB on
    /// the main thread). ⛔ Main thread only.</summary>
    public void Request(TrafficSessionKey? key, IReadOnlyList<TrafficVehicleType>? types = null)
    {
        if (Equals(Volatile.Read(ref _requested), key)) return;
        Volatile.Write(ref _types, types ?? Array.Empty<TrafficVehicleType>());
        Volatile.Write(ref _requested, key);
    }

    /// <summary>The session for the current request, once the stepping thread has built it. ⛔ Main thread only.</summary>
    public TrafficSession? Current
    {
        get
        {
            var s = Volatile.Read(ref _session);
            var want = Volatile.Read(ref _requested);
            return s is not null && want is not null && Equals(s.Key, want) ? s : null;
        }
    }

    /// <summary>How far SUMO may run (cluster sim time); also the time a NEW session starts at (N3). ⛔ Main thread only.</summary>
    public void SetTarget(TrafficSessionKey key, double clusterTime)
    {
        lock (_targetGate) { _targetKey = key; _target = clusterTime; }
    }

    // ── stepping thread ────────────────────────────────────────────────────────────────────────

    /// <summary>One stepping tick: (re)build the requested session if needed, then step it to its target.</summary>
    public void SteppingTick()
    {
        var want = Volatile.Read(ref _requested);
        var have = Volatile.Read(ref _session);
        if (have is not null && !Equals(have.Key, want))
        {
            Volatile.Write(ref _session, null);
            have.Dispose();
            have = null;
        }
        if (want is null) return;

        double target;
        bool targeted;
        lock (_targetGate) { targeted = Equals(_targetKey, want); target = _target; }
        if (!targeted) return;   // the main thread has not said WHEN yet: a session must start at the cluster time (N3)

        if (have is null)
        {
            if (Equals(_failed, want)) return;   // a net SUMO could not load is not retried every tick
            try
            {
                have = new TrafficSession(want, _options, Volatile.Read(ref _types), target);
                Volatile.Write(ref _session, have);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException
                                          or System.Xml.XmlException or UnauthorizedAccessException)
            {
                _failed = want;
                Fdp.Core.Logging.FdpLog<TrafficRuntime>.Warn("[Traffic] SUMO could not start on '{0}': {1}", want.NetFile, ex.Message);
                return;
            }
        }
        Interlocked.Add(ref _steps, have.StepTo(target));
    }

    public void Dispose()
    {
        Volatile.Write(ref _requested, null);
        Interlocked.Exchange(ref _session, null)?.Dispose();
    }
}
