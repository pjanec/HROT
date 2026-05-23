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

    /// <summary>
    /// When true, RecordTrace(HsmStateEntered) increments per-StableId entry counters.
    /// False by default; set to true before activating heatmap view.
    /// </summary>
    bool HeatmapModeActive { get; set; }

    /// <summary>
    /// Returns a snapshot of per-StableId state-entry counts for the given asset,
    /// or null if not attached or heatmap mode is inactive.
    /// </summary>
    IReadOnlyDictionary<Guid, int>? GetStateEntryCounts(Guid assetId);

    /// <summary>Resets all state-entry counters to zero.</summary>
    void ResetStateEntryCounts();

    event Action<HsmBreakpointHit>? OnBreakpointHit;
    event Action<HsmStateEntered>? OnStateEntered;
    event Action<HsmStateExited>? OnStateExited;
    event Action<HsmTransitionFired>? OnTransitionFired;
    event Action<HsmEventQueued>? OnEventQueued;
    event Action<HsmRegionConflict>? OnRegionConflict;
    event Action<HsmGuardEvaluated>? OnGuardEvaluated;
    event Action<HsmTimerEvent>? OnTimerEvent;
}
