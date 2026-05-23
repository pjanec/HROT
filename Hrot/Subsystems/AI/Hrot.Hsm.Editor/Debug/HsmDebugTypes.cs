using Fdp.Core;
using Fhsm.Kernel.Data;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.Hsm.Editor.Debug;

// ---------------------------------------------------------------------------
// Snapshot types
// ---------------------------------------------------------------------------

/// <summary>Immutable point-in-time snapshot of one HSM instance's kernel state.</summary>
public sealed record HsmInstanceSnapshot(
    Entity Self,
    Guid AssetId,
    /// <summary>Symbolicated StableIds of all currently active leaf states.</summary>
    IReadOnlyList<Guid> ActiveLeafStableIds,
    IReadOnlyList<HsmEventQueueEntry> EventQueue,
    IReadOnlyList<HsmTimerSlot> TimerSlots,
    IReadOnlyList<HsmHistorySlot> HistorySlots,
    InstancePhase Phase,
    byte MicroStep,
    byte ConsecutiveClamps,
    InstanceFlags Flags,
    uint RngState,
    ushort Generation);

/// <summary>One entry in the pending event queue of an HSM instance.</summary>
public sealed record HsmEventQueueEntry(
    ushort EventId,
    string EventName,
    EventFlags Flags,
    EventPriority Priority,
    int QueuePosition);

/// <summary>State of one timer slot (owner state + remaining ticks).</summary>
public sealed record HsmTimerSlot(
    int SlotIndex,
    Guid? OwningStateStableId,
    float RemainingTicks);

/// <summary>Recorded history state in one history slot.</summary>
public sealed record HsmHistorySlot(
    int SlotIndex,
    Guid? OwningCompositeStableId,
    Guid? RecordedChildStableId,
    bool IsDeepHistory);

// ---------------------------------------------------------------------------
// Trace record hierarchy
// ---------------------------------------------------------------------------

/// <summary>Base for all HSM kernel trace records stored in the session history ring.</summary>
public abstract record HsmTraceRecord(float SimulationTime);

/// <summary>A state-entered event emitted by the kernel tracer.</summary>
public sealed record HsmStateEntered(
    Entity Self,
    Guid AssetId,
    Guid StateStableId,
    float SimulationTime) : HsmTraceRecord(SimulationTime);

/// <summary>A state-exited event emitted by the kernel tracer.</summary>
public sealed record HsmStateExited(
    Entity Self,
    Guid AssetId,
    Guid StateStableId,
    float SimulationTime) : HsmTraceRecord(SimulationTime);

/// <summary>A transition-fired event emitted by the kernel tracer.</summary>
public sealed record HsmTransitionFired(
    Entity Self,
    Guid AssetId,
    Guid TransitionVisualId,
    Guid SourceStateStableId,
    Guid TargetStateStableId,
    ushort EventId,
    bool GuardResult,
    ushort SyncGroupId,
    float SimulationTime) : HsmTraceRecord(SimulationTime);

/// <summary>An event-enqueued notification from the kernel tracer.</summary>
public sealed record HsmEventQueued(
    Entity Self,
    Guid AssetId,
    ushort EventId,
    string EventName,
    EventPriority Priority,
    float SimulationTime) : HsmTraceRecord(SimulationTime);

/// <summary>A command-lane conflict detected between two parallel states.</summary>
public sealed record HsmRegionConflict(
    Entity Self,
    Guid AssetId,
    Guid StateAStableId,
    Guid StateBStableId,
    byte ConflictingLane,
    float SimulationTime) : HsmTraceRecord(SimulationTime);

/// <summary>A guard function evaluation result.</summary>
public sealed record HsmGuardEvaluated(
    Entity Self,
    Guid AssetId,
    Guid TransitionVisualId,
    string GuardFunctionName,
    bool Result,
    float SimulationTime) : HsmTraceRecord(SimulationTime);

/// <summary>A timer-slot fired or was set.</summary>
public sealed record HsmTimerEvent(
    Entity Self,
    Guid AssetId,
    int SlotIndex,
    Guid? OwningStateStableId,
    float SimulationTime) : HsmTraceRecord(SimulationTime);

// ---------------------------------------------------------------------------
// Breakpoint hit notification (separate from the trace ring)
// ---------------------------------------------------------------------------

/// <summary>Raised when a state/transition breakpoint fires during HSM execution.</summary>
public sealed record HsmBreakpointHit(
    Breakpoint Breakpoint,
    Entity Self,
    /// <summary>Populated when the breakpoint is on a state; null for transition breakpoints.</summary>
    Guid? StateStableId,
    /// <summary>Populated when the breakpoint is on a transition; null for state breakpoints.</summary>
    Guid? TransitionVisualId,
    float SimulationTime);
