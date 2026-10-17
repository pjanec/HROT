using Sim.Core;
using Sim.Replication;
using Sim.Viewer.Motion;

namespace Hrot.Traffic;

/// <summary>One SUMO car as the editor sees it this frame: terrain-local metres, Z up, HROT yaw (0 = east, counter-clockwise).</summary>
public readonly record struct TrafficCarPose(VehicleHandle Handle, string Id, float X, float Y, float Z, float YawRad, float Length, float Width);

/// <summary>
/// ⭐ AQ93 R-4020 — the RECEIVING side of SUMO's wire: every car dead-reckoned at one instant of the HROT clock, exactly the way
/// every SumoSharp viewer does it — <see cref="DrClock.ResolveAt"/> (the deterministic, clock-driven entry point) then the shared
/// <see cref="KinematicReconstructor"/> — and placed into terrain-local metres through the net's placement.
/// </summary>
/// <remarks>
/// <para>⭐ The recipe is <c>Sim.IgBridge.IgBridgeSession.EmitVehicles</c>'s (SumoSharp), not a new one: resolve at an instant held a
/// PLAYOUT DELAY behind the newest sample so the bracket interpolates instead of extrapolating, feed the reconstructor the real
/// elapsed time per car, skip a car whose resolve is unavailable this frame.</para>
/// <para>⭐ Heights are SUMO's (R-4025, E2): a lane's own Z. A flat net is at Z 0 — the terrain's ground datum for a generated
/// terrain (R-4030 drape is a later slice).</para>
/// <para>⛔ Main thread only.</para>
/// </remarks>
public sealed class TrafficMirror
{
    private readonly DrClock _clock = new();
    private readonly KinematicReconstructor _recon = new();
    private readonly Dictionary<VehicleHandle, double> _lastResolved = new();
    private readonly List<VehicleHandle> _gone = new();
    private readonly SumoNetPlacement _placement;

    public TrafficMirror(SumoNetPlacement placement) => _placement = placement ?? throw new ArgumentNullException(nameof(placement));

    /// <summary>Resolves every live car at SUMO time <paramref name="sumoTime"/> into <paramref name="into"/> (cleared first).</summary>
    public void Resolve(IReplicationSource source, ILaneShapeSource lanes, double sumoTime, List<TrafficCarPose> into)
    {
        into.Clear();
        foreach (var kv in source.History)
        {
            var handle = kv.Key;
            var history = kv.Value;
            if (history.Count == 0 || !source.Dims.TryGetValue(handle, out var dims)) continue;   // not announced, or despawned
            if (sumoTime < history[0].TimestampSeconds) continue;                               // not yet resolvable

            var resolved = _clock.ResolveAt(history, sumoTime, lanes);
            float dt = _lastResolved.TryGetValue(handle, out var last)
                ? (float)Math.Clamp(sumoTime - last, 1e-3, 0.5)
                : 1f / 60f;
            _lastResolved[handle] = sumoTime;

            var r = _recon.Resolve(handle, resolved, lanes, (dims.Length, dims.Width), dt);
            if (!r.Ok) continue;

            var (x, y) = _placement.ToLocal(r.CenterX, r.CenterY);
            // SumoSharp's heading is a COMPASS bearing (0 = north, clockwise: dx = sin h, dy = cos h); HROT yaw is 0 = east, CCW.
            float yaw = (float)(Math.PI / 2.0 - r.HeadingDeg * Math.PI / 180.0);
            source.Names.TryGetValue(handle, out var id);
            into.Add(new TrafficCarPose(handle, id ?? handle.Index.ToString(), (float)x, (float)y, (float)r.Z, yaw, dims.Length, dims.Width));
        }

        // Forget cars SUMO no longer announces, so a recycled slot starts its own smoothing.
        _gone.Clear();
        foreach (var h in _lastResolved.Keys)
            if (!source.Dims.ContainsKey(h)) _gone.Add(h);
        foreach (var h in _gone) _lastResolved.Remove(h);
    }
}
