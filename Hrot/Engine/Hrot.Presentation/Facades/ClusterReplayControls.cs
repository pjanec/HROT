using System;
using Fdp.Toolkit.Orchestration;
using Hrot.Presentation.DebugApi;

namespace Hrot.UI.Common.Facades;

/// <summary>
/// ⭐⭐ CE-3329 row 21 — <b>the ONE construction of the review / replay session's master intents.</b> The debug HTTP routes
/// (<c>POST /review</c>, <c>/review/exit</c>, <c>/replay/seek</c>, <c>/replay/axis</c>) and every <see cref="ITimeTransportFacade"/> build
/// them here, so a click and a request are the same request. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §8 row 21.
/// </summary>
public static class ReplaySessionIntents
{
    /// <summary>Review the live run from <paramref name="wallSeconds"/> after the zero or <paramref name="simSeconds"/> of the run's own
    /// clock (the sim target wins); neither = the live point (the master's default).</summary>
    public static TransitionStateIntent EnterReview(double? wallSeconds = null, double? simSeconds = null) => new()
    {
        TransactionId    = Guid.NewGuid(),
        TargetState      = ClusterState.OperatingReview,
        TargetWallTicks  = wallSeconds is { } w ? SecondsToTicks(w) : 0,   // 0 + no sim target = the live point
        TargetSimSeconds = simSeconds,
    };

    /// <summary>Back to the live run, where it paused.</summary>
    public static TransitionStateIntent ExitReview() => new()
    {
        TransactionId = Guid.NewGuid(),
        TargetState   = ClusterState.OperatingLive,
    };

    /// <summary>A replay / review seek: <paramref name="wallSeconds"/> after the zero, or <paramref name="simSeconds"/> (which wins).</summary>
    public static SeekReplayIntent Seek(double wallSeconds, double? simSeconds = null) => new()
    {
        RequestId        = Guid.NewGuid(),
        TargetWallTicks  = SecondsToTicks(wallSeconds),
        TargetSimSeconds = simSeconds,
    };

    /// <summary>The session's axis.</summary>
    public static SetReplayAxisIntent SetAxis(ReplayAxis axis) => new() { RequestId = Guid.NewGuid(), Axis = axis };

    /// <summary>A time on <paramref name="axis"/> as the two targets: Sim ⇒ the sim target; Wall ⇒ the wall seconds.</summary>
    public static (double Wall, double? Sim) OnAxis(ReplayAxis axis, double t)
        => axis == ReplayAxis.Sim ? (0, t) : (t, null);

    private static long SecondsToTicks(double seconds) => (long)Math.Round(Math.Max(0, seconds) * TimeSpan.TicksPerSecond);
}

/// <summary>
/// ⭐⭐ CE-3329 row 21 (one facade) — <b>the ONE construction of the dry run's intents</b>: Play / Step from edit and Stop of a dry run, the
/// same request from the facade on every host and from the editor's <c>IPreviewController</c>. ⛔ SUPERSEDED: the editor's local shortcut
/// that triggered the dry-run handler directly (DESIGN_Http_Control_Through_The_Cluster_Master §7 "no shortcuts").
/// </summary>
public static class DryRunIntents
{
    /// <summary>Enter the dry run — running (the master resumes the clock when it lands) or paused at its first frame.</summary>
    public static TransitionStateIntent Enter(bool startPaused) => new()
    {
        TransactionId = Guid.NewGuid(),
        TargetState   = ClusterState.OperatingPreview,
        TimeMode      = startPaused ? "Deterministic" : "Continuous",
    };

    /// <summary>Leave the dry run: the world rewinds, the master puts the clock back and pauses (back in edit).</summary>
    public static TransitionStateIntent Exit() => new()
    {
        TransactionId = Guid.NewGuid(),
        TargetState   = ClusterState.OperatingEdit,
    };
}

/// <summary>
/// ⭐⭐ CE-3329 row 21 — the review / replay half of <see cref="ITimeTransportFacade"/>, written ONCE: every facade implementation
/// delegates its <c>Mode</c>, <c>Axis</c>, <c>SetAxis</c>, <c>EnterReview</c>, <c>ExitReview</c>, <c>LivePoint</c> and <c>Seek</c> here.
/// It reads the cluster state and the session layer (<see cref="ReplaySession"/>, from <c>ReplaySessionTopic</c>) and publishes
/// through <see cref="IClusterMasterControl"/> — the debug surface's own master path.
/// </summary>
public sealed class ClusterReplayControls
{
    private readonly Func<ClusterState> _state;
    private readonly Func<ReplaySession?> _session;
    private readonly Func<ReplayAxis> _axis;
    private readonly IClusterMasterControl _master;

    /// <param name="state">The cluster's state as this host knows it.</param>
    /// <param name="session">The session layer as this host knows it (<see langword="null"/> = none yet).</param>
    /// <param name="axis">The session's axis as the master last said (it rides every cluster-state publication).</param>
    /// <param name="master">Where the intents go (<see cref="OrchestrationBusMasterControl"/> on the host's orchestration bus).</param>
    public ClusterReplayControls(Func<ClusterState> state, Func<ReplaySession?> session, Func<ReplayAxis> axis, IClusterMasterControl master)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _axis = axis ?? throw new ArgumentNullException(nameof(axis));
        _master = master ?? throw new ArgumentNullException(nameof(master));
    }

    /// <summary>The facade's mode for a cluster state.</summary>
    public static TransportMode ModeOf(ClusterState state) => state switch
    {
        ClusterState.OperatingEdit    => TransportMode.Edit,
        ClusterState.OperatingPreview => TransportMode.Preview,
        ClusterState.OperatingLive    => TransportMode.Live,
        _ when ClusterStates.IsReview(state) => TransportMode.Review,
        ClusterState.OperatingReplay or ClusterState.LoadingReplay or ClusterState.UnloadingReplay => TransportMode.Replay,
        _ => TransportMode.Idle,
    };

    public TransportMode Mode => ModeOf(_state());

    public ReplayAxis Axis => _axis();

    public void SetAxis(ReplayAxis axis) => _master.SetReplayAxis(ReplaySessionIntents.SetAxis(axis));

    public void EnterReview(double? at)
    {
        if (at is not { } t) { _master.Transition(ReplaySessionIntents.EnterReview()); return; }
        var (wall, sim) = ReplaySessionIntents.OnAxis(Axis, t);
        _master.Transition(ReplaySessionIntents.EnterReview(sim is null ? wall : null, sim));
    }

    /// <summary>⭐ CE-3330 E3 — a review from LIVE, by the run's sim seconds (the host has no session / wall zero yet; the sim target wins).</summary>
    public void EnterReviewAtSim(double simSeconds) => _master.Transition(ReplaySessionIntents.EnterReview(simSeconds: simSeconds));

    public void ExitReview() => _master.Transition(ReplaySessionIntents.ExitReview());

    public double? LivePoint
    {
        get
        {
            var s = _session();
            if (s is not { Active: true, IsReview: true }) return null;
            return Axis == ReplayAxis.Sim ? s.LivePointSimSeconds : s.LivePointSeconds;
        }
    }

    public void Seek(double t)
    {
        var (wall, sim) = ReplaySessionIntents.OnAxis(Axis, t);
        _master.SeekReplay(ReplaySessionIntents.Seek(wall, sim));
    }
}
