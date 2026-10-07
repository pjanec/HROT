// ReplaySeekProcessManager.cs
// Owns the seek preconditions (SlaveNodeSetUpdatedEvent, PauseTimeIntent) and the
// SnapAndPause call after a successful NodeReplaySeek operation.
// See TASK-T002 / DESIGN.md Phase 2.2

using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Controllers;
using Fdp.Toolkit.Time.Domain;

namespace Hrot.Orchestrator;

/// <summary>
/// Process Manager that owns the seek preconditions and post-seek clock snap.
/// On <see cref="SeekReplayIntent"/>: publishes <see cref="SlaveNodeSetUpdatedEvent"/>
/// and <see cref="PauseTimeIntent"/> before <see cref="ClusterMaster"/> fans out the seek.
/// On <see cref="ClusterOpCompletedEvent"/> with <see cref="ReplaySeekResult"/> payload:
/// asks for the jump through <see cref="ITimeCommands.SnapTo"/>.
/// </summary>
public sealed class ReplaySeekProcessManager
{
    private readonly FdpEventBus          _bus;
    private readonly ITimeCommands        _time;   // Q86 §4-C: commands, never the clock itself

    // NodeId -> SubsystemName, maintained from NodeHeartbeatEvent.
    private readonly Dictionary<int, string> _nodeSubsystems = new();

    public ReplaySeekProcessManager(FdpEventBus bus, ITimeCommands time)
    {
        _bus        = bus        ?? throw new ArgumentNullException(nameof(bus));
        _time       = time       ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Processes one frame. Should be called BEFORE <see cref="ClusterMaster.Tick"/>
    /// so that precondition events are published before the seek fan-out.
    /// </summary>
    public void Tick()
    {
        // Maintain local replica of node subsystem names from heartbeats.
        foreach (var hb in _bus.ReadManaged<NodeHeartbeatEvent>())
        {
            if (hb.SubsystemName != null)
                _nodeSubsystems[hb.NodeId] = hb.SubsystemName;
        }

        // On SeekReplayIntent: publish precondition events (TASK-T002).
        foreach (var intent in _bus.ReadManaged<SeekReplayIntent>())
        {
            var slaveIds = new System.Collections.Generic.HashSet<int>();
            foreach (var kv in _nodeSubsystems)
            {
                if (kv.Value is "SimHost" or "IG" or "CGF")
                    slaveIds.Add(kv.Key);
            }
            _bus.PublishManaged(new SlaveNodeSetUpdatedEvent { SlaveNodeIds = slaveIds });
            _bus.PublishManaged(new PauseTimeIntent());
        }

        // On ClusterOpCompletedEvent with ReplaySeekResult: snap master clock.
        foreach (var ev in _bus.ReadManaged<ClusterOpCompletedEvent>())
        {
            if (ev.ResultPayload is ReplaySeekResult sr && sr.RestoredTime.TotalWallTicks != 0)
            {
                // The slave roster was set by this manager's own seek precondition (SlaveNodeSetUpdatedEvent +
                // PauseTimeIntent, above); the snap keeps it.
                _time.SnapTo(sr.RestoredTime);
            }
        }
    }
}
