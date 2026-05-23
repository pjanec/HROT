using Fdp.Core;
using Fbt;
using Hrot.Editor.AiShared.Debug;

namespace Hrot.BTree.Editor.Debug;

/// <summary>Phase of a BTree asynchronous token (channel).</summary>
public enum BTreeAsyncPhase { Issued, Resolved, Aborted }

/// <summary>
/// Immutable snapshot of a single BTree entity's runtime state at one point in time.
/// All fields are symbolicated (VisualId-based) where available.
/// </summary>
public sealed record BehaviorTreeStateSnapshot(
    Entity Self,
    Guid AssetId,
    /// <summary>Raw blob index of the currently executing node (-1 if idle).</summary>
    int RunningNodeIndex,
    /// <summary>Symbolicated VisualId of the running node; null if debug metadata unavailable.</summary>
    Guid? RunningElementId,
    int StackPointer,
    IReadOnlyList<int> NodeIndexStack,
    IReadOnlyList<Guid?> StackElementIds,
    IReadOnlyList<int> LocalRegisters,
    IReadOnlyList<ulong> AsyncHandles,
    uint TreeVersion);

/// <summary>Record of a single node execution event emitted by the kernel tracer.</summary>
public sealed record BTreeNodeExecuted(
    Entity Self,
    Guid AssetId,
    Guid NodeVisualId,
    NodeStatus Status,
    float SimulationTime,
    uint Tick);

/// <summary>Record of an asynchronous token lifecycle event.</summary>
public sealed record BTreeAsyncEvent(
    Entity Self,
    Guid AssetId,
    Guid NodeVisualId,
    int RequestId,
    uint TreeVersion,
    BTreeAsyncPhase Phase,
    float SimulationTime);

/// <summary>Raised when a breakpoint fires during BTree execution.</summary>
public sealed record BTreeBreakpointHit(
    Breakpoint Breakpoint,
    Entity Self,
    /// <summary>null when break-on-enter; populated when break-on-result.</summary>
    NodeStatus? StatusAtHit,
    float SimulationTime);
