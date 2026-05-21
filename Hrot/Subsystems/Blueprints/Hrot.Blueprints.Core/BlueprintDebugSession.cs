using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Production debug session. Wires DebugProbe probe calls to breakpoint checking,
/// execution history, and editor UI event dispatch.
/// Stub implementation for TASK-DBG-001; breakpoint logic filled in by DBG-003.
/// </summary>
public sealed class BlueprintDebugSession : IBlueprintDebugSession
{
    private readonly BlueprintRegistry _registry;
    private readonly ISimulationView _view;
    private readonly IBlueprintTimeController _timeController;

    // Minimal breakpoint storage for SC3 wiring.
    private readonly HashSet<string> _nodeBreakpoints = new();

    // Registered debug maps, indexed by AssetId.
    private readonly Dictionary<Guid, DebugMapIndex> _debugMaps = new();

    // Per-entity execution history ring-buffers.
    private readonly Dictionary<Entity, ExecutionHistory> _history = new();

    public BlueprintDebugSession(
        BlueprintRegistry registry,
        ISimulationView view,
        IBlueprintTimeController timeController)
    {
        _registry        = registry        ?? throw new ArgumentNullException(nameof(registry));
        _view            = view            ?? throw new ArgumentNullException(nameof(view));
        _timeController  = timeController  ?? throw new ArgumentNullException(nameof(timeController));
    }

    // ---- IBlueprintProbeSink ------------------------------------------------

    public void OnNodeEnter(Entity self, string nodeId)
    {
        // Minimal breakpoint wiring for SC3: check if any registered breakpoint matches
        // the entering node and request a soft pause if so.
        if (_nodeBreakpoints.Contains(nodeId))
        {
            _timeController.RequestPause();
            OnBreakpointHit?.Invoke(new BreakpointHit(self, nodeId, Guid.Empty, 0f, 0u));
            OnSessionStateChanged?.Invoke();
        }
        // Record execution history for this entity.
        if (!_history.TryGetValue(self, out var hist))
            _history[self] = hist = new ExecutionHistory();
        hist.Record(new NodeHistoryEntry(nodeId, _view.Tick, _view.Time));
    }

    public void OnPinValueChanged<T>(Entity self, string pinId, T value)
        where T : unmanaged
    {
        // Watch dispatch implemented in DBG-004.
    }

    public void OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName)
    {
        // Step depth tracking implemented in DBG-003.
    }

    public void OnPeerCallExit(Entity entity)
    {
        // Step depth tracking implemented in DBG-003.
    }

    // ---- IBlueprintDebugSession -- lifecycle --------------------------------

    public bool IsAttached => true;
    public void Detach() => throw new NotImplementedException();

    // ---- IBlueprintDebugSession -- breakpoints ------------------------------

    public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)
    {
        // Full matching logic in DBG-003. Minimal wiring: track by nodeId string for SC3.
        _nodeBreakpoints.Add(nodeId.ToString());
        return default;
    }

    public void ClearBreakpoint(BreakpointId id) => throw new NotImplementedException();
    public void ClearAllBreakpoints() => _nodeBreakpoints.Clear();
    public IReadOnlyList<Breakpoint> GetBreakpoints() => throw new NotImplementedException();
    public bool IsAnyBreakpointActive => _nodeBreakpoints.Count > 0;

    // ---- IBlueprintDebugSession -- watches ----------------------------------

    public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId) => throw new NotImplementedException();
    public void RemoveWatch(WatchId id) => throw new NotImplementedException();
    public void ClearAllWatches() => throw new NotImplementedException();
    public IReadOnlyList<Watch> GetWatches() => throw new NotImplementedException();
    public bool IsAnyWatchActive => false;

    // ---- IBlueprintDebugSession -- pause state ------------------------------

    public bool IsPaused => _timeController.IsPausedByDebugger;
    public Breakpoint? PausedAt => null;
    public Entity? PausedOnEntity => null;

    // ---- IBlueprintDebugSession -- pause control ----------------------------

    public void Continue()    => throw new NotImplementedException();
    public void StepOver()    => throw new NotImplementedException();
    public void StepInto()    => throw new NotImplementedException();
    public void StepOut()     => throw new NotImplementedException();
    public void Pause()       => throw new NotImplementedException();

    // ---- IBlueprintDebugSession -- inspection -------------------------------

    public BlueprintStateSnapshot? GetCurrentStateSnapshot() => throw new NotImplementedException();
    public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100)
        => Array.Empty<NodeExecuted>();

    // Non-interface overload: returns per-entity execution history.
    // Per-entity view is the primary entry point; GetRecentNodeHistory (no entity) deferred to DBG-005.
    public IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity entity, int maxCount = 100)
    {
        if (!_history.TryGetValue(entity, out var hist))
            return Array.Empty<NodeHistoryEntry>();
        return hist.GetRecent(maxCount);
    }

    // ---- IBlueprintDebugSession -- map registration ------------------------

    public void RegisterDebugMap(DebugMap map)
    {
        var index = new DebugMapIndex(map);
        if (_debugMaps.TryGetValue(map.AssetId, out var existing) &&
            existing.StructureHash != map.StructureHash)
        {
            // Structure changed: clear breakpoints for this asset and notify.
            // Full per-asset breakpoint filtering deferred to DBG-003; stub clears all.
            _nodeBreakpoints.Clear();
            OnBreakpointListChanged?.Invoke(map.AssetId);
            // Stale watch marking deferred to DBG-004.
        }
        _debugMaps[map.AssetId] = index;
    }

    public void UnregisterDebugMap(Guid assetId)
    {
        _debugMaps.Remove(assetId);
        // Stale watch cleanup deferred to DBG-004.
    }

    // ---- IBlueprintDebugSession -- events -----------------------------------

    public event Action<BreakpointHit>? OnBreakpointHit;
    public event Action? OnSessionStateChanged;
    public event Action<Guid>? OnBreakpointListChanged;

    // Explicit implementations for events not yet raised (stubs for DBG-002 / DBG-003 / DBG-004).
    private Action<NodeExecuted>? _onNodeExecuted;
    event Action<NodeExecuted>? IBlueprintDebugSession.OnNodeExecuted
    {
        add    => _onNodeExecuted += value;
        remove => _onNodeExecuted -= value;
    }

    private Action<PinValueChanged>? _onPinValueChangedEvent;
    event Action<PinValueChanged>? IBlueprintDebugSession.OnPinValueChangedEvent
    {
        add    => _onPinValueChangedEvent += value;
        remove => _onPinValueChangedEvent -= value;
    }
}
