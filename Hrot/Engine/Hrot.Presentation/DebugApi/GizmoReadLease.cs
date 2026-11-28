using System;
using System.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐⭐ CE-11045 (HL-1, docs/DESIGN_Threaded_Simulation_Presentation.md §16) — a TIMED reference on the map pack's existing gizmo gate (<see cref="GizmoExecutionController"/>,
/// GZH-003: the gizmo group is enabled while at least one viewer holds a reference). A HEADLESS editor passes <c>StartEnabled = false</c>; nobody attaches a viewer
/// (no <c>PerspectiveCoordinatorSystem</c> under the editor, §3.2d ①), so the gate is opened by a READER: <c>GET /panels/_gizmo</c> calls <see cref="Touch"/>, which takes
/// ONE reference (the first read after idle) and keeps it while reads keep coming; <see cref="Tick"/> (the host's frame) releases it after <see cref="KeepAlive"/> without one.
/// ⛔ Only for a host that STARTED disabled: on a windowed editor (<c>StartEnabled = true</c>, count 0) a release to 0 would shut the group.
/// </summary>
public sealed class GizmoReadLease
{
    /// <summary>How long after the last read the reference is kept (HL-1: 5 s).</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(5);

    private readonly GizmoExecutionController _gate;
    private readonly Func<long> _now;
    private readonly object _lock = new();
    private long _lastRead;
    private bool _held;

    /// <param name="gate">The host's gizmo gate (<c>MapInteraction.Gate</c>).</param>
    /// <param name="now">Monotonic ticks in <see cref="Stopwatch.Frequency"/> units (default: the stopwatch); a rail passes a manual clock.</param>
    public GizmoReadLease(GizmoExecutionController gate, Func<long>? now = null)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _now = now ?? Stopwatch.GetTimestamp;
    }

    /// <summary>True while the lease holds its reference (the gizmos are drawing for a reader).</summary>
    public bool Held { get { lock (_lock) return _held; } }

    /// <summary>A reader read the gizmo feed (any thread). Returns true when this read OPENED the gate (it was idle): the group draws from the next pulled tick, so this read saw an undrawn frame.</summary>
    public bool Touch()
    {
        lock (_lock)
        {
            _lastRead = _now();
            if (_held) return false;
            _held = true;
            _gate.AddListener();
            return true;
        }
    }

    /// <summary>The host's frame: release the reference once <see cref="KeepAlive"/> passed without a read.</summary>
    public void Tick()
    {
        lock (_lock)
        {
            if (!_held || _now() - _lastRead <= (long)(KeepAlive.TotalSeconds * Stopwatch.Frequency)) return;
            _held = false;
            _gate.RemoveListener();
        }
    }
}
