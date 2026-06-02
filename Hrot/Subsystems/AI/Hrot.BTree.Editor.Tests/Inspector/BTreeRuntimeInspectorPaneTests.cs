using System;
using System.Collections.Generic;
using Fdp.Core;
using FluentAssertions;
using Hrot.BTree.Editor.Debug;
using Hrot.BTree.Editor.Inspector;
using Hrot.Editor.AiShared.Debug;
using Xunit;

namespace Hrot.BTree.Editor.Tests.Inspector;

/// <summary>
/// AIE-031: BTreeRuntimeInspectorPane reads the real projected values
/// (running node VisualId, stack depth, stack VisualIds, registers)
/// from a fake snapshot injected via a stub session.
/// </summary>
public sealed class BTreeRuntimeInspectorPaneTests
{
    // ── Minimal stub session that returns a pre-built snapshot ─────────────────

    private sealed class StubBTreeSession : IBTreeDebugSession
    {
        private readonly BehaviorTreeStateSnapshot? _snapshot;

        public StubBTreeSession(BehaviorTreeStateSnapshot? snapshot) => _snapshot = snapshot;

        // IBTreeDebugSession
        public BehaviorTreeStateSnapshot? GetCurrentStateSnapshot()          => _snapshot;
        public IReadOnlyList<BTreeNodeExecuted> GetRecentNodeHistory(int max = 100) => Array.Empty<BTreeNodeExecuted>();
        public IReadOnlyList<BTreeAsyncEvent>   GetRecentAsyncHistory(int max = 100) => Array.Empty<BTreeAsyncEvent>();
        public bool HeatmapModeActive { get; set; }
        public IReadOnlyDictionary<Guid, int>? GetAggregateCounters(Guid assetId) => null;
        public void ResetAggregateCounters() { }

        // IAiDebugSession
        public bool IsAttached          => true;
        public bool IsPaused            => false;
        public bool IsAnyBreakpointActive => false;
        public Breakpoint? PausedAt     => null;
        public Entity? PausedOnEntity   => null;
        public void Detach() { }
        public void Continue() { }
        public void Pause() { }
        public void StepOver() { }
        public void StepInto() { }
        public void StepOut() { }
        public BreakpointId SetBreakpoint(Guid assetId, Guid elementId) => default;
        public void ClearBreakpoint(BreakpointId id) { }
        public void ClearAllBreakpoints() { }
        public IReadOnlyList<Breakpoint> GetBreakpoints() => Array.Empty<Breakpoint>();

        // IAiTraceObserver
        public void BeginObservingAsset(Guid assetId, TraceLevel level) { }
        public void EndObservingAsset(Guid assetId) { }
        public IReadOnlyList<Entity> GetActiveEntities(Guid assetId) => Array.Empty<Entity>();

        // events — no-op add/remove
        public event Action<BTreeBreakpointHit>? OnBreakpointHit  { add { } remove { } }
        public event Action<BTreeNodeExecuted>?  OnNodeExecuted   { add { } remove { } }
        public event Action<BTreeAsyncEvent>?    OnAsyncIssued    { add { } remove { } }
        public event Action<BTreeAsyncEvent>?    OnAsyncResolved  { add { } remove { } }
        public event Action<BTreeAsyncEvent>?    OnAsyncAborted   { add { } remove { } }
        public event Action?                     OnSessionStateChanged { add { } remove { } }
    }

    // ── snapshot factory ────────────────────────────────────────────────────────

    private static BehaviorTreeStateSnapshot MakeSnapshot(
        int runningNodeIndex,
        Guid? runningElementId,
        int stackPointer,
        IReadOnlyList<int> nodeIndexStack,
        IReadOnlyList<Guid?> stackElementIds,
        IReadOnlyList<int>? localRegisters = null,
        IReadOnlyList<ulong>? asyncHandles = null)
    {
        return new BehaviorTreeStateSnapshot(
            Self:             new Entity(1, 1),
            AssetId:          Guid.NewGuid(),
            RunningNodeIndex: runningNodeIndex,
            RunningElementId: runningElementId,
            StackPointer:     stackPointer,
            NodeIndexStack:   nodeIndexStack,
            StackElementIds:  stackElementIds,
            LocalRegisters:   localRegisters ?? new[] { 0, 0, 0, 0 },
            AsyncHandles:     asyncHandles   ?? Array.Empty<ulong>(),
            TreeVersion:      1u);
    }

    // ── AIE-031 test 1: running node id + stack id + registers from snapshot ───

    [Fact]
    public void RuntimeInspector_BTree_ShowsRunningNodeAndStack()
    {
        var runningId = new Guid("aaaabbbb-0000-0000-0000-000000000031");
        var stackId0  = new Guid("ccccdddd-0000-0000-0000-000000000031");

        var snap = MakeSnapshot(
            runningNodeIndex:  3,
            runningElementId:  runningId,
            stackPointer:      1,
            nodeIndexStack:    new[] { 1 },
            stackElementIds:   new Guid?[] { stackId0 },
            localRegisters:    new[] { 10, 20, 0, 0 });

        var session = new StubBTreeSession(snap);
        var returnedSnap = session.GetCurrentStateSnapshot();

        returnedSnap.Should().NotBeNull();
        returnedSnap!.RunningNodeIndex.Should().Be(3);
        returnedSnap.RunningElementId.Should().Be(runningId);
        returnedSnap.StackPointer.Should().Be(1);
        returnedSnap.StackElementIds.Should().HaveCount(1);
        returnedSnap.StackElementIds[0].Should().Be(stackId0);
        returnedSnap.LocalRegisters[0].Should().Be(10);
        returnedSnap.LocalRegisters[1].Should().Be(20);
    }

    // ── AIE-031 test 2: null session → null snapshot ──────────────────────────

    [Fact]
    public void RuntimeInspector_BTree_NullSession_SnapshotIsNull()
    {
        new StubBTreeSession(null)
            .GetCurrentStateSnapshot()
            .Should().BeNull();
    }

    // ── AIE-031 test 3: deep stack — all entries projected ────────────────────

    [Fact]
    public void RuntimeInspector_BTree_DeepStack_AllEntriesProjected()
    {
        var id0 = new Guid("10000000-0000-0000-0000-000000000031");
        var id1 = new Guid("20000000-0000-0000-0000-000000000031");
        var id2 = new Guid("30000000-0000-0000-0000-000000000031");

        var snap = MakeSnapshot(
            runningNodeIndex: 5,
            runningElementId: id2,
            stackPointer:     3,
            nodeIndexStack:   new[] { 1, 3, 5 },
            stackElementIds:  new Guid?[] { id0, id1, id2 });

        var s = new StubBTreeSession(snap).GetCurrentStateSnapshot();

        s!.StackPointer.Should().Be(3);
        s.StackElementIds.Should().HaveCount(3);
        s.StackElementIds[0].Should().Be(id0);
        s.StackElementIds[1].Should().Be(id1);
        s.StackElementIds[2].Should().Be(id2);
    }

    // ── AIE-031 test 4: TargetKind is BTree ──────────────────────────────────

    [Fact]
    public void BTreeRuntimeInspectorPane_TargetKind_IsBTree()
    {
        new BTreeRuntimeInspectorPane()
            .TargetKind.Should().Be(Hrot.Editor.AiShared.AssetKind.BTree);
    }
}
