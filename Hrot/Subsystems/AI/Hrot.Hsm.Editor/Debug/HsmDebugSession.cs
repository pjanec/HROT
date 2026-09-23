using System.Runtime.CompilerServices;
using Fdp.Core;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior;
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

        // ⭐⭐⭐ O7c-④d (2026-09-23): THE DECODE IS SIZE-DRIVEN, AND THE SIZE COMES FROM THE SLOT.
        //   📄 DESIGN_Occurrence_Scoped_Storage.md §31.19.
        //
        //   ⛔ Before this, the snapshot read BrainHsm128 and therefore rendered the 128 tier and
        //     nothing else — a 64-byte machine was decoded as if it were 128 (reading 64 bytes past
        //     its own state), and a 256-byte machine showed 4 of its 8 regions. ⚠ Neither could
        //     actually happen while the COMPONENT was the store, because the component WAS 128 bytes
        //     for every machine. ⇒ the slot is what makes the other two tiers reachable, and it is
        //     the same change that makes decoding them mandatory rather than hypothetical.
        //
        //   ⭐⭐ The Decode*64 helpers, kept unused since O7c-①, get their consumer here — exactly the
        //     "unreferenced is not unintentional" call that spared them. The 256 siblings are new.
        if (RootHsmAccess.TryGetInstance(repo, entity, out byte* instance, out int instanceSize))
        {
            var header = (InstanceHeader*)instance;
            snap = new HsmInstanceSnapshot(
                entity, _metadataAssetId,
                DecodeLeaves(instance, instanceSize),
                DecodeEventQueue(instance, instanceSize),
                DecodeTimerSlots(instance, instanceSize),
                DecodeHistorySlots(instance, instanceSize),
                header->Phase,
                header->MicroStep,
                0,
                header->Flags,
                header->RngState,
                header->Generation);
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

    // ---- O7c-④d: size-driven dispatch ------------------------------------
    //
    // ⭐⭐ ONE arm per kernel tier, selected by the SLOT'S width. ⛔ An unrecognised width decodes to
    //   nothing rather than guessing: the tiers are the kernel's, and a size this table does not know
    //   is a kernel change, not a case to approximate. ⚠ A debug surface draws what is there.

    /// <summary>
    /// ⭐⭐⭐ <b>The leaves come from the KERNEL, not from a tier table repeated here.</b>
    /// <c>HsmKernel.GetActiveLeafIds</c> (the <c>ExtDeps</c> addition <c>O7c</c>-④b made, §31.16.1)
    /// returns both the pointer AND the region count for the size — 2, 4 or 8. ⇒ the one fact that
    /// would otherwise be duplicated in every size-driven reader stays in the one place that owns it.
    /// </summary>
    private unsafe IReadOnlyList<Guid> DecodeLeaves(byte* instance, int instanceSize)
    {
        ushort* leaves = HsmKernel.GetActiveLeafIds(instance, instanceSize, out int count);
        if (leaves == null || count <= 0) return Array.Empty<Guid>();

        var result = new List<Guid>(count);
        for (int i = 0; i < count; i++)
        {
            ushort id = leaves[i];
            if (id == 0xFFFF) continue;
            if (_metadata != null && _metadata.StateStableIds.TryGetValue(id, out var sid))
                result.Add(sid);
        }
        return result;
    }

    private unsafe IReadOnlyList<HsmEventQueueEntry> DecodeEventQueue(byte* instance, int instanceSize)
        => instanceSize switch
        {
            64  => DecodeEventQueue64(*(HsmInstance64*)instance),
            128 => DecodeEventQueue128(*(HsmInstance128*)instance),
            256 => DecodeEventQueue256(*(HsmInstance256*)instance),
            _   => Array.Empty<HsmEventQueueEntry>()
        };

    private unsafe IReadOnlyList<HsmTimerSlot> DecodeTimerSlots(byte* instance, int instanceSize)
        => instanceSize switch
        {
            64  => DecodeTimerSlots64(*(HsmInstance64*)instance),
            128 => DecodeTimerSlots128(*(HsmInstance128*)instance),
            256 => DecodeTimerSlots256(*(HsmInstance256*)instance),
            _   => Array.Empty<HsmTimerSlot>()
        };

    private unsafe IReadOnlyList<HsmHistorySlot> DecodeHistorySlots(byte* instance, int instanceSize)
        => instanceSize switch
        {
            64  => DecodeHistorySlots64(*(HsmInstance64*)instance),
            128 => DecodeHistorySlots128(*(HsmInstance128*)instance),
            256 => DecodeHistorySlots256(*(HsmInstance256*)instance),
            _   => Array.Empty<HsmHistorySlot>()
        };

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

    /// <summary>
    /// ⭐ The 256 tier's hybrid queue: one reserved interrupt slot then a shared ring of 5.
    /// ⚠ The ring capacity differs from the 128 tier's 2 — that is the whole reason a tier table
    /// cannot be shared between the two arms.
    /// </summary>
    private unsafe IReadOnlyList<HsmEventQueueEntry> DecodeEventQueue256(HsmInstance256 state)
    {
        int count = state.InterruptSlotUsed + state.EventCount;
        if (count <= 0) return Array.Empty<HsmEventQueueEntry>();

        var result = new List<HsmEventQueueEntry>(count);
        // EventBuffer layout: [0-23] interrupt slot, [24-155] shared ring (up to 5 events).
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
        int ringCount = Math.Min((int)state.EventCount, 5);
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

    private unsafe IReadOnlyList<HsmTimerSlot> DecodeTimerSlots256(HsmInstance256 state)
    {
        var result = new List<HsmTimerSlot>(8);
        for (int i = 0; i < 8; i++)
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

    private unsafe IReadOnlyList<HsmHistorySlot> DecodeHistorySlots256(HsmInstance256 state)
    {
        var result = new List<HsmHistorySlot>(16);
        for (int i = 0; i < 16; i++)
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
