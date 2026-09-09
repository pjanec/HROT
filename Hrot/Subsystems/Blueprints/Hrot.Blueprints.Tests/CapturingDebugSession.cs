using Fdp.Core;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using System.Linq;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// Test double: records every IBlueprintProbeSink notification for assertion.
/// Also implements IBlueprintDebugSession with a simple in-memory breakpoint set.
/// </summary>
public sealed class CapturingDebugSession : IBlueprintProbeSink, IBlueprintDebugSession
{
    private readonly List<NodeEnterRecord> _nodeEntries = new();
    private readonly List<PinValueRecord>  _pinValues   = new();
    private readonly HashSet<string>       _breakpoints = new();
    private readonly Dictionary<Guid, DebugMap> _maps   = new();
    private int _nextBpId = 1;
    private readonly Dictionary<BreakpointId, Breakpoint> _bpRecords = new();

    // ---- IBlueprintProbeSink ------------------------------------------------

    public void OnNodeEnter(Entity self, string nodeId)
    {
        _nodeEntries.Add(new NodeEnterRecord(self, nodeId, Time: 0f));
        var nodeExecuted = new NodeExecuted(self, Guid.Empty, Guid.Empty, nodeId, 0f, 0u);
        lock (_recentNodeHistory)
            _recentNodeHistory.Add(nodeExecuted);
        OnNodeExecuted?.Invoke(nodeExecuted);
        if (_breakpoints.Contains(nodeId))
            OnBreakpointHit?.Invoke(new BreakpointHit(self, nodeId, Guid.Empty, 0f, 0u));
    }

    public void OnPinValueChanged<T>(Entity self, string pinId, T value)
        where T : unmanaged
        => _pinValues.Add(new PinValueRecord(self, pinId, value));

    public void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName) { }
    public void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName) { }

    // ---- IBlueprintDebugSession -- lifecycle --------------------------------

    public bool IsAttached => true;
    public void Attach()  { }
    public void Detach()  { }

    // ---- IBlueprintDebugSession -- breakpoints (string overloads for tests) --

    public void SetBreakpoint(string nodeId)   => _breakpoints.Add(nodeId);
    public void ClearBreakpoint(string nodeId) => _breakpoints.Remove(nodeId);
    public bool IsAnyBreakpointActive          => _breakpoints.Count > 0;

    // IBlueprintDebugSession GUID-based breakpoint methods
    public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)
    {
        var nodeIdStr = nodeId.ToString("D");
        var id        = new BreakpointId(_nextBpId++);
        var bp = new Breakpoint(id, assetId, graphId, nodeIdStr, 0, true);
        _bpRecords[id] = bp;
        _breakpoints.Add(nodeIdStr);
        return id;
    }
    public void ClearBreakpoint(BreakpointId id)
    {
        if (_bpRecords.TryGetValue(id, out var bp))
        {
            _breakpoints.Remove(bp.NodeId);
            _bpRecords.Remove(id);
        }
    }
    public void ClearAllBreakpoints()
    {
        _breakpoints.Clear();
        _bpRecords.Clear();
    }
    public IReadOnlyList<Breakpoint> GetBreakpoints()
        => _bpRecords.Values.ToList().AsReadOnly();

    // ---- IBlueprintDebugSession -- watches ----------------------------------

    public bool IsAnyWatchActive => false;
    public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType) => throw new NotImplementedException();
    public void RemoveWatch(WatchId id) => throw new NotImplementedException();
    public void ClearAllWatches() => throw new NotImplementedException();
    public IReadOnlyList<Watch> GetWatches() => throw new NotImplementedException();

    // ---- IBlueprintDebugSession -- pause state ------------------------------

    // Settable for test control.
    public bool IsPaused { get; set; } = false;
    public Breakpoint? PausedAt => null;
    public Entity? PausedOnEntity => null;

    // ---- IBlueprintDebugSession -- entity filter ----------------------------

    public void SetEntityFilter(Entity? entity) { }
    public Entity? GetEntityFilter() => null;

    // ---- IBlueprintDebugSession -- active entity tracking ------------------

    public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

    // ---- IBlueprintDebugSession -- pause control ----------------------------

    public int ContinueCallCount { get; private set; }
    public int StepOverCallCount { get; private set; }
    public int StepIntoCallCount { get; private set; }
    public int StepOutCallCount { get; private set; }
    public int StepBackCallCount { get; private set; }

    public void Continue()  { ContinueCallCount++; }
    public void StepOver()  { StepOverCallCount++; }
    public void StepInto()  { StepIntoCallCount++; }
    public void StepOut()   { StepOutCallCount++; }
    public void StepBack()  { StepBackCallCount++; }
    public void Pause()     { }

    // NGS-2.1: virtual pointer (stub — always -1 in the capturing test double).
    public int CurrentNodePointer => -1;
    public string? CurrentNodeId => null;
    public int RecordedNodeCount => 0;

    // ---- IBlueprintDebugSession -- inspection -------------------------------

    public BlueprintStateSnapshot? GetCurrentStateSnapshot() => null;
    public BlueprintStateSnapshot? CaptureLiveState(Entity self, Guid assetId) => null;
    private readonly List<NodeExecuted> _recentNodeHistory = new();

    public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100)
    {
        var entries = _recentNodeHistory;
        return entries.Skip(Math.Max(0, entries.Count - maxCount)).Take(maxCount).ToList().AsReadOnly();
    }
    public IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity entity, int maxCount = 100)
        => Array.Empty<NodeHistoryEntry>();
    public IReadOnlyList<CallFrame> GetCurrentCallStack() => Array.Empty<CallFrame>();

    // ---- IBlueprintDebugSession -- events -----------------------------------

    public event Action<BreakpointHit>? OnBreakpointHit;
    public event Action<NodeExecuted>?  OnNodeExecuted;
    public event Action? OnSessionStateChanged;
    public event Action<Guid>? OnBreakpointListChanged;

    // Explicit interface impl: avoids conflict with generic method OnPinValueChanged<T>.
    private Action<PinValueChanged>? _pinValueChangedHandlers;

    event Action<PinValueChanged>? IBlueprintDebugSession.OnPinValueChangedEvent
    {
        add    => _pinValueChangedHandlers += value;
        remove => _pinValueChangedHandlers -= value;
    }

    public void RegisterDebugMap(DebugMap map)   => _maps[map.AssetId] = map;
    public void UnregisterDebugMap(Guid assetId) => _maps.Remove(assetId);

    public bool IsNodeBreakpointable(Guid assetId, Guid graphId, Guid nodeId)
        => !_maps.TryGetValue(assetId, out var map)
            || map.BreakpointTargets.ContainsKey(nodeId);

    // ---- IBlueprintDebugSession -- PDB locator ------------------------------

    public void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver) { }

    // ---- IBlueprintDebugSession -- hot reload --------------------------------

    public void OnHotReloadBegin() { }
    public void OnHotReloadCompleted(Guid[] reloadedAssetIds) { }

    // BPF-003: tick boundary reset (no-op in test double; tests invoke directly).
    // BP-35: also counted, so a test can assert that DebugProbe.NewTick actually reaches this
    // session -- including when it sits behind a MultiplexingProbeSink, where the
    // `Sink as IBlueprintDebugSession` cast in DebugProbe would otherwise silently miss it.
    public void OnNewTick() => NewTickCount++;

    /// <summary>Number of <see cref="OnNewTick"/> calls received (BP-35).</summary>
    public int NewTickCount { get; private set; }

    // ---- Inspection helpers -------------------------------------------------

    public IReadOnlyList<NodeEnterRecord> NodeEntries => _nodeEntries;
    public IReadOnlyList<PinValueRecord>  PinValues   => _pinValues;

    public bool Hit(string nodeId)     => _nodeEntries.Any(r => r.NodeId == nodeId);
    public int  HitCount(string nodeId) => _nodeEntries.Count(r => r.NodeId == nodeId);

    public IReadOnlyList<NodeEnterRecord> HitsFor(Entity self)
        => _nodeEntries.Where(r => r.Self == self).ToList();

    public void Clear()
    {
        _nodeEntries.Clear();
        _pinValues.Clear();
    }
}

public sealed record NodeEnterRecord(Entity Self, string NodeId, float Time);
public sealed record PinValueRecord(Entity Self, string PinId, object? Value);
