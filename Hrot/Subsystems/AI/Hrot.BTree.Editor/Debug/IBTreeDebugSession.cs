using Hrot.Editor.AiShared.Debug;

namespace Hrot.BTree.Editor.Debug;

/// <summary>
/// BTree-specific extension of the shared AI debug session interface.
/// Adds snapshot access, node-execution history, async-token history,
/// and BTree-specific event surface.
/// </summary>
public interface IBTreeDebugSession : IAiDebugSession
{
    /// <summary>
    /// Returns the current kernel state snapshot for the focused entity,
    /// or null if no entity is attached or debug metadata is unavailable.
    /// </summary>
    BehaviorTreeStateSnapshot? GetCurrentStateSnapshot();

    /// <summary>Returns the last <paramref name="max"/> node execution records (most recent last).</summary>
    IReadOnlyList<BTreeNodeExecuted> GetRecentNodeHistory(int max = 100);

    /// <summary>Returns the last <paramref name="max"/> async token events (most recent last).</summary>
    IReadOnlyList<BTreeAsyncEvent> GetRecentAsyncHistory(int max = 100);

    /// <summary>
    /// When true, RecordNodeExecuted increments per-node aggregate entry counters.
    /// False by default; set to true before activating heatmap view.
    /// </summary>
    bool HeatmapModeActive { get; set; }

    /// <summary>
    /// Returns a snapshot of per-VisualId aggregate entry counts for the given asset,
    /// or null if not attached or heatmap mode is inactive.
    /// </summary>
    IReadOnlyDictionary<Guid, int>? GetAggregateCounters(Guid assetId);

    /// <summary>Resets all aggregate counters to zero.</summary>
    void ResetAggregateCounters();

    event Action<BTreeBreakpointHit>? OnBreakpointHit;
    event Action<BTreeNodeExecuted>? OnNodeExecuted;
    event Action<BTreeAsyncEvent>? OnAsyncIssued;
    event Action<BTreeAsyncEvent>? OnAsyncResolved;
    event Action<BTreeAsyncEvent>? OnAsyncAborted;
}
