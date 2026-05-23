using Fdp.Core;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.BTree.Editor.Debug;

/// <summary>
/// Production implementation of IBTreeDebugSession.
/// Maintains in-memory ring buffers for node-execution and async-event history.
/// GetCurrentStateSnapshot() returns null until kernel wiring is in place (Slice 3+).
/// Step-control methods are no-ops until the kernel adapter is wired.
/// </summary>
public sealed class BTreeDebugSession : AiDebugSessionBase, IBTreeDebugSession
{
    private const int MaxHistory = 200;

    private readonly List<BTreeNodeExecuted> _nodeHistory = new();
    private readonly List<BTreeAsyncEvent>   _asyncHistory = new();
    private bool _heatmapModeActive;
    private readonly Dictionary<Guid, int> _aggregateCounters = new();

    public event Action<BTreeBreakpointHit>? OnBreakpointHit;
    public event Action<BTreeNodeExecuted>?  OnNodeExecuted;
    public event Action<BTreeAsyncEvent>?    OnAsyncIssued;
    public event Action<BTreeAsyncEvent>?    OnAsyncResolved;
    public event Action<BTreeAsyncEvent>?    OnAsyncAborted;

    // ---- IBTreeDebugSession ------------------------------------------------

    // Returns null until the kernel snapshot adapter is implemented (Slice 3+).
    public BehaviorTreeStateSnapshot? GetCurrentStateSnapshot() => null;

    public IReadOnlyList<BTreeNodeExecuted> GetRecentNodeHistory(int max = 100)
    {
        int start = Math.Max(0, _nodeHistory.Count - max);
        return _nodeHistory.GetRange(start, _nodeHistory.Count - start);
    }

    public IReadOnlyList<BTreeAsyncEvent> GetRecentAsyncHistory(int max = 100)
    {
        int start = Math.Max(0, _asyncHistory.Count - max);
        return _asyncHistory.GetRange(start, _asyncHistory.Count - start);
    }

    public bool HeatmapModeActive
    {
        get => _heatmapModeActive;
        set => _heatmapModeActive = value;
    }

    public IReadOnlyDictionary<Guid, int>? GetAggregateCounters(Guid assetId)
    {
        if (!IsAttached || !HeatmapModeActive)
            return null;
        return _aggregateCounters;
    }

    public void ResetAggregateCounters() => _aggregateCounters.Clear();

    // ---- Kernel adapter entry points (called by future kernel adapter) -----

    /// <summary>Records a node-execution event from the kernel tracer.</summary>
    public void RecordNodeExecuted(BTreeNodeExecuted record)
    {
        if (_nodeHistory.Count >= MaxHistory)
            _nodeHistory.RemoveAt(0);
        _nodeHistory.Add(record);
        if (_heatmapModeActive)
        {
            _aggregateCounters.TryGetValue(record.NodeVisualId, out var prev);
            _aggregateCounters[record.NodeVisualId] = prev + 1;
        }
        OnNodeExecuted?.Invoke(record);
    }

    /// <summary>Records an async token lifecycle event from the kernel tracer.</summary>
    public void RecordAsyncEvent(BTreeAsyncEvent record)
    {
        if (_asyncHistory.Count >= MaxHistory)
            _asyncHistory.RemoveAt(0);
        _asyncHistory.Add(record);
        switch (record.Phase)
        {
            case BTreeAsyncPhase.Issued:   OnAsyncIssued?.Invoke(record);   break;
            case BTreeAsyncPhase.Resolved: OnAsyncResolved?.Invoke(record); break;
            case BTreeAsyncPhase.Aborted:  OnAsyncAborted?.Invoke(record);  break;
        }
    }

    /// <summary>Called by the kernel adapter when a breakpoint fires.</summary>
    public void RaiseBreakpointHit(BTreeBreakpointHit hit)
    {
        IsPaused = true;
        PausedAt = hit.Breakpoint;
        PausedOnEntity = hit.Self;
        OnBreakpointHit?.Invoke(hit);
        RaiseSessionStateChanged();
    }

    // ---- AiDebugSessionBase overrides (no-ops until kernel wiring) ---------

    protected override void OnContinueImpl()   { }
    protected override void OnPauseImpl()      { }
    protected override void OnStepOverImpl()   { }
    protected override void OnStepIntoImpl()   { }
    protected override void OnStepOutImpl()    { }

    protected override void OnDetachImpl()
    {
        _nodeHistory.Clear();
        _asyncHistory.Clear();
        _aggregateCounters.Clear();
        _heatmapModeActive = false;
    }
}
