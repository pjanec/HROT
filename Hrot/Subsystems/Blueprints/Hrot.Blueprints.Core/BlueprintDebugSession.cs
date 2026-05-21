using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Production debug session. Wires DebugProbe probe calls to breakpoint checking,
/// execution history, and editor UI event dispatch.
/// Implements soft-pause semantics per Patch 1: probes never block the calling thread.
/// </summary>
public sealed class BlueprintDebugSession : IBlueprintDebugSession
{
    private readonly BlueprintRegistry _registry;
    private readonly ISimulationView _view;
    private readonly IBlueprintTimeController _timeController;

    // Breakpoint storage: indexed by BreakpointId for management, by node-id string for fast probe lookup.
    private readonly Dictionary<BreakpointId, Breakpoint> _breakpoints    = new();
    private readonly Dictionary<string, Breakpoint>       _bpByNodeString = new(StringComparer.Ordinal);
    private int _nextBpId = 1;

    // Registered debug maps, indexed by AssetId.
    private readonly Dictionary<Guid, DebugMapIndex> _debugMaps = new();

    // Per-entity execution history ring-buffers.
    private readonly Dictionary<Entity, ExecutionHistory> _history = new();

    // Per-entity call depth counter for step semantics.
    private readonly Dictionary<Entity, int> _currentCallDepth = new();

    // Pause state (soft-pause per Patch 1).
    private bool       _isPaused;
    private Breakpoint? _pausedAt;
    private Entity?    _pausedOnEntity;

    // Step state.
    private StepMode _stepMode      = StepMode.None;
    private Entity   _stepFromEntity;
    private int      _stepFromDepth;

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
        // Record execution history for this entity.
        if (!_history.TryGetValue(self, out var hist))
            _history[self] = hist = new ExecutionHistory();
        hist.Record(new NodeHistoryEntry(nodeId, _view.Tick, _view.Time));

        // Check breakpoints: re-entrant guard prevents extra RequestPause when already paused.
        if (!_isPaused && _bpByNodeString.TryGetValue(nodeId, out var bp))
            HandleBreakpointHit(self, bp, nodeId);

        // Check step mode (only when not already paused from the BP check above).
        if (_stepMode != StepMode.None && !_isPaused && self == _stepFromEntity)
        {
            int depth = _currentCallDepth.GetValueOrDefault(self, 0);
            bool matched = _stepMode switch
            {
                StepMode.Into => true,                     // any next node for this entity
                StepMode.Over => depth <= _stepFromDepth,  // same or shallower depth
                StepMode.Out  => depth < _stepFromDepth,   // strictly shallower
                _             => false,
            };
            if (matched)
            {
                _stepMode = StepMode.None;
                // Pseudo-breakpoint: no real BP entry; step hit uses same event as breakpoint.
                var pseudoBp = new Breakpoint(default, Guid.Empty, Guid.Empty, nodeId, 0, true);
                HandleBreakpointHit(self, pseudoBp, nodeId);
            }
        }
    }

    public void OnPinValueChanged<T>(Entity self, string pinId, T value)
        where T : unmanaged
    {
        // Watch dispatch implemented in DBG-004.
    }

    public void OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName)
    {
        _currentCallDepth[entity] = _currentCallDepth.GetValueOrDefault(entity, 0) + 1;
    }

    public void OnPeerCallExit(Entity entity)
    {
        int current = _currentCallDepth.GetValueOrDefault(entity, 0);
        _currentCallDepth[entity] = Math.Max(0, current - 1);
    }

    // ---- IBlueprintDebugSession -- lifecycle --------------------------------

    public bool IsAttached => true;
    public void Detach() => throw new NotImplementedException();

    // ---- IBlueprintDebugSession -- breakpoints ------------------------------

    public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)
    {
        var nodeIdStr = nodeId.ToString("D");
        var id = new BreakpointId(_nextBpId++);
        var bp = new Breakpoint(id, assetId, graphId, nodeIdStr, 0, true);
        _breakpoints[id]          = bp;
        _bpByNodeString[nodeIdStr] = bp;
        return id;
    }

    public void ClearBreakpoint(BreakpointId id)
    {
        if (_breakpoints.TryGetValue(id, out var bp))
        {
            _breakpoints.Remove(id);
            _bpByNodeString.Remove(bp.NodeId);
        }
    }

    public void ClearAllBreakpoints()
    {
        _breakpoints.Clear();
        _bpByNodeString.Clear();
    }

    public IReadOnlyList<Breakpoint> GetBreakpoints()
        => _breakpoints.Values.ToList().AsReadOnly();

    public bool IsAnyBreakpointActive => _breakpoints.Count > 0;

    // ---- IBlueprintDebugSession -- watches ----------------------------------

    public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId) => throw new NotImplementedException();
    public void RemoveWatch(WatchId id) => throw new NotImplementedException();
    public void ClearAllWatches() => throw new NotImplementedException();
    public IReadOnlyList<Watch> GetWatches() => throw new NotImplementedException();
    public bool IsAnyWatchActive => false;

    // ---- IBlueprintDebugSession -- pause state ------------------------------

    public bool IsPaused => _isPaused;
    public Breakpoint? PausedAt => _pausedAt;
    public Entity? PausedOnEntity => _pausedOnEntity;

    // ---- IBlueprintDebugSession -- pause control ----------------------------

    public void Continue()
    {
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _stepMode       = StepMode.None;
        _timeController.RequestResume();
        OnSessionStateChanged?.Invoke();
    }

    public void Pause()
    {
        _timeController.RequestPause();
        _isPaused = true;
        OnSessionStateChanged?.Invoke();
    }

    public void StepOver()
    {
        var fromEntity = _pausedOnEntity ?? default;
        _stepMode       = StepMode.Over;
        _stepFromEntity = fromEntity;
        _stepFromDepth  = _currentCallDepth.GetValueOrDefault(fromEntity, 0);
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _timeController.RequestStepOneTick();
        OnSessionStateChanged?.Invoke();
    }

    public void StepInto()
    {
        var fromEntity = _pausedOnEntity ?? default;
        _stepMode       = StepMode.Into;
        _stepFromEntity = fromEntity;
        _stepFromDepth  = _currentCallDepth.GetValueOrDefault(fromEntity, 0);
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _timeController.RequestStepOneTick();
        OnSessionStateChanged?.Invoke();
    }

    public void StepOut()
    {
        var fromEntity = _pausedOnEntity ?? default;
        _stepMode       = StepMode.Out;
        _stepFromEntity = fromEntity;
        _stepFromDepth  = _currentCallDepth.GetValueOrDefault(fromEntity, 0);
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _timeController.RequestStepOneTick();
        OnSessionStateChanged?.Invoke();
    }

    // ---- IBlueprintDebugSession -- inspection -------------------------------

    public BlueprintStateSnapshot? GetCurrentStateSnapshot()
        => _isPaused && _pausedOnEntity.HasValue
            ? new BlueprintStateSnapshot(_pausedOnEntity.Value, Guid.Empty)  // assetId stub until DBG-004
            : null;

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
            var toRemove = _breakpoints.Values
                .Where(bp => bp.AssetId == map.AssetId)
                .Select(bp => bp.Id)
                .ToList();
            foreach (var id in toRemove)
                ClearBreakpoint(id);
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

    // ---- Private helpers ----------------------------------------------------

    // Handles a breakpoint hit (or pseudo-breakpoint from step matching).
    // Soft-pause per Patch 1: sets _isPaused, requests pause at next frame boundary,
    // fires events, and returns immediately without blocking the thread.
    private void HandleBreakpointHit(Entity self, Breakpoint bp, string nodeId)
    {
        _isPaused        = true;
        _pausedAt        = bp;
        _pausedOnEntity  = self;
        _stepMode        = StepMode.None;
        _stepFromEntity  = default;
        _stepFromDepth   = 0;

        // Increment hit count for real breakpoints (pseudo-BPs have Id.Value == 0).
        if (bp.Id.Value != 0 && _breakpoints.ContainsKey(bp.Id))
        {
            var updated = bp with { HitCount = bp.HitCount + 1 };
            _breakpoints[bp.Id]          = updated;
            _bpByNodeString[bp.NodeId]   = updated;
            _pausedAt                    = updated;

            var assetId = updated.AssetId;
            OnBreakpointListChanged?.Invoke(assetId);

            _timeController.RequestPause();
            OnBreakpointHit?.Invoke(new BreakpointHit(self, nodeId, assetId, _view.Time, _view.Tick));
        }
        else
        {
            _timeController.RequestPause();
            OnBreakpointHit?.Invoke(new BreakpointHit(self, nodeId, bp.AssetId, _view.Time, _view.Tick));
        }

        OnSessionStateChanged?.Invoke();
    }
}

