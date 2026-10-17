using System.Numerics;
using Sim.Pedestrians.Lod;

namespace Hrot.Traffic;

/// <summary>
/// One SUMO pedestrian as this node sees it this frame: terrain-local metres, Z up, HROT yaw (0 = east, counter-clockwise, from its
/// motion); its OWNER (the node that sent it), and its network id — its SUMO name, allocated by the serving node (AQ93 §15 K2).
/// </summary>
public readonly record struct TrafficPedPose(int Owner, long NetworkId, string Name, float X, float Y, float Z, float YawRad);

/// <summary>
/// ⭐ AQ93 §15 K3 — the RECEIVING side of ONE sender's pedestrian wire: every live pedestrian at one instant of the cluster clock,
/// through SumoSharp's own <see cref="PedRemoteReconstructor.CopyLivePeds"/> (H14: sim-time poses, frozen while the clock is paused),
/// placed into terrain-local metres. One per sender, like <see cref="TrafficMirror"/>. ⛔ Main thread only.
/// </summary>
/// <remarks>⭐ The wire carries no heading; the yaw is the direction it last moved (kept while it stands).</remarks>
public sealed class TrafficPedMirror
{
    private readonly SumoNetPlacement _placement;
    private readonly int _owner;
    private readonly List<RemotePedPose> _scratch = new();
    private readonly Dictionary<int, (Vector2 At, float Yaw)> _last = new();
    private readonly HashSet<int> _live = new();
    private readonly List<int> _gone = new();

    public TrafficPedMirror(SumoNetPlacement placement, int owner)
    {
        _placement = placement ?? throw new ArgumentNullException(nameof(placement));
        _owner = owner;
    }

    /// <summary>Resolves every live pedestrian of <paramref name="peds"/> at cluster time <paramref name="time"/> and APPENDS it.</summary>
    public void Resolve(PedRemoteReconstructor peds, double time, List<TrafficPedPose> into)
    {
        peds.CopyLivePeds(time, _scratch);
        _live.Clear();
        foreach (var p in _scratch)
        {
            if (!p.Visible) continue;
            _live.Add(p.Id);
            var (x, y) = _placement.ToLocal(p.X, p.Y);
            var at = new Vector2((float)x, (float)y);
            float yaw = 0f;
            if (_last.TryGetValue(p.Id, out var last))
            {
                var d = at - last.At;
                yaw = d.LengthSquared() > 1e-4f ? MathF.Atan2(d.Y, d.X) : last.Yaw;
            }
            _last[p.Id] = (at, yaw);
            long netId = long.TryParse(p.Name, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
            into.Add(new TrafficPedPose(_owner, netId, p.Name, at.X, at.Y, (float)p.Z, yaw));
        }
        _gone.Clear();
        foreach (var id in _last.Keys) if (!_live.Contains(id)) _gone.Add(id);
        foreach (var id in _gone) _last.Remove(id);
    }
}
