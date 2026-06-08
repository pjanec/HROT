using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
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
    // TEMP DIAGNOSTIC — remove after debugging breakpoint issue.
    private static int _diagCount;
    private static readonly string _diagLogPath = @"D:\Work\IOS-IG-SimHost-FDP-2\bp-diag.log";
    private static void DiagLog(string msg)
    {
        if (_diagCount++ < 200)
            System.IO.File.AppendAllText(_diagLogPath, $"{System.DateTime.Now:HH:mm:ss.fff} {msg}\n");
    }

    // Per-frame dedup set: cleared by OnNewTick. Prevents double-pause for multiple entities
    // hitting the same breakpoint in the same tick (BPF-003 section 9.2).
    private readonly HashSet<BreakpointId> _firedBreakpointsThisTick = new();

    // Watch storage: indexed by WatchId for management, by pin-id string for fast probe lookup.
    private readonly Dictionary<WatchId, Watch>   _watches            = new();
    private readonly Dictionary<string, Watch>    _watchesByPinString = new(StringComparer.Ordinal);
    private int _nextWatchId = 1;

    // Registered debug maps, indexed by AssetId.
    private readonly Dictionary<Guid, DebugMapIndex> _debugMaps = new();

    // Per-entity execution history ring-buffers.
    private readonly Dictionary<Entity, ExecutionHistory> _history = new();

    // Per-entity active call depth counter for step semantics.
    private readonly Dictionary<Entity, int> _currentCallDepth = new();

    // Per-entity call frame stack for GetCurrentCallStack() (Editor DD §8.7).
    private readonly Dictionary<Entity, List<CallFrame>> _callStacks = new();

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
    private uint     _stepFromTick;  // used by StepOut at depth 0 (BPF-005)

    // Entity filter: when set, only events from this entity are processed.
    private Entity? _entityFilter;

    // Optional data-breakpoint manager.
    private Hrot.Diagnostics.Breakpoints.IDataBreakpointManager? _dataBreakpointManager;

    // Tracks the manager-side BreakpointId for each session-side BreakpointId.
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
        if (_entityFilter.HasValue && self != _entityFilter.Value) return;

        if (!_history.TryGetValue(self, out var hist))
            _history[self] = hist = new ExecutionHistory();
        hist.Record(new NodeHistoryEntry(nodeId, _view.Tick, _view.Time));

        _onNodeExecuted?.Invoke(new NodeExecuted(self, Guid.Empty, Guid.Empty, nodeId, _view.Time, _view.Tick));

        DiagLog($"OnNodeEnter entity={self.Index} nodeId='{nodeId}' bpDictSize={_bpByNodeString.Count} inDict={_bpByNodeString.ContainsKey(nodeId)} mgr={_dataBreakpointManager != null} paused={_isPaused}");

        if (_bpByNodeString.TryGetValue(nodeId, out var bp))
        {
            if (bp.Enabled && !bp.IsStale)
            {
                bool hashOk = true;
                if (bp.AssetStructureHashAtSetTime != 0 &&
                    _debugMaps.TryGetValue(bp.AssetId, out var mapIdx) &&
                    mapIdx.StructureHash != bp.AssetStructureHashAtSetTime)
                {
                    hashOk = false;
                    var stale = bp with { IsStale = true };
                    _breakpoints[bp.Id]        = stale;
                    _bpByNodeString[bp.NodeId] = stale;
                }

                if (hashOk)
                {
                    if (_firedBreakpointsThisTick.Add(bp.Id))
                    {
                        if (!_isPaused)
                            HandleBreakpointHit(self, bp, nodeId);
                        else
                            IncrementHitCountOnly(bp); // paused already: accumulate but no re-pause
                    }
                    else
                    {
                        // Same-tick dedup: accumulate HitCount across entities (design §9.2).
                        IncrementHitCountOnly(bp);
                    }
                }
            }
        }

        if (_stepMode != StepMode.None && !_isPaused)
        {
            // BPF-005: entity-death abandonment.
            if (_stepFromEntity != default && !_view.IsAlive(_stepFromEntity))
            {
                _stepMode       = StepMode.None;
                _stepFromEntity = default;
                _stepFromDepth  = 0;
                return;
            }

            if (self == _stepFromEntity)
            {
                int depth = _currentCallDepth.GetValueOrDefault(self, 0);
                bool matched = _stepMode switch
                {
                    StepMode.Into => true,
                    StepMode.Over => depth <= _stepFromDepth,
                    // BPF-005: StepOut at depth 0 re-pauses on next tick boundary.
                    StepMode.Out  => depth < _stepFromDepth ||
                                     (depth == 0 && _stepFromDepth == 0 && _view.Tick > _stepFromTick),
                    _             => false,
                };
                if (matched)
                {
                    _stepMode = StepMode.None;
                    var pseudoBp = new Breakpoint(default, Guid.Empty, Guid.Empty, nodeId, 0, true);
                    HandleBreakpointHit(self, pseudoBp, nodeId);
                }
            }
        }
    }

    public void OnPinValueChanged<T>(Entity self, string pinId, T value)
        where T : unmanaged
    {
        if (_entityFilter.HasValue && self != _entityFilter.Value) return;

        if (!_watchesByPinString.TryGetValue(pinId, out var watch))
            return;

        watch.WriteValue(value, self, _view.Tick);

        var evt = _onPinValueChangedEvent;
        if (evt != null)
        {
            evt.Invoke(new PinValueChanged(
                self,
                pinId,
                watch.LastValueBytes.ToArray(),
                watch.ExpectedType,
                _view.Tick));
        }
    }

    // BPF-004: peerAssetIdString is a Guid in "D" format.
    public void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName)
    {
        int prevDepth = _currentCallDepth.GetValueOrDefault(self, 0);
        _currentCallDepth[self] = prevDepth + 1;
        if (prevDepth == 0)
        {
            var assetId = Guid.TryParse(peerAssetIdString, out var parsed) ? parsed : Guid.Empty;
            if (!_activeEntities.TryGetValue(assetId, out var set))
                _activeEntities[assetId] = set = new HashSet<Entity>();
            set.Add(self);
        }

        if (!_callStacks.TryGetValue(self, out var stack))
            _callStacks[self] = stack = new List<CallFrame>();
        stack.Add(new CallFrame(peerAssetIdString, methodName, prevDepth));
    }

    // BPF-004: peerAssetIdString is a Guid in "D" format.
    public void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName)
    {
        int current = _currentCallDepth.GetValueOrDefault(self, 0);
        int next    = Math.Max(0, current - 1);
        _currentCallDepth[self] = next;
        if (next == 0)
        {
            foreach (var set in _activeEntities.Values)
                set.Remove(self);
        }

        if (_callStacks.TryGetValue(self, out var stack) && stack.Count > 0)
            stack.RemoveAt(stack.Count - 1);
    }

    // ---- IBlueprintDebugSession -- lifecycle --------------------------------

    private bool _isAttached;

    public bool IsAttached => _isAttached;

    public void Attach()
    {
        _isAttached    = true;
        DebugProbe.Sink = this;
    }

    public void Detach()
    {
        _isAttached = false;
        if (_isPaused) Continue();
        DebugProbe.Sink = NullProbeSink.Instance;
        _breakpoints.Clear();
        _bpByNodeString.Clear();
        _firedBreakpointsThisTick.Clear();
        _watches.Clear();
        _watchesByPinString.Clear();
        _activeEntities.Clear();
        _history.Clear();
        _currentCallDepth.Clear();
        _callStacks.Clear();
        _debugMaps.Clear();
        _pdbLocators.Clear();
        OnSessionStateChanged?.Invoke();
    }

    // ---- IBlueprintDebugSession -- breakpoints ------------------------------

    public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)
    {
        var nodeIdStr = nodeId.ToString("D");
        var id        = new BreakpointId(_nextBpId++);

        ulong hash = 0;
        if (_debugMaps.TryGetValue(assetId, out var mapIdx))
            hash = mapIdx.StructureHash;

        var bp = new Breakpoint(id, assetId, graphId, nodeIdStr, 0, true)
        {
            AssetStructureHashAtSetTime = hash,
        };
        _breakpoints[id]           = bp;
        _bpByNodeString[nodeIdStr] = bp;

        // TEMP DIAGNOSTIC: resolve which node this actually targets + list probe-eligible nodes.
        if (_debugMaps.TryGetValue(assetId, out var diagMap))
        {
            var hit = diagMap.TryResolveNode(nodeIdStr);
            DiagLog($"SetBreakpoint nodeIdStr='{nodeIdStr}' resolved={(hit == null ? "<NOT-IN-MAP / not probe-eligible>" : $"'{hit.DisplayName}' kind={hit.NodeKind}")} bpCount={_breakpoints.Count + 1} mgr={_dataBreakpointManager != null}");
            DiagLog($"  probe-eligible nodes in asset: {string.Join(", ", diagMap.AllNodes.Select(n => $"{n.NodeIdString}='{n.DisplayName}'"))}");
        }
        else
        {
            DiagLog($"SetBreakpoint nodeIdStr='{nodeIdStr}' assetId={assetId} <NO DEBUG MAP REGISTERED> bpCount={_breakpoints.Count + 1} mgr={_dataBreakpointManager != null}");
        }

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
            _firedBreakpointsThisTick.Remove(id);

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
        _firedBreakpointsThisTick.Clear();
    }

    public IReadOnlyList<Breakpoint> GetBreakpoints()
        => _breakpoints.Values.ToList().AsReadOnly();

    public bool IsAnyBreakpointActive => _breakpoints.Count > 0;

    // ---- IBlueprintDebugSession -- watches ----------------------------------

    public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType)
    {
        var id    = new WatchId(_nextWatchId++);
        var watch = new Watch(id, assetId, graphId, pinId, displayName, expectedType);
        _watches[id]                           = watch;
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

    public void SetDataBreakpointManager(Hrot.Diagnostics.Breakpoints.IDataBreakpointManager? manager)
    {
        if (_dataBreakpointManager != null)
        {
            foreach (var mgrId in _mgrBpIds.Values)
                _dataBreakpointManager.Remove(mgrId);
            _mgrBpIds.Clear();
        }

        _dataBreakpointManager = manager;

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
        // Clear dedup set so the next hit after Continue() is treated as a fresh event.
        _firedBreakpointsThisTick.Clear();
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
        var fromEntity  = _pausedOnEntity ?? default;
        _stepMode       = StepMode.Over;
        _stepFromEntity = fromEntity;
        _stepFromDepth  = _currentCallDepth.GetValueOrDefault(fromEntity, 0);
        _stepFromTick   = _view.Tick;
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _timeController.RequestStepOneTick();
        OnSessionStateChanged?.Invoke();
    }

    public void StepInto()
    {
        var fromEntity  = _pausedOnEntity ?? default;
        _stepMode       = StepMode.Into;
        _stepFromEntity = fromEntity;
        _stepFromDepth  = _currentCallDepth.GetValueOrDefault(fromEntity, 0);
        _stepFromTick   = _view.Tick;
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _timeController.RequestStepOneTick();
        OnSessionStateChanged?.Invoke();
    }

    // BPF-005: StepOut tracks _stepFromTick so depth-0 step can re-pause at next tick boundary.
    public void StepOut()
    {
        var fromEntity  = _pausedOnEntity ?? default;
        _stepMode       = StepMode.Out;
        _stepFromEntity = fromEntity;
        _stepFromDepth  = _currentCallDepth.GetValueOrDefault(fromEntity, 0);
        _stepFromTick   = _view.Tick;
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _timeController.RequestStepOneTick();
        OnSessionStateChanged?.Invoke();
    }

    // ---- IBlueprintDebugSession -- inspection -------------------------------

    // BPF-001: Implement fully populated state snapshot.
    public BlueprintStateSnapshot? GetCurrentStateSnapshot()
    {
        if (!_isPaused || !_pausedOnEntity.HasValue || _pausedAt is null) return null;
        return CaptureStateSnapshot(_pausedOnEntity.Value, _pausedAt.AssetId);
    }

    /// <summary>
    /// Returns a live (non-pause-gated) snapshot of the working-state for the given entity
    /// and blueprint asset. Calls <see cref="CaptureStateSnapshot"/> directly without
    /// requiring the session to be paused.
    /// </summary>
    public BlueprintStateSnapshot? CaptureLiveState(Entity self, Guid assetId)
        => CaptureStateSnapshot(self, assetId);

    private BlueprintStateSnapshot? CaptureStateSnapshot(Entity self, Guid assetId)
    {
        _debugMaps.TryGetValue(assetId, out var mapIndex);
        var assetName = mapIndex?.AssetName ?? assetId.ToString("D");

        var bpId = BlueprintIdHash.Compute(assetId);
        _registry.TryGetById(bpId, out var def);

        if (def != null)
            assetName = def.Name;

        var dispatch = def?.Kind ?? BlueprintDispatchKind.Library;
        var fields   = new Dictionary<string, object>();
        BlueprintLatentCursor? cursor = null;

        if (def != null)
        {
            switch (def.Kind)
            {
                case BlueprintDispatchKind.AiPrimitive:
                    CaptureAiPrimitiveState(self, def, mapIndex, fields);
                    break;
                case BlueprintDispatchKind.Instance:
                    CaptureInstanceStateFromDefinition(self, bpId, mapIndex, def, fields, out cursor);
                    break;
            }
        }

        return new BlueprintStateSnapshot(
            Self:        self,
            AssetId:     assetId,
            AssetName:   assetName,
            Dispatch:    dispatch,
            FieldValues: fields,
            Cursor:      cursor);
    }

    // Reads AiPrimitive working-state fields from Blackboard1024 (BPF-001 section 8.6).
    private void CaptureAiPrimitiveState(
        Entity self, BlueprintDefinition def, DebugMapIndex? mapIndex,
        Dictionary<string, object> outFields)
    {
        if (!_view.HasComponent<Blackboard1024>(self)) return;
        ref readonly var bb = ref _view.GetComponentRO<Blackboard1024>(self);

        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
            System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in bb, 1));

        if (bytes.Length < 8) return;

        ulong storedHash = System.Runtime.InteropServices.MemoryMarshal.Read<ulong>(bytes);
        if (storedHash != def.StructureHash) return;

        var layoutFields = mapIndex?.StateLayout.Fields;
        if (layoutFields != null && layoutFields.Count > 0)
        {
            foreach (var field in layoutFields)
            {
                int start = 8 + field.OffsetBytes;
                if (start + field.SizeBytes > bytes.Length) continue;
                var fieldType = ResolveType(field.Type);
                if (fieldType is null) continue;
                var raw = MarshalFromBytes(bytes.Slice(start, field.SizeBytes).ToArray(), fieldType);
                if (raw != null) outFields[field.Name] = raw;
            }
        }
        else
        {
            foreach (var (name, descriptor) in def.StateFields)
            {
                int start = 8 + descriptor.OffsetBytes;
                if (start + descriptor.SizeBytes > bytes.Length) continue;
                var raw = MarshalFromBytes(bytes.Slice(start, descriptor.SizeBytes).ToArray(), descriptor.ClrType);
                if (raw != null) outFields[name] = raw;
            }
        }
    }

    // Instance state byte access requires the partition allocator, not wired in here.
    private unsafe void CaptureInstanceStateFromDefinition(
        Entity self, int blueprintId, DebugMapIndex? mapIndex, BlueprintDefinition def,
        Dictionary<string, object> outFields, out BlueprintLatentCursor? cursor)
    {
        cursor = null;

        if (_view.HasComponent<BlueprintBlackboard1024>(self))
        {
            ref readonly var bb = ref _view.GetComponentRO<BlueprintBlackboard1024>(self);
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in bb, 1));
            ReadInstanceState(bytes, blueprintId, mapIndex?.StateLayout, def, outFields, out cursor);
        }
        else if (_view.HasComponent<BlueprintBlackboard4096>(self))
        {
            ref readonly var bb = ref _view.GetComponentRO<BlueprintBlackboard4096>(self);
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in bb, 1));
            ReadInstanceState(bytes, blueprintId, mapIndex?.StateLayout, def, outFields, out cursor);
        }
        else if (_view.HasComponent<BlueprintBlackboard16384>(self))
        {
            ref readonly var bb = ref _view.GetComponentRO<BlueprintBlackboard16384>(self);
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in bb, 1));
            ReadInstanceState(bytes, blueprintId, mapIndex?.StateLayout, def, outFields, out cursor);
        }
    }

    internal static unsafe void ReadInstanceState(
        ReadOnlySpan<byte> bytes, int blueprintId, DebugStateLayout? stateLayout,
        BlueprintDefinition? def,
        Dictionary<string, object> outFields, out BlueprintLatentCursor? cursor)
    {
        cursor = null;
        fixed (byte* memory = bytes)
        {
            if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out int payloadOffset))
                return;

            if (payloadOffset + 16 > bytes.Length) return;
            cursor = System.Runtime.InteropServices.MemoryMarshal.Read<BlueprintLatentCursor>(
                bytes.Slice(payloadOffset, 16));

            // Prefer DebugMap StateLayout when available (has editor-authored offsets).
            if (stateLayout != null && stateLayout.Fields.Count > 0)
            {
                foreach (var field in stateLayout.Fields)
                {
                    int fieldStart = payloadOffset + field.OffsetBytes;
                    if (fieldStart + field.SizeBytes > bytes.Length || field.SizeBytes <= 0) continue;
                    var fieldType = ResolveType(field.Type);
                    if (fieldType == null) continue;
                    var raw = MarshalFromBytes(bytes.Slice(fieldStart, field.SizeBytes).ToArray(), fieldType);
                    if (raw != null) outFields[field.Name] = raw;
                }
            }
            // Fallback: use BlueprintDefinition.StateFields (compiled offset/size from registrar).
            else if (def?.StateFields is { Count: > 0 } stateFields)
            {
                foreach (var (name, descriptor) in stateFields)
                {
                    int fieldStart = payloadOffset + descriptor.OffsetBytes;
                    if (fieldStart + descriptor.SizeBytes > bytes.Length || descriptor.SizeBytes <= 0) continue;
                    var raw = MarshalFromBytes(bytes.Slice(fieldStart, descriptor.SizeBytes).ToArray(), descriptor.ClrType);
                    if (raw != null) outFields[name] = raw;
                }
            }
        }
    }

    public IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100)
    {
        var all = new List<NodeExecuted>();
        foreach (var (entity, hist) in _history)
        {
            foreach (var entry in hist.GetRecent(maxCount))
                all.Add(new NodeExecuted(entity, Guid.Empty, Guid.Empty, entry.NodeId, entry.SimTime, entry.Tick));
        }
        if (all.Count > maxCount)
            all.Sort((a, b) => b.Tick.CompareTo(a.Tick));
        return all.Count <= maxCount ? all.AsReadOnly() : all.Take(maxCount).ToList().AsReadOnly();
    }

    public IReadOnlyList<CallFrame> GetCurrentCallStack()
    {
        // Return the call stack for the currently paused entity (Editor DD §8.7).
        if (_pausedOnEntity.HasValue &&
            _callStacks.TryGetValue(_pausedOnEntity.Value, out var stack) &&
            stack.Count > 0)
        {
            return stack.AsReadOnly();
        }
        return Array.Empty<CallFrame>();
    }

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
            // BPF-003: mark breakpoints stale instead of clearing them.
            foreach (var bp in _breakpoints.Values.Where(b => b.AssetId == map.AssetId).ToList())
            {
                var stale = bp with { IsStale = true };
                _breakpoints[bp.Id]        = stale;
                _bpByNodeString[bp.NodeId] = stale;
            }
            OnBreakpointListChanged?.Invoke(map.AssetId);
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
        foreach (var watch in _watches.Values)
            watch.IsStale = true;
        OnSessionStateChanged?.Invoke();
    }

    public void OnHotReloadCompleted(Guid[] reloadedAssetIds)
    {
        foreach (var assetId in reloadedAssetIds)
        {
            // BPF-036: only clear stale for watches whose pin still exists in the new debug map.
            // Watches for pins that were deleted in the new version remain stale so the UI
            // shows them as frozen rather than falsely "live".
            _debugMaps.TryGetValue(assetId, out var newMapIndex);

            foreach (var watch in _watches.Values.Where(w => w.AssetId == assetId))
            {
                if (newMapIndex != null && newMapIndex.TryGetPinById(watch.PinId) != null)
                    watch.IsStale = false;
                // else: pin no longer in map -> leave IsStale = true
            }
            OnBreakpointListChanged?.Invoke(assetId);
        }
        OnSessionStateChanged?.Invoke();
    }

    // BPF-003: reset per-frame dedup set at tick boundary.
    public void OnNewTick() => _firedBreakpointsThisTick.Clear();

    // ---- Private helpers ----------------------------------------------------

    // BPF-003: increments HitCount without triggering a new pause (same-tick dedup path).
    private void IncrementHitCountOnly(Breakpoint bp)
    {
        if (bp.Id.Value == 0 || !_breakpoints.ContainsKey(bp.Id)) return;
        var updated = bp with { HitCount = bp.HitCount + 1 };
        _breakpoints[bp.Id]        = updated;
        _bpByNodeString[bp.NodeId] = updated;
    }

    private void HandleBreakpointHit(Entity self, Breakpoint bp, string nodeId)
    {
        DiagLog($"BREAKPOINT HIT! nodeId='{nodeId}' entity={self.Index}");

        _isPaused        = true;
        _pausedAt        = bp;
        _pausedOnEntity  = self;
        _stepMode        = StepMode.None;
        _stepFromEntity  = default;
        _stepFromDepth   = 0;

        if (bp.Id.Value != 0 && _breakpoints.ContainsKey(bp.Id))
        {
            var updated = bp with { HitCount = bp.HitCount + 1 };
            _breakpoints[bp.Id]        = updated;
            _bpByNodeString[bp.NodeId] = updated;
            _pausedAt                  = updated;

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

    private string? ResolveSourceFilePath(Guid assetId, string nodeId)
    {
        if (!_pdbLocators.TryGetValue(assetId, out var locator)) return null;
        if (!_debugMaps.TryGetValue(assetId, out var index)) return null;
        var entry = index.TryResolveNode(nodeId);
        if (entry == null || entry.SourceStartLine == 0) return null;
        return locator();
    }

    private int? ResolveSourceLine(Guid assetId, string nodeId)
    {
        if (!_debugMaps.TryGetValue(assetId, out var index)) return null;
        var entry = index.TryResolveNode(nodeId);
        if (entry == null || entry.SourceStartLine == 0) return null;
        return entry.SourceStartLine;
    }

    private static Type? ResolveType(string typeFullName) => typeFullName switch
    {
        "System.Int32"   => typeof(int),
        "System.Single"  => typeof(float),
        "System.Boolean" => typeof(bool),
        "System.UInt32"  => typeof(uint),
        "System.Int64"   => typeof(long),
        "System.Double"  => typeof(double),
        "System.UInt64"  => typeof(ulong),
        "System.Int16"   => typeof(short),
        "System.Byte"    => typeof(byte),
        _                => Type.GetType(typeFullName),
    };

    public static object? MarshalFromBytes(byte[] bytes, Type type)
    {
        if (bytes == null || bytes.Length == 0) return null;
        if (type == typeof(int))    return System.Runtime.InteropServices.MemoryMarshal.Read<int>(bytes);
        if (type == typeof(float))  return System.Runtime.InteropServices.MemoryMarshal.Read<float>(bytes);
        if (type == typeof(bool))   return bytes[0] != 0;
        if (type == typeof(uint))   return System.Runtime.InteropServices.MemoryMarshal.Read<uint>(bytes);
        if (type == typeof(long))   return System.Runtime.InteropServices.MemoryMarshal.Read<long>(bytes);
        if (type == typeof(double)) return System.Runtime.InteropServices.MemoryMarshal.Read<double>(bytes);
        return bytes;
    }
}