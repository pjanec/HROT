using System.Runtime.CompilerServices;
using Fdp.Core;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Hsm.Editor.Debug;

/// <summary>
/// Production implementation of IHsmDebugSession.
/// Maintains a unified in-memory ring buffer of HSM trace records.
/// GetCurrentStateSnapshot() returns null until kernel wiring is in place (Slice 3+).
/// Step-control methods are no-ops until the kernel adapter is wired.
/// </summary>
public sealed class HsmDebugSession : AiDebugSessionBase, IHsmDebugSession
{
    private const int MaxHistory = 200;

    private readonly List<HsmTraceRecord> _history = new();
    private bool _heatmapModeActive;
    private readonly Dictionary<Guid, int> _stateEntryCounts = new();

    private HsmInstanceSnapshot? _currentSnapshot;
    private ushort _lastReadPos;

    private enum StepMode { None, Over, Into, Out }
    private StepMode _stepMode = StepMode.None;
    private byte _stepFromMicroStep;
    private bool _nodeProcessedSinceStep;

    public event Action<HsmBreakpointHit>?  OnBreakpointHit;
    public event Action<HsmStateEntered>?   OnStateEntered;
    public event Action<HsmStateExited>?    OnStateExited;
    public event Action<HsmTransitionFired>? OnTransitionFired;
    public event Action<HsmEventQueued>?    OnEventQueued;
    public event Action<HsmRegionConflict>? OnRegionConflict;
    public event Action<HsmGuardEvaluated>? OnGuardEvaluated;
    public event Action<HsmTimerEvent>?     OnTimerEvent;

    public HsmDebugSession(AiTracerCoordinator? coordinator = null) : base(coordinator) { }

    // ---- IHsmDebugSession ------------------------------------------------

    public HsmInstanceSnapshot? GetCurrentStateSnapshot() => _currentSnapshot;

    public IReadOnlyList<HsmTraceRecord> GetRecentTraceHistory(int max = 100)
    {
        int start = Math.Max(0, _history.Count - max);
        return _history.GetRange(start, _history.Count - start);
    }

    public bool HeatmapModeActive
    {
        get => _heatmapModeActive;
        set => _heatmapModeActive = value;
    }

    public IReadOnlyDictionary<Guid, int>? GetStateEntryCounts(Guid assetId)
    {
        if (!IsAttached || !HeatmapModeActive)
            return null;
        return _stateEntryCounts;
    }

    public void ResetStateEntryCounts() => _stateEntryCounts.Clear();

    // ---- ECS snapshot + trace polling (called once per frame) -------------

    /// <summary>
    /// Reads the current HSM instance snapshot from the entity and polls
    /// any pending trace records from HsmTraceWorkingMemory1024.
    /// </summary>
    public unsafe void Update(EntityRepository repo, Entity entity)
    {
        // === Snapshot ===
        HsmInstanceSnapshot? snap = null;

        if (repo.HasComponent<BrainHsm64>(entity))
        {
            ref readonly var comp = ref repo.GetComponentRO<BrainHsm64>(entity);
            snap = new HsmInstanceSnapshot(
                entity, Guid.Empty,
                Array.Empty<Guid>(),
                Array.Empty<HsmEventQueueEntry>(),
                Array.Empty<HsmTimerSlot>(),
                Array.Empty<HsmHistorySlot>(),
                comp.State.Header.Phase,
                comp.State.Header.MicroStep,
                0,
                comp.State.Header.Flags,
                comp.State.Header.RngState,
                comp.State.Header.Generation);
        }
        else if (repo.HasComponent<BrainHsm128>(entity))
        {
            ref readonly var comp = ref repo.GetComponentRO<BrainHsm128>(entity);
            snap = new HsmInstanceSnapshot(
                entity, Guid.Empty,
                Array.Empty<Guid>(),
                Array.Empty<HsmEventQueueEntry>(),
                Array.Empty<HsmTimerSlot>(),
                Array.Empty<HsmHistorySlot>(),
                comp.State.Header.Phase,
                comp.State.Header.MicroStep,
                0,
                comp.State.Header.Flags,
                comp.State.Header.RngState,
                comp.State.Header.Generation);
        }
        _currentSnapshot = snap;

        // === Trace polling ===
        if (!repo.HasComponent<HsmTraceWorkingMemory1024>(entity))
            return;

        ref readonly var trace = ref repo.GetComponentRO<HsmTraceWorkingMemory1024>(entity);
        if (trace.WritePos == _lastReadPos)
            return;

        ref var traceMut = ref Unsafe.AsRef(in trace);
        HsmTraceWorkingMemory1024* tracePtr = (HsmTraceWorkingMemory1024*)Unsafe.AsPointer(ref traceMut);
        byte* bufBase = tracePtr->Buffer;
        ushort pos = _lastReadPos;
        while (pos != trace.WritePos)
        {
            var hdr = (TraceRecordHeader*)(bufBase + pos);
            switch (hdr->OpCode)
            {
                case TraceOpCode.StateEnter:
                    _nodeProcessedSinceStep = true;
                    RecordTrace(new HsmStateEntered(
                        entity, Guid.Empty, Guid.Empty, (float)hdr->Timestamp));
                    break;
                case TraceOpCode.StateExit:
                    RecordTrace(new HsmStateExited(
                        entity, Guid.Empty, Guid.Empty, (float)hdr->Timestamp));
                    break;
                case TraceOpCode.Transition:
                    _nodeProcessedSinceStep = true;
                    var trans = (TraceTransition*)(bufBase + pos);
                    RecordTrace(new HsmTransitionFired(
                        entity, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty,
                        trans->TriggerEventId, false, 0, (float)hdr->Timestamp));
                    break;
            }
            pos = (ushort)((pos + HsmTraceWorkingMemory1024.RecordStride)
                           % HsmTraceWorkingMemory1024.PayloadBytes);
        }
        _lastReadPos = trace.WritePos;

        // Step-mode auto-pause evaluation
        if (_stepMode != StepMode.None && _currentSnapshot is not null)
        {
            bool shouldPause = _stepMode switch
            {
                StepMode.Over => _currentSnapshot.MicroStep != _stepFromMicroStep,
                StepMode.Into => _nodeProcessedSinceStep,
                StepMode.Out  => _currentSnapshot.MicroStep != _stepFromMicroStep,
                _             => false
            };
            if (shouldPause)
            {
                _stepMode = StepMode.None;
                Coordinator.RequestPause();
            }
        }
    }

    // ---- Kernel adapter entry points (called by future kernel adapter) ---

    /// <summary>Records a kernel trace event and fires the appropriate typed event.</summary>
    public void RecordTrace(HsmTraceRecord record)
    {
        if (_history.Count >= MaxHistory)
            _history.RemoveAt(0);
        _history.Add(record);

        switch (record)
        {
            case HsmStateEntered    e:
                if (_heatmapModeActive)
                {
                    _stateEntryCounts.TryGetValue(e.StateStableId, out var prev);
                    _stateEntryCounts[e.StateStableId] = prev + 1;
                }
                OnStateEntered?.Invoke(e);
                break;
            case HsmStateExited     e: OnStateExited?.Invoke(e);     break;
            case HsmTransitionFired e: OnTransitionFired?.Invoke(e); break;
            case HsmEventQueued     e: OnEventQueued?.Invoke(e);     break;
            case HsmRegionConflict  e: OnRegionConflict?.Invoke(e);  break;
            case HsmGuardEvaluated  e: OnGuardEvaluated?.Invoke(e);  break;
            case HsmTimerEvent      e: OnTimerEvent?.Invoke(e);      break;
        }
    }

    /// <summary>Called by the kernel adapter when a breakpoint fires.</summary>
    public void RaiseBreakpointHit(HsmBreakpointHit hit)
    {
        IsPaused = true;
        PausedAt = hit.Breakpoint;
        PausedOnEntity = hit.Self;
        OnBreakpointHit?.Invoke(hit);
        RaiseSessionStateChanged();
    }

    // ---- AiDebugSessionBase overrides ------------------------------------

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
        _stepFromMicroStep      = _currentSnapshot?.MicroStep ?? 0;
        _stepMode               = StepMode.Over;
        _nodeProcessedSinceStep = false;
        Coordinator.RequestStepOneTick();
    }

    protected override void OnStepIntoImpl()
    {
        _stepFromMicroStep      = _currentSnapshot?.MicroStep ?? 0;
        _stepMode               = StepMode.Into;
        _nodeProcessedSinceStep = false;
        Coordinator.RequestStepOneTick();
    }

    protected override void OnStepOutImpl()
    {
        _stepFromMicroStep      = _currentSnapshot?.MicroStep ?? 0;
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
        _history.Clear();
        _stateEntryCounts.Clear();
        _heatmapModeActive      = false;
    }
}
