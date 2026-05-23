using Hrot.Editor.AiShared.Debug;

namespace Hrot.Hsm.Editor.Debug;

/// <summary>
/// HSM-specific extension of the shared AI debug session interface.
/// Adds snapshot access, a unified trace-history ring, and typed event surfaces
/// for each HSM kernel event category.
/// </summary>
public interface IHsmDebugSession : IAiDebugSession
{
    /// <summary>
    /// Returns the current kernel state snapshot for the focused entity,
    /// or null if no entity is attached or debug metadata is unavailable.
    /// </summary>
    HsmInstanceSnapshot? GetCurrentStateSnapshot();

    /// <summary>Returns the last <paramref name="max"/> trace records (most recent last).</summary>
    IReadOnlyList<HsmTraceRecord> GetRecentTraceHistory(int max = 100);

    event Action<HsmBreakpointHit>? OnBreakpointHit;
    event Action<HsmStateEntered>? OnStateEntered;
    event Action<HsmStateExited>? OnStateExited;
    event Action<HsmTransitionFired>? OnTransitionFired;
    event Action<HsmEventQueued>? OnEventQueued;
    event Action<HsmRegionConflict>? OnRegionConflict;
    event Action<HsmGuardEvaluated>? OnGuardEvaluated;
    event Action<HsmTimerEvent>? OnTimerEvent;
}
