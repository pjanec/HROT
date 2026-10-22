// PreviewClockProcessManager.cs
// ⭐ CE-3299 B2 / D5 — a cluster dry run puts the CLOCK back with the world.

using System;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Fdp.ModuleHost.Time;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ <c>CE-3299</c> B2 / <c>D5</c> (restore design §5) — <b>the clock goes back with the world when a CLUSTER dry run ends.</b>
/// Every node's <c>ReferencePreviewHandler</c> rewinds its world at <c>UnloadingPreview</c>; the time it rewinds to is the master's, so
/// the master snaps its clock — the one time discontinuity (<see cref="ITimeCommands.SnapTo"/>, Time Architecture §12a) — to the
/// position it held when the dry run started, and every node follows the master's clock.
/// <para>The editor's own Play/Stop already does this locally (<c>EditorPreviewController</c>, <c>CE-3156</c>); this closes the same gap
/// on the path the cluster master drives (<c>LoadingPreview</c> → … → <c>UnloadingPreview</c> → <c>OperatingEdit</c>), which had none.</para>
/// </summary>
public sealed class PreviewClockProcessManager
{
    private readonly FdpEventBus     _bus;
    private readonly ITimeController _reads;
    private readonly ITimeCommands   _time;

    private ClusterState _last;
    private GlobalTime?  _enteredAt;

    public PreviewClockProcessManager(FdpEventBus bus, ITimeController reads, ITimeCommands time)
    {
        _bus   = bus   ?? throw new ArgumentNullException(nameof(bus));
        _reads = reads ?? throw new ArgumentNullException(nameof(reads));
        _time  = time  ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>The position the running dry run will be put back to (null when none runs). Exposed for rails.</summary>
    public GlobalTime? EnteredAt => _enteredAt;

    public void Tick()
    {
        foreach (var ev in _bus.ReadManaged<ClusterStateTransitionedEvent>())
        {
            var now = ev.NewStateId;
            if (now == ClusterState.LoadingPreview && _enteredAt == null)
                _enteredAt = _reads.GetCurrentState();   // the position the world snapshot belongs to — taken before time runs
            else if (now == ClusterState.OperatingEdit && _last == ClusterState.UnloadingPreview && _enteredAt is { } at)
            {
                _time.SnapTo(at);
                _enteredAt = null;
            }
            else if (now is not (ClusterState.LoadingPreview or ClusterState.OperatingPreview or ClusterState.UnloadingPreview))
                _enteredAt = null;   // the dry run ended some other way (abort, a load) — nothing to put back
            _last = now;
        }
    }
}
