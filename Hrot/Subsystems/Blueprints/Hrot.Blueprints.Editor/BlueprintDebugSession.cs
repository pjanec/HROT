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
    private readonly IEngineDebugTimeController _timeController;

    // Breakpoint storage: indexed by BreakpointId for management, by node-id string for fast probe lookup.
    private readonly Dictionary<BreakpointId, Breakpoint> _breakpoints    = new();
    private readonly Dictionary<string, Breakpoint>       _bpByNodeString = new(StringComparer.Ordinal);
    private int _nextBpId = 1;

    // Watch storage: indexed by WatchId for management, by pin-id string for fast probe lookup.
    private readonly Dictionary<WatchId, Watch>   _watches            = new();
    private readonly Dictionary<string, Watch>    _watchesByPinString = new(StringComparer.Ordinal);
    private int _nextWatchId = 1;

    // Registered debug maps, indexed by AssetId.
    private readonly Dictionary<Guid, DebugMapIndex> _debugMaps = new();

    // Per-entity execution history ring-buffers.
    private readonly Dictionary<Entity, ExecutionHistory> _history = new();

    // Per-entity call depth counter for step semantics.
    private readonly Dictionary<Entity, int> _currentCallDepth = new();

    // Per-asset active entity tracking (entities with call depth > 0).
    private readonly Dictionary<Guid, HashSet<Entity>> _activeEntities = new();

    // PDB locators: asset-id -> resolver returning pdb path.
    private readonly Dictionary<Guid, Func<string>> _pdbLocators = new();

    // Pause state (soft-pause per Patch 1).
    private bool       _isPaused;
    private Breakpoint? _pausedAt;
    private Entity?    _pausedOnEntity;

    // Step state.
    private StepMode _stepMode      = StepMode.None;
    private Entity   _stepFromEntity;
    private int      _stepFromDepth;

    // Entity filter: when set, only events from this entity are processed.
    private Entity? _entityFilter;

    // Optional data-breakpoint manager. When set, external hits from Blueprint probes
    // are routed through it (triggers triple-buffer rewind + OnBreakpointHit) instead
    // of requesting a raw pause via _timeController.
    private Hrot.Diagnostics.Breakpoints.IDataBreakpointManager? _dataBreakpointManager;

    // Tracks the manager-side BreakpointId for each session-side BreakpointId so we
    // can clean up ExternalHitTag registrations when a breakpoint is cleared.
    private readonly Dictionary<BreakpointId, Hrot.Diagnostics.Breakpoints.BreakpointId> _mgrBpIds = new();

    public BlueprintDebugSession(
        BlueprintRegistry registry,
        ISimulationView view,
        IEngineDebugTimeController timeController)
    {
        _registry        = registry        ?? throw new ArgumentNullException(nameof(registry));
        _view            = view            ?? throw new ArgumentNullException(nameof(view));
        _timeController  = timeController  ?? throw new ArgumentNullException(nameof(timeController));
    }

    // ---- IBlueprintProbeSink ------------------------------------------------

    public void OnNodeEnter(Entity self, string nodeId)
    {
        // Entity filter: skip events from entities that are not the filtered entity.
        if (_entityFilter.HasValue && self != _entityFilter.Value) return;

        // Record execution history for this entity.
        if (!_history.TryGetValue(self, out var hist))
            _history[self] = hist = new ExecutionHistory();
        hist.Record(new NodeHistoryEntry(nodeId, _view.Tick, _view.Time));

        // Fire OnNodeExecuted so subscribers (e.g., Callstack window) can update the trail.
        _onNodeExecuted?.Invoke(new NodeExecuted(self, Guid.Empty, Guid.Empty, nodeId, _view.Time, _view.Tick));

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
        // Entity filter: skip events from entities that are not the filtered entity.
        if (_entityFilter.HasValue && self != _entityFilter.Value) return;

        if (!_watchesByPinString.TryGetValue(pinId, out var watch))
            return;  // no watch for this pin -- zero allocation path

        watch.WriteValue(value, self, _view.Tick);

        // Fire event only if there are listeners (avoid ToArray() allocation when no listeners).
        var evt = _onPinValueChangedEvent;
        if (evt != null)
        {
            evt.Invoke(new PinValueChanged(
                self,
                pinId,
                watch.LastValueBytes.ToArray(),    // 1 allocation only when listener present
                watch.ExpectedType,
                _view.Tick));
        }
    }

    public void OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName)
    {
        int prevDepth = _currentCallDepth.GetValueOrDefault(entity, 0);
        _currentCallDepth[entity] = prevDepth + 1;
        if (prevDepth == 0)
        {
            // Entity entering first-level call; find asset by name or use Guid.Empty as fallback.
            var assetId = Guid.Empty;
            foreach (var kv in _debugMaps)
            {
                if (kv.Value.AssetName == targetAssetName)
                {
                    assetId = kv.Key;
                    break;
                }
            }
            if (!_activeEntities.TryGetValue(assetId, out var set))
                _activeEntities[assetId] = set = new HashSet<Entity>();
            set.Add(entity);
        }
    }

    public void OnPeerCallExit(Entity entity)
    {
        int current = _currentCallDepth.GetValueOrDefault(entity, 0);
        int next    = Math.Max(0, current - 1);
        _currentCallDepth[entity] = next;
        if (next == 0)
        {
            // Entity exiting last call; remove from all active sets.
            foreach (var set in _activeEntities.Values)
                set.Remove(entity);
        }
    }

    // ---- IBlueprintDebugSession -- lifecycle --------------------------------

    public bool IsAttached => true;

    public void Detach()
    {
        if (_isPaused) Continue();
        DebugProbe.Sink = NullProbeSink.Instance;
        _breakpoints.Clear();
        _bpByNodeString.Clear();
        _watches.Clear();
        _watchesByPinString.Clear();
        _activeEntities.Clear();
        _history.Clear();
        _currentCallDepth.Clear();
        _debugMaps.Clear();
        _pdbLocators.Clear();
        OnSessionStateChanged?.Invoke();
    }

    // ---- IBlueprintDebugSession -- breakpoints ------------------------------

    public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)
    {
        var nodeIdStr = nodeId.ToString("D");
        var id = new BreakpointId(_nextBpId++);
        var bp = new Breakpoint(id, assetId, graphId, nodeIdStr, 0, true);
        _breakpoints[id]          = bp;
        _bpByNodeString[nodeIdStr] = bp;

        // Register a matching tag predicate in the data-breakpoint manager so that
        // OnExternalHit(nodeIdStr, entity) finds a registered BP and triggers OnHit.
        if (_dataBreakpointManager != null)
        {
            var mgrId = _dataBreakpointManager.AddBreakpoint(
                new Fdp.Toolkit.ReplayBrowser.Search.ExternalHitTagPredicateDto { Tag = nodeIdStr },
                displayName: $"Blueprint node {nodeIdStr}");
            _mgrBpIds[id] = mgrId;
        }

        return id;
    }

    public void ClearBreakpoint(BreakpointId id)
    {
        if (_breakpoints.TryGetValue(id, out var bp))
        {
            _breakpoints.Remove(id);
            _bpByNodeString.Remove(bp.NodeId);

            if (_dataBreakpointManager != null && _mgrBpIds.TryGetValue(id, out var mgrId))
            {
                _dataBreakpointManager.Remove(mgrId);
                _mgrBpIds.Remove(id);
            }
        }
    }

    public void ClearAllBreakpoints()
    {
        if (_dataBreakpointManager != null)
        {
            foreach (var mgrId in _mgrBpIds.Values)
                _dataBreakpointManager.Remove(mgrId);
        }
        _mgrBpIds.Clear();
        _breakpoints.Clear();
        _bpByNodeString.Clear();
    }

    public IReadOnlyList<Breakpoint> GetBreakpoints()
        => _breakpoints.Values.ToList().AsReadOnly();

    public bool IsAnyBreakpointActive => _breakpoints.Count > 0;

    // ---- IBlueprintDebugSession -- watches ----------------------------------

    public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType)
    {
        var id    = new WatchId(_nextWatchId++);
        var watch = new Watch(id, assetId, graphId, pinId, displayName, expectedType);
        _watches[id]                       = watch;
        _watchesByPinString[watch.PinIdString] = watch;
        return id;
    }

    public void RemoveWatch(WatchId id)
    {
        if (_watches.TryGetValue(id, out var watch))
        {
            _watches.Remove(id);
            _watchesByPinString.Remove(watch.PinIdString);
        }
    }

    public void ClearAllWatches()
    {
        _watches.Clear();
        _watchesByPinString.Clear();
    }

    public IReadOnlyList<Watch> GetWatches()
        => _watches.Values.ToList().AsReadOnly();

    public bool IsAnyWatchActive => _watches.Count > 0;

    // ---- IBlueprintDebugSession -- entity filter ----------------------------

    public void SetEntityFilter(Entity? entity) => _entityFilter = entity;
    public Entity? GetEntityFilter() => _entityFilter;

    /// <summary>
    /// Wires in the data-breakpoint manager so that Blueprint probe hits are routed
    /// through the triple-buffer rewind path instead of the raw time-controller pause.
    /// Any session breakpoints that were registered before this call are registered
    /// retroactively so <c>OnExternalHit</c> can find them.
    /// </summary>
    public void SetDataBreakpointManager(Hrot.Diagnostics.Breakpoints.IDataBreakpointManager? manager)
    {
        // Unregister old manager registrations.
        if (_dataBreakpointManager != null)
        {
            foreach (var mgrId in _mgrBpIds.Values)
                _dataBreakpointManager.Remove(mgrId);
            _mgrBpIds.Clear();
        }

        _dataBreakpointManager = manager;

        // Register existing session BPs with the new manager.
        if (_dataBreakpointManager != null)
        {
            foreach (var bp in _breakpoints.Values)
            {
                var mgrId = _dataBreakpointManager.AddBreakpoint(
                    new Fdp.Toolkit.ReplayBrowser.Search.ExternalHitTagPredicateDto { Tag = bp.NodeId },
                    displayName: $"Blueprint node {bp.NodeId}");
                _mgrBpIds[bp.Id] = mgrId;
            }
        }
    }

    // ---- IBlueprintDebugSession -- active entity tracking ------------------

    public IReadOnlyList<Entity> GetActiveEntities(Guid assetId)
        => _activeEntities.TryGetValue(assetId, out var set)
            ? set.ToList().AsReadOnly()
            : Array.Empty<Entity>().AsReadOnly();

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
    {
        var all = new List<NodeExecuted>();
        foreach (var (entity, hist) in _history)
        {
            foreach (var entry in hist.GetRecent(maxCount))
                all.Add(new NodeExecuted(entity, Guid.Empty, Guid.Empty, entry.NodeId, entry.SimTime, entry.Tick));
        }
        // Return the most recent maxCount entries across all entities.
        if (all.Count > maxCount)
            all.Sort((a, b) => b.Tick.CompareTo(a.Tick));
        return all.Count <= maxCount ? all.AsReadOnly() : all.Take(maxCount).ToList().AsReadOnly();
    }

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
            // Mark watches stale for this asset (structure changed; cached values are invalid).
            foreach (var watch in _watches.Values.Where(w => w.AssetId == map.AssetId))
                watch.IsStale = true;
        }
        _debugMaps[map.AssetId] = index;
    }

    public void UnregisterDebugMap(Guid assetId)
    {
        _debugMaps.Remove(assetId);
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

    // ---- IBlueprintDebugSession -- PDB locator ------------------------------

    public void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver)
        => _pdbLocators[assetId] = pdbPathResolver;

    // ---- IBlueprintDebugSession -- hot reload --------------------------------

    public void OnHotReloadBegin()
    {
        if (_isPaused) Continue();
        // Mark all watches as stale (reload invalidates runtime state).
        foreach (var watch in _watches.Values)
            watch.IsStale = true;
        OnSessionStateChanged?.Invoke();
    }

    public void OnHotReloadCompleted(Guid[] reloadedAssetIds)
    {
        // Re-validate watches: if watch's asset was reloaded and is now in _debugMaps, clear stale flag.
        foreach (var assetId in reloadedAssetIds)
        {
            foreach (var watch in _watches.Values.Where(w => w.AssetId == assetId))
                watch.IsStale = false;  // map was reloaded; watch is valid again
            // Fire breakpoint list changed for affected assets.
            OnBreakpointListChanged?.Invoke(assetId);
        }
        OnSessionStateChanged?.Invoke();
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
        // Pseudo-breakpoints (step hits) have default BreakpointId (Value == 0); skip hit count.
        if (bp.Id.Value != 0 && _breakpoints.ContainsKey(bp.Id))
        {
            var updated = bp with { HitCount = bp.HitCount + 1 };
            _breakpoints[bp.Id]          = updated;
            _bpByNodeString[bp.NodeId]   = updated;
            _pausedAt                    = updated;

            var assetId = updated.AssetId;

            if (_dataBreakpointManager != null)
                _dataBreakpointManager.OnExternalHit(nodeId, self);
            else
                _timeController.RequestPause();
            OnBreakpointHit?.Invoke(new BreakpointHit(
                self, nodeId, assetId, _view.Time, _view.Tick,
                ResolveSourceFilePath(assetId, nodeId),
                ResolveSourceLine(assetId, nodeId)));
        }
        else
        {
            if (_dataBreakpointManager != null)
                _dataBreakpointManager.OnExternalHit(nodeId, self);
            else
                _timeController.RequestPause();
            OnBreakpointHit?.Invoke(new BreakpointHit(
                self, nodeId, bp.AssetId, _view.Time, _view.Tick,
                ResolveSourceFilePath(bp.AssetId, nodeId),
                ResolveSourceLine(bp.AssetId, nodeId)));
        }

        OnSessionStateChanged?.Invoke();
    }

    // Resolve source file path for a node hit via PDB locator + debug map lookup.
    private string? ResolveSourceFilePath(Guid assetId, string nodeId)
    {
        if (!_pdbLocators.TryGetValue(assetId, out var locator)) return null;
        if (!_debugMaps.TryGetValue(assetId, out var index)) return null;
        var entry = index.TryResolveNode(nodeId);
        if (entry == null || entry.SourceStartLine == 0) return null;
        return locator();
    }

    // Resolve source start line for a node hit via debug map lookup.
    private int? ResolveSourceLine(Guid assetId, string nodeId)
    {
        if (!_debugMaps.TryGetValue(assetId, out var index)) return null;
        var entry = index.TryResolveNode(nodeId);
        if (entry == null || entry.SourceStartLine == 0) return null;
        return entry.SourceStartLine;
    }

    // UI/inspection helper: decode a raw byte buffer into a typed value (not on probe path).
    public static object? MarshalFromBytes(byte[] bytes, Type type)
    {
        if (bytes == null || bytes.Length == 0) return null;
        if (type == typeof(int))    return System.Runtime.InteropServices.MemoryMarshal.Read<int>(bytes);
        if (type == typeof(float))  return System.Runtime.InteropServices.MemoryMarshal.Read<float>(bytes);
        if (type == typeof(bool))   return bytes[0] != 0;
        if (type == typeof(uint))   return System.Runtime.InteropServices.MemoryMarshal.Read<uint>(bytes);
        if (type == typeof(long))   return System.Runtime.InteropServices.MemoryMarshal.Read<long>(bytes);
        if (type == typeof(double)) return System.Runtime.InteropServices.MemoryMarshal.Read<double>(bytes);
        // Fallback for unrecognized types: return raw bytes as-is.
        return bytes;
    }
}
