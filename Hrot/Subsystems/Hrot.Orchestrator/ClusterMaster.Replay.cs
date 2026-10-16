using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.NED.Descriptors.Orchestration;
using ClusterState  = Hrot.NED.Descriptors.Orchestration.ClusterState;
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

    /// <summary>⭐ C9 — the replay timeline's zero the master sends on every seek; 0 until a replay has been prepared.</summary>
    public long ReplayAnchorWallTicks => _replayAnchorWallTicks;

    /// <summary>
    /// ⭐ Called while a transition is fanned out: remember that it enters a replay, and where. ⛔ The planner's seek step is
    /// NOT fanned out with the transition any more — its payload is built before any node has answered the prepare round.
    /// </summary>
    private void NoteReplayEntry(Guid txId, IEnumerable<ISysOpStep> trajectory)
    {
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
            FdpLog<ClusterMaster>.Info(
                "[Orchestrator] C9: replay zero = {0} (span to {1}, {2:F1}s) — sent on every seek.",
                span.FirstFrameWallTicks, span.LastFrameWallTicks, span.DurationSeconds);
        }

        if (_replayEntryTx != txId) return;
        _replayEntryTx = null;
        _eventBus?.PublishManaged(new SeekReplayIntent
        {
            RequestId       = Guid.NewGuid(),
            TargetWallTicks = _replayEntryRelTicks,
        });
        FdpLog<ClusterMaster>.Info(
            "[Orchestrator] C9: replay entered — seeking every node to {0} ticks after the replay's zero.", _replayEntryRelTicks);
    }
}
