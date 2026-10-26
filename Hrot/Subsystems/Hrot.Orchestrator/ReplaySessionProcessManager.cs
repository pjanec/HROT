using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ <c>CE-3329</c> (rows 32, 45; §9e–§9g) — <b>the master's session layer</b>: builds the <see cref="ReplaySession"/> from what the
/// master holds (the zero, the length, the axis, the cluster's time map, the checkpoint marks, the review's id and live point) and
/// publishes it whenever it changes — <see cref="ReplaySessionPublishedEvent"/> for the DDS writer, <see cref="ReplaySessionUpdateEvent"/>
/// for this host's own readers. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §9f.
/// </summary>
/// <remarks>
/// ⭐ Change-driven, never per frame: one comparison a tick (<see cref="ReplaySession.SameAs"/>); a sample only when something a host
/// draws moved. ⭐ The first tick publishes the inactive sample, so a late joiner always has one (the topic is TransientLocal).
/// </remarks>
public sealed class ReplaySessionProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly ClusterMaster _master;
    private readonly ReviewProcessManager _review;
    private ReplaySession? _published;

    /// <summary>The newest sample this master published (diagnostics, rails).</summary>
    public ReplaySession? Current => _published;

    public ReplaySessionProcessManager(FdpEventBus bus, ClusterMaster master, ReviewProcessManager review)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _review = review ?? throw new ArgumentNullException(nameof(review));
    }

    /// <summary>Call after the master's tick: the round that completed this frame has already updated the zero, the map and the marks.</summary>
    public void Tick()
    {
        var next = Build();
        if (next.SameAs(_published)) return;
        _published = next;
        _bus.PublishManaged(new ReplaySessionPublishedEvent { Session = next });
        _bus.PublishManaged(new ReplaySessionUpdateEvent { Session = next });
    }

    private ReplaySession Build()
    {
        var state = (Fdp.Toolkit.Orchestration.ClusterState)(int)_master.CurrentClusterState;
        if (!ClusterStates.IsReplaying(state)) return ReplaySession.None(_master.SessionId);

        long zero = _master.ReplayAnchorWallTicks;
        var session = new ReplaySession
        {
            SessionId     = _master.SessionId,
            ExerciseId    = _master.ActiveExerciseId,
            Active        = true,
            IsReview      = ClusterStates.IsReview(state),
            ReviewId      = _review.ReviewId,
            Axis          = _master.ReplayAxis,
            ZeroWallTicks = zero,
            LengthSeconds = _master.ReplayDurationSeconds,
            TimeMap       = _master.ReplayTimeMap.HasSimTime ? _master.ReplayTimeMap : null,
            Checkpoints   = _master.ReplayCheckpoints,
            LoopFromSeconds = _master.ReplayLoop?.FromSeconds,   // ⭐ CE-3329 §12n
            LoopToSeconds   = _master.ReplayLoop?.ToSeconds,
        };
        if (session.IsReview && _review.HasLivePoint)
        {
            var l = _review.LivePoint;
            session.LivePointSimSeconds = l.TotalTime;
            if (zero > 0 && l.TotalWallTicks > 0) session.LivePointSeconds = (l.TotalWallTicks - zero) / (double)TimeSpan.TicksPerSecond;
        }
        return session;
    }
}
