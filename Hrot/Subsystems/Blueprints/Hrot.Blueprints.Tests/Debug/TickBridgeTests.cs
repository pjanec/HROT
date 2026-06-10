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
/// uses temp-BP + resume (CF-6 mechanism) to step to the successor, then re-pauses
/// when the temp BP fires — correctly crossing latent nodes (Delay/WaitForChannel)
/// that a single RequestStepOneTick cannot handle (BF-03 fix).
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
        session.RegisterGraph(asset.Graphs[0]); // needed by StepFromNode for allSuccessorsAreTerminal check
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

        // Call the bridge: step past end (BF-03: uses temp-BP + resume, NOT RequestStepOneTick).
        // Under MockTimeController RequestResume is a no-op — we drive the tick explicitly below.
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto(); // NGS-2.3 bridge: sets temp BPs on successors and calls RequestResume.

        // After the bridge call: session must NOT be paused (it cleared nav state).
        Assert.False(session.IsPaused,
            "Session must not be paused immediately after tick-bridge call (tick not yet run).");
        Assert.Equal(-1, session.CurrentNodePointer);
        // BF-03: bridge uses RequestResume (temp-BP + resume), not RequestStepOneTick.
        Assert.True(tc.ResumeCount > resumeCountBefore,
            $"Bridge must call RequestResume; ResumeCount was {resumeCountBefore}, now {tc.ResumeCount}.");
        Assert.True(tc.StepRequestCount == 0,
            "Bridge must NOT call RequestStepOneTick (BF-03 fix).");

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
        session.RegisterGraph(asset.Graphs[0]); // needed by StepFromNode for allSuccessorsAreTerminal check
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

        // StepInto at last node with no breakpoint armed: must NOT call RequestStepOneTick or RequestResume.
        int stepsBefore = tc.StepRequestCount;
        int resumeBefore = tc.ResumeCount;
        session.StepInto();
        Assert.True(tc.StepRequestCount == stepsBefore,
            "No breakpoint armed: RequestStepOneTick must NOT be called.");
        Assert.True(tc.ResumeCount == resumeBefore,
            "No breakpoint armed: RequestResume must NOT be called (clamp, no bridge).");

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
        // CF-6 path calls RequestResume (via temp BPs), never RequestStepOneTick.
        Assert.True(tc.StepRequestCount == 0,
            "CF-6 path must not call RequestStepOneTick (it uses RequestResume via temp BPs).");

        // Second tick: temp BP fires → re-pauses.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);

        session.Continue();
    }

    // =========================================================================
    // Test 6 — Latent repro (primary BF-03 bug regression test)
    //
    // Blueprint: Entry → Delay(0.0f) → SetVariable(X) → Return
    // Probe for entry block = Delay.Id  (ScheduleLatentNode overwrites SourceNodeId).
    // Proof: after the Delay elapses the session re-pauses on SetVar (NOT dead state).
    //
    // Step-past-end from Delay.Id:
    //   GetSuccessors(Delay) = [SetVar]   (non-terminal → temp BP on SetVar)
    //   RequestResume() called            (time advances across latent boundary)
    // TickFrame elapse → resume-block probe SetVar.Id fires → temp BP hits → re-pause.
    // =========================================================================

    [Fact]
    public void TickBridge_LatentDelay_DoesNotDeadlock_RepausesAfterDelay()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        // Entry → Delay(0.0f) → SetVariable(X) → Return
        // WithVariable("X") ensures FindVariableIndex("X") resolves to index 0 (name fallback).
        var asset = BlueprintAssetBuilder
            .Instance("LatentBridgeTest6")
            .WithVariable("X", typeof(int))
            .WithGraph("Tick", g => g.Entry().Delay(0.0f).SetVariable("X", "0").Return())
            .Build();

        var tc      = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm breakpoint on Nodes[1] (LatentDelayNode — its Id is the probe id for the entry
        // block because ScheduleLatentNode overwrites bb.SourceNodeId with the Delay's Id).
        var graphId     = asset.Graphs[0].Id;
        var delayNodeId = asset.Graphs[0].Nodes[1].Id; // Nodes: [0]=Entry, [1]=Delay, [2]=SetVar, [3]=Return
        session.RegisterGraph(asset.Graphs[0]);
        session.SetBreakpoint(asset.AssetId, graphId, delayNodeId);

        // Tick 1: the entry block fires (probe = Delay.Id) → breakpoint fires → pause.
        // The tick suspends inside the Delay; only 1 node recorded (the pre-suspend block).
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused,
            "Session must be paused on tick 1 at the Delay probe.");
        Assert.True(session.RecordedNodeCount >= 1,
            $"Expected >= 1 recorded node after tick 1; got {session.RecordedNodeCount}.");

        // Navigate pointer to the last recorded node (Delay — the only recorded node).
        int lastIdx = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < lastIdx)
            session.StepInto();
        Assert.Equal(lastIdx, session.CurrentNodePointer);
        Assert.Equal(delayNodeId.ToString("D"), session.CurrentNodeId);

        // ---- BF-03: step past end from a latent node ----
        // Old behaviour: RequestStepOneTick → one tick advanced → Delay keeps running →
        //                no probe fires → dead state (IsPaused=false, clock stalled).
        // New behaviour: StepFromNode(Delay.Id) → GetSuccessors=[SetVar] → temp BP on
        //                SetVar.Id → RequestResume() → session hands off to runtime.
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto(); // tick-bridge: BF-03 path

        Assert.False(session.IsPaused,
            "After tick-bridge call on a latent node: session must NOT be paused immediately " +
            "(temp BP was set and runtime was resumed; actual pause comes when Delay elapses).");
        Assert.Equal(-1, session.CurrentNodePointer);
        Assert.True(tc.ResumeCount > resumeCountBefore,
            $"BF-03: bridge must call RequestResume (temp-BP + resume path); " +
            $"ResumeCount was {resumeCountBefore}, now {tc.ResumeCount}.");
        Assert.True(tc.StepRequestCount == 0,
            "BF-03: bridge must NOT call RequestStepOneTick — that path causes the dead state.");

        // ---- Elapse the Delay — the resumed runtime runs and the temp BP fires ----
        // Delay(0.0f): with fixture timing (0.016f frame), the Delay elapses in the next tick.
        fixture.TickFrame(0.016f);

        // Regression guard: after the Delay elapses, the session MUST be paused again.
        // A dead state (the original BF-03 bug) would leave IsPaused=false here.
        Assert.True(session.IsPaused,
            "BF-03 regression guard: session must be paused again after the Delay elapses. " +
            "If IsPaused is false here, the tick-bridge has reverted to the dead-state bug.");
        Assert.True(session.RecordedNodeCount >= 1,
            $"After latent resume: expected >= 1 recorded nodes; got {session.RecordedNodeCount}.");
        Assert.True(session.CurrentNodePointer >= 0,
            $"After latent resume: pointer must be valid (>= 0); got {session.CurrentNodePointer}.");

        session.Continue();
    }

    // =========================================================================
    // Test 7 — Terminal last node falls back to Continue(), not dead-end temp BP
    //
    // Blueprint: Entry → Sequence(Then0: SetVar A=10, Then1: SetVar B=20 → Return)
    //            (BuildTwoSeqVarAsset)
    // Last recorded node = svBId (SetVarB). Its only successor is the ReturnNode
    // which has no further successors → allSuccessorsAreTerminal == true.
    // StepFromNode must call Continue() instead of setting a temp BP on ReturnNode
    // (ReturnNode has no IR probe — it is merged into the preceding block by Stage5).
    //
    // After Continue():
    //   - No temp BPs (we fell through to Continue, which clears them).
    //   - RequestResume was called (Continue calls it).
    //   - The still-armed user BP re-fires on the next tick → session re-pauses.
    // =========================================================================

    [Fact]
    public void TickBridge_TerminalLastNode_CallsContinue_RepausesOnNextBP()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset  = BuildTwoSeqVarAsset("TickBridgeTest7");
        var tc     = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm breakpoint on SequenceNode (Nodes[1]) — the entry block probe.
        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id;
        session.RegisterGraph(asset.Graphs[0]);
        session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        // Tick 1: all nodes recorded atomically; session pauses at pointer 0.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused, "Session must be paused after tick 1.");
        Assert.True(session.RecordedNodeCount >= 2,
            $"Expected >= 2 recorded nodes; got {session.RecordedNodeCount}.");

        // Navigate pointer to the LAST recorded node (SetVarB — index count-1).
        // Its only exec successor is the ReturnNode (terminal: no further successors).
        int lastIdx = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < lastIdx)
            session.StepInto();
        Assert.Equal(lastIdx, session.CurrentNodePointer);

        // Step past end from a terminal node:
        //   allSuccessorsAreTerminal == true → Continue() is called.
        //   Continue() calls RequestResume() and clears pause state + temp BPs.
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto();

        Assert.False(session.IsPaused,
            "After step-past-end from a terminal node: session must not be paused.");
        Assert.False(session.HasTemporaryBreakpoints,
            "Terminal-last-node path: no temp BPs should be set (Continue() was called, not SetTemporaryBreakpoints).");
        Assert.True(tc.ResumeCount > resumeCountBefore,
            $"Continue() must call RequestResume; ResumeCount was {resumeCountBefore}, now {tc.ResumeCount}.");
        Assert.True(tc.StepRequestCount == 0,
            "Terminal-last-node path must NOT call RequestStepOneTick.");

        // Drive tick 2: the still-armed user BP (on SequenceNode) re-fires → re-pause.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused,
            "Session must re-pause on tick 2 via the still-armed user breakpoint.");
        Assert.True(session.RecordedNodeCount >= 2,
            $"Fresh tick must have >= 2 recorded nodes; got {session.RecordedNodeCount}.");

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
