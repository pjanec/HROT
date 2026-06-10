using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.ReplayBrowser.Search;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Editor.Debug;
using BPCompilerMode = Hrot.Blueprints.Core.Compiler.CompilerMode;
using Graph = Hrot.Blueprints.Core.Assets.Graph;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// A step target: an authored node to set a temporary breakpoint on.
/// Used by CF-6 stepping to compute successors and set one-shot invisible breakpoints.
/// </summary>
public readonly record struct BreakpointTarget(Guid AssetId, Guid GraphId, Guid NodeId);

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

    // Breakpoint storage: indexed by BreakpointId for management, by probe-id string for fast probe lookup.
    // _bpByNodeString uses List<Breakpoint> because several exec nodes can share one probe id
    // (many-to-one mapping via BreakpointTargets).
    private readonly Dictionary<BreakpointId, Breakpoint>     _breakpoints    = new();
    private readonly Dictionary<string, List<Breakpoint>>     _bpByNodeString = new(StringComparer.Ordinal);
    private int _nextBpId = 1;

    // Per-frame dedup set: cleared by OnNewTick. Prevents double-pause for multiple entities
    // hitting the same breakpoint in the same tick (BPF-003 section 9.2).
    private readonly HashSet<BreakpointId> _firedBreakpointsThisTick = new();

    // Watch storage: indexed by WatchId for management, by pin-id string for fast probe lookup.
    private readonly Dictionary<WatchId, Watch>   _watches            = new();
    private readonly Dictionary<string, Watch>    _watchesByPinString = new(StringComparer.Ordinal);
    private int _nextWatchId = 1;

    // Graph structure for stepping: graphId → Graph (CF-6).
    // Registered when a blueprint document is opened; used by ExecSuccessors
    // to compute next exec node(s) during stepping.
    private readonly Dictionary<Guid, Graph> _graphs = new();

    // Temporary breakpoints for stepping (CF-6). Keyed by probe-id string.
    // Cleared on hit or on Continue(). Not exposed via GetBreakpoints().
    private readonly Dictionary<string, List<Breakpoint>> _tempBreakpoints = new(StringComparer.Ordinal);

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

    // Step state (legacy — CF-6 replaces _stepMode with temporary breakpoints;
    // retained for backward compatibility).
    private StepMode _stepMode      = StepMode.None;
    private Entity   _stepFromEntity;
    private int      _stepFromDepth;
    private uint     _stepFromTick;  // used by StepOut at depth 0 (BPF-005); legacy

    // Entity filter: when set, only events from this entity are processed.
    private Entity? _entityFilter;

    // Optional data-breakpoint manager.
    private Hrot.Diagnostics.Breakpoints.IDataBreakpointManager? _dataBreakpointManager;

    // Tracks the manager-side BreakpointId for each session-side BreakpointId.
    private readonly Dictionary<BreakpointId, Hrot.Diagnostics.Breakpoints.BreakpointId> _mgrBpIds = new();

    // Tracks DBM-side BreakpointIds for temporary breakpoints (CF-6).
    // Temps are forwarded to DBM so the pause mechanism (repo rewind + _isPaused flag)
    // works correctly. Removed when temps are cleared.
    private readonly List<Hrot.Diagnostics.Breakpoints.BreakpointId> _tempMgrBpIds = new();

    // Sub-tick snapshot recorder (NGS-2.0).
    // Records per-node ECS deltas during a debug-active tick for sub-tick state inspection.
    // Null-safe: when _liveRepo is null (not wired), recording is silently disabled.
    private readonly SubTickSnapshotRecorder _recorder = new SubTickSnapshotRecorder();
    private EntityRepository? _liveRepo;

    // Tick-boundary detection: we detect a new tick by observing _view.Tick change in OnNewTick().
    // Stored as uint? so the first tick always triggers BeginTick.
    private uint? _lastRecordedTick;

    // Instrumentation callback: invoked when a breakpoint or watch is set on an asset
    // with no DebugMap registered yet, so the editor can transparently compile in the
    // required mode (Debug / Trace) without the user clicking Compile manually.
    private Func<Guid, BPCompilerMode, Task>? _onInstrumentationRequested;

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

        // Don't fire overlay updates after we've already paused (e.g. temp BP hit
        // earlier this tick).  Without this the overlay shows the last-executed node
        // (which may be after the probe that triggered the pause), not the
        // pause-triggering node itself.
        if (!_isPaused)
            _onNodeExecuted?.Invoke(new NodeExecuted(self, Guid.Empty, Guid.Empty, nodeId, _view.Time, _view.Tick));

        // NGS-2.0: record per-node ECS snapshot delta when armed (recording active).
        // Called AFTER history and overlay so existing CF-6 behavior is undisturbed.
        // Guard: liveRepo must be wired and a tick recording must be in progress
        // (_lastRecordedTick set by OnNewTick). If OnNewTick hasn't fired yet (e.g. an
        // older construction path skips it), log once and skip silently.
        if (RecordingActive)
        {
            if (_lastRecordedTick.HasValue)
            {
                _recorder.RecordNodeEntry(_liveRepo!, nodeId);
            }
            else
            {
                // Breakpoint is armed but BeginTick was never called — live repo wired
                // late or OnNewTick not in the caller's frame loop.
                System.Diagnostics.Debug.WriteLine(
                    "[BlueprintDebugSession] RecordingActive but BeginTick not called yet. " +
                    "Ensure SetLiveRepository() and OnNewTick() are called before the tick executes.");
            }
        }

        // CF-6: Check temporary breakpoints FIRST. When stepping, suppress user breakpoints.
        if (_tempBreakpoints.Count > 0)
        {
            if (_tempBreakpoints.TryGetValue(nodeId, out var tempList) && !_isPaused)
            {
                var tempBp = tempList[0]; // first matching temp
                ClearTemporaryBreakpoints(); // auto-clear ALL temps on first hit
                HandleBreakpointHit(self, tempBp, nodeId);
            }
            // When temps are active, skip user breakpoint matching entirely (suppression).
            // Also skip the legacy step-mode check below — temp BPs handle stepping now.
            return;
        }

        if (_bpByNodeString.TryGetValue(nodeId, out var bpList))
        {
            // Snapshot to avoid "collection modified" during enumeration
            // (HandleBreakpointHit / IncrementHitCountOnly modify the list via ReplaceInBpList).
            var snapshot = bpList.ToArray();
            foreach (var bp in snapshot)
            {
                if (!bp.Enabled || bp.IsStale) continue;

                bool hashOk = true;
                if (bp.AssetStructureHashAtSetTime != 0 &&
                    _debugMaps.TryGetValue(bp.AssetId, out var mapIdx) &&
                    mapIdx.StructureHash != bp.AssetStructureHashAtSetTime)
                {
                    hashOk = false;
                    var stale = bp with { IsStale = true };
                    _breakpoints[bp.Id] = stale;
                    ReplaceInBpList(bp.ProbeNodeId, bp, stale);
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

        // Legacy step-mode matching: retained for backward compatibility but dead code
        // now that Step methods use temporary breakpoints (CF-6). The StepOver/Into/Out
        // methods no longer set _stepMode; they compute successors and set temp BPs instead.
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
        _graphs.Clear();
        ClearTemporaryBreakpoints(); // also clears _tempMgrBpIds via DBM Remove
        OnSessionStateChanged?.Invoke();
    }

    // ---- Graph registration for stepping (CF-6) ----------------------------

    /// <summary>
    /// Registers a <see cref="Graph"/> structure for stepping.
    /// Call when a blueprint document is opened so that <see cref="ExecSuccessors"/>
    /// can compute next exec node(s) during Step operations.
    /// </summary>
    public void RegisterGraph(Graph graph)
    {
        _graphs[graph.Id] = graph;
    }

    // ---- IBlueprintDebugSession -- breakpoints ------------------------------

    // Interface method (no enabled parameter — always enabled).
    public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)
        => SetBreakpoint(assetId, graphId, nodeId, enabled: true);

    /// <summary>
    /// Overload with explicit <paramref name="enabled"/> and <paramref name="triggerInstrumentation"/> flags.
    /// Used by session restore (CF-8) to restore disabled breakpoints and defer instrumentation
    /// to after editor initialization (when QuickReload infrastructure is fully ready).
    /// When <c>enabled</c> is <c>false</c>, the breakpoint is NOT forwarded to the
    /// <see cref="DataBreakpointManager"/> and cannot trigger a pause until enabled.
    /// When <c>triggerInstrumentation</c> is <c>false</c>, the CF-7-rev callback is NOT invoked;
    /// use <see cref="RequestInstrumentationForPendingAssets"/> later to trigger it.
    /// </summary>
    public BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId, bool enabled,
        bool triggerInstrumentation = true)
    {
        // Auto-instrumentation: if no DebugMap yet for this asset, request a Debug compile.
        // Deferred when triggerInstrumentation is false (restore during editor init).
        if (triggerInstrumentation && !_debugMaps.ContainsKey(assetId) && _onInstrumentationRequested != null)
        {
            _ = _onInstrumentationRequested.Invoke(assetId, BPCompilerMode.Debug);
        }

        var nodeIdStr  = nodeId.ToString("D");
        var id         = new BreakpointId(_nextBpId++);

        // Resolve clicked nodeId → block probe id via BreakpointTargets.
        string probeIdStr;
        ulong hash = 0;
        if (_debugMaps.TryGetValue(assetId, out var mapIdx))
        {
            hash = mapIdx.StructureHash;
            probeIdStr = mapIdx.BreakpointTargets.TryGetValue(nodeId, out var blockProbeId)
                ? blockProbeId.ToString("D")
                : nodeIdStr; // not in targets → fall back to clicked id
        }
        else
        {
            probeIdStr = nodeIdStr; // no map yet — tentative, key by clicked id
        }

        var bp = new Breakpoint(id, assetId, graphId, nodeIdStr, 0, enabled)
        {
            AssetStructureHashAtSetTime = hash,
            ProbeNodeId = probeIdStr,
        };
        _breakpoints[id] = bp;

        if (!_bpByNodeString.TryGetValue(probeIdStr, out var list))
            _bpByNodeString[probeIdStr] = list = new List<Breakpoint>();
        list.Add(bp);

        // Only forward enabled breakpoints to the DataBreakpointManager.
        // Disabled breakpoints should not trigger a pause — they just show the marker.
        if (enabled && _dataBreakpointManager != null)
        {
            var mgrId = _dataBreakpointManager.AddBreakpoint(
                new Fdp.Toolkit.ReplayBrowser.Search.ExternalHitTagPredicateDto { Tag = probeIdStr },
                displayName: $"Blueprint node {nodeIdStr}",
                sourceElementId: nodeId);  // authored node GUID — needed for CF-8 persistence
            _mgrBpIds[id] = mgrId;
        }

        return id;
    }

    public void ClearBreakpoint(BreakpointId id)
    {
        if (_breakpoints.TryGetValue(id, out var bp))
        {
            _breakpoints.Remove(id);
            _firedBreakpointsThisTick.Remove(id);

            var probeId = string.IsNullOrEmpty(bp.ProbeNodeId) ? bp.NodeId : bp.ProbeNodeId;
            if (_bpByNodeString.TryGetValue(probeId, out var list))
            {
                list.Remove(bp);
                if (list.Count == 0)
                    _bpByNodeString.Remove(probeId);
            }

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

    // ---- Temporary breakpoints for stepping (CF-6) -------------------------

    /// <summary>
    /// Sets one-shot temporary breakpoints for stepping. These are invisible
    /// (not in GetBreakpoints()), not forwarded to DBM, and auto-cleared on first hit.
    /// Suppresses user breakpoints while temps are active.
    /// </summary>
    public void SetTemporaryBreakpoints(IEnumerable<BreakpointTarget> targets)
    {
        ClearTemporaryBreakpoints();
        foreach (var t in targets)
        {
            // Translate authored node id → block-probe id via BreakpointTargets.
            string probeId = ResolveProbeId(t.AssetId, t.NodeId);
            var bp = new Breakpoint(default, t.AssetId, t.GraphId, t.NodeId.ToString("D"), 0, true)
            {
                ProbeNodeId = probeId,
            };
            if (!_tempBreakpoints.TryGetValue(probeId, out var list))
                _tempBreakpoints[probeId] = list = new List<Breakpoint>();
            list.Add(bp);

            // Forward to DBM so the pause mechanism works correctly (repo rewind +
            // _isPaused flag in OnHit).  Without this the latency system schedules
            // the next tick and the sim never stays paused.
            if (_dataBreakpointManager != null)
            {
                var mgrId = _dataBreakpointManager.AddBreakpoint(
                    new Fdp.Toolkit.ReplayBrowser.Search.ExternalHitTagPredicateDto { Tag = probeId },
                    displayName: "Step temp");
                _tempMgrBpIds.Add(mgrId);
            }
        }
    }

    private string ResolveProbeId(Guid assetId, Guid authoredNodeId)
    {
        if (_debugMaps.TryGetValue(assetId, out var idx) &&
            idx.BreakpointTargets.TryGetValue(authoredNodeId, out var blockProbeId))
            return blockProbeId.ToString("D");
        return authoredNodeId.ToString("D"); // fallback
    }

    private void ClearTemporaryBreakpoints()
    {
        if (_dataBreakpointManager != null)
        {
            foreach (var mgrId in _tempMgrBpIds)
                _dataBreakpointManager.Remove(mgrId);
            _tempMgrBpIds.Clear();
        }
        _tempBreakpoints.Clear();
    }

    public bool HasTemporaryBreakpoints => _tempBreakpoints.Count > 0;

    // ---- IBlueprintDebugSession -- watches ----------------------------------

    public WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType)
    {
        // Auto-instrumentation: if no DebugMap yet for this asset, request a Trace compile.
        // Trace mode includes watch probes + breakpoints. If the asset is already Debug-compiled
        // the instrumentation service handles upgrading without recompiling if possible.
        if (!_debugMaps.ContainsKey(assetId) && _onInstrumentationRequested != null)
        {
            _ = _onInstrumentationRequested.Invoke(assetId, BPCompilerMode.Trace);
        }

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
                var probeId = string.IsNullOrEmpty(bp.ProbeNodeId) ? bp.NodeId : bp.ProbeNodeId;
                var mgrId = _dataBreakpointManager.AddBreakpoint(
                    new Fdp.Toolkit.ReplayBrowser.Search.ExternalHitTagPredicateDto { Tag = probeId },
                    displayName: $"Blueprint node {bp.NodeId}");
                _mgrBpIds[bp.Id] = mgrId;
            }
        }
    }

    /// <summary>
    /// Wires the concrete live <see cref="EntityRepository"/> for sub-tick snapshot recording (NGS-2.0).
    /// Must be called from the same site as <see cref="SetDataBreakpointManager"/>.
    /// When not called, recording is silently disabled (safe default; logs once if a breakpoint
    /// is armed but the repo is missing, so the gap is visible without crashing).
    /// </summary>
    public void SetLiveRepository(EntityRepository? repo)
    {
        _liveRepo           = repo;
        _lastRecordedTick   = null; // force BeginTick on next armed tick
    }

    /// <summary>
    /// Number of per-node recordings captured during the most recent debug-active tick.
    /// Zero when recording is off (unarmed, or <see cref="SetLiveRepository"/> not called).
    /// </summary>
    public int RecordedNodeCount => _recorder.Count;

    /// <summary>
    /// Returns the node-id string for the recorded entry at logical <paramref name="index"/>
    /// (0 = first node entered during the recorded tick).
    /// </summary>
    public string RecordedNodeIdAt(int index) => _recorder.NodeIdAt(index);

    /// <summary>
    /// Reconstructs the whole-repo ECS state as-of entering the node at <paramref name="nodeIndex"/>
    /// into <paramref name="scratchRepo"/> by replaying the keyframe baseline and sequential deltas.
    /// The caller owns <paramref name="scratchRepo"/> and must have registered the same component
    /// types as the live repo.
    /// </summary>
    public void RestoreRecordedNode(int nodeIndex, EntityRepository scratchRepo)
        => _recorder.RestoreTo(nodeIndex, scratchRepo);

    /// <summary>
    /// <see cref="RecordingActive"/> is true when recording should happen for the current entity/tick:
    /// <list type="bullet">
    ///   <item>A live <see cref="EntityRepository"/> has been wired via <see cref="SetLiveRepository"/>.</item>
    ///   <item>At least one enabled user breakpoint OR temp breakpoint is active.</item>
    /// </list>
    /// When false, ZERO recorder work is done — normal runtime overhead is unchanged.
    /// </summary>
    private bool RecordingActive =>
        _liveRepo != null &&
        (_breakpoints.Count > 0 || _tempBreakpoints.Count > 0);

    /// <summary>
    /// Sets the instrumentation callback that is invoked when a breakpoint or watch is set
    /// on an asset that has no <see cref="DebugMap"/> registered yet. The callback receives
    /// the asset id and the required <see cref="CompilerMode"/>.
    /// This is an implementation detail of <see cref="BlueprintDebugSession"/> — it is NOT
    /// on <see cref="IBlueprintDebugSession"/>. Test code can set it via a cast.
    /// </summary>
    public void SetInstrumentationCallback(Func<Guid, BPCompilerMode, Task>? callback)
    {
        _onInstrumentationRequested = callback;
    }

    // ---- CF-8: Session persistence restore ----------------------------------

    /// <summary>
    /// Restores node breakpoints from a persisted session file.
    /// Instrumentation is deferred — breakpoints are stored tentatively.
    /// Call <see cref="RequestInstrumentationForPendingAssets"/> after the editor
    /// is fully initialized to trigger QuickReload for each affected asset.
    /// </summary>
    public void RestoreNodeBreakpoints(IReadOnlyList<Hrot.Diagnostics.Breakpoints.NodeBreakpointEntry> entries)
    {
        foreach (var e in entries)
        {
            // triggerInstrumentation: false — defer to after editor init
            SetBreakpoint(e.AssetId, e.GraphId, e.NodeId, enabled: e.Enabled,
                triggerInstrumentation: false);
        }
    }

    /// <summary>
    /// Restores watches from a persisted session file.
    /// Instrumentation is deferred — call <see cref="RequestInstrumentationForPendingAssets"/>
    /// after editor init.
    /// </summary>
    public void RestoreWatches(IReadOnlyList<Hrot.Diagnostics.Breakpoints.WatchEntry> entries)
    {
        foreach (var e in entries)
        {
            var expectedType = Type.GetType(e.ExpectedTypeName);
            if (expectedType == null)
                continue; // type not available → skip
            AddWatch(e.AssetId, e.GraphId, e.PinId, e.DisplayName, expectedType);
        }
    }

    /// <summary>
    /// Triggers instrumentation (CF-7-rev) for all assets that have breakpoints
    /// or watches but no registered <see cref="DebugMap"/>. Call after editor
    /// initialization is complete and the QuickReload infrastructure is ready.
    /// </summary>
    public void RequestInstrumentationForPendingAssets()
    {
        if (_onInstrumentationRequested == null) return;

        var pendingAssetIds = new HashSet<Guid>();

        // Collect assets from breakpoints without DebugMap.
        foreach (var bp in _breakpoints.Values)
        {
            if (!_debugMaps.ContainsKey(bp.AssetId))
                pendingAssetIds.Add(bp.AssetId);
        }

        // Collect assets from watches without DebugMap.
        foreach (var w in _watches.Values)
        {
            if (!_debugMaps.ContainsKey(w.AssetId))
                pendingAssetIds.Add(w.AssetId);
        }

        foreach (var assetId in pendingAssetIds)
        {
            // Trace if any watch exists for this asset; otherwise Debug.
            var mode = _watches.Values.Any(w => w.AssetId == assetId)
                ? BPCompilerMode.Trace
                : BPCompilerMode.Debug;
            _ = _onInstrumentationRequested.Invoke(assetId, mode);
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
        ClearTemporaryBreakpoints(); // discard any leftover temps (CF-6)
        _timeController.RequestResume();
        OnSessionStateChanged?.Invoke();
    }

    public void Pause()
    {
        _timeController.RequestPause();
        _isPaused = true;
        OnSessionStateChanged?.Invoke();
    }

    /// <summary>
    /// Slice-1 stepping: all three step commands (Over/Into/Out) converge to
    /// "step to next exec node."  Computes successors of the currently-paused node,
    /// sets temporary breakpoints on them, suppresses user breakpoints, and resumes.
    /// On hitting a temp target, pauses and auto-clears temps.
    /// Cross-peer-call stepping is deferred (Slice-2).
    ///
    /// Falls back to legacy <see cref="_stepMode"/> tick-matching when no graph
    /// is registered for the paused asset (backward compatible with existing
    /// step-mode tests and pre-CF-6 behavior).
    /// </summary>
    public void StepOver() => Step(StepMode.Over);
    public void StepInto() => Step(StepMode.Into);
    public void StepOut()  => Step(StepMode.Out);

    /// <summary>
    /// Computes the immediate exec successors of the currently-paused node,
    /// sets invisible one-shot temporary breakpoints on them, and resumes.
    /// Falls back to single-tick stepping with <paramref name="fallbackStepMode"/>
    /// when no graph is registered or the paused node cannot be resolved.
    /// When the paused-at node has no successors (terminal node), calls
    /// <see cref="Continue"/> to resume without temp breakpoints.
    /// </summary>
    private void Step(StepMode fallbackStepMode)
    {
        if (!_isPaused || _pausedAt == null)
            return;

        var pausedNodeId = _pausedAt.NodeId;
        var assetId = _pausedAt.AssetId;
        var graphId = _pausedAt.GraphId;

        // Find the graph structure.
        if (!_graphs.TryGetValue(graphId, out var graph))
        {
            // No graph registered — fall back to legacy step-mode matching.
            LegacyStepOneTick(fallbackStepMode);
            return;
        }

        // Parse the authored node ID from the paused breakpoint.
        if (!Guid.TryParse(pausedNodeId, out var authoredNodeId))
        {
            LegacyStepOneTick(fallbackStepMode);
            return;
        }

        // Compute next exec successors from the graph.
        var successors = ExecSuccessors.GetSuccessors(graph, authoredNodeId);
        if (successors.Count == 0)
        {
            // Terminal node — nothing to step to. Just resume.
            Continue();
            return;
        }

        // Set invisible one-shot temporary breakpoints on all successors.
        // These are translated through BreakpointTargets (authored → block-probe id)
        // and suppress user breakpoints until a temp target fires.
        var targets = successors.Select(s => new BreakpointTarget(assetId, graphId, s));
        SetTemporaryBreakpoints(targets);

        // Resume (not single-tick) — temp BPs handle the pause.
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _stepMode       = StepMode.None;
        _firedBreakpointsThisTick.Clear();
        _timeController.RequestResume();
        OnSessionStateChanged?.Invoke();
    }

    /// <summary>
    /// Legacy single-tick step: sets <see cref="_stepMode"/> and steps one tick.
    /// Used as fallback when no graph is registered for the paused asset.
    /// The <see cref="OnNodeEnter"/> handler matches the next probed node
    /// against the step criteria and re-pauses.
    /// </summary>
    private void LegacyStepOneTick(StepMode mode)
    {
        var fromEntity  = _pausedOnEntity ?? default;
        _stepMode       = mode;
        _stepFromEntity = fromEntity;
        _stepFromDepth  = _currentCallDepth.GetValueOrDefault(fromEntity, 0);
        _stepFromTick   = _view.Tick;
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _firedBreakpointsThisTick.Clear();
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
                _breakpoints[bp.Id] = stale;
                ReplaceInBpList(bp.ProbeNodeId, bp, stale);
            }
            OnBreakpointListChanged?.Invoke(map.AssetId);
            foreach (var watch in _watches.Values.Where(w => w.AssetId == map.AssetId))
                watch.IsStale = true;
        }
        _debugMaps[map.AssetId] = index;

        // CF-7-rev: re-resolve tentative breakpoints' ProbeNodeId now that the DebugMap
        // has arrived. Without this, breakpoints set before the first compile would never
        // match the runtime probe id (they'd keep the authored node id as fallback).
        ReResolveBreakpointsForAsset(map.AssetId, index);
    }

    public void UnregisterDebugMap(Guid assetId)
    {
        _debugMaps.Remove(assetId);
    }

    public bool IsNodeBreakpointable(Guid assetId, Guid graphId, Guid nodeId)
    {
        // If no DebugMap is registered yet, be optimistic — the user can set
        // breakpoints before compiling; they'll become active once a compile
        // registers the map. After a map is registered, only nodes present in
        // BreakpointTargets (exec nodes) are breakpointable. Data nodes
        // (GetVariable, LiteralNode, CastNode, pure FunctionCall) and unknown
        // ids return false.
        if (!_debugMaps.TryGetValue(assetId, out var index))
            return true; // no map yet — allow tentative breakpoints
        return index.BreakpointTargets.ContainsKey(nodeId);
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
    // NGS-2.0: also start a new sub-tick recording session when recording is active.
    public void OnNewTick()
    {
        _firedBreakpointsThisTick.Clear();

        // Start a new recording session when armed and the tick advanced.
        // _view.Tick is SimulationTick (frozen during a debug tick; advances on real ticks).
        uint currentTick = _view.Tick;
        if (RecordingActive && currentTick != _lastRecordedTick)
        {
            _lastRecordedTick = currentTick;
            _recorder.BeginTick(_liveRepo!);
        }
    }

    // ---- Private helpers ----------------------------------------------------

    // Replaces a breakpoint in the _bpByNodeString list (used when updating stale flag,
    // hit count, etc. via with-expression which creates a new record instance).
    private void ReplaceInBpList(string probeId, Breakpoint old, Breakpoint updated)
    {
        if (!_bpByNodeString.TryGetValue(probeId, out var list)) return;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == old.Id)
            {
                list[i] = updated;
                return;
            }
        }
    }

    /// <summary>
    /// Re-resolves tentative breakpoints for the given asset after a <see cref="DebugMap"/>
    /// has been registered. Breakpoints set before the map arrived use the authored node id
    /// as a fallback <see cref="Breakpoint.ProbeNodeId"/>; after the map arrives, this method
    /// updates them to the correct block-probe id from <see cref="DebugMapIndex.BreakpointTargets"/>,
    /// re-keys the <c>_bpByNodeString</c> lookup, and re-forwards to <c>_dataBreakpointManager</c>.
    /// </summary>
    private void ReResolveBreakpointsForAsset(Guid assetId, DebugMapIndex index)
    {
        var assetBps = _breakpoints.Values
            .Where(b => b.AssetId == assetId)
            .ToList();

        foreach (var bp in assetBps)
        {
            // Parse the authored node id from bp.NodeId.
            if (!Guid.TryParse(bp.NodeId, out var authoredNodeId))
                continue;

            string oldProbeId = string.IsNullOrEmpty(bp.ProbeNodeId) ? bp.NodeId : bp.ProbeNodeId;

            // Determine the correct probe id from BreakpointTargets.
            string newProbeId;
            if (index.BreakpointTargets.TryGetValue(authoredNodeId, out var blockProbeId))
                newProbeId = blockProbeId.ToString("D");
            else
            {
                // CF-8: authored node is not in BreakpointTargets.
                // If the breakpoint was previously resolved to a block-probe id
                // (different from the authored node id), the node was deleted —
                // mark stale per BPF-003.  Otherwise it was always a fallback and
                // we leave it as-is (it was never resolved).
                if (oldProbeId != bp.NodeId)
                {
                    var stale = bp with { IsStale = true };
                    _breakpoints[bp.Id] = stale;
                    ReplaceInBpList(oldProbeId, bp, stale);
                }
                continue;
            }

            // No change needed.
            if (oldProbeId == newProbeId)
                continue;

            // Remove from old probe-keyed lookup.
            if (_bpByNodeString.TryGetValue(oldProbeId, out var list))
            {
                list.Remove(bp);
                if (list.Count == 0)
                    _bpByNodeString.Remove(oldProbeId);
            }

            // Update the breakpoint record.
            var updated = bp with { ProbeNodeId = newProbeId, IsStale = false };
            _breakpoints[bp.Id] = updated;

            // Add to new probe-keyed lookup.
            if (!_bpByNodeString.TryGetValue(newProbeId, out var newList))
                _bpByNodeString[newProbeId] = newList = new List<Breakpoint>();
            newList.Add(updated);

            // Re-forward to DataBreakpointManager with the correct probe id.
            if (_dataBreakpointManager != null && _mgrBpIds.TryGetValue(bp.Id, out var mgrId))
            {
                _dataBreakpointManager.Remove(mgrId);
                _mgrBpIds.Remove(bp.Id);

                var newMgrId = _dataBreakpointManager.AddBreakpoint(
                    new ExternalHitTagPredicateDto { Tag = newProbeId },
                    displayName: $"Blueprint node {bp.NodeId}",
                    sourceElementId: authoredNodeId);
                _mgrBpIds[updated.Id] = newMgrId;
            }
        }
    }

    // BPF-003: increments HitCount without triggering a new pause (same-tick dedup path).
    private void IncrementHitCountOnly(Breakpoint bp)
    {
        if (bp.Id.Value == 0 || !_breakpoints.ContainsKey(bp.Id)) return;
        var updated = bp with { HitCount = bp.HitCount + 1 };
        _breakpoints[bp.Id] = updated;
        ReplaceInBpList(bp.ProbeNodeId, bp, updated);
    }

    private void HandleBreakpointHit(Entity self, Breakpoint bp, string nodeId)
    {
        _isPaused        = true;
        _pausedAt        = bp;
        _pausedOnEntity  = self;
        _stepMode        = StepMode.None;
        _stepFromEntity  = default;
        _stepFromDepth   = 0;

        if (bp.Id.Value != 0 && _breakpoints.ContainsKey(bp.Id))
        {
            var updated = bp with { HitCount = bp.HitCount + 1 };
            _breakpoints[bp.Id] = updated;
            ReplaceInBpList(bp.ProbeNodeId, bp, updated);
            _pausedAt            = updated;

            var assetId = updated.AssetId;

            // Session always requests pause directly via its own time controller,
            // independent of DataBreakpointManager's internal _isPaused flag
            // (which can drift and block re-hits after Continue).
            _timeController.RequestPause();
            _dataBreakpointManager?.OnExternalHit(nodeId, self);

            OnBreakpointHit?.Invoke(new BreakpointHit(
                self, nodeId, assetId, _view.Time, _view.Tick,
                ResolveSourceFilePath(assetId, nodeId),
                ResolveSourceLine(assetId, nodeId)));
        }
        else
        {
            _timeController.RequestPause();
            _dataBreakpointManager?.OnExternalHit(nodeId, self);

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