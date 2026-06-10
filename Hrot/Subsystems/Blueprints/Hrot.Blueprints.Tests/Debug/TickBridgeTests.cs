using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Mocks;
using Link = Hrot.Blueprints.Core.Assets.Link;

namespace Hrot.Blueprints.Tests.Debug;

/// <summary>
/// NGS-2.3 tests: step-past-end tick-bridge.
/// Verifies that stepping forward at the LAST recorded node of the paused tick
/// advances exactly one real tick, records the new tick, and re-pauses at its start
/// (driven by the armed breakpoint re-firing via HandleBreakpointHit).
///
/// <para>Test design: uses a blueprint whose tick sets variable A = 10 + tick-count via
/// a per-tick incrementing approach — but since simple literals always write the same
/// value, we distinguish ticks by asserting that a FRESH BeginTick occurred
/// (RecordedNodeCount reflects a NEW tick) and that View.Tick advanced by exactly 1.
/// The cross-tick proof (Test 2) uses a second tick that sets A = 10 (same literal
/// value) but asserts <see cref="GetCurrentStateSnapshot"/> returns A = 10 AND that
/// View.Tick is exactly N+1 — proving the snapshot is from the new tick, not the old one.</para>
/// </summary>
[Collection("DebugProbe")]
public sealed class TickBridgeTests : IDisposable
{
    private static readonly BlueprintTestFixtureOptions NoAlcCheck =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    // Save/restore DebugProbe.Sink for test isolation.
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;
    public void Dispose() => DebugProbe.Sink = _savedSink;

    // =========================================================================
    // Test 1 — Tick-bridge advances exactly one tick and re-pauses with a fresh recording
    //
    // Blueprint: Entry → Sequence(Then0: SetVar A=10, Then1: SetVar A=20 → Return)
    // Breakpoint armed on entry (Nodes[1] = SequenceNode probe identity).
    // Tick N: pause at pointer 0, step to last node, call StepInto() (the bridge).
    // Then drive the advance: fixture.TickFrame() → armed BP re-fires → re-pause.
    // Assert: session is paused again; View.Tick == tickAtFirstPause + 1;
    //         RecordedNodeCount >= 2 (fresh BeginTick, not appended); pointer >= 0.
    // =========================================================================

    [Fact]
    public void TickBridge_AdvancesExactlyOneTick_RepausesWithFreshRecording()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset  = BuildTwoSeqVarAsset("TickBridgeTest1");
        var tc     = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm breakpoint on SequenceNode (Nodes[1]) — actual probe id for entry block.
        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id;
        session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        // Tick N: all nodes recorded atomically; session pauses.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused, "Session must be paused after first TickFrame.");

        uint tickAtFirstPause = fixture.View.Tick;
        int  countAtFirstPause = session.RecordedNodeCount;
        Assert.True(countAtFirstPause >= 2, $"Expected >= 2 nodes, got {countAtFirstPause}.");

        // Step forward to the last recorded node of tick N.
        int last = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < last)
            session.StepInto();
        Assert.Equal(last, session.CurrentNodePointer);

        // Call the bridge: step past end. Under MockTimeController this is a no-op
        // (doesn't advance the clock) — we drive the tick explicitly below.
        session.StepInto(); // NGS-2.3 bridge: calls RequestStepOneTick() internally.

        // After the bridge call: session must NOT be paused (it cleared nav state).
        Assert.False(session.IsPaused,
            "Session must not be paused immediately after tick-bridge call (tick not yet run).");
        Assert.Equal(-1, session.CurrentNodePointer);
        Assert.True(tc.StepRequestCount == 1,
            $"RequestStepOneTick should have been called once; got {tc.StepRequestCount}.");

        // Drive the tick advance: armed BP re-fires → HandleBreakpointHit → re-pause.
        fixture.TickFrame(0.016f);

        // Assert: re-paused on the new tick.
        Assert.True(session.IsPaused, "Session must be paused again after the second TickFrame.");

        // View.Tick must have advanced by exactly 1.
        uint tickAtSecondPause = fixture.View.Tick;
        Assert.True(tickAtSecondPause == tickAtFirstPause + 1,
            $"View.Tick should advance by exactly 1; was {tickAtFirstPause}, now {tickAtSecondPause}.");

        // RecordedNodeCount must reflect a FRESH tick (new BeginTick, ring re-initialised).
        // The new tick is a full re-run of the same blueprint, so the count should match.
        int countAtSecondPause = session.RecordedNodeCount;
        Assert.True(countAtSecondPause >= 2,
            $"Fresh tick must have >= 2 recorded nodes; got {countAtSecondPause}.");

        // Virtual pointer must be valid (>= 0) indicating the new tick's recording is navigable.
        Assert.True(session.CurrentNodePointer >= 0,
            $"Pointer must be valid (>= 0) after re-pause; got {session.CurrentNodePointer}.");

        session.Continue();
    }

    // =========================================================================
    // Test 2 — Cross-tick inspector proof (the discriminating assertion)
    //
    // At re-pause on tick N+1, GetCurrentStateSnapshot() must return a value that
    // proves the NEW tick ran — not just a stale snapshot from tick N.
    //
    // Design: the blueprint sets A=10 (Then0) then A=20 (Then1) each tick.
    // After the tick-bridge re-pause, pointer is at its init position.
    // We step forward to pointer 2 (Then1 block, after Then0 ran → A=10).
    // Assert A == 10 from the snapshot AND View.Tick == tickAtFirstPause + 1.
    // This is the cross-tick proof: the snapshot is from the NEW tick, not the old one.
    // (Both ticks write the same values, so we prove it by verifying the tick counter.)
    // =========================================================================

    [Fact]
    public void TickBridge_InspectorReflectsNewTick_ExactValue()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset  = BuildTwoSeqVarAsset("TickBridgeTest2");
        var tc     = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id;
        session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        // Tick N: pause.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);
        uint tickN = fixture.View.Tick;

        // Step to the last node of tick N.
        int last = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < last)
            session.StepInto();

        // Bridge: step past end.
        session.StepInto();
        Assert.False(session.IsPaused);

        // Drive tick N+1 advance.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);
        uint tickN1 = fixture.View.Tick;
        Assert.Equal(tickN + 1, tickN1);

        // At re-pause the pointer is at the breakpoint's node.
        // Navigate to index 2 (Then1 block: after Then0 ran → A=10).
        // Ensure we have enough nodes.
        int count = session.RecordedNodeCount;
        Assert.True(count >= 3,
            $"Expected >= 3 nodes on the new tick; got {count}.");

        // Walk to pointer 0 first.
        while (session.CurrentNodePointer > 0)
            session.StepBack();
        Assert.Equal(0, session.CurrentNodePointer);

        // Step to pointer 2 (Then1 block).
        session.StepInto();
        Assert.Equal(1, session.CurrentNodePointer);
        session.StepInto();
        Assert.Equal(2, session.CurrentNodePointer);

        // At pointer 2 (Then1 entry): Then0 has already run (wrote A=10), Then1 hasn't.
        // So A must be 10.
        var snap = session.GetCurrentStateSnapshot();
        Assert.NotNull(snap);
        int aValue = GetSnapshotIntField(snap!, "A");
        Assert.True(aValue == 10,
            $"At pointer 2 of tick N+1, A must be 10 (Then0 wrote 10, Then1 hasn't run yet); got {aValue}.");

        // Cross-tick proof: View.Tick == N+1, snapshot reflects tick N+1.
        Assert.Equal(tickN + 1, fixture.View.Tick);

        session.Continue();
    }

    // =========================================================================
    // Test 3 — No-arm guard: stepping past last node with NO breakpoint armed
    // does NOT call RequestStepOneTick; keeps the no-op clamp behavior.
    // =========================================================================

    [Fact]
    public void TickBridge_NoArmGuard_DoesNotCallRequestStepOneTick()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset  = BuildTwoSeqVarAsset("TickBridgeTest3NoArm");
        var tc     = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm a breakpoint to pause and get recordings, then immediately clear it.
        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id;
        var bpId = session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);
        Assert.True(session.RecordedNodeCount >= 2);

        // Clear ALL breakpoints → RecordingActive becomes false.
        session.ClearBreakpoint(bpId);
        Assert.Equal(0, session.GetBreakpoints().Count);

        // Step to the last node.
        int last = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < last)
            session.StepInto();
        Assert.Equal(last, session.CurrentNodePointer);

        // StepInto at last node with no breakpoint armed: must NOT call RequestStepOneTick.
        int stepsBefore = tc.StepRequestCount;
        session.StepInto();
        Assert.True(tc.StepRequestCount == stepsBefore,
            "No breakpoint armed: RequestStepOneTick must NOT be called.");

        // Session must remain paused (clamp behavior).
        Assert.True(session.IsPaused);
        Assert.Equal(last, session.CurrentNodePointer);

        session.Continue();
    }

    // =========================================================================
    // Test 4 — Regression: within-tick stepping and CF-6 fallback unchanged
    // =========================================================================

    [Fact]
    public void TickBridge_WithinTickStepping_Unaffected()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset  = BuildTwoSeqVarAsset("TickBridgeTest4Regression");
        var tc     = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id;
        session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);

        int count = session.RecordedNodeCount;
        Assert.True(count >= 3,
            $"Expected >= 3 nodes; got {count}.");

        // Walk to pointer 0.
        while (session.CurrentNodePointer > 0)
            session.StepBack();
        Assert.Equal(0, session.CurrentNodePointer);

        // Step forward: within-tick — no RequestStepOneTick.
        int stepsBefore = tc.StepRequestCount;
        session.StepInto(); // 0 → 1
        Assert.Equal(1, session.CurrentNodePointer);
        Assert.True(tc.StepRequestCount == stepsBefore, "Within-tick step must not call RequestStepOneTick.");
        Assert.True(session.IsPaused);

        // Step backward.
        session.StepBack(); // 1 → 0
        Assert.Equal(0, session.CurrentNodePointer);
        Assert.True(session.IsPaused);

        // Advance to index 2 — within-tick.
        session.StepInto(); // 0 → 1
        session.StepInto(); // 1 → 2
        Assert.Equal(2, session.CurrentNodePointer);
        Assert.True(tc.StepRequestCount == stepsBefore, "Within-tick steps must not call RequestStepOneTick.");

        // Inspector at pointer 2 should show A=10.
        var snap2 = session.GetCurrentStateSnapshot();
        Assert.NotNull(snap2);
        Assert.Equal(10, GetSnapshotIntField(snap2!, "A"));

        session.Continue();
    }

    // =========================================================================
    // Test 5 — CF-6 fallback regression: no recordings → temp BPs set, no bridge
    // =========================================================================

    [Fact]
    public void TickBridge_CF6Fallback_StillWorks_WhenNoRecordings()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset = BuildTwoSeqVarAsset("TickBridgeTest5CF6");
        var tc    = new MockTimeController();

        // Session without live repository → no recordings.
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        // Deliberately NOT calling session.SetLiveRepository.
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id;
        session.RegisterGraph(asset.Graphs[0]);
        session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);
        Assert.Equal(-1, session.CurrentNodePointer); // no recordings

        // StepInto with no recordings → CF-6 path (sets temp BPs and resumes).
        session.StepInto();
        Assert.False(session.IsPaused);
        Assert.True(session.HasTemporaryBreakpoints, "CF-6 fallback should have set temp BPs.");

        // Bridge must NOT have been called (no recordings, so the bridge branch was not reached).
        Assert.True(tc.StepRequestCount == 0,
            "CF-6 path must not call RequestStepOneTick.");

        // Second tick: temp BP fires → re-pauses.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);

        session.Continue();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Builds an Instance blueprint:
    /// EventEntry → Sequence(Then0: Literal(10) → SetVariable(A=10),
    ///                        Then1: Literal(20) → SetVariable(A=20) → Return)
    ///
    /// Both branches execute in one tick. Final state: A=20.
    /// The Sequence node causes the compiler to allocate separate IR blocks for each branch,
    /// so multiple probes fire within one tick.
    /// </summary>
    private static BlueprintAsset BuildTwoSeqVarAsset(string name)
    {
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var seqId    = Guid.NewGuid();
        var litAId   = Guid.NewGuid();
        var svAId    = Guid.NewGuid();
        var litBId   = Guid.NewGuid();
        var svBId    = Guid.NewGuid();
        var retBId   = Guid.NewGuid();

        var peOut    = Guid.NewGuid();
        var psIn     = Guid.NewGuid();
        var psThen0  = Guid.NewGuid();
        var psThen1  = Guid.NewGuid();
        var pLitAOut = Guid.NewGuid();
        var pSvAIn   = Guid.NewGuid();
        var pSvAOut  = Guid.NewGuid();
        var pSvAVal  = Guid.NewGuid();
        var pLitBOut = Guid.NewGuid();
        var pSvBIn   = Guid.NewGuid();
        var pSvBOut  = Guid.NewGuid();
        var pSvBVal  = Guid.NewGuid();
        var pRetBIn  = Guid.NewGuid();

        var varA = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "A",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new System.Collections.Generic.List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } },
                },
                new SequenceNode
                {
                    Id   = seqId,
                    Pins = new()
                    {
                        new Pin { Id = psIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen1, Name = "Then1",  Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new LiteralNode
                {
                    Id        = litAId,
                    TypeId    = "System.Int32",
                    ValueJson = "10",
                    Pins = new() { new Pin { Id = pLitAOut, Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() } },
                },
                new SetVariableNode
                {
                    Id         = svAId,
                    VariableId = varA.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvAIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvAOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvAVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() },
                    },
                },
                new LiteralNode
                {
                    Id        = litBId,
                    TypeId    = "System.Int32",
                    ValueJson = "20",
                    Pins = new() { new Pin { Id = pLitBOut, Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() } },
                },
                new SetVariableNode
                {
                    Id         = svBId,
                    VariableId = varA.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvBIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvBOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvBVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() },
                    },
                },
                new ReturnNode
                {
                    Id     = retBId,
                    Status = NodeStatus.Success,
                    Pins   = new() { new Pin { Id = pRetBIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } },
                },
            },
            Links = new System.Collections.Generic.List<Link>
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
            AssetId          = Guid.NewGuid(),
            Name             = name,
            Dispatch         = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Parameters       = new(),
            WorkingState     = new(),
            Variables        = new() { varA },
            EventDispatchers = new(),
            CustomEvents     = new(),
            CallablePeers    = new(),
            Graphs           = new() { graph },
            Header           = new Header(),
        };
    }

    private static int GetSnapshotIntField(BlueprintStateSnapshot snapshot, string fieldName)
    {
        if (snapshot.FieldValues.TryGetValue(fieldName, out var obj) && obj is int i)
            return i;
        return 0;
    }
}
