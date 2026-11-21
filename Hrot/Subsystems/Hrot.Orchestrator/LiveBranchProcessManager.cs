// LiveBranchProcessManager.cs
// Owns the Live-from-Replay branch interlock: freeze before fan-out, restore after ACK.
// See TASK-T001 / DESIGN.md Phase 2.1

using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using HrotClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager that owns the Live-from-Replay temporal interlock (CGF1-S0305).
/// Calls <see cref="ReplayMasterModule.FreezeTime"/> before the PrepareLive fan-out
/// and <see cref="ReplayMasterModule.RestoreTime"/> + <see cref="MasterSyncController.SnapAndPause"/>
/// after the branch completes.
/// Must be ticked BEFORE <see cref="ClusterMaster.Tick"/> so FreezeTime runs before the fan-out.
/// </summary>
public sealed class LiveBranchProcessManager
{
    private readonly FdpEventBus          _bus;
    private readonly ReplayMasterModule   _replayMasterModule;
    private readonly ITimeCommands        _time;   // Q86 §4-C: commands, never the clock itself
    private readonly Func<Fdp.Core.FlightRecorder.ReplayTimeMap>? _map;      // ⭐ CE-4065 — the cluster's replay map (the session's)
    private readonly Func<long>?                                 _anchorWallTicks;
    private (Fdp.Core.FlightRecorder.ReplayTimeMap Map, long Anchor)? _branchFrom;
    private readonly Func<float>? _operatorRate;   // ⭐ CE-4067 — the rate the operator chose (the replay clock's owner holds it)   // taken when the branch starts, while the cluster replays

    // Last known DSM state, updated each tick from ClusterStateTransitionedEvent.
    private HrotClusterState _lastKnownDsmState;

    public LiveBranchProcessManager(
        FdpEventBus          bus,
        ReplayMasterModule   replayMasterModule,
        ITimeCommands        time,
        Func<Fdp.Core.FlightRecorder.ReplayTimeMap>? map = null,
        Func<long>?          anchorWallTicks = null,
        Func<float>?         operatorRate = null)
    {
        _operatorRate    = operatorRate;
        _map             = map;
        _anchorWallTicks = anchorWallTicks;
        _bus                = bus                ?? throw new ArgumentNullException(nameof(bus));
        _replayMasterModule = replayMasterModule ?? throw new ArgumentNullException(nameof(replayMasterModule));
        _time               = time               ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Processes one frame. Must be called BEFORE <see cref="ClusterMaster.Tick"/> so that
    /// <see cref="ReplayMasterModule.FreezeTime"/> runs before the PrepareLive fan-out.
    /// </summary>
    public void Tick()
    {
        // Update last known DSM state from ClusterStateTransitionedEvent.
        foreach (var ev in _bus.ReadManaged<ClusterStateTransitionedEvent>())
        {
            _lastKnownDsmState = (HrotClusterState)(int)ev.NewStateId;
        }

        // Detect Live-from-Replay branch intent and freeze time before ClusterMaster fans out PrepareLive.
        var fdpLoadLive = (Fdp.Toolkit.Orchestration.ClusterState)(int)HrotClusterState.LoadingLive;
        var fdpOpLive   = (Fdp.Toolkit.Orchestration.ClusterState)(int)HrotClusterState.OperatingLive;
        foreach (var intent in _bus.ReadManaged<TransitionStateIntent>())
        {
            bool passesLoadingLive = intent.TargetState == fdpLoadLive
                                  || intent.TargetState == fdpOpLive;
            if (passesLoadingLive && _lastKnownDsmState == HrotClusterState.OperatingReplay)
            {
                _replayMasterModule.FreezeTime(_operatorRate?.Invoke());   // ⭐ CE-4067 — the live run resumes at the operator's rate
                _branchFrom = _map is null || _anchorWallTicks is null ? null : (_map(), _anchorWallTicks());
            }
        }

        // Restore time and snap master clock after the branch operation completes.
        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.ResultPayload is LiveBranchResult lbr && lbr.HistoricalTime.TotalWallTicks != 0)
            {
                _replayMasterModule.RestoreTime();
                // The slave roster is KEPT (null): this used to pass an EMPTY set (a TASK-T001 TODO), so after a
                // branch a cluster step stopped waiting for the slaves' ACKs.
                _time.SnapTo(OnTheSimClock(lbr.HistoricalTime));
                _branchFrom = null;
            }
            // ⭐ CE-3299 B4 (D5) — a checkpoint restore is a branch too (D18), from a LIVE cluster: no replay freeze to undo; the
            //   clock goes back to the earliest node's checkpoint and the cluster stays paused (SnapTo pauses).
            else if (ev.ResultPayload is CheckpointRestoreResult crr && crr.RestoredTime.TotalWallTicks != 0)
            {
                Fdp.Core.Logging.FdpLog<LiveBranchProcessManager>.Info(
                    "[Orchestrator] checkpoint restore: the clock goes back to sim time {0:F3} s and stays paused.", crr.RestoredTime.TotalTime);
                _time.SnapTo(crr.RestoredTime);
            }
        }
    }

    /// <summary>
    /// ⭐⭐ CE-4065 (DESIGN_Replay_Time_Axis_And_Review §4: the replay clock is the WALL playhead, the world's time the frame's SIM time) —
    /// a branch resumes on the SIM time of the frame on screen: the replay clock's playhead (seconds after the zero) read through the
    /// cluster's own replay map, the one every node agreed at prepare (R3). ⛔ Was the playhead itself, a WALL time: branched at sim 5 s
    /// the live run read 12.71 s (paused wall included), so every time-stamped datum (a knock-down's end, a strike timer) came due early.
    /// A recording with no sim time (v6) keeps the old behaviour.
    /// </summary>
    private GlobalTime OnTheSimClock(GlobalTime historical)
    {
        if (_branchFrom is not { } from || !from.Map.HasSimTime || from.Anchor <= 0) return historical;
        double sim = from.Map.SimAt(from.Anchor + (long)Math.Round(historical.TotalTime * TimeSpan.TicksPerSecond));
        if (double.IsNaN(sim)) return historical;
        historical.TotalTime = sim;
        return historical;
    }
}
