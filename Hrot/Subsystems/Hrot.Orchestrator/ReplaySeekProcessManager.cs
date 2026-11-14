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

    // ⭐ P3 — the master's step roster (by ROLE). ⛔ Was a private replica of heartbeat subsystem NAMES matched against
    //   "SimHost" or "IG" or "CGF" — a second copy of the rule, which let a Player gate every seek.
    private readonly Func<IReadOnlySet<int>> _stepRoster;

    public ReplaySeekProcessManager(FdpEventBus bus, ITimeCommands time, Func<IReadOnlySet<int>> stepRoster)
    {
        _bus        = bus        ?? throw new ArgumentNullException(nameof(bus));
        _time       = time       ?? throw new ArgumentNullException(nameof(time));
        _stepRoster = stepRoster ?? throw new ArgumentNullException(nameof(stepRoster));
    }

    /// <summary>
    /// Processes one frame. Should be called BEFORE <see cref="ClusterMaster.Tick"/>
    /// so that precondition events are published before the seek fan-out.
    /// </summary>
    public void Tick()
    {
        // On SeekReplayIntent: publish precondition events (TASK-T002).
        foreach (var intent in _bus.ReadManaged<SeekReplayIntent>())
        {
            _bus.PublishManaged(new SlaveNodeSetUpdatedEvent { SlaveNodeIds = _stepRoster() });
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
