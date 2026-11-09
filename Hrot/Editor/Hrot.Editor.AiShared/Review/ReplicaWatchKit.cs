using System;
using Fdp.Core;

namespace Hrot.Editor.AiShared.Review;

/// <summary>⭐ Review §6.13 — what a host wires for the replica watch beyond the lane: the Details forms' eye and the lane's settings popup, built from the ONE lane (same for the editor's Review and the Replay Browser).</summary>
public sealed class ReplicaWatchKit
{
    public ReplicaWatchLane Lane { get; }
    public ReplicaWatchEye Eye { get; }
    public ReplicaWatchSettings Settings { get; }

    /// <param name="lane">The host's lane.</param>
    /// <param name="world">The world that holds the selected unit now (the shown merged world) — the attribute picker lists what the unit has.</param>
    public ReplicaWatchKit(ReplicaWatchLane lane, Func<EntityRepository?> world)
    {
        Lane = lane;
        Eye = new ReplicaWatchEye(lane);
        Settings = new ReplicaWatchSettings(lane, () =>
        {
            if (world() is not { } w || lane.Unit <= 0) return null;
            var e = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(w, lane.Unit);
            return e.IsNull ? null : (w, e);
        }, () => lane.UnitLabel);
    }
}
