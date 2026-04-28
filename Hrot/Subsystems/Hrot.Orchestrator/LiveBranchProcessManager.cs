// LiveBranchProcessManager.cs
// Owns the Live-from-Replay branch interlock: freeze before fan-out, restore after ACK.
// See TASK-T001 / DESIGN.md Phase 2.1

using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
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
    private readonly MasterSyncController _masterSync;

    // Last known DSM state, updated each tick from ClusterStateTransitionedEvent.
    private HrotClusterState _lastKnownDsmState;

    public LiveBranchProcessManager(
        FdpEventBus          bus,
        ReplayMasterModule   replayMasterModule,
        MasterSyncController masterSync)
    {
        _bus                = bus                ?? throw new ArgumentNullException(nameof(bus));
        _replayMasterModule = replayMasterModule ?? throw new ArgumentNullException(nameof(replayMasterModule));
        _masterSync         = masterSync         ?? throw new ArgumentNullException(nameof(masterSync));
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
                _replayMasterModule.FreezeTime();
            }
        }

        // Restore time and snap master clock after the branch operation completes.
        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.ResultPayload is LiveBranchResult lbr && lbr.HistoricalTime.TotalWallTicks != 0)
            {
                _replayMasterModule.RestoreTime();
                // TODO: wire active node IDs (TASK-T001 follow-up)
                _masterSync.SnapAndPause(
                    lbr.HistoricalTime.TotalWallTicks,
                    lbr.HistoricalTime.TotalTime,
                    new HashSet<int>());
            }
        }
    }
}
