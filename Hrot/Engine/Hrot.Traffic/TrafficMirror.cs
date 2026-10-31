using Sim.Core;
using Sim.Replication;
using Sim.Viewer.Motion;

namespace Hrot.Traffic;

/// <summary>
/// One SUMO car as this node sees it this frame: terrain-local metres, Z up, HROT yaw (0 = east, counter-clockwise); its OWNER (the node
/// that sent it, N5), its network id (its SUMO vehicle id, N2; 0 when the sender named it otherwise), its TKB type (T1; 0 = not mapped)
/// and its colour (0xRRGGBB, 0 = none, T2).
/// </summary>
public readonly record struct TrafficCarPose(int Owner, VehicleHandle Handle, string Id, long NetworkId, long TkbType, uint Color,
                                             float X, float Y, float Z, float YawRad, float Length, float Width);

/// <summary>
/// ⭐ AQ93 R-4020 — the RECEIVING side of ONE sender's SUMO wire: every car dead-reckoned at one instant of the cluster clock, exactly the
/// way every SumoSharp viewer does it — <see cref="DrClock.ResolveAt"/> (the deterministic, clock-driven entry point) then the shared
/// <see cref="KinematicReconstructor"/> — and placed into terrain-local metres through the net's placement. One instance per sender: the
/// reconstructor keeps per-car state keyed by handle, and two senders' engines number their cars independently (R-4055).
/// </summary>
/// <remarks>
/// <para>⭐ The recipe is <c>Sim.IgBridge.IgBridgeSession.EmitVehicles</c>'s (SumoSharp): resolve at an instant held a PLAYOUT DELAY behind
/// the newest sample so the bracket interpolates, feed the reconstructor the real elapsed time per car, skip a car whose resolve is
/// unavailable this frame. ⭐ The wire is stamped in cluster time (N3), so every node resolves the SAME instant.</para>
/// <para>⭐ Heights are SUMO's (R-4025, E2): a lane's own Z. ⛔ Main thread only.</para>
/// </remarks>
public sealed class TrafficMirror
{
    private readonly DrClock _clock = new();
    private readonly KinematicReconstructor _recon = new();
    private readonly Dictionary<VehicleHandle, double> _lastResolved = new();
    private readonly List<VehicleHandle> _gone = new();
    private readonly SumoNetPlacement _placement;
    private readonly int _owner;

    /// <param name="owner">The node that sends this wire (N5): the owner of every car on it.</param>
    public TrafficMirror(SumoNetPlacement placement, int owner)
    {
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
        _owner = owner;
    }

    public int Owner => _owner;

    /// <summary>Resolves every live car on <paramref name="source"/> at cluster time <paramref name="time"/> and APPENDS it to
    /// <paramref name="into"/>.</summary>
    public void Resolve(IReplicationSource source, ILaneShapeSource lanes, double time, List<TrafficCarPose> into)
    {
        foreach (var kv in source.History)
        {
            var handle = kv.Key;
            var history = kv.Value;
            if (history.Count == 0 || !source.Dims.TryGetValue(handle, out var dims)) continue;   // not announced, or despawned
            if (time < history[0].TimestampSeconds) continue;                                    // not yet resolvable

            var resolved = _clock.ResolveAt(history, time, lanes);
            float dt = _lastResolved.TryGetValue(handle, out var last)
                ? (float)Math.Clamp(time - last, 1e-3, 0.5)
                : 1f / 60f;
            _lastResolved[handle] = time;

            var r = _recon.Resolve(handle, resolved, lanes, (dims.Length, dims.Width), dt);
            if (!r.Ok) continue;

            var (x, y) = _placement.ToLocal(r.CenterX, r.CenterY);
            // SumoSharp's heading is a COMPASS bearing (0 = north, clockwise: dx = sin h, dy = cos h); HROT yaw is 0 = east, CCW.
            float yaw = (float)(Math.PI / 2.0 - r.HeadingDeg * Math.PI / 180.0);
            source.Names.TryGetValue(handle, out var id);
            id ??= handle.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            long netId = long.TryParse(id, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
            source.Tags.TryGetValue(handle, out var tag);
            into.Add(new TrafficCarPose(_owner, handle, id, netId, tag.ExternalType, tag.Color != 0 ? tag.Color : TrafficPaint.Of(id, tag.ExternalType),   // ⭐ CE-1457 (TC4)
                (float)x, (float)y, (float)r.Z, yaw, dims.Length, dims.Width));
        }

        // Forget cars SUMO no longer announces, so a recycled slot starts its own smoothing.
        _gone.Clear();
        foreach (var h in _lastResolved.Keys)
            if (!source.Dims.ContainsKey(h)) _gone.Add(h);
        foreach (var h in _gone) _lastResolved.Remove(h);
    }
}
