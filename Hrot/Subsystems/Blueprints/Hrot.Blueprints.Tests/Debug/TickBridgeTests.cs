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
    // Test 7 — Terminal last node (synchronous tick end): BF-04 sets temp BP on
    //           the first node of the next iteration, NOT Continue().
    //
    // Blueprint: Entry → Sequence(Then0: SetVar A=10, Then1: SetVar A=20 → Return)
    //            (BuildTwoSeqVarAsset)
    // Last recorded node = svBId (SetVarB). Its only successor is ReturnNode
    // (terminal: no further successors) → allSuccessorsAreTerminal == true.
    //
    // BF-04 behaviour (replaces the old BF-03 Continue() fallback in the bridge):
    //   StepFromNodeOrNextIteration finds EventEntryNode's successor (seqId = first
    //   executable node) → sets one-shot temp BP on seqId → RequestResume().
    //
    // After StepInto():
    //   - HasTemporaryBreakpoints == true (temp BP on seqId is armed).
    //   - RequestResume() was called (NOT RequestStepOneTick).
    //   - Session is not paused.
    //
    // After next TickFrame:
    //   - Temp BP fires on seqId → re-pause on the FIRST node (seqId), not the
    //     user BP's node (which happens to be the same seqId here — in the
    //     discriminating Test 8 they differ and the explicit NOT assertion is made).
    //
    // (Changed from BF-03 "Continue() / no temp BPs" because the bridge's end-of-tick
    // path must now land on the graph's first executable node, not the user breakpoint.
    // The old behaviour would be equivalent only when the BP happens to be on seqId —
    // the discriminating case in Test 8 shows the difference when BP is on a mid-chain
    // node. For correctness we unify the path: always temp-BP-on-first-node.)
    // =========================================================================

    [Fact]
    public void TickBridge_TerminalLastNode_SetsFirstNodeTempBP_NotContinue()
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

        // Arm breakpoint on SequenceNode (Nodes[1] = seqId) — the entry block probe.
        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id; // seqId = entry's first exec successor
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

        // BF-04 bridge: step past end of a terminal last node.
        //   allSuccessorsAreTerminal == true → entry's first-node path.
        //   Entry → seqId (non-terminal) → temp BP on seqId → RequestResume().
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto();

        Assert.False(session.IsPaused,
            "After step-past-end from a terminal node: session must not be paused.");
        // BF-04: temp BP on the first node (seqId) is armed — NOT a plain Continue().
        Assert.True(session.HasTemporaryBreakpoints,
            "BF-04 bridge must arm a temp BP on the entry's first executable node, not just Continue().");
        Assert.True(tc.ResumeCount > resumeCountBefore,
            $"Bridge must call RequestResume; ResumeCount was {resumeCountBefore}, now {tc.ResumeCount}.");
        Assert.True(tc.StepRequestCount == 0,
            "Terminal-last-node path must NOT call RequestStepOneTick.");

        // Drive tick 2: temp BP on seqId fires → re-pause on the first node.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused,
            "Session must re-pause on tick 2 via the temp BP on the first node.");
        Assert.True(session.RecordedNodeCount >= 2,
            $"Fresh tick must have >= 2 recorded nodes; got {session.RecordedNodeCount}.");

        // Landing node must be seqId (entry's exec successor = first executable node).
        var landingNodeId = session.CurrentNodeId;
        Assert.Equal(probeNodeId.ToString("D"), landingNodeId);

        session.Continue();
    }

    // =========================================================================
    // Test 8 — BF-04 discriminating test: step-past-end-of-tick lands on the
    //           FIRST node, NOT the breakpoint node.
    //
    // Graph shape (Count5-like):
    //   Entry → Seq(firstNode=seqId) → SetVar(X, BP here) → Delay(0.0f) → Return
    //
    // The BP is on SetVar (mid-chain), NOT on seqId (first node).
    // After pause on SetVar, step to Delay (last recorded), step past →
    // elapse Delay via TickFrame → assert re-pauses on seqId (first node) AND
    // explicitly NOT on SetVar (the breakpoint node).
    //
    // This is the discriminating assertion BF-03 lacked: the landing node must be
    // the graph's first executable node, never the user breakpoint's node.
    // =========================================================================

    [Fact]
    public void TickBridge_StepPastEndOfTick_LandsOnFirstNode_NotBreakpoint()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        // Build the Count5-like graph manually.
        //
        // Shape: Entry → Sequence(seqId = firstNode)
        //            Then0: SetVar(X=7, bpNode/svId)  [no exec-out link → falls through to Then1]
        //            Then1: Delay(0.0f, delayId) → Return
        //
        // The Sequence is the first executable node (entry's exec-successor).
        // The BP is on SetVar(X=7) in Then0 (probe = svId, different from seqId).
        // Then1 path: Delay → Return = end-of-tick terminal path.
        //
        // Stage5 scheduling:
        //   - Block A (entry+seq): SourceNodeId = seqId
        //   - Block B (Then0):    SourceNodeId = svId; svId falls through to Then1 block
        //   - Block C (Then1):    ScheduleLatentNode(Delay) → SourceNodeId = delayId
        //   - Block D (resume):   SourceNodeId = null (ReturnNode, no probe inserted)
        //
        // Execution order (Tick N):
        //   Block A fires (seqId probe) → Block B fires (svId probe) → BP triggers → PAUSE.
        //   Block C still fires (delayId probe) and is recorded (recording continues while paused).
        //   Delay suspends — tick ends.
        //
        // The recorded nodes are: [seqId, svId, delayId].
        // BP fires on svId (pointer = index 1). Step to delayId (last, index 2). Step past:
        //   Delay's successor = Return (terminal) → allSuccessorsAreTerminal == true.
        //   BF-04: entry's successor = seqId (non-terminal) → temp BP on seqId.
        //   RequestResume() called.
        //
        // Frame N+1: Delay(0.0f) elapses → Block D (no probe, just Return) → blueprint restarts.
        //            Next iteration: Block A fires (seqId probe) → temp BP hits → PAUSE.
        //            (With 0.0f delay, both the continuation and the restart happen in one frame.)
        //
        // Discriminating assertion: CurrentNodeId == seqId, NOT svId.
        var graphId   = Guid.NewGuid();
        var entryId   = Guid.NewGuid();
        var seqId     = Guid.NewGuid();  // SequenceNode — first executable node (entry's successor)
        var svId      = Guid.NewGuid();  // SetVariableNode Then0 — the BP node (NOT the first node)
        var delayId   = Guid.NewGuid();  // LatentDelayNode Then1 — last recorded node
        var retId     = Guid.NewGuid();  // Return for Then1 (Delay's only successor = terminal)

        var litId7    = Guid.NewGuid();  // LiteralNode value=7 for svId (data pin, not exec)

        var peOut     = Guid.NewGuid();
        var psIn      = Guid.NewGuid();
        var psThen0   = Guid.NewGuid();
        var psThen1   = Guid.NewGuid();
        var pSvIn     = Guid.NewGuid();
        var pSvOut    = Guid.NewGuid();  // svId exec-out has NO link → falls through to Then1 block
        var pSvVal    = Guid.NewGuid();
        var pLitOut   = Guid.NewGuid();
        var pDelayIn  = Guid.NewGuid();
        var pDelayOut = Guid.NewGuid();
        var pRetIn    = Guid.NewGuid();

        var varX = new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "X",
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
                // Then0: SetVar(X=7) — the BP node; exec-out NOT linked so it falls through to Then1
                new LiteralNode { Id = litId7, TypeId = "System.Int32", ValueJson = "7",
                    Pins = new() { new Pin { Id = pLitOut, Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() } } },
                new SetVariableNode
                {
                    Id         = svId,
                    VariableId = varX.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = pSvVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() },
                    },
                },
                // Then1: Delay(0.0f) → Return (the end-of-tick terminal path)
                new LatentDelayNode
                {
                    Id   = delayId,
                    Pins = new()
                    {
                        new Pin { Id = pDelayIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pDelayOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new System.Collections.Generic.List<Link>
            {
                // Entry → Seq
                new() { FromNodeId = entryId, FromPinId = peOut,     ToNodeId = seqId,   ToPinId = psIn     },
                // Then0: Seq → SetVar(7) [svId's exec-out has NO link → falls through]
                new() { FromNodeId = seqId,   FromPinId = psThen0,   ToNodeId = svId,    ToPinId = pSvIn    },
                // Data: Literal(7) → SetVar.Value
                new() { FromNodeId = litId7,  FromPinId = pLitOut,   ToNodeId = svId,    ToPinId = pSvVal   },
                // Then1: Seq → Delay → Return
                new() { FromNodeId = seqId,   FromPinId = psThen1,   ToNodeId = delayId, ToPinId = pDelayIn  },
                new() { FromNodeId = delayId, FromPinId = pDelayOut, ToNodeId = retId,   ToPinId = pRetIn    },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId          = Guid.NewGuid(),
            Name             = "Count5DiscriminatingTest8",
            Dispatch         = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Parameters       = new(), WorkingState = new(),
            Variables        = new() { varX },
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs           = new() { graph },
            Header           = new Header(),
        };

        var tc      = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm breakpoint on SetVar (svId) — mid-chain, NOT the first node.
        session.RegisterGraph(graph);
        session.SetBreakpoint(asset.AssetId, graphId, svId);

        // Tick 1: Block A (seqId probe) → Block B (svId probe) → BP fires → PAUSE.
        // Recording continues while paused: Block C (delayId probe) also recorded.
        // Delay suspends. Recorded = [seqId, svId, delayId]. Pointer = index of svId (1).
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused, "Session must pause on SetVar breakpoint.");

        // Recordings: seqId (0), svId (1), delayId (2) — at least 2 (seqId + svId minimum).
        Assert.True(session.RecordedNodeCount >= 2,
            $"Expected >= 2 recorded nodes (seqId + svId minimum); got {session.RecordedNodeCount}.");

        // Navigate pointer to the LAST recorded node (delayId, index = RecordedNodeCount-1).
        int lastIdx = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < lastIdx)
            session.StepInto();
        Assert.Equal(lastIdx, session.CurrentNodePointer);

        // ---- BF-04: step past end of tick from Delay ----
        // Delay's successor = ReturnNode (terminal) → allSuccessorsAreTerminal == true.
        // BF-04: set temp BP on entry's successor (seqId = FIRST executable node) → RequestResume().
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto(); // bridge

        Assert.False(session.IsPaused,
            "After step-past-end: session must not be paused immediately (temp BP armed, runtime resumed).");
        Assert.True(session.HasTemporaryBreakpoints,
            "BF-04: temp BP on the first node must be armed.");
        Assert.True(tc.ResumeCount > resumeCountBefore,
            $"Bridge must call RequestResume; was {resumeCountBefore}, now {tc.ResumeCount}.");
        Assert.True(tc.StepRequestCount == 0,
            "BF-04 bridge must NOT call RequestStepOneTick.");

        // ---- Elapse the Delay, then drive the next full iteration ----
        // The Instance blueprint state machine handles Delay in two phases:
        //   Frame N+1: cursor check → Delay elapsed → WriteCursorResumeAt(0) → Goto(resumeBlock=Block D)
        //              → Block D runs (Return, no probe, SourceNodeId=null) → function returns.
        //              No probes fire. Temp BP still armed.
        //   Frame N+2: cursor=0 → dispatch jumps to Entry → Block A fires (seqId probe)
        //              → temp BP hits → PAUSE.
        //
        // Note: this is different from Test 6's single-TickFrame Delay pattern!
        // Test 6: Delay→SetVar→Return — the resume block (SetVar) fires in Frame N+1.
        // Test 8: Delay→Return — the resume block (Return, no probe) fires in Frame N+1;
        //         the fresh iteration from Entry fires in Frame N+2.
        fixture.TickFrame(0.016f);  // Frame N+1: Delay elapses, Return fires, cursor reset to 0.

        // After Frame N+1: NOT paused — Block D (Return) fired but no probe.
        Assert.False(session.IsPaused,
            $"After Delay elapses (Block D = Return, no probe): session must NOT be paused. " +
            $"IsPaused={session.IsPaused}, HasTempBP={session.HasTemporaryBreakpoints}.");
        Assert.True(session.HasTemporaryBreakpoints,
            "Temp BP must still be armed (not yet fired — the Delay continuation block had no probe).");

        fixture.TickFrame(0.016f);  // Frame N+2: Entry fires → Block A (seqId probe) → temp BP → PAUSE.

        // No-dead-state regression: session must now be paused on the first node.
        Assert.True(session.IsPaused,
            $"BF-04 regression guard: session must be paused on the first node of the next iteration. " +
            $"IsPaused={session.IsPaused}, HasTempBP={session.HasTemporaryBreakpoints}, " +
            $"CurrentNodeId={session.CurrentNodeId ?? "<null>"}, RecordedCount={session.RecordedNodeCount}");

        // === THE DISCRIMINATING ASSERTION (BF-03 lacked this) ===
        // Landing node must be seqId (entry's exec-successor = first executable node).
        string? landingNodeId = session.CurrentNodeId;
        string seqIdStr  = seqId.ToString("D");
        string svIdStr   = svId.ToString("D");

        Assert.True(landingNodeId == seqIdStr,
            $"BF-04: landing node must be seqId (first node = {seqIdStr}), " +
            $"got {landingNodeId ?? "<null>"}.");

        // Explicitly assert it is NOT the breakpoint's node.
        Assert.True(landingNodeId != svIdStr,
            "BF-04 discriminating assertion: landing node must NOT be the breakpoint's node (svId). " +
            "If this fails, the bridge is incorrectly falling through to Continue() / user-BP semantics.");

        session.Continue();
    }

    // =========================================================================
    // Test 9 — Non-terminal post-latent (BF-03 behavior preserved):
    //           Delay → SetVar → Return   steps to SetVar (not to first node).
    //
    // When the last recorded node (Delay) has a non-terminal successor (SetVar),
    // the bridge uses the BF-03 path (StepFromNode → temp BP on SetVar), NOT the
    // BF-04 entry-successor path. The next-iteration semantics only apply when ALL
    // successors of the last node are terminal.
    // =========================================================================

    [Fact]
    public void TickBridge_NonTerminalPostLatent_StillLandsOnSuccessor()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        // Build: Entry → Delay(0.0f) → SetVariable(X) → Return
        // (same as Test 6's layout — non-terminal Delay successor = SetVar)
        var asset = BlueprintAssetBuilder
            .Instance("NonTerminalPostLatentTest9")
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

        // Arm breakpoint on the Delay (Nodes[1]).
        var graphId     = asset.Graphs[0].Id;
        var delayNodeId = asset.Graphs[0].Nodes[1].Id;
        session.RegisterGraph(asset.Graphs[0]);
        session.SetBreakpoint(asset.AssetId, graphId, delayNodeId);

        // Tick 1: entry block fires (probe = Delay.Id) → BP fires → pause.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);

        // Navigate to last recorded node (Delay).
        int lastIdx = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < lastIdx)
            session.StepInto();
        Assert.Equal(lastIdx, session.CurrentNodePointer);

        // Step past Delay: Delay's successor is SetVar (non-terminal) → BF-03 path.
        // Temp BP is set on SetVar; NO entry-successor path is used.
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto();

        Assert.False(session.IsPaused);
        Assert.True(session.HasTemporaryBreakpoints,
            "Non-terminal case: temp BP on SetVar must be armed.");
        Assert.True(tc.ResumeCount > resumeCountBefore,
            "Bridge must call RequestResume.");

        // Elapse Delay → SetVar probe fires → temp BP → re-pause on SetVar.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused,
            "Session must re-pause after Delay elapses (SetVar probe fires temp BP).");

        // Landing node: SetVar (Nodes[2]), NOT the EventEntry's successor.
        var setVarNodeId = asset.Graphs[0].Nodes[2].Id;
        var setVarNodeIdStr = setVarNodeId.ToString("D");
        Assert.True(session.CurrentNodeId == setVarNodeIdStr,
            $"Non-terminal post-latent: landing node must be SetVar ({setVarNodeIdStr}), " +
            $"not the graph's first node. Got: {session.CurrentNodeId ?? "<null>"}.");

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
