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
using EventEntryNode = Hrot.Blueprints.Core.Assets.EventEntryNode;
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
public sealed class BlueprintDebugSession : IBlueprintDebugSession, Hrot.Editor.AiShared.Debug.IAiDebugSession
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

    // Sub-tick snapshot recorder (NGS-2.0 / NGS-2.1).
    // Records per-node ECS deltas during a debug-active tick for sub-tick state inspection.
    // Null-safe: when _liveRepo is null (not wired), recording is silently disabled.
    private readonly SubTickSnapshotRecorder _recorder = new SubTickSnapshotRecorder();
    private EntityRepository? _liveRepo;

    // Tick-boundary detection: we detect a new tick by observing _view.Tick change in OnNewTick().
    // Stored as uint? so the first tick always triggers BeginTick.
    private uint? _lastRecordedTick;

    // The entity whose recordings are currently owned by the ring.
    // Set on pause (= _pausedOnEntity) so that OnNodeEnter scopes to a single entity.
    // null means "scope to _entityFilter" (pre-pause, armed tick).
    private Entity? _recordingEntity;

    // Virtual pointer for node-granular navigation (NGS-2.1).
    // -1 when no recordings are active.
    private int _nodePointer = -1;

    // NGS-2.3 tick-bridge: one-shot flag set when Step* is pressed at the last recorded
    // node of a tick while RecordingActive.  On the first OnNodeEnter of the resumed tick
    // for the debugged entity, the session re-pauses and sets _nodePointer = 0 (the first
    // recorded node of the new tick) without consulting authored-graph topology at all.
    // Cleared on HandleBreakpointHit, Continue(), and Detach().
    private bool _stepResumePending;
    // Asset/graph context saved when _stepResumePending is set so the pseudo-BP pseudo-pause
    // can carry the correct AssetId for state inspection (CaptureStateSnapshot).
    private Guid _stepResumeAssetId;
    private Guid _stepResumeGraphId;

    // Scratch repo for inspector redirect (NGS-2.2).
    // Lazily created; seeded via SyncFrom(liveRepo) on each new pause.
    private EntityRepository? _scratchRepo;

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

        // NGS-2.0/CT0a: record per-node ECS snapshot delta when armed and entity-scoped.
        // Entity scope rule:
        //   - If _recordingEntity is set (set on pause or by the entity that owns an armed BP),
        //     only record when self == _recordingEntity.
        //   - Otherwise fall back to _entityFilter (when set); any entity passes if no filter.
        // This prevents multiple instrumented entities from interleaving into one ring.
        // Called AFTER history and overlay so existing CF-6 behavior is undisturbed.
        if (RecordingActive && IsRecordingEntity(self))
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

        // NGS-2.3 tick-bridge: "pause on the first recorded node of the resumed tick" mode.
        // When _stepResumePending is set and this is the first probe for the recording entity
        // in the newly-resumed tick (_recorder.Count == 1 after RecordNodeEntry above), re-pause
        // immediately without consulting authored-graph topology.  This correctly handles
        // Sequence branch ordering, nested Sequences, and latent resume continuations because
        // the recorder captures actual execution order — not static authored-graph successors.
        //
        // Edge case: if the resumed tick records nothing for the entity (blueprint finished /
        // entity died), _stepResumePending is never cleared here.  It will be cleared by the
        // next HandleBreakpointHit, Continue(), or Detach() — no dead-stall.
        if (_stepResumePending && RecordingActive && IsRecordingEntity(self) && _recorder.Count == 1)
        {
            _stepResumePending = false;
            // Use an anonymous pseudo-breakpoint (id=0) to avoid mutating the real BP list.
            // Carry the saved AssetId/GraphId so CaptureStateSnapshot can look up the
            // blueprint definition and return the correct field values for inspection.
            var pseudoBp = new Breakpoint(default, _stepResumeAssetId, _stepResumeGraphId, nodeId, 0, true);
            HandleBreakpointHit(self, pseudoBp, nodeId);
            // Return early: the pseudo-BP pause supersedes temp and user BPs for this probe.
            return;
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
        _stepResumePending = false;
        // NGS-2.2: dispose scratch repo if created.
        _scratchRepo?.Dispose();
        _scratchRepo = null;
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
    /// Implements the interface property <see cref="IBlueprintDebugSession.RecordedNodeCount"/>.
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

    // ── NGS-2.1 — Virtual pointer ────────────────────────────────────────────

    /// <summary>
    /// Current virtual-pointer index into the sub-tick recording ring.
    /// -1 when no node-granular recordings are active (not paused or no recordings).
    /// </summary>
    public int CurrentNodePointer => _nodePointer;

    /// <summary>
    /// Node-id string at the current virtual-pointer position.
    /// Null when <see cref="CurrentNodePointer"/> is -1.
    /// </summary>
    public string? CurrentNodeId =>
        _nodePointer >= 0 && _nodePointer < _recorder.Count
            ? _recorder.NodeIdAt(_nodePointer)
            : null;

    /// <summary>
    /// Move the virtual pointer one node backward (towards index 0).
    /// Clamped at 0 — calling at index 0 is a no-op.
    /// Only meaningful while <see cref="IsPaused"/> and recordings exist.
    /// Does NOT touch the time controller (clock stays paused).
    /// </summary>
    public void StepBack()
    {
        if (_nodePointer <= 0 || _recorder.Count == 0) return;
        _nodePointer--;
        RestorePointerToScratch();
        OnSessionStateChanged?.Invoke();
    }

    /// <summary>
    /// Initialises the virtual pointer on pause.
    /// Searches for a recording whose node-id matches the paused node id;
    /// defaults to the last recorded index when not found.
    /// After setting the pointer, restores the scratch repo to that node's state.
    /// </summary>
    private void InitNodePointerOnPause(string pausedNodeId)
    {
        int count = _recorder.Count;
        if (count == 0)
        {
            _nodePointer = -1;
            return;
        }

        // Find the last matching recording (latest match wins for multi-visit nodes).
        int found = -1;
        for (int i = count - 1; i >= 0; i--)
        {
            if (_recorder.NodeIdAt(i) == pausedNodeId)
            {
                found = i;
                break;
            }
        }
        _nodePointer = found >= 0 ? found : count - 1;
        // NOTE: RestorePointerToScratch() is intentionally NOT called here.
        // This method fires inside HandleBreakpointHit, which is called during the
        // blueprint tick (via DebugProbe.NodeEnter → OnNodeEnter).  Calling SyncFrom
        // + RestoreTo while the tick system holds live-repo references can corrupt the
        // in-flight delta chain and will throw if the tick aborts early.
        // Restoration is deferred and called lazily by CaptureStateSnapshot(), StepBack(),
        // and StepForwardOrCF6() — all of which execute AFTER the tick completes.
    }

    /// <summary>
    /// Restores the scratch repo to the state as-of the current pointer, seeded via
    /// <c>SyncFrom(liveRepo)</c> so the scratch carries all component registrations and
    /// current live data. Then applies the sub-tick deltas up to and including the pointer.
    ///
    /// Scratch registration approach (NGS-2.2):
    /// SyncFrom(liveRepo) auto-registers any missing component types in the scratch
    /// (by reflection inside EntityRepository.SyncFrom) before copying data — so no
    /// manual pre-registration is required. The keyframe playback lands on top of
    /// the synced state, then deltas layer sub-tick mutations.
    /// </summary>
    private void RestorePointerToScratch()
    {
        if (_nodePointer < 0 || _liveRepo == null || _recorder.Count == 0) return;

        // Lazily create the scratch repo.
        _scratchRepo ??= new EntityRepository();

        // Seed registrations + live baseline so PlaybackSystem.ApplyFrame finds all tables.
        // Use includeTransient: true so SyncFrom uses GetSnapshotableMask(true) = all registered
        // component types, which is a superset of the recordable types the keyframe contains.
        // Without this, components marked [DataPolicy(DataPolicy.NoSnapshot)] are recordable but
        // NOT snapshotable — the keyframe captures them but the scratch repo never registered
        // the type → PlaybackSystem.ApplyChunkData throws "type ID not found".
        _scratchRepo.SyncFrom(_liveRepo, includeTransient: true);

        // Replay keyframe + deltas[0.._nodePointer] into scratch.
        _recorder.RestoreTo(_nodePointer, _scratchRepo);
    }

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
    /// Entity-scope gate for recording (CT0a).
    /// Returns true when <paramref name="entity"/> should have its probe recorded:
    /// <list type="bullet">
    ///   <item>If <see cref="_recordingEntity"/> is set, only the exact match passes.</item>
    ///   <item>Otherwise, if <see cref="_entityFilter"/> is set, only the filter entity passes.</item>
    ///   <item>Otherwise (both null) any entity passes — this is the single-entity test case.</item>
    /// </list>
    /// </summary>
    private bool IsRecordingEntity(Entity entity)
    {
        if (_recordingEntity.HasValue) return entity == _recordingEntity.Value;
        if (_entityFilter.HasValue)    return entity == _entityFilter.Value;
        return true; // no scope set — allow all (single-entity test scenarios)
    }

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

        // NGS-2.1: clear virtual pointer and recording-entity scope.
        _nodePointer        = -1;
        _recordingEntity    = null;
        _stepResumePending  = false;
        _stepResumeAssetId  = Guid.Empty;
        _stepResumeGraphId  = Guid.Empty;

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
    /// "step to next exec node."
    ///
    /// NGS-2.1 override: when node-granular recordings exist for the paused entity,
    /// Step* moves the virtual pointer forward by one recording slot. The clock stays
    /// paused; no re-execution occurs. When the pointer reaches the last recorded node,
    /// an additional step triggers the NGS-2.3 tick-bridge (see StepForwardOrCF6):
    /// one real tick is requested and the armed breakpoint re-fires on the new tick,
    /// re-pausing with a fresh per-tick recording and a re-initialised pointer.
    ///
    /// Fallback (no recordings): computes successors via graph structure, sets temporary
    /// breakpoints, suppresses user breakpoints, and resumes — the CF-6 path.
    /// This fallback is also used when no graph is registered for the paused asset
    /// (backward compatible with existing step-mode tests and pre-CF-6 behavior).
    /// </summary>
    public void StepOver() => StepForwardOrCF6(StepMode.Over);
    public void StepInto() => StepForwardOrCF6(StepMode.Into);
    public void StepOut()  => StepForwardOrCF6(StepMode.Out);

    /// <summary>
    /// NGS-2.1 / NGS-2.3: move the virtual pointer forward by one slot when recordings exist.
    /// When already at the last recorded index and a breakpoint is still armed
    /// (<see cref="RecordingActive"/>), sets <see cref="_stepResumePending"/> and resumes
    /// the simulation.  On the first <see cref="OnNodeEnter"/> of the resumed tick for the
    /// recording entity, the session re-pauses and sets <see cref="_nodePointer"/> = 0
    /// (first recorded node of the new tick) — without consulting authored-graph topology.
    /// This correctly handles Sequence branch ordering, nested Sequences, and latent
    /// resume continuations (BF-03, BF-04, and the Sequence step-over bug fixed here).
    /// Falls back to CF-6 temp-BP stepping when no recordings exist for the paused entity.
    /// </summary>
    private void StepForwardOrCF6(StepMode fallbackStepMode)
    {
        // Node-granular path: recordings exist → just move the pointer.
        if (_isPaused && _nodePointer >= 0 && _recorder.Count > 0)
        {
            int last = _recorder.Count - 1;
            if (_nodePointer < last)
            {
                _nodePointer++;
                RestorePointerToScratch();
                OnSessionStateChanged?.Invoke();
                return;
            }

            // --- NGS-2.3 tick-bridge (unified BF-03 / BF-04 / Sequence fix) ---
            // Pointer is at the last recorded node (end of this tick's recording).
            // Only step forward when a breakpoint is armed (RecordingActive).
            //
            // New approach: instead of guessing the next pause target from authored-graph
            // ExecSuccessors topology (which cannot model Sequence branch ordering or
            // latent resume successors), set a one-shot "_stepResumePending" flag and
            // resume.  OnNodeEnter will re-pause on the FIRST recorded node of the next
            // tick for this entity — the actual execution-order answer, correct for any
            // graph topology including nested Sequences and latent resumes.
            //
            // The no-recording CF-6 fallback path (below) is unchanged.
            if (RecordingActive)
            {
                // Save asset/graph context for the pseudo-BP created by OnNodeEnter so
                // that CaptureStateSnapshot can look up the blueprint definition by AssetId.
                _stepResumeAssetId  = _pausedAt?.AssetId ?? Guid.Empty;
                _stepResumeGraphId  = _pausedAt?.GraphId ?? Guid.Empty;
                _stepResumePending  = true;
                _isPaused           = false;
                _pausedAt           = null;
                _pausedOnEntity     = null;
                _nodePointer        = -1;
                _stepMode           = StepMode.None;
                _firedBreakpointsThisTick.Clear();
                // Keep _recordingEntity so the same entity is scoped for the resumed tick.
                _timeController.RequestResume();
                OnSessionStateChanged?.Invoke();
                return;
            }

            // No breakpoint armed: cannot guarantee re-pause after advancing.
            // Keep the existing no-op clamp so the pointer stays at the last node.
            // (The user must arm a breakpoint before using step-past-end.)
            OnSessionStateChanged?.Invoke();
            return;
        }

        // CF-6 fallback: no recordings → use temp-BP graph stepping.
        Step(fallbackStepMode);
    }

    /// <summary>
    /// BF-04 tick-bridge helper: step from the last recorded node of the current tick.
    ///
    /// <para>If <paramref name="fromNodeId"/> has non-terminal exec successors (e.g.
    /// <c>Delay→SetVar</c>), delegates to <see cref="StepFromNode"/> which sets a temp BP
    /// on those successors (existing BF-03 behaviour).</para>
    ///
    /// <para>If all successors are terminal (e.g. <c>Delay→Return</c> = end-of-tick), the
    /// next probe to fire will be the <b>first node of the next iteration</b>, reached when the
    /// tick restarts after the latent completes.  We find that target by taking the
    /// exec-successors of the graph's <see cref="EventEntryNode"/>, set one-shot temp BPs on
    /// the non-terminal ones, and call <see cref="RequestResume"/>.  The temp BP fires when
    /// the tick restarts, re-pausing on the first node — NOT the user breakpoint.</para>
    ///
    /// <para>Degenerate cases (no registered graph, no <c>EventEntryNode</c>, no
    /// entry-successors, node id not parseable): fall back to <see cref="Continue"/>.</para>
    /// </summary>
    private void StepFromNodeOrNextIteration(Guid assetId, Guid graphId, string fromNodeId, StepMode legacyFallback)
    {
        // Resolve graph and last-node id.
        if (!_graphs.TryGetValue(graphId, out var graph))
        {
            LegacyStepOneTick(legacyFallback);
            return;
        }

        if (!Guid.TryParse(fromNodeId, out var lastAuthoredId))
        {
            LegacyStepOneTick(legacyFallback);
            return;
        }

        // Check whether the last recorded node has non-terminal successors.
        var successors = ExecSuccessors.GetSuccessors(graph, lastAuthoredId);
        bool allTerminal = successors.Count == 0 ||
                           successors.All(s => ExecSuccessors.GetSuccessors(graph, s).Count == 0);

        if (!allTerminal)
        {
            // (a) Non-terminal path: existing BF-03 behaviour — delegate to StepFromNode.
            StepFromNode(assetId, graphId, fromNodeId, legacyFallback);
            return;
        }

        // (b) End-of-tick path (all successors terminal): target the first executable node(s)
        // of the next iteration = exec-successors of the graph's EventEntryNode.
        var entryNode = graph.Nodes.OfType<EventEntryNode>().FirstOrDefault();
        if (entryNode == null)
        {
            // Degenerate: no EventEntryNode in the graph. Fall back to Continue().
            Continue();
            return;
        }

        var entrySuccessors = ExecSuccessors.GetSuccessors(graph, entryNode.Id);
        var firstNodes = entrySuccessors
            .Where(s => ExecSuccessors.GetSuccessors(graph, s).Count > 0)
            .ToList();

        if (firstNodes.Count == 0)
        {
            // Degenerate: no non-terminal entry-successors (empty graph or only terminals).
            Continue();
            return;
        }

        // Set one-shot temp BPs on the first executable node(s) of the next iteration.
        var targets = firstNodes.Select(s => new BreakpointTarget(assetId, graphId, s));
        SetTemporaryBreakpoints(targets);

        // Clear pause/nav state. Keep _recordingEntity so the same entity is recorded next tick.
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _nodePointer    = -1;
        _stepMode       = StepMode.None;
        _firedBreakpointsThisTick.Clear();

        // Resume — the temp BP handles re-pause at the first node of the next iteration.
        _timeController.RequestResume();
        OnSessionStateChanged?.Invoke();
    }

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

        StepFromNode(_pausedAt.AssetId, _pausedAt.GraphId, _pausedAt.NodeId, fallbackStepMode);
    }

    /// <summary>
    /// Core of CF-6 stepping: computes exec successors of <paramref name="fromNodeId"/>
    /// in the specified graph, sets one-shot temporary breakpoints on them, clears pause/nav
    /// state, and resumes the time controller.
    ///
    /// <para>Used by both <see cref="Step"/> (stepping from <c>_pausedAt</c>) and the
    /// NGS-2.3 tick-bridge in <see cref="StepForwardOrCF6"/> (stepping from the LAST
    /// RECORDED node, which may be different from <c>_pausedAt</c> after within-tick
    /// pointer navigation).</para>
    ///
    /// <para>Handles latent nodes (Delay/WaitForChannel) correctly: temp BPs are set on
    /// successors and the session resumes; the BP fires when the latent node completes
    /// (however many ticks later), preventing the dead "Not paused" state that occurred
    /// when the bridge used <c>RequestStepOneTick</c>.</para>
    ///
    /// <para>Terminal node (no successors) → <see cref="Continue()"/>.</para>
    /// <para>No graph registered or node not parseable → <see cref="LegacyStepOneTick"/>.</para>
    /// </summary>
    private void StepFromNode(Guid assetId, Guid graphId, string fromNodeId, StepMode legacyFallback)
    {
        // Find the graph structure.
        if (!_graphs.TryGetValue(graphId, out var graph))
        {
            // No graph registered — fall back to legacy step-mode matching.
            LegacyStepOneTick(legacyFallback);
            return;
        }

        // Parse the authored node ID.
        if (!Guid.TryParse(fromNodeId, out var authoredNodeId))
        {
            LegacyStepOneTick(legacyFallback);
            return;
        }

        // Compute next exec successors from the graph.
        var successors = ExecSuccessors.GetSuccessors(graph, authoredNodeId);
        if (successors.Count == 0)
        {
            // Terminal node — nothing to step to. Just resume (Continue).
            Continue();
            return;
        }

        // Terminal-successor guard: if every immediate successor is itself a terminal
        // node (no further exec successors), it is a probe-less "sink" block (e.g. a
        // ReturnNode that was merged into the preceding block by Stage5).  Setting a
        // temp BP on such a node is a dead-end because no DebugProbe.NodeEnter fires
        // for it — the probe fires with the predecessor's id.  In this case fall back
        // to Continue() so the session stays live and the user BP re-fires on the next
        // tick, equivalent to "step past the end of the synchronous chain."
        bool allSuccessorsAreTerminal = successors.All(
            s => ExecSuccessors.GetSuccessors(graph, s).Count == 0);
        if (allSuccessorsAreTerminal)
        {
            Continue();
            return;
        }

        // Set invisible one-shot temporary breakpoints on all NON-TERMINAL successors.
        // Skip terminal successors (ReturnNode etc.) that have no probe of their own.
        // These are translated through BreakpointTargets (authored → block-probe id)
        // and suppress user breakpoints until a temp target fires.
        var nonTerminalSuccessors = successors
            .Where(s => ExecSuccessors.GetSuccessors(graph, s).Count > 0)
            .ToList();
        if (nonTerminalSuccessors.Count == 0)
        {
            // All non-terminal successors filtered out (shouldn't reach here given the
            // guard above, but be defensive).
            Continue();
            return;
        }
        var targets = nonTerminalSuccessors.Select(s => new BreakpointTarget(assetId, graphId, s));
        SetTemporaryBreakpoints(targets);

        // Clear pause/nav state so this session is not considered "paused" while
        // resuming. Keep _recordingEntity so the same entity is recorded on the next tick.
        _isPaused       = false;
        _pausedAt       = null;
        _pausedOnEntity = null;
        _nodePointer    = -1;
        _stepMode       = StepMode.None;
        _firedBreakpointsThisTick.Clear();

        // Resume (not single-tick) — temp BPs handle the re-pause.
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
    // NGS-2.2: when the virtual pointer is active, read from the scratch repo instead of _view.
    public BlueprintStateSnapshot? GetCurrentStateSnapshot()
    {
        if (!_isPaused || !_pausedOnEntity.HasValue || _pausedAt is null) return null;
        return CaptureStateSnapshot(_pausedOnEntity.Value, _pausedAt.AssetId);
    }

    /// <summary>
    /// Returns a live (non-pause-gated) snapshot of the working-state for the given entity
    /// and blueprint asset. Calls <see cref="CaptureStateSnapshot"/> directly without
    /// requiring the session to be paused. Always reads from the live view (not the scratch),
    /// because this method is for live inspection outside of a node-granular navigation session.
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
            // NGS-2.2: use scratch repo as the inspection view when the virtual pointer is active.
            // This returns per-node state at the pointer instead of the paused (post-tick) live state.
            // When pointer is -1 (no recordings), fall back to _view (unchanged behavior).
            // Lazy restore: InitNodePointerOnPause deliberately skips RestorePointerToScratch
            // (which runs inside a tick), so we do it here on first access after a pause.
            if (_nodePointer >= 0) RestorePointerToScratch();
            ISimulationView inspectionView = (_nodePointer >= 0 && _scratchRepo != null)
                ? (ISimulationView)_scratchRepo
                : _view;

            switch (def.Kind)
            {
                case BlueprintDispatchKind.AiPrimitive:
                    CaptureAiPrimitiveState(self, def, mapIndex, fields, inspectionView);
                    break;
                case BlueprintDispatchKind.Instance:
                    CaptureInstanceStateFromDefinition(self, bpId, mapIndex, def, fields, out cursor, inspectionView);
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
    // NGS-2.2: accepts an explicit view so the caller can redirect to the scratch repo.
    private void CaptureAiPrimitiveState(
        Entity self, BlueprintDefinition def, DebugMapIndex? mapIndex,
        Dictionary<string, object> outFields,
        ISimulationView? view = null)
    {
        var effectiveView = view ?? _view;
        if (!effectiveView.HasComponent<Blackboard1024>(self)) return;
        ref readonly var bb = ref effectiveView.GetComponentRO<Blackboard1024>(self);

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
    // NGS-2.2: accepts an explicit view so the caller can redirect to the scratch repo.
    private unsafe void CaptureInstanceStateFromDefinition(
        Entity self, int blueprintId, DebugMapIndex? mapIndex, BlueprintDefinition def,
        Dictionary<string, object> outFields, out BlueprintLatentCursor? cursor,
        ISimulationView? view = null)
    {
        cursor = null;
        var effectiveView = view ?? _view;

        if (effectiveView.HasComponent<BlueprintBlackboard1024>(self))
        {
            ref readonly var bb = ref effectiveView.GetComponentRO<BlueprintBlackboard1024>(self);
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in bb, 1));
            ReadInstanceState(bytes, blueprintId, mapIndex?.StateLayout, def, outFields, out cursor);
        }
        else if (effectiveView.HasComponent<BlueprintBlackboard4096>(self))
        {
            ref readonly var bb = ref effectiveView.GetComponentRO<BlueprintBlackboard4096>(self);
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(in bb, 1));
            ReadInstanceState(bytes, blueprintId, mapIndex?.StateLayout, def, outFields, out cursor);
        }
        else if (effectiveView.HasComponent<BlueprintBlackboard16384>(self))
        {
            ref readonly var bb = ref effectiveView.GetComponentRO<BlueprintBlackboard16384>(self);
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

        // NGS-2.1/CT0a: lock recording entity to the paused entity.
        _recordingEntity = self;

        // NGS-2.1: initialise the virtual pointer.
        // Prefer the index whose node-id matches the paused node. If not found (e.g.
        // no recordings yet or no match), default to the last recorded index.
        InitNodePointerOnPause(nodeId);

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

    /// <summary>
    /// ⭐⭐ <c>S3</c> — FQN → loaded CLR type, <b>across every loaded assembly</b>.
    ///
    /// <para>
    /// 🔴 <c>Type.GetType(fqn)</c> alone searches only the CALLING assembly and corelib, so it never
    /// found a game struct — <c>Fdp.Core.FixedString32</c>, <c>Hrot.AI.Behaviors.Brains.MemberSlotList</c>
    /// — and the field was silently <b>skipped</b>, not shown as undecodable. ⭐ The nine-case switch
    /// below is kept: it short-circuits the common primitives before any assembly walk, and it is what
    /// makes the FALLBACK's cost irrelevant.
    /// </para>
    ///
    /// <para>
    /// ⚠ Mirrors <c>ComponentFieldReflector.ResolveType</c> deliberately — the same
    /// <c>EditorTypeResolutionScope</c> (BP-62), so a referenced-but-not-yet-loaded assembly is
    /// force-loaded rather than silently missing here and present there.
    /// </para>
    /// </summary>
    public static Type? ResolveType(string typeFullName) => typeFullName switch
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
        _                => Hrot.Blueprints.Editor.NodeDrawers.ComponentFieldReflector
                                .ResolveType(typeFullName),
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
        // FC-2/LV-5: narrow primitives, so fixed-list ELEMENTS of these types render as values.
        if (type == typeof(byte))   return bytes[0];
        if (type == typeof(sbyte))  return unchecked((sbyte)bytes[0]);
        if (type == typeof(short))  return System.Runtime.InteropServices.MemoryMarshal.Read<short>(bytes);
        if (type == typeof(ushort)) return System.Runtime.InteropServices.MemoryMarshal.Read<ushort>(bytes);
        if (type == typeof(ulong))  return System.Runtime.InteropServices.MemoryMarshal.Read<ulong>(bytes);
        if (TryFormatFixedList(bytes, type, out var formatted)) return formatted;
        if (TryReadStruct(bytes, type, out var value)) return value;
        return bytes;
    }

    /// <summary>
    /// ⭐⭐⭐ <c>S3</c> / <c>BP-01</c> — <b>the struct arm.</b> Seven of the editor's eighteen offerable
    /// types (<c>Vector2/3/4</c>, <c>Quaternion</c>, <c>FixedString32/64/128</c>) reached
    /// <c>return bytes</c>, so the watch panel showed raw hex for a type the picker itself offers.
    /// ⛔ <b>That was never a panel bug.</b>
    ///
    /// <para>
    /// ⭐ <b>Reflection is the ruled mechanism</b>, and the ruling bounds it: <i>"reflection-based for
    /// structs (UI decode only, <b>not on the probe path</b>)"</i>
    /// (<c>_DONE/blueprints-1/TASK-DETAIL.md:1840</c>). ✅ Honoured by placement — every caller of
    /// <see cref="MarshalFromBytes"/> is a snapshot/watch read; the probe path never reaches here.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>A structural rule, not an allow-list</b> — an allow-list is the very defect <c>S5</c>
    /// exists to remove, one layer down. The bound is <b>exactness</b>: an unmanaged value type whose
    /// managed size is exactly the slice it was handed. ⚠ The design's <i>"small structs only"</i>
    /// (<c>blueprint-dbg-1:193</c>) is a SCOPE note on what the Debug DD covered, not a byte ceiling;
    /// inventing a number here would just re-create the silent skip above it.
    /// </para>
    ///
    /// <para>
    /// ⛔ <b><see cref="System.Runtime.InteropServices.MemoryMarshal"/>, not
    /// <c>Marshal.PtrToStructure</c>.</b> The bytes are the MANAGED layout the generated writer stores
    /// (<c>Unsafe.As</c> onto the blackboard); <c>PtrToStructure</c> reads the MARSHALLED one, and the
    /// two differ on <c>bool</c>. Reading with the wrong model yields a plausible wrong value, which is
    /// worse than hex.
    /// </para>
    /// </summary>
    internal static bool TryReadStruct(byte[] bytes, Type type, out object? value)
    {
        value = null;
        if (!type.IsValueType || type.IsEnum || type.IsPrimitive || type.IsGenericTypeDefinition)
            return false;

        try
        {
            if ((bool)IsReferenceOrContainsReferencesMethod.MakeGenericMethod(type).Invoke(null, null)!)
                return false;                                       // managed => not blittable bytes
            if ((int)UnsafeSizeOfMethod.MakeGenericMethod(type).Invoke(null, null)! != bytes.Length)
                return false;                                       // ⛔ exactness IS the bound

            value = ReadManagedMethod.MakeGenericMethod(type).Invoke(null, new object[] { bytes });
            return true;
        }
        catch
        {
            // A ref struct, a pointer field, an open generic — unreflectable. ⭐ Fall through to the
            // raw bytes rather than throwing: the watch panel must never take the session down.
            return false;
        }
    }

    /// <summary>The one generic frame the reflective call above needs, so the span is created INSIDE
    /// (a <c>Span&lt;byte&gt;</c> cannot be boxed into an <c>object[]</c> argument list).</summary>
    private static object ReadManaged<T>(byte[] bytes) where T : struct
        => System.Runtime.InteropServices.MemoryMarshal.Read<T>(bytes)!;

    private static readonly System.Reflection.MethodInfo IsReferenceOrContainsReferencesMethod =
        typeof(System.Runtime.CompilerServices.RuntimeHelpers)
            .GetMethod(nameof(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences))!;

    private static readonly System.Reflection.MethodInfo UnsafeSizeOfMethod =
        typeof(System.Runtime.CompilerServices.Unsafe)
            .GetMethod(nameof(System.Runtime.CompilerServices.Unsafe.SizeOf))!;

    private static readonly System.Reflection.MethodInfo ReadManagedMethod =
        typeof(BlueprintDebugSession).GetMethod(nameof(ReadManaged),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    /// <summary>
    /// FC-2/LV-5 → FC-3c: thin delegate to <see cref="Fdp.Core.FixedListFormatter"/> — THE
    /// single definition of the list summary string, shared with the StructEdit
    /// <c>FixedListBufferViewProvider</c>'s collapsed row. This watch stays a transient
    /// bytes→string call by design: the wrapper types live in COLLECTIBLE ALCs and nothing
    /// here may retain them (see Q#21 D addendum). Recognition is structural
    /// (<see cref="Fdp.Core.FixedListShape"/>) — generated <c>__List_…</c> wrappers and
    /// hand-authored A1 wrappers both render.
    /// </summary>
    internal static bool TryFormatFixedList(byte[] bytes, Type type, out string formatted)
        => Fdp.Core.FixedListFormatter.TryFormat(bytes, type, out formatted);

    // ── IAiDebugSession bridge (toolbar/registry surface; UBP toolbar debug icons) ───────────────
    // BlueprintDebugSession also implements IAiDebugSession so it can be the registry's ActiveSession,
    // which drives the main-toolbar debug icons. The shared step/pause/state members above already
    // satisfy the contract; only the breakpoint members collide (AiShared BreakpointId/Breakpoint are
    // distinct types from the Core ones) → explicit interface impls that bridge to the Core store.

    Hrot.Editor.AiShared.Debug.BreakpointId Hrot.Editor.AiShared.Debug.IAiDebugSession.SetBreakpoint(
        Guid assetId, Guid elementId)
    {
        // The 2-arg AiShared surface has no graphId; bridge to the 3-arg Core API with Guid.Empty.
        var coreId = SetBreakpoint(assetId, Guid.Empty, elementId);
        return new Hrot.Editor.AiShared.Debug.BreakpointId(coreId.Value);
    }

    void Hrot.Editor.AiShared.Debug.IAiDebugSession.ClearBreakpoint(Hrot.Editor.AiShared.Debug.BreakpointId id)
        => ClearBreakpoint(new BreakpointId(id.Value));

    IReadOnlyList<Hrot.Editor.AiShared.Debug.Breakpoint>
        Hrot.Editor.AiShared.Debug.IAiDebugSession.GetBreakpoints()
    {
        var core = GetBreakpoints();
        var list = new List<Hrot.Editor.AiShared.Debug.Breakpoint>(core.Count);
        foreach (var bp in core) list.Add(ToAiSharedBreakpoint(bp));
        return list;
    }

    Hrot.Editor.AiShared.Debug.Breakpoint? Hrot.Editor.AiShared.Debug.IAiDebugSession.PausedAt
        => PausedAt is { } core ? ToAiSharedBreakpoint(core) : null;

    private static Hrot.Editor.AiShared.Debug.Breakpoint ToAiSharedBreakpoint(Breakpoint bp)
        => new(
            new Hrot.Editor.AiShared.Debug.BreakpointId(bp.Id.Value),
            bp.AssetId,
            Guid.TryParse(bp.NodeId, out var nid) ? nid : Guid.Empty,
            bp.HitCount,
            bp.Enabled,
            bp.NodeId);

    // IAiTraceObserver: blueprint trace data flows through DebugProbe.Sink (global), not the per-asset
    // AiTracerCoordinator ref-counting used by BTree/HSM — so these are intentional no-ops here.
    void Hrot.Editor.AiShared.Debug.IAiTraceObserver.BeginObservingAsset(
        Guid assetId, Hrot.Editor.AiShared.Debug.TraceLevel level) { }
    void Hrot.Editor.AiShared.Debug.IAiTraceObserver.EndObservingAsset(Guid assetId) { }
}