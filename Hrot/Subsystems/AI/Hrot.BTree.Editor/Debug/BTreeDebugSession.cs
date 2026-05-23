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

    public event Action<BTreeBreakpointHit>? OnBreakpointHit;
    public event Action<BTreeNodeExecuted>?  OnNodeExecuted;
    public event Action<BTreeAsyncEvent>?    OnAsyncIssued;
    public event Action<BTreeAsyncEvent>?    OnAsyncResolved;
    public event Action<BTreeAsyncEvent>?    OnAsyncAborted;

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

            _currentSnapshot = new BehaviorTreeStateSnapshot(
                entity, Guid.Empty, runningNodeIndex, null,
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
                    RecordNodeExecuted(new BTreeNodeExecuted(
                        entity, Guid.Empty, Guid.Empty,
                        rec->Status, 0f, rec->Timestamp));
                    break;
                case BTreeTraceOpCode.WaitStarted:
                    RecordAsyncEvent(new BTreeAsyncEvent(
                        entity, Guid.Empty, Guid.Empty,
                        rec->NodeIndex, 0u, BTreeAsyncPhase.Issued, 0f));
                    break;
                case BTreeTraceOpCode.WaitCompleted:
                    RecordAsyncEvent(new BTreeAsyncEvent(
                        entity, Guid.Empty, Guid.Empty,
                        rec->NodeIndex, 0u, BTreeAsyncPhase.Resolved, 0f));
                    break;
            }
            pos = (ushort)((pos + BTreeTraceWorkingMemory1024.RecordStride)
                           % BTreeTraceWorkingMemory1024.PayloadBytes);
        }
        _lastReadPos = trace.WritePos;
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

    // ---- AiDebugSessionBase overrides (no-ops until kernel wiring) ---------

    protected override void OnContinueImpl()   { }
    protected override void OnPauseImpl()      { }
    protected override void OnStepOverImpl()   { }
    protected override void OnStepIntoImpl()   { }
    protected override void OnStepOutImpl()    { }

    protected override void OnDetachImpl()
    {
        _currentSnapshot   = null;
        _lastReadPos       = 0;
        _nodeHistory.Clear();
        _asyncHistory.Clear();
        _aggregateCounters.Clear();
        _heatmapModeActive = false;
    }
}
