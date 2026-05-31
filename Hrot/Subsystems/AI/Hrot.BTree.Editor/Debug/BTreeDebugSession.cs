using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
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

    private BehaviorTreeStateSnapshot? _currentSnapshot;
    private ushort _lastReadPos;

    // BPF-026/BPF-045: debug metadata for node index -> VisualId symbolication.
    private NodeDebugMetadata[]? _debugMetadata;
    private Guid _assetId = Guid.Empty;

    private enum StepMode { None, Over, Into, Out }
    private StepMode _stepMode = StepMode.None;
    private int  _stepFromStackDepth;
    private bool _nodeProcessedSinceStep;

    public event Action<BTreeBreakpointHit>? OnBreakpointHit;
    public event Action<BTreeNodeExecuted>?  OnNodeExecuted;
    public event Action<BTreeAsyncEvent>?    OnAsyncIssued;
    public event Action<BTreeAsyncEvent>?    OnAsyncResolved;
    public event Action<BTreeAsyncEvent>?    OnAsyncAborted;

    public BTreeDebugSession(AiTracerCoordinator? coordinator = null) : base(coordinator) { }

    // ---- IBTreeDebugSession ------------------------------------------------

    public BehaviorTreeStateSnapshot? GetCurrentStateSnapshot() => _currentSnapshot;

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

    /// <summary>
    /// Stores the per-node debug metadata so that node indices can be symbolicated
    /// to VisualIds in snapshots and trace events.  Call once after loading the asset.
    /// </summary>
    public void SetDebugMetadata(NodeDebugMetadata[]? metadata, Guid assetId)
    {
        _debugMetadata = metadata;
        _assetId       = assetId;
    }

    /// <summary>Returns the VisualId for the given node index, or null when unavailable.</summary>
    private Guid? GetVisualId(int nodeIndex)
    {
        if (_debugMetadata == null || nodeIndex < 0 || nodeIndex >= _debugMetadata.Length)
            return null;
        string raw = _debugMetadata[nodeIndex].VisualId;
        if (string.IsNullOrEmpty(raw)) return null;
        return Guid.TryParse(raw, out var g) ? g : (Guid?)null;
    }

    /// <summary>
    /// Converts a node index to its VisualId using the stored debug metadata.
    /// Returns null when metadata is not set or the index is out of range.
    /// Exposed as internal for testability.
    /// </summary>
    internal Guid? TrySymbolicateIndex(int nodeIndex) => GetVisualId(nodeIndex);

    // ---- ECS snapshot + trace polling (called once per frame) ---------------

    /// <summary>
    /// Reads the current BehaviorTreeState snapshot from the entity and polls
    /// any pending trace records from BTreeTraceWorkingMemory1024.
    /// </summary>
    public unsafe void Update(EntityRepository repo, Entity entity)
    {
        // === Snapshot ===
        if (!repo.HasComponent<BrainBTreeState>(entity))
        {
            _currentSnapshot = null;
        }
        else
        {
            ref readonly var comp = ref repo.GetComponentRO<BrainBTreeState>(entity);
            ushort runningNodeIndex = comp.State.RunningNodeIndex;
            ushort sp               = comp.State.StackPointer;
            uint   treeVersion      = comp.State.TreeVersion;

            int stackLen = Math.Min(8, (int)sp + 1);
            var stack    = new int[stackLen];
            var stackIds = new Guid?[stackLen];
            var regs     = new int[4];
            var handles  = new ulong[3];

            ref var stateMut = ref Unsafe.AsRef(in comp.State);
            BehaviorTreeState* statePtr = (BehaviorTreeState*)Unsafe.AsPointer(ref stateMut);
            for (int i = 0; i < stackLen; i++) stack[i]   = statePtr->NodeIndexStack[i];
            for (int i = 0; i < 4; i++)        regs[i]    = statePtr->LocalRegisters[i];
            for (int i = 0; i < 3; i++)        handles[i] = statePtr->AsyncHandles[i];

            // BPF-026: symbolicate running node index and stack entries to VisualIds.
            Guid? runningElementId = GetVisualId(runningNodeIndex);
            for (int i = 0; i < stackLen; i++)
                stackIds[i] = GetVisualId(stack[i]);

            _currentSnapshot = new BehaviorTreeStateSnapshot(
                entity, _assetId, runningNodeIndex, runningElementId,
                sp, stack, stackIds, regs, handles, treeVersion);
        }

        // === Trace polling ===
        if (!repo.HasComponent<BTreeTraceWorkingMemory1024>(entity))
            return;

        ref readonly var trace = ref repo.GetComponentRO<BTreeTraceWorkingMemory1024>(entity);
        if (trace.WritePos == _lastReadPos)
            return;

        ref var traceMut = ref Unsafe.AsRef(in trace);
        BTreeTraceWorkingMemory1024* tracePtr = (BTreeTraceWorkingMemory1024*)Unsafe.AsPointer(ref traceMut);
        byte* bufBase = tracePtr->Buffer;
        ushort pos = _lastReadPos;
        while (pos != trace.WritePos)
        {
            var rec = (BTreeTraceRecord*)(bufBase + pos);
            switch (rec->OpCode)
            {
                case BTreeTraceOpCode.NodeEvaluated:
                    _nodeProcessedSinceStep = true;
                    // BPF-045: use node index to look up the VisualId.
                    RecordNodeExecuted(new BTreeNodeExecuted(
                        entity, _assetId, GetVisualId(rec->NodeIndex) ?? Guid.Empty,
                        rec->Status, 0f, rec->Timestamp));
                    break;
                case BTreeTraceOpCode.WaitStarted:
                    // BPF-045: use node index to look up the VisualId.
                    RecordAsyncEvent(new BTreeAsyncEvent(
                        entity, _assetId, GetVisualId(rec->NodeIndex) ?? Guid.Empty,
                        rec->NodeIndex, 0u, BTreeAsyncPhase.Issued, 0f));
                    break;
                case BTreeTraceOpCode.WaitCompleted:
                    RecordAsyncEvent(new BTreeAsyncEvent(
                        entity, _assetId, GetVisualId(rec->NodeIndex) ?? Guid.Empty,
                        rec->NodeIndex, 0u, BTreeAsyncPhase.Resolved, 0f));
                    break;
            }
            pos = (ushort)((pos + BTreeTraceWorkingMemory1024.RecordStride)
                           % BTreeTraceWorkingMemory1024.PayloadBytes);
        }
        _lastReadPos = trace.WritePos;

        // Step-mode auto-pause evaluation
        if (_stepMode != StepMode.None && _currentSnapshot is not null)
        {
            bool shouldPause = _stepMode switch
            {
                StepMode.Over => _currentSnapshot.StackPointer == _stepFromStackDepth,
                StepMode.Into => _nodeProcessedSinceStep,
                StepMode.Out  => _currentSnapshot.StackPointer < _stepFromStackDepth,
                _             => false
            };
            if (shouldPause)
            {
                _stepMode = StepMode.None;
                Coordinator.RequestPause();
            }
        }
    }

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

    // ---- AiDebugSessionBase overrides -------------------------------------

    protected override void OnContinueImpl()
    {
        _stepMode = StepMode.None;
        Coordinator.RequestContinue();
    }

    protected override void OnPauseImpl()
    {
        Coordinator.RequestPause();
    }

    protected override void OnStepOverImpl()
    {
        _stepFromStackDepth     = _currentSnapshot?.StackPointer ?? 0;
        _stepMode               = StepMode.Over;
        _nodeProcessedSinceStep = false;
        Coordinator.RequestStepOneTick();
    }

    protected override void OnStepIntoImpl()
    {
        _stepFromStackDepth     = _currentSnapshot?.StackPointer ?? 0;
        _stepMode               = StepMode.Into;
        _nodeProcessedSinceStep = false;
        Coordinator.RequestStepOneTick();
    }

    protected override void OnStepOutImpl()
    {
        _stepFromStackDepth     = _currentSnapshot?.StackPointer ?? 0;
        _stepMode               = StepMode.Out;
        _nodeProcessedSinceStep = false;
        Coordinator.RequestStepOneTick();
    }

    protected override void OnDetachImpl()
    {
        _stepMode               = StepMode.None;
        _nodeProcessedSinceStep = false;
        _currentSnapshot        = null;
        _lastReadPos            = 0;
        _nodeHistory.Clear();
        _asyncHistory.Clear();
        _aggregateCounters.Clear();
        _heatmapModeActive      = false;
    }
}
