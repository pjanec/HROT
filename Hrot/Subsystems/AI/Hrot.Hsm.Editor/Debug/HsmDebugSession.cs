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

    // BPF-023: metadata used to symbolicate ActiveLeafIds -> StableIds.
    private MachineMetadata? _metadata;
    private Guid             _metadataAssetId;

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

    // BPF-023: store metadata so Update() can symbolicate ActiveLeafIds.
    public void SetMetadata(Guid assetId, MachineMetadata? metadata)
    {
        _metadataAssetId = assetId;
        _metadata        = metadata;
    }

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
                entity, _metadataAssetId,
                DecodeLeaves64(comp.State, 2),
                DecodeEventQueue64(comp.State),
                DecodeTimerSlots64(comp.State),
                DecodeHistorySlots64(comp.State),
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
                entity, _metadataAssetId,
                DecodeLeaves128(comp.State, 4),
                DecodeEventQueue128(comp.State),
                DecodeTimerSlots128(comp.State),
                DecodeHistorySlots128(comp.State),
                comp.State.Header.Phase,
                comp.State.Header.MicroStep,
                0,
                comp.State.Header.Flags,
                comp.State.Header.RngState,
                comp.State.Header.Generation);
        }
        _currentSnapshot = snap;

        // BPF-024: StepOut/StepOver depend only on snapshot phase/microstep, not on trace.
        // Evaluate them here, before the trace-guard return.
        if (_stepMode is StepMode.Over or StepMode.Out && _currentSnapshot is not null)
        {
            bool shouldPause = _stepMode switch
            {
                StepMode.Over => _currentSnapshot.MicroStep != _stepFromMicroStep,
                // BPF-024: StepOut waits until the instance re-enters Activity phase.
                StepMode.Out  => _currentSnapshot.Phase == InstancePhase.Activity,
                _             => false
            };
            if (shouldPause)
            {
                _stepMode = StepMode.None;
                Coordinator.RequestPause();
            }
        }

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

        // StepInto pause: requires a trace event (node entry or transition) to have occurred.
        if (_stepMode == StepMode.Into && _nodeProcessedSinceStep)
        {
            _stepMode = StepMode.None;
            Coordinator.RequestPause();
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

    // ---- BPF-023: active-leaf decode helpers -----------------------------

    private unsafe IReadOnlyList<Guid> DecodeLeaves64(HsmInstance64 state, int slotCount)
    {
        var result = new List<Guid>(slotCount);
        for (int i = 0; i < slotCount; i++)
        {
            ushort id = state.ActiveLeafIds[i];
            if (id == 0xFFFF) continue;
            if (_metadata != null && _metadata.StateStableIds.TryGetValue(id, out var sid))
                result.Add(sid);
        }
        return result;
    }

    private unsafe IReadOnlyList<Guid> DecodeLeaves128(HsmInstance128 state, int slotCount)
    {
        var result = new List<Guid>(slotCount);
        for (int i = 0; i < slotCount; i++)
        {
            ushort id = state.ActiveLeafIds[i];
            if (id == 0xFFFF) continue;
            if (_metadata != null && _metadata.StateStableIds.TryGetValue(id, out var sid))
                result.Add(sid);
        }
        return result;
    }

    // ---- BPF-010: event-queue, timer-slot and history-slot decode helpers ----

    private unsafe IReadOnlyList<HsmEventQueueEntry> DecodeEventQueue64(HsmInstance64 state)
    {
        int count = state.EventCount;
        if (count <= 0) return Array.Empty<HsmEventQueueEntry>();

        var result = new List<HsmEventQueueEntry>(count);
        // HsmInstance64 uses a single shared queue; EventBuffer holds up to 1 event (24 bytes).
        // Clamp to the capacity of one event.
        int actual = Math.Min(count, 1);
        for (int i = 0; i < actual; i++)
        {
            var ev = (HsmEvent*)(state.EventBuffer + i * sizeof(HsmEvent));
            string name = _metadata != null
                ? _metadata.GetEventName(ev->EventId)
                : ev->EventId.ToString();
            result.Add(new HsmEventQueueEntry(ev->EventId, name, ev->Flags, ev->Priority, i));
        }
        return result;
    }

    private unsafe IReadOnlyList<HsmEventQueueEntry> DecodeEventQueue128(HsmInstance128 state)
    {
        int count = state.InterruptSlotUsed + state.EventCount;
        if (count <= 0) return Array.Empty<HsmEventQueueEntry>();

        var result = new List<HsmEventQueueEntry>(count);
        // EventBuffer layout: [0-23] interrupt slot, [24-67] shared ring (up to 2 events).
        int pos = 0;
        if (state.InterruptSlotUsed != 0)
        {
            var ev = (HsmEvent*)(state.EventBuffer);
            string name = _metadata != null
                ? _metadata.GetEventName(ev->EventId)
                : ev->EventId.ToString();
            result.Add(new HsmEventQueueEntry(ev->EventId, name, ev->Flags, ev->Priority, pos));
            pos++;
        }
        int ringCount = Math.Min((int)state.EventCount, 2);
        for (int i = 0; i < ringCount; i++)
        {
            var ev = (HsmEvent*)(state.EventBuffer + 24 + i * sizeof(HsmEvent));
            string name = _metadata != null
                ? _metadata.GetEventName(ev->EventId)
                : ev->EventId.ToString();
            result.Add(new HsmEventQueueEntry(ev->EventId, name, ev->Flags, ev->Priority, pos));
            pos++;
        }
        return result;
    }

    private unsafe IReadOnlyList<HsmTimerSlot> DecodeTimerSlots64(HsmInstance64 state)
    {
        var result = new List<HsmTimerSlot>(2);
        for (int i = 0; i < 2; i++)
        {
            uint deadline = state.TimerDeadlines[i];
            if (deadline == 0) continue;
            result.Add(new HsmTimerSlot(i, OwningStateStableId: null, RemainingTicks: (float)deadline));
        }
        return result;
    }

    private unsafe IReadOnlyList<HsmTimerSlot> DecodeTimerSlots128(HsmInstance128 state)
    {
        var result = new List<HsmTimerSlot>(4);
        for (int i = 0; i < 4; i++)
        {
            uint deadline = state.TimerDeadlines[i];
            if (deadline == 0) continue;
            result.Add(new HsmTimerSlot(i, OwningStateStableId: null, RemainingTicks: (float)deadline));
        }
        return result;
    }

    private unsafe IReadOnlyList<HsmHistorySlot> DecodeHistorySlots64(HsmInstance64 state)
    {
        var result = new List<HsmHistorySlot>(2);
        for (int i = 0; i < 2; i++)
        {
            ushort childId = state.HistorySlots[i];
            if (childId == 0xFFFF) continue;
            Guid? childSid = (_metadata != null && _metadata.StateStableIds.TryGetValue(childId, out var sg))
                ? sg : (Guid?)null;
            result.Add(new HsmHistorySlot(i, OwningCompositeStableId: null, childSid, IsDeepHistory: false));
        }
        return result;
    }

    private unsafe IReadOnlyList<HsmHistorySlot> DecodeHistorySlots128(HsmInstance128 state)
    {
        var result = new List<HsmHistorySlot>(8);
        for (int i = 0; i < 8; i++)
        {
            ushort childId = state.HistorySlots[i];
            if (childId == 0xFFFF) continue;
            Guid? childSid = (_metadata != null && _metadata.StateStableIds.TryGetValue(childId, out var sg))
                ? sg : (Guid?)null;
            result.Add(new HsmHistorySlot(i, OwningCompositeStableId: null, childSid, IsDeepHistory: false));
        }
        return result;
    }
}
