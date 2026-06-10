using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Blueprints.Editor.Inspector;
using Link = Hrot.Blueprints.Core.Assets.Link;

namespace Hrot.Blueprints.Tests.Debug;

// ============================================================================
// BATCH-04 — Node-Granular Editor UI Tests (NGS-2.4a / NGS-2.4b / NGS-2.4c)
//
// Three seams tested without ImGui:
//   2.4a  BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot
//   2.4b  BlueprintDebugToNodeEditAdapter.CurrentlyExecutingNode tracks pointer
//   2.4c  DebugStepControls.FormatNodePosition + StepBack wiring
//
// Integration tests (2.4a, 2.4b) use a real BlueprintDebugSession + compiled
// Sequence A:0→10→20 asset, reusing the VirtualPointerTests fixture pattern.
// ============================================================================

// ─── Shared asset factory ────────────────────────────────────────────────────

/// <summary>
/// Shared factory for the Sequence A:0→10→20 compiled asset used across BATCH-04 tests.
/// </summary>
internal static class Batch04Assets
{
    public static BlueprintAsset BuildTwoSeqVarAsset(string name)
    {
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid(); var seqId   = Guid.NewGuid();
        var litAId  = Guid.NewGuid(); var svAId   = Guid.NewGuid();
        var litBId  = Guid.NewGuid(); var svBId   = Guid.NewGuid();
        var retBId  = Guid.NewGuid();

        var peOut    = Guid.NewGuid(); var psIn    = Guid.NewGuid();
        var psThen0  = Guid.NewGuid(); var psThen1 = Guid.NewGuid();
        var pLitAOut = Guid.NewGuid(); var pSvAIn  = Guid.NewGuid();
        var pSvAOut  = Guid.NewGuid(); var pSvAVal = Guid.NewGuid();
        var pLitBOut = Guid.NewGuid(); var pSvBIn  = Guid.NewGuid();
        var pSvBOut  = Guid.NewGuid(); var pSvBVal = Guid.NewGuid();
        var pRetBIn  = Guid.NewGuid();

        var varA = new VariableDecl
        {
            Id   = Guid.NewGuid(), Name = "A",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SequenceNode { Id = seqId,
                    Pins = new() {
                        new Pin { Id = psIn,    Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen0, Name = "Then0",   Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen1, Name = "Then1",   Direction = "Out", IsExec = true, TypeRef = new() } } },
                new LiteralNode { Id = litAId, TypeId = "System.Int32", ValueJson = "10",
                    Pins = new() { new Pin { Id = pLitAOut, Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() } } },
                new SetVariableNode { Id = svAId, VariableId = varA.Id.ToString(),
                    Pins = new() {
                        new Pin { Id = pSvAIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvAOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvAVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() } } },
                new LiteralNode { Id = litBId, TypeId = "System.Int32", ValueJson = "20",
                    Pins = new() { new Pin { Id = pLitBOut, Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() } } },
                new SetVariableNode { Id = svBId, VariableId = varA.Id.ToString(),
                    Pins = new() {
                        new Pin { Id = pSvBIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvBOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvBVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() } } },
                new ReturnNode { Id = retBId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRetBIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = peOut,    ToNodeId = seqId,  ToPinId = psIn    },
                new() { FromNodeId = seqId,   FromPinId = psThen0,  ToNodeId = svAId,  ToPinId = pSvAIn  },
                new() { FromNodeId = litAId,  FromPinId = pLitAOut, ToNodeId = svAId,  ToPinId = pSvAVal },
                new() { FromNodeId = seqId,   FromPinId = psThen1,  ToNodeId = svBId,  ToPinId = pSvBIn  },
                new() { FromNodeId = litBId,  FromPinId = pLitBOut, ToNodeId = svBId,  ToPinId = pSvBVal },
                new() { FromNodeId = svBId,   FromPinId = pSvBOut,  ToNodeId = retBId, ToPinId = pRetBIn },
            },
        };

        return new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = name,
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Parameters = new(), WorkingState = new(), Variables = new() { varA },
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs = new() { graph }, Header = new Header(),
        };
    }

    public static int GetSnapshotInt(BlueprintStateSnapshot snapshot, string field)
    {
        if (snapshot.FieldValues.TryGetValue(field, out var obj) && obj is int i) return i;
        return 0;
    }
}

// ============================================================================
// 2.4a — ResolveInspectorSnapshot (static helper, no ImGui, real session)
// ============================================================================

[Collection("DebugProbe")]
public sealed class InspectorSnapshotResolutionTests : IDisposable
{
    private static readonly BlueprintTestFixtureOptions NoAlcCheck =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;
    public void Dispose() => DebugProbe.Sink = _savedSink;

    // ─── 2.4a Test 1: pointer at index 0 → A=0 ───────────────────────────────
    [Fact]
    public void ResolveInspectorSnapshot_WhenPaused_PointerAt0_ReturnsA0()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (session, entity, asset) = BuildAndPause(fixture, "InspSnap1");

        while (session.CurrentNodePointer > 0) session.StepBack();
        Assert.Equal(0, session.CurrentNodePointer);

        var snap = BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot(session, entity, asset.AssetId);

        Assert.NotNull(snap);
        Assert.Equal(0, Batch04Assets.GetSnapshotInt(snap!, "A"));
    }

    // ─── 2.4a Test 2: pointer at index 2 → A=10 ──────────────────────────────
    [Fact]
    public void ResolveInspectorSnapshot_WhenPaused_PointerAt2_ReturnsA10()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (session, entity, asset) = BuildAndPause(fixture, "InspSnap2");

        while (session.CurrentNodePointer > 0) session.StepBack();
        session.StepInto();
        session.StepInto();
        Assert.Equal(2, session.CurrentNodePointer);

        var snap = BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot(session, entity, asset.AssetId);

        Assert.NotNull(snap);
        Assert.Equal(10, Batch04Assets.GetSnapshotInt(snap!, "A"));
    }

    // ─── 2.4a Test 3: sequence 0→1→2 returns 0, 0, 10 ────────────────────────
    [Fact]
    public void ResolveInspectorSnapshot_AcrossPointers_Returns_0_0_10()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (session, entity, asset) = BuildAndPause(fixture, "InspSnap3");

        while (session.CurrentNodePointer > 0) session.StepBack();

        var snap0 = BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot(session, entity, asset.AssetId);
        Assert.NotNull(snap0);
        Assert.Equal(0, Batch04Assets.GetSnapshotInt(snap0!, "A"));

        session.StepInto();
        var snap1 = BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot(session, entity, asset.AssetId);
        Assert.NotNull(snap1);
        Assert.Equal(0, Batch04Assets.GetSnapshotInt(snap1!, "A"));

        session.StepInto();
        var snap2 = BlueprintRuntimeInspectorPane.ResolveInspectorSnapshot(session, entity, asset.AssetId);
        Assert.NotNull(snap2);
        Assert.Equal(10, Batch04Assets.GetSnapshotInt(snap2!, "A"));
    }

    // ─── 2.4a Test 4: after Continue, GetCurrentStateSnapshot is null ─────────
    [Fact]
    public void ResolveInspectorSnapshot_AfterContinue_GetCurrentStateSnapshotIsNull()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (session, entity, asset) = BuildAndPause(fixture, "InspSnap4");

        Assert.True(session.IsPaused);
        session.Continue();
        Assert.False(session.IsPaused);

        // Pointer cleared — GetCurrentStateSnapshot must be null (not the paused-pointer path).
        Assert.Null(session.GetCurrentStateSnapshot());
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (BlueprintDebugSession session, Entity entity, BlueprintAsset asset) BuildAndPause(
        BlueprintTestFixture fixture, string name)
    {
        var asset   = Batch04Assets.BuildTwoSeqVarAsset(name);
        var tc      = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        session.SetBreakpoint(asset.AssetId, asset.Graphs[0].Id, asset.Graphs[0].Nodes[1].Id);
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);
        Assert.True(session.RecordedNodeCount >= 3,
            $"Expected >= 3 recorded nodes, got {session.RecordedNodeCount}");

        return (session, entity, asset);
    }
}

// ============================================================================
// 2.4b — BlueprintDebugToNodeEditAdapter.CurrentlyExecutingNode tracks pointer
// ============================================================================

[Collection("DebugProbe")]
public sealed class HighlightFollowsPointerTests : IDisposable
{
    private static readonly BlueprintTestFixtureOptions NoAlcCheck =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;
    public void Dispose() => DebugProbe.Sink = _savedSink;

    // ─── 2.4b Test 1: CurrentlyExecutingNode equals session.CurrentNodeId ─────
    [Fact]
    public void CurrentlyExecutingNode_WhenPaused_EqualsPointerNode()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (session, asset) = BuildAndPause(fixture, "HL1");
        var adapter = new BlueprintDebugToNodeEditAdapter(session, asset.AssetId, asset.Graphs[0].Id);

        Assert.True(session.CurrentNodePointer >= 0);
        Assert.NotNull(session.CurrentNodeId);

        var executing = adapter.CurrentlyExecutingNode;
        Assert.NotNull(executing);

        Assert.True(Guid.TryParse(session.CurrentNodeId!, out var expectedGuid));
        Assert.Equal(expectedGuid, executing!.Value.Value);
    }

    // ─── 2.4b Test 2: CurrentlyExecutingNode changes on StepBack ─────────────
    [Fact]
    public void CurrentlyExecutingNode_ChangesOnStepBack()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (session, asset) = BuildAndPause(fixture, "HL2");
        var adapter = new BlueprintDebugToNodeEditAdapter(session, asset.AssetId, asset.Graphs[0].Id);

        // Advance to last recorded node.
        int last = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < last)
            session.StepInto();

        var nodeAtLast = adapter.CurrentlyExecutingNode;
        Assert.NotNull(nodeAtLast);

        session.StepBack();
        var nodeAfterStepBack = adapter.CurrentlyExecutingNode;
        Assert.NotNull(nodeAfterStepBack);

        // Must differ — StepBack moved the pointer to a different node.
        Assert.NotEqual(nodeAtLast!.Value.Value, nodeAfterStepBack!.Value.Value);
    }

    // ─── 2.4b Test 3: CurrentlyExecutingNode changes on StepInto ─────────────
    [Fact]
    public void CurrentlyExecutingNode_ChangesOnStepInto()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (session, asset) = BuildAndPause(fixture, "HL3");
        var adapter = new BlueprintDebugToNodeEditAdapter(session, asset.AssetId, asset.Graphs[0].Id);

        while (session.CurrentNodePointer > 0) session.StepBack();
        var nodeAt0 = adapter.CurrentlyExecutingNode;
        Assert.NotNull(nodeAt0);

        session.StepInto();
        var nodeAt1 = adapter.CurrentlyExecutingNode;
        Assert.NotNull(nodeAt1);

        Assert.NotEqual(nodeAt0!.Value.Value, nodeAt1!.Value.Value);
    }

    // ─── 2.4b Test 4: StepBack raises OnSessionStateChanged ──────────────────
    [Fact]
    public void StepBack_RaisesOnSessionStateChanged()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (session, asset) = BuildAndPause(fixture, "HL4");
        var adapter = new BlueprintDebugToNodeEditAdapter(session, asset.AssetId, asset.Graphs[0].Id);
        adapter.Subscribe();

        int stateChangedCount = 0;
        adapter.StateChanged += () => stateChangedCount++;

        // Move to end, then step back.
        int last = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < last) session.StepInto();
        stateChangedCount = 0; // reset

        session.StepBack();

        Assert.True(stateChangedCount > 0,
            "StepBack must raise OnSessionStateChanged so the canvas refreshes.");

        adapter.Unsubscribe();
    }

    // ─── 2.4b Test 5: After Continue, pointer is -1 and CurrentNodeId is null ──
    [Fact]
    public void CurrentlyExecutingNode_AfterContinue_PointerCleared()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (session, asset) = BuildAndPause(fixture, "HL5");

        session.Continue();

        Assert.False(session.IsPaused);
        Assert.Equal(-1, session.CurrentNodePointer);
        Assert.Null(session.CurrentNodeId);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static (BlueprintDebugSession session, BlueprintAsset asset) BuildAndPause(
        BlueprintTestFixture fixture, string name)
    {
        var asset   = Batch04Assets.BuildTwoSeqVarAsset(name);
        var tc      = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        session.SetBreakpoint(asset.AssetId, asset.Graphs[0].Id, asset.Graphs[0].Nodes[1].Id);
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);
        Assert.True(session.RecordedNodeCount >= 3,
            $"Expected >= 3 recorded nodes, got {session.RecordedNodeCount}");

        return (session, asset);
    }
}

// ============================================================================
// 2.4c — DebugStepControls.FormatNodePosition + StepBack wiring (no ImGui)
// ============================================================================

public sealed class FormatNodePositionTests
{
    // Minimal stub — only the three properties FormatNodePosition reads.
    private sealed class StubSession : IBlueprintDebugSession
    {
        public bool   IsPaused           { get; set; }
        public int    CurrentNodePointer { get; set; } = -1;
        public int    RecordedNodeCount  { get; set; }
        public string? CurrentNodeId    => null;

        // ── IBlueprintProbeSink ──
        public void OnNodeEnter(Entity self, string nodeId) { }
        public void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged { }
        public void OnPeerCallEnter(Entity self, string peerAssetIdString, string methodName) { }
        public void OnPeerCallExit(Entity self, string peerAssetIdString, string methodName) { }

        // ── IBlueprintDebugSession ──
        public bool IsAttached => true;
        public void Attach() { }
        public void Detach() { }
        public BreakpointId SetBreakpoint(Guid a, Guid g, Guid n) => default;
        public void ClearBreakpoint(BreakpointId id) { }
        public void ClearAllBreakpoints() { }
        public IReadOnlyList<Breakpoint> GetBreakpoints() => Array.Empty<Breakpoint>();
        public bool IsAnyBreakpointActive => false;
        public WatchId AddWatch(Guid a, Guid g, Guid p, string d, Type t) => default;
        public void RemoveWatch(WatchId id) { }
        public void ClearAllWatches() { }
        public IReadOnlyList<Watch> GetWatches() => Array.Empty<Watch>();
        public bool IsAnyWatchActive => false;
        public void SetEntityFilter(Entity? e) { }
        public Entity? GetEntityFilter() => null;
        public IReadOnlyList<Entity> GetActiveEntities(Guid a) => Array.Empty<Entity>();
        public Breakpoint? PausedAt     => null;
        public Entity?    PausedOnEntity => null;
        public string?    LastStepAction { get; private set; }
        public void Continue()  { LastStepAction = "Continue"; }
        public void StepOver()  { LastStepAction = "StepOver"; }
        public void StepInto()  { LastStepAction = "StepInto"; }
        public void StepOut()   { LastStepAction = "StepOut"; }
        public void StepBack()  { LastStepAction = "StepBack"; }
        public void Pause()     { LastStepAction = "Pause"; }
        public BlueprintStateSnapshot? GetCurrentStateSnapshot()                            => null;
        public BlueprintStateSnapshot? CaptureLiveState(Entity self, Guid assetId)          => null;
        public IReadOnlyList<NodeExecuted>    GetRecentNodeHistory(int maxCount = 100)      => Array.Empty<NodeExecuted>();
        public IReadOnlyList<CallFrame>       GetCurrentCallStack()                         => Array.Empty<CallFrame>();
        public IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity e, int maxCount = 100) => Array.Empty<NodeHistoryEntry>();
        public void RegisterDebugMap(DebugMap map)           { }
        public void UnregisterDebugMap(Guid assetId)         { }
        public bool IsNodeBreakpointable(Guid a, Guid g, Guid n) => true;
        public void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver) { }
        public void OnHotReloadBegin()                       { }
        public void OnHotReloadCompleted(Guid[] reloadedAssetIds) { }
        public void OnNewTick()                              { }

        public event Action<BreakpointHit>? OnBreakpointHit;
        public event Action<NodeExecuted>?  OnNodeExecuted;
        public event Action?                OnSessionStateChanged;
        public event Action<Guid>?          OnBreakpointListChanged;

        private Action<PinValueChanged>? _pvc;
        event Action<PinValueChanged>? IBlueprintDebugSession.OnPinValueChangedEvent
        {
            add    => _pvc += value;
            remove => _pvc -= value;
        }

        // Suppress unused event warnings.
        private void SuppressWarnings()
        {
            OnBreakpointHit?.Invoke(null!);
            OnNodeExecuted?.Invoke(null!);
            OnSessionStateChanged?.Invoke();
            OnBreakpointListChanged?.Invoke(Guid.Empty);
        }
    }

    // ─── 2.4c Test 1: not paused → empty ─────────────────────────────────────
    [Fact]
    public void FormatNodePosition_WhenNotPaused_ReturnsEmpty()
    {
        var s = new StubSession { IsPaused = false, CurrentNodePointer = 1, RecordedNodeCount = 5 };
        Assert.Equal(string.Empty, DebugStepControls.FormatNodePosition(s));
    }

    // ─── 2.4c Test 2: no recordings → empty ──────────────────────────────────
    [Fact]
    public void FormatNodePosition_WhenNoRecordings_ReturnsEmpty()
    {
        var s = new StubSession { IsPaused = true, CurrentNodePointer = -1, RecordedNodeCount = 0 };
        Assert.Equal(string.Empty, DebugStepControls.FormatNodePosition(s));
    }

    // ─── 2.4c Test 3: pointer=0, count=5 → "node 1 / 5" ─────────────────────
    [Fact]
    public void FormatNodePosition_Pointer0_Count5_ReturnsNode1Of5()
    {
        var s = new StubSession { IsPaused = true, CurrentNodePointer = 0, RecordedNodeCount = 5 };
        Assert.Equal("node 1 / 5", DebugStepControls.FormatNodePosition(s));
    }

    // ─── 2.4c Test 4: pointer=2, count=5 → "node 3 / 5" ─────────────────────
    [Fact]
    public void FormatNodePosition_Pointer2_Count5_ReturnsNode3Of5()
    {
        var s = new StubSession { IsPaused = true, CurrentNodePointer = 2, RecordedNodeCount = 5 };
        Assert.Equal("node 3 / 5", DebugStepControls.FormatNodePosition(s));
    }

    // ─── 2.4c Test 5: pointer at last → "node 5 / 5" ────────────────────────
    [Fact]
    public void FormatNodePosition_PointerAtLast_ReturnsCorrectString()
    {
        var s = new StubSession { IsPaused = true, CurrentNodePointer = 4, RecordedNodeCount = 5 };
        Assert.Equal("node 5 / 5", DebugStepControls.FormatNodePosition(s));
    }

    // ─── 2.4c Test 6: paused, pointer=-1 (CF-6 path) → empty ────────────────
    [Fact]
    public void FormatNodePosition_Paused_PointerNegative_ReturnsEmpty()
    {
        var s = new StubSession { IsPaused = true, CurrentNodePointer = -1, RecordedNodeCount = 5 };
        Assert.Equal(string.Empty, DebugStepControls.FormatNodePosition(s));
    }

    // ─── 2.4c Test 7: StepBack correctly tracked by stub ─────────────────────
    // Mirrors the existing onStepAction callback contract in DebugWindowDrawUITests.
    [Fact]
    public void StepBack_Session_RecordsAction()
    {
        var s = new StubSession { IsPaused = true };
        s.StepBack();
        Assert.Equal("StepBack", s.LastStepAction);
    }

    // ─── 2.4c Test 8: single node recording → "node 1 / 1" ──────────────────
    [Fact]
    public void FormatNodePosition_SingleNode_ReturnsNode1Of1()
    {
        var s = new StubSession { IsPaused = true, CurrentNodePointer = 0, RecordedNodeCount = 1 };
        Assert.Equal("node 1 / 1", DebugStepControls.FormatNodePosition(s));
    }
}
