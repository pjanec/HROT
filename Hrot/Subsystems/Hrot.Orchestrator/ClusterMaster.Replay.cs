using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
using ReplayAxis    = Fdp.Toolkit.Orchestration.ReplayAxis;
using SetReplayAxisIntent = Fdp.Toolkit.Orchestration.SetReplayAxisIntent;
using SetReplayLoopIntent = Fdp.Toolkit.Orchestration.SetReplayLoopIntent;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ <c>C9</c> (Q91 §4-P, <c>R-4012</c>) — <b>ONE replay timeline.</b> The replay's zero is the earliest first frame across
/// every node's recording, learnt from the prepare round and sent on every seek; entering a replay runs a seek ROUND (to the
/// requested moment, else the start) once that round has completed, which both delivers the zero and snaps every clock.
/// <para>📐 Measured before: every node measured replay time from its OWN first frame (a late Player was shifted by its join
/// delay), the clock entered the replay at the live clock's time — past every recording's end — and the planner's entry seek
/// was fanned out in the transition's burst with a throw-away transaction, so it never ran as a round and nothing snapped.</para>
/// </summary>
public sealed partial class ClusterMaster
{
    private long  _replayAnchorWallTicks;   // the replay's zero; 0 = not known (nodes use their own start)
    private Guid? _replayEntryTx;           // the transition round that enters OperatingReplay
    private long  _replayEntryRelTicks;     // where it enters (the operator's hint, else 0 = the start)
    private double? _replayEntrySimSeconds; // ⭐ CE-3329 R11 — where it enters in RECORDED sim time (wins when set)
    private ReplayAxis _replayAxis = ReplayAxis.Sim;   // ⭐ CE-3329 R2 / R-3058 — the session's axis; Sim by default

    /// <summary>⭐ CE-3329 R-3058 — the replay session's time axis (what every host's timeline draws, what a seek means, whether play
    /// jumps over a paused stretch). Rides every cluster-state publication.</summary>
    public ReplayAxis ReplayAxis => _replayAxis;

    /// <summary>
    /// ⭐ CE-3329 R-3058 — any host switches the axis for the whole session: no seek (the frame on screen stays), the cluster state is
    /// broadcast again so every node learns it, and what comes next follows it (a play on Sim jumps the next pause).
    /// </summary>
    private void ProcessSetReplayAxisIntent(SetReplayAxisIntent intent)
    {
        _replayAxis = intent.Axis;
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-3329: the replay axis is now {0}.", intent.Axis);
        PublishClusterState(_currentDsmState);
        PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
    }

    private (double FromSeconds, double ToSeconds)? _replayLoop;   // ⭐ CE-3329 §12n — wall seconds after the zero

    /// <summary>⭐ CE-3329 §12n — the replay's LOOP range in WALL seconds after its zero, or <see langword="null"/>. A session setting
    /// held here and enforced by <c>ReplayClockProcessManager</c>; cleared whenever the cluster leaves a replay / review.</summary>
    public (double FromSeconds, double ToSeconds)? ReplayLoop => _replayLoop;

    /// <summary>
    /// ⭐ CE-3329 §12n — any host sets or clears the loop. Sim bounds are resolved through <see cref="RelativeTicksForSim"/>; valid only
    /// <c>0 ≤ from &lt; to ≤ length</c> (length checked when known), else <c>Rejected</c> and the previous loop stays. All-null = clear.
    /// </summary>
    private void ProcessSetReplayLoopIntent(SetReplayLoopIntent intent)
    {
        if (intent.IsClear)
        {
            _replayLoop = null;
            FdpLog<ClusterMaster>.Info("[Orchestrator] CE-3329 §12n: the replay loop is cleared.");
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
            return;
        }

        var loop = intent.Resolve(sim => RelativeTicksForSim(sim) is { } rel ? rel / (double)TimeSpan.TicksPerSecond : null,
                                  _replayDurationSeconds);
        if (loop is not { } resolved)
        {
            FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-3329 §12n: a loop refused — needs 0 <= from < to <= the replay's length ({0}s); the previous loop stays.",
                _replayDurationSeconds);
            PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Rejected);
            return;
        }

        _replayLoop = resolved;
        FdpLog<ClusterMaster>.Info("[Orchestrator] CE-3329 §12n: the replay loops [{0:F2}s, {1:F2}s].", resolved.FromSeconds, resolved.ToSeconds);
        PublishOpStatus(intent.RequestId, OrchestrationStatusCode.Success);
    }

    private Fdp.Core.FlightRecorder.ReplayTimeMap _replayTimeMap = Fdp.Core.FlightRecorder.ReplayTimeMap.Empty;

    /// <summary>⭐ CE-3329 P1 (R3) — the cluster's wall ↔ sim map of the loaded replay (the nodes' maps intersected by
    /// <c>ReplayConsensusAggregator</c>); <see cref="Fdp.Core.FlightRecorder.ReplayTimeMap.Empty"/> before a replay or for v6 recordings.</summary>
    public Fdp.Core.FlightRecorder.ReplayTimeMap ReplayTimeMap => _replayTimeMap;

    private Fdp.Core.FlightRecorder.Metadata.CheckpointMark[] _replayCheckpoints = Array.Empty<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>();
    private double _replayDurationSeconds;

    /// <summary>⭐ CE-3329 §9f — the loaded replay's (or review's) checkpoint marks, the union across the nodes.</summary>
    public Fdp.Core.FlightRecorder.Metadata.CheckpointMark[] ReplayCheckpoints => _replayCheckpoints;

    /// <summary>⭐ CE-3329 §9f — the loaded replay's length in wall seconds after its zero (a review's: up to the live point).</summary>
    public double ReplayDurationSeconds => _replayDurationSeconds;

    /// <summary>
    /// ⭐ CE-3329 P1 (R-3057 R11, design §4 "a sim seek") — ticks after the replay's zero for a target in RECORDED sim time: the
    /// cluster map's wall time (a paused instant lands at the stretch's END, so its changes show). <see langword="null"/> when the
    /// replay has no sim axis (a v6 recording, or no map yet).
    /// </summary>
    /// <param name="landOnFrame">⭐ CE-4066 — true for a SEEK (the entry, a seek round): resolved through
    /// <see cref="Fdp.Core.FlightRecorder.ReplayTimeMap.SeekWallAt"/> so an on-or-before landing is ON the frame recorded at that sim
    /// time; false for a loop's bounds, which are a range (exact).</param>
    internal long? RelativeTicksForSim(double simSeconds, bool landOnFrame = false)
    {
        if (!_replayTimeMap.HasSimTime || _replayAnchorWallTicks <= 0) return null;
        long wall = landOnFrame ? _replayTimeMap.SeekWallAt(simSeconds) : _replayTimeMap.WallAt(simSeconds);
        return Math.Max(0, wall - _replayAnchorWallTicks);
    }

    /// <summary>⭐ C9 — the replay timeline's zero the master sends on every seek; 0 until a replay has been prepared.</summary>
    public long ReplayAnchorWallTicks => _replayAnchorWallTicks;

    /// <summary>
    /// ⭐ Called while a transition is fanned out: remember that it enters a replay, and where. ⛔ The planner's seek step is
    /// NOT fanned out with the transition any more — its payload is built before any node has answered the prepare round.
    /// </summary>
    private long _reviewEntryWallTicks;   // ⭐ CE-3329 P3 — where a review enters (long.MaxValue = the live point)

    private void NoteReplayEntry(Guid txId, IEnumerable<ISysOpStep> trajectory)
    {
        // ⭐ CE-3329 P3 — a review enters through the SAME seek round as a replay (§3c): the nodes' prepare results give the zero
        //   and the map, then one seek lands every node on the moment (the live point unless one was named).
        if (trajectory.OfType<TransitionStep>().Any(t => t.TargetState == ClusterState.OperatingReview))
        {
            _replayEntryTx       = txId;
            _replayEntryRelTicks = _reviewEntryWallTicks;
            return;
        }
        if (!trajectory.OfType<TransitionStep>().Any(t => t.TargetState == ClusterState.OperatingReplay)) return;
        _replayEntryTx       = txId;
        _replayEntryRelTicks = trajectory.OfType<OperationStep>()
            .Where(s => s.Operation == ClusterOpType.ReplaySeek && s.DomainPayload is ReplaySeekPayload)
            .Select(s => ((ReplaySeekPayload)s.DomainPayload!).TargetWallTicks)
            .LastOrDefault();
    }

    /// <summary>
    /// ⭐ Called when a round completes successfully: a prepare round's aggregate carries the cluster's span; the round that
    /// entered the replay starts the entry seek through the ordinary operator path (pause, tracked round, clock snap).
    /// </summary>
    private void OnRoundSucceededForReplay(Guid txId, object? aggregated)
    {
        if (aggregated is ReplayPrepareResult { FirstFrameWallTicks: > 0 } span)
        {
            _replayAnchorWallTicks = span.FirstFrameWallTicks;
            _replayTimeMap = span.TimeMap ?? Fdp.Core.FlightRecorder.ReplayTimeMap.Empty;   // ⭐ CE-3329 R3
            _replayCheckpoints = span.Checkpoints ?? Array.Empty<Fdp.Core.FlightRecorder.Metadata.CheckpointMark>();   // ⭐ CE-3329 §9f
            _replayDurationSeconds = span.DurationSeconds;
            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] C9: replay zero = {0} (span to {1}, {2:F1}s) — sent on every seek.",
                span.FirstFrameWallTicks, span.LastFrameWallTicks, span.DurationSeconds);
        }

        if (_replayEntryTx != txId) return;
        _replayEntryTx = null;
        // ⭐ CE-3329 R11 — an entry named in sim time resolves now that the nodes' maps are in
        if (_replayEntrySimSeconds is { } entrySim)
        {
            if (RelativeTicksForSim(entrySim, landOnFrame: true) is { } rel) _replayEntryRelTicks = rel;
            else FdpLog<ClusterMaster>.Warn("[Orchestrator] CE-3329: the replay has no sim axis (a v6 recording) — atSimSeconds {0} ignored, entering at {1} ticks.",
                     entrySim, _replayEntryRelTicks);
            _replayEntrySimSeconds = null;
        }
        _eventBus?.PublishManaged(new SeekReplayIntent
        {
            RequestId       = Guid.NewGuid(),
            TargetWallTicks = _replayEntryRelTicks,
        });
        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] C9: replay entered — seeking every node to {0} ticks after the replay's zero.", _replayEntryRelTicks);
    }
}
