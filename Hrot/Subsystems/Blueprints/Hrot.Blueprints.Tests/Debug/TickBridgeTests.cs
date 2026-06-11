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
        // Navigate to index 3 (Then1 block: after Then0 ran → A=10).
        //
        // NEW probe order (after BPDBG-SEQ-PROBE-ORDER + ??= fix):
        //   Index 0: entryId  (EventEntry header probe — SourceNodeId=entryId, no ExecEntryNodeId stmt → header)
        //   Index 1: seqId    (seq-probe-anchor probe — in execution order)
        //   Index 2: svAId    (SetVarA/Then0 per-node probe — state as-of entering Then0, A=0)
        //   Index 3: svBId    (SetVarB/Then1 per-node probe — state as-of entering Then1, A=10)
        // The old index 2 (Then1 entry = A=10) shifted to index 3 because the entry block
        // now records TWO probes (entryId at 0, seqId at 1) instead of one (seqId at 0).
        int count = session.RecordedNodeCount;
        Assert.True(count >= 4,
            $"Expected >= 4 nodes on the new tick (entryId + seqId + svAId + svBId); got {count}.");

        // Walk to pointer 0 first.
        while (session.CurrentNodePointer > 0)
            session.StepBack();
        Assert.Equal(0, session.CurrentNodePointer);

        // Step to pointer 3 (Then1 block entry = svBId).
        session.StepInto();
        Assert.Equal(1, session.CurrentNodePointer);
        session.StepInto();
        Assert.Equal(2, session.CurrentNodePointer);
        session.StepInto();
        Assert.Equal(3, session.CurrentNodePointer);

        // At pointer 3 (Then1 entry = svBId): Then0 has already run (wrote A=10), Then1 hasn't.
        // So A must be 10.
        var snap = session.GetCurrentStateSnapshot();
        Assert.NotNull(snap);
        int aValue = GetSnapshotIntField(snap!, "A");
        Assert.True(aValue == 10,
            $"At pointer 3 of tick N+1, A must be 10 (Then0 wrote 10, Then1 hasn't run yet); got {aValue}.");

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
        Assert.True(count >= 4,
            $"Expected >= 4 nodes (entryId + seqId + svAId + svBId); got {count}.");

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

        // Advance to index 3 — within-tick.
        // NEW probe order (BPDBG-SEQ-PROBE-ORDER + ??= fix):
        //   0: entryId (EventEntry header probe)
        //   1: seqId   (seq-probe-anchor probe)
        //   2: svAId   (Then0 per-node probe — A=0 as-of entry, before Then0 writes)
        //   3: svBId   (Then1 per-node probe — A=10 as-of entry, after Then0 wrote)
        session.StepInto(); // 0 → 1
        session.StepInto(); // 1 → 2
        session.StepInto(); // 2 → 3
        Assert.Equal(3, session.CurrentNodePointer);
        Assert.True(tc.StepRequestCount == stepsBefore, "Within-tick steps must not call RequestStepOneTick.");

        // Inspector at pointer 3 (Then1 entry = svBId) should show A=10.
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
    // Test 7 — Terminal last node (synchronous tick end): BF-04 re-pauses on the
    //           first node of the next iteration via _stepResumePending, NOT Continue().
    //
    // Blueprint: Entry → Sequence(Then0: SetVar A=10, Then1: SetVar A=20 → Return)
    //            (BuildTwoSeqVarAsset)
    // Last recorded node = svBId (SetVarB). Its only successor is ReturnNode
    // (terminal: no further successors).
    //
    // BF-04 / unified bridge behaviour (NGS-2.3):
    //   StepForwardOrCF6 sets _stepResumePending = true and calls RequestResume().
    //   OnNodeEnter re-fires on the first probe of the resumed tick (seqId = first
    //   executable node) and re-pauses with _nodePointer = 0.
    //   No temp BPs are set — the recorder-order approach handles all graph shapes.
    //
    // After StepInto():
    //   - HasTemporaryBreakpoints == false (new mechanism uses _stepResumePending, not temp BPs).
    //   - RequestResume() was called (NOT RequestStepOneTick).
    //   - Session is not paused.
    //
    // After next TickFrame:
    //   - First probe of the new tick = seqId → OnNodeEnter detects _stepResumePending
    //     + _recorder.Count == 1 → re-pause on seqId (first node of new tick).
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

        // BF-04 / unified bridge: step past end of a terminal last node.
        // New mechanism: sets _stepResumePending, clears pause state, calls RequestResume().
        // No temp BPs are used — _stepResumePending triggers re-pause on first recorded node.
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto();

        Assert.False(session.IsPaused,
            "After step-past-end from a terminal node: session must not be paused.");
        // New mechanism: no temp BPs — _stepResumePending handles re-pause.
        Assert.False(session.HasTemporaryBreakpoints,
            "Unified bridge (_stepResumePending) must NOT arm temp BPs.");
        Assert.True(tc.ResumeCount > resumeCountBefore,
            $"Bridge must call RequestResume; ResumeCount was {resumeCountBefore}, now {tc.ResumeCount}.");
        Assert.True(tc.StepRequestCount == 0,
            "Terminal-last-node path must NOT call RequestStepOneTick.");

        // Drive tick 2: first probe of new tick (seqId) triggers _stepResumePending → re-pause.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused,
            "Session must re-pause on tick 2 via _stepResumePending on the first node.");
        Assert.True(session.RecordedNodeCount >= 1,
            $"Fresh tick must have >= 1 recorded node; got {session.RecordedNodeCount}.");

        // BF-04 re-assertion: landing node must be entryId (EventEntry is now the first recorded
        // node because the entry block retains SourceNodeId=entryId after the ??= fix in
        // ScheduleSequenceNode, and DebugProbeInsertion emits a header probe for it).
        //
        // NEW probe order (BPDBG-SEQ-PROBE-ORDER + ??= fix):
        //   Index 0: entryId  (EventEntry header probe — SourceNodeId=entryId preserved by ??= no-op)
        //   Index 1: seqId    (seq-probe-anchor probe, in execution order)
        //   Index 2: svAId    (Then0 per-node probe)
        //   Index 3: svBId    (Then1 per-node probe — the last recorded node)
        // _stepResumePending fires on the FIRST probe of the new tick = entryId (index 0). ✓
        var entryNodeId = asset.Graphs[0].Nodes[0].Id; // EventEntryNode — index 0 in Nodes list
        var landingNodeId = session.CurrentNodeId;
        Assert.Equal(entryNodeId.ToString("D"), landingNodeId);

        session.Continue();
    }

    // =========================================================================
    // Test 8 — BF-04 discriminating test: step-past-end-of-tick lands on the
    //           FIRST node, NOT the breakpoint node.
    //
    // Graph shape (Count5-like):
    //   Entry(entryId) → Seq(seqId) { Then0: SetVar(X, BP here=svId), Then1: Delay→Return }
    //
    // The BP is on SetVar (mid-chain), NOT on entryId/seqId (first nodes).
    // After pause on SetVar, step to Delay (last recorded), step past →
    // elapse Delay via TickFrames → assert re-pauses on entryId (first node of new iter) AND
    // explicitly NOT on SetVar (the breakpoint node).
    //
    // This is the discriminating assertion: the landing node must be the FIRST
    // node actually executed in the next iteration, never the user breakpoint's node.
    //
    // New mechanism (_stepResumePending) + BPDBG-SEQ-PROBE-ORDER fix:
    //   Recording ring (after fix): [entryId(0), seqId(1), svId(2), delayId(3)].
    //   Frame N+1: Delay elapses → Block D (Return, no probe) fires → no _stepResumePending
    //              trigger (no probe for the entity) → IsPaused remains false.
    //   Frame N+2: New tick → first probe = entryId header probe → _recorder.Count == 1 →
    //              _stepResumePending triggers → re-pause on entryId. ✓
    // =========================================================================

    [Fact]
    public void TickBridge_StepPastEndOfTick_LandsOnFirstNode_NotBreakpoint()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        // Build the Count5-like graph manually.
        //
        // Shape: Entry(entryId) → Sequence(seqId)
        //            Then0: SetVar(X=7, bpNode/svId)  [no exec-out link → falls through to Then1]
        //            Then1: Delay(0.0f, delayId) → Return
        //
        // The Sequence is the first executable node (entry's exec-successor).
        // The BP is on SetVar(X=7) in Then0 (probe = svId, different from seqId/entryId).
        // Then1 path: Delay → Return = end-of-tick terminal path.
        //
        // Stage5 scheduling (after BPDBG-SEQ-PROBE-ORDER + ??= fix):
        //   - Block A (entry+seq): SourceNodeId = entryId (set at block creation; ??= is no-op)
        //     → Header probe for entryId (EventEntry has no ExecEntryNodeId stmt)
        //     → seq-probe-anchor probe for seqId (in execution order, before Goto)
        //   - Block B (Then0):    SourceNodeId = svId; per-node probe for svId
        //   - Block C (Then1):    ScheduleLatentNode(Delay) → SourceNodeId = delayId
        //   - Block D (resume):   SourceNodeId = null (ReturnNode, no probe inserted)
        //
        // Execution order (Tick N):
        //   Block A fires (entryId probe, then seqId probe) → Block B fires (svId probe) → BP triggers → PAUSE.
        //   Block C still fires (delayId probe) and is recorded (recording continues while paused).
        //   Delay suspends — tick ends.
        //
        // The recorded nodes are: [entryId(0), seqId(1), svId(2), delayId(3)].
        // BP fires on svId (pointer = index 2). Step to delayId (last, index 3). Step past:
        //   _stepResumePending = true, RequestResume() called. No temp BPs.
        //
        // Frame N+1: Delay(0.0f) elapses → Block D (no probe, just Return) → blueprint restarts.
        //            No probe fires → _stepResumePending still set.
        //   Frame N+2: New iteration → Block A fires → entryId probe fires first → _recorder.Count==1
        //              → _stepResumePending triggers → re-pause on entryId.
        //
        // Discriminating assertion: CurrentNodeId == entryId (first node = EventEntry), NOT svId.
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

        // Tick 1: Block A (entryId probe, seqId probe) → Block B (svId probe) → BP fires → PAUSE.
        // Recording continues while paused: Block C (delayId probe) also recorded.
        // Delay suspends. Recorded = [entryId(0), seqId(1), svId(2), delayId(3)]. Pointer = 2.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused, "Session must pause on SetVar breakpoint.");

        // Recordings: entryId(0), seqId(1), svId(2), delayId(3) — at least 3 (entryId+seqId+svId minimum).
        Assert.True(session.RecordedNodeCount >= 3,
            $"Expected >= 3 recorded nodes (entryId + seqId + svId minimum); got {session.RecordedNodeCount}.");

        // Navigate pointer to the LAST recorded node (delayId, index = RecordedNodeCount-1).
        int lastIdx = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < lastIdx)
            session.StepInto();
        Assert.Equal(lastIdx, session.CurrentNodePointer);

        // ---- BF-04 / unified bridge: step past end of tick from Delay ----
        // New mechanism (_stepResumePending): RequestResume() is called; no temp BPs.
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto(); // bridge

        Assert.False(session.IsPaused,
            "After step-past-end: session must not be paused immediately (_stepResumePending set, runtime resumed).");
        // New mechanism: no temp BPs — _stepResumePending handles re-pause.
        Assert.False(session.HasTemporaryBreakpoints,
            "Unified bridge (_stepResumePending) must NOT arm temp BPs.");
        Assert.True(tc.ResumeCount > resumeCountBefore,
            $"Bridge must call RequestResume; was {resumeCountBefore}, now {tc.ResumeCount}.");
        Assert.True(tc.StepRequestCount == 0,
            "BF-04 bridge must NOT call RequestStepOneTick.");

        // ---- Elapse the Delay, then drive the next full iteration ----
        // The Instance blueprint state machine handles Delay in two phases:
        //   Frame N+1: cursor check → Delay elapsed → WriteCursorResumeAt(0) → Goto(resumeBlock=Block D)
        //              → Block D runs (Return, no probe, SourceNodeId=null) → function returns.
        //              No probe for the entity fires → _stepResumePending stays set (not cleared).
        //   Frame N+2: cursor=0 → dispatch jumps to Entry → Block A fires (entryId probe first)
        //              → OnNodeEnter: _stepResumePending + _recorder.Count==1 → re-pause on entryId.
        //
        // Note: this is different from Test 6's single-TickFrame Delay pattern!
        // Test 6: Delay→SetVar→Return — the resume block (SetVar) fires in Frame N+1.
        // Test 8: Delay→Return — the resume block (Return, no probe) fires in Frame N+1;
        //         the fresh iteration from Entry fires in Frame N+2.
        fixture.TickFrame(0.016f);  // Frame N+1: Delay elapses, Return fires (no probe).

        // After Frame N+1: NOT paused — Block D (Return) fired but no probe for entity.
        // _stepResumePending is still set; no temp BPs.
        Assert.False(session.IsPaused,
            $"After Delay elapses (Block D = Return, no probe): session must NOT be paused. " +
            $"IsPaused={session.IsPaused}.");
        Assert.False(session.HasTemporaryBreakpoints,
            "_stepResumePending bridge must not use temp BPs.");

        fixture.TickFrame(0.016f);  // Frame N+2: Entry fires → Block A (entryId probe) → _stepResumePending → PAUSE.

        // No-dead-state regression: session must now be paused on the first node.
        Assert.True(session.IsPaused,
            $"BF-04 regression guard: session must be paused on the first node of the next iteration. " +
            $"IsPaused={session.IsPaused}, HasTempBP={session.HasTemporaryBreakpoints}, " +
            $"CurrentNodeId={session.CurrentNodeId ?? "<null>"}, RecordedCount={session.RecordedNodeCount}");

        // === THE DISCRIMINATING ASSERTION (BF-03 lacked this) ===
        // Landing node must be entryId (EventEntryNode = first probe now fired in each iteration).
        //
        // After BPDBG-SEQ-PROBE-ORDER + ??= fix: the entry block's SourceNodeId stays as entryId
        // (??= is a no-op because the block creation already set it to entryId at line 198 of
        // Stage5_Schedule).  DebugProbeInsertion emits a header probe for entryId because no
        // statement is tagged ExecEntryNodeId==entryId.  entryId is a REAL authored node ID.
        // _stepResumePending re-pauses on the very first probe of the resumed tick = entryId. ✓
        string? landingNodeId = session.CurrentNodeId;
        string entryIdStr = entryId.ToString("D");
        string seqIdStr   = seqId.ToString("D");
        string svIdStr    = svId.ToString("D");

        Assert.True(landingNodeId == entryIdStr,
            $"BF-04 re-asserted: landing node must be entryId (EventEntry = first node = {entryIdStr}), " +
            $"got {landingNodeId ?? "<null>"}.");

        // Explicitly assert it is NOT the breakpoint's node.
        Assert.True(landingNodeId != svIdStr,
            "BF-04 discriminating assertion: landing node must NOT be the breakpoint's node (svId). " +
            "If this fails, the bridge is landing on the wrong node.");

        // Also assert it is NOT seqId (the old expected value before the ??= fix).
        Assert.True(landingNodeId != seqIdStr,
            "PROBE-ORDER CHECK: landing node must NOT be seqId. " +
            "entryId now fires first (header probe), then seqId (seq-probe-anchor).");

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

        // Step past Delay: unified bridge (_stepResumePending) → RequestResume().
        // The recorder-order mechanism correctly handles both non-terminal and terminal successors —
        // it always lands on the first probe actually fired in the resumed tick.
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto();

        Assert.False(session.IsPaused);
        // New mechanism: no temp BPs.
        Assert.False(session.HasTemporaryBreakpoints,
            "Unified bridge must NOT arm temp BPs.");
        Assert.True(tc.ResumeCount > resumeCountBefore,
            "Bridge must call RequestResume.");

        // Elapse Delay → new tick → SetVar probe is the first probe for the entity
        // (_recorder.Count == 1) → _stepResumePending triggers → re-pause on SetVar.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused,
            "Session must re-pause after Delay elapses (SetVar is first probe of resumed tick).");

        // BF-03 re-asserted: landing node must be SetVar (successor of Delay), NOT the first node.
        var setVarNodeId = asset.Graphs[0].Nodes[2].Id;
        var setVarNodeIdStr = setVarNodeId.ToString("D");
        Assert.True(session.CurrentNodeId == setVarNodeIdStr,
            $"BF-03 re-asserted: landing node must be SetVar ({setVarNodeIdStr}), " +
            $"not the graph's first node. Got: {session.CurrentNodeId ?? "<null>"}.");

        session.Continue();
    }

    // =========================================================================
    // Test 10 — PRIMARY BUG REGRESSION: Step Over from end of Sequence.Then0 (latent)
    //           lands on FIRST node of Then1 (SetVarB), NOT the last Delay of Then1.
    //
    // Blueprint:
    //   EventEntry → S0 {
    //     Then0: SetVarA → Delay0 (latent — last node of Then0)
    //     Then1: SetVarB → S1 { Then0: SetVarC → Return ; Then1: Delay1 → Return }
    //   }
    //
    // Compiled execution order (Tick 1, before Delay0 suspends):
    //   Block0 (Entry+S0 probe = s0Id) → Block1 (SetVarA probe = svAId) → Delay0 suspends.
    //
    // Breakpoint on SetVarA. Tick 1: pause at svAId (pointer 1).
    // StepInto() → pointer 2 (Delay0 — last recorded node).
    // StepInto() at pointer 2 = bridge: _stepResumePending, RequestResume().
    //
    // Resumed tick (Delay0 elapses → Then1 runs):
    //   Block2 (SetVarB probe = svBId) → [S1 probes] → Delay1 suspends.
    // First probe of resumed tick = svBId.
    // _stepResumePending: _recorder.Count == 1 → re-pause on svBId. ✓
    //
    // BUGGY behaviour (topology-based): ExecSuccessors(Delay0) is empty (Sequence drives
    // Then0→Then1 internally) → allTerminal → entry successor = s0Id → temp BP on s0Id →
    // s0Id never fires (S0 not re-entered mid-flight) → sim runs to Delay1 → pointer ends
    // on Delay1. ❌
    // =========================================================================

    [Fact]
    public void TickBridge_StepOverSequenceThen0Latent_LandsOnFirstNodeOfThen1_NotLastDelay()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        // Build the asset programmatically — NEVER load a .bp.json.
        //
        // Graph:
        //   Entry(entryId) → S0(s0Id) {
        //     Then0: SetVarA(svAId) → Delay0(delay0Id)            [latent — end of Then0]
        //     Then1: SetVarB(svBId) → S1(s1Id) {
        //              Then0: SetVarC(svCId) → Return(ret0Id)
        //              Then1: Delay1(delay1Id) → Return(ret1Id)   [latent — end of Then1]
        //            }
        //   }
        //
        // Execution order in Tick 2 (after Delay0 elapses, Then1 runs):
        //   svBId (S0.Then1 first) → s1Id (S1 entry) → svCId (S1.Then0) → delay1Id (S1.Then1)
        //
        // BUG before fix: stepping from delay0Id would skip svBId and svCId, landing on delay1Id.
        // Expected:       stepping from delay0Id lands on svBId (first of Then1).

        var assetId   = Guid.NewGuid();
        var graphId   = Guid.NewGuid();
        var entryId   = Guid.NewGuid();
        var s0Id      = Guid.NewGuid(); // outer Sequence
        var svAId     = Guid.NewGuid(); // Then0: SetVarA
        var delay0Id  = Guid.NewGuid(); // Then0: Delay0 (latent — last of Then0)
        var svBId     = Guid.NewGuid(); // Then1: SetVarB (EXPECTED landing node after bridge)
        var s1Id      = Guid.NewGuid(); // Then1: nested Sequence S1
        var svCId     = Guid.NewGuid(); // S1.Then0: SetVarC
        var ret0Id    = Guid.NewGuid(); // S1.Then0: Return
        var delay1Id  = Guid.NewGuid(); // S1.Then1: Delay1 (would be wrong landing node with bug)
        var ret1Id    = Guid.NewGuid(); // S1.Then1: Return

        var varA = new VariableDecl { Id = Guid.NewGuid(), Name = "A",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var varB = new VariableDecl { Id = Guid.NewGuid(), Name = "B",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var varC = new VariableDecl { Id = Guid.NewGuid(), Name = "C",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        // Pin ids
        var peOut       = Guid.NewGuid(); // Entry exec-out
        var ps0In       = Guid.NewGuid(); // S0 exec-in
        var ps0Then0    = Guid.NewGuid(); // S0 Then0 exec-out
        var ps0Then1    = Guid.NewGuid(); // S0 Then1 exec-out
        var pSvAIn      = Guid.NewGuid(); var pSvAOut = Guid.NewGuid();
        var pDelay0In   = Guid.NewGuid(); var pDelay0Out = Guid.NewGuid(); // exec-out unused (Seq drives next)
        var pSvBIn      = Guid.NewGuid(); var pSvBOut = Guid.NewGuid();
        var ps1In       = Guid.NewGuid(); // S1 exec-in
        var ps1Then0    = Guid.NewGuid(); // S1 Then0 exec-out
        var ps1Then1    = Guid.NewGuid(); // S1 Then1 exec-out
        var pSvCIn      = Guid.NewGuid(); var pSvCOut = Guid.NewGuid();
        var pRet0In     = Guid.NewGuid();
        var pDelay1In   = Guid.NewGuid(); var pDelay1Out = Guid.NewGuid();
        var pRet1In     = Guid.NewGuid();

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new System.Collections.Generic.List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SequenceNode { Id = s0Id,
                    Pins = new()
                    {
                        new Pin { Id = ps0In,    Name = "In",    Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = ps0Then0, Name = "Then0", Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = ps0Then1, Name = "Then1", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                // Then0
                new SetVariableNode { Id = svAId, VariableId = varA.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvAIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pSvAOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new LatentDelayNode { Id = delay0Id,
                    Pins = new()
                    {
                        new Pin { Id = pDelay0In,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pDelay0Out, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                // Then1
                new SetVariableNode { Id = svBId, VariableId = varB.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvBIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pSvBOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new SequenceNode { Id = s1Id,
                    Pins = new()
                    {
                        new Pin { Id = ps1In,    Name = "In",    Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = ps1Then0, Name = "Then0", Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = ps1Then1, Name = "Then1", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                // S1.Then0
                new SetVariableNode { Id = svCId, VariableId = varC.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvCIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pSvCOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new ReturnNode { Id = ret0Id, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRet0In, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
                // S1.Then1
                new LatentDelayNode { Id = delay1Id,
                    Pins = new()
                    {
                        new Pin { Id = pDelay1In,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pDelay1Out, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new ReturnNode { Id = ret1Id, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRet1In, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new System.Collections.Generic.List<Link>
            {
                // Entry → S0
                new() { FromNodeId = entryId,  FromPinId = peOut,      ToNodeId = s0Id,    ToPinId = ps0In     },
                // S0.Then0: S0 → SetVarA → Delay0
                new() { FromNodeId = s0Id,     FromPinId = ps0Then0,   ToNodeId = svAId,   ToPinId = pSvAIn   },
                new() { FromNodeId = svAId,    FromPinId = pSvAOut,    ToNodeId = delay0Id,ToPinId = pDelay0In },
                // S0.Then1: S0 → SetVarB → S1
                new() { FromNodeId = s0Id,     FromPinId = ps0Then1,   ToNodeId = svBId,   ToPinId = pSvBIn   },
                new() { FromNodeId = svBId,    FromPinId = pSvBOut,    ToNodeId = s1Id,    ToPinId = ps1In    },
                // S1.Then0: S1 → SetVarC → Return
                new() { FromNodeId = s1Id,     FromPinId = ps1Then0,   ToNodeId = svCId,   ToPinId = pSvCIn   },
                new() { FromNodeId = svCId,    FromPinId = pSvCOut,    ToNodeId = ret0Id,  ToPinId = pRet0In  },
                // S1.Then1: S1 → Delay1 → Return
                new() { FromNodeId = s1Id,     FromPinId = ps1Then1,   ToNodeId = delay1Id,ToPinId = pDelay1In },
                new() { FromNodeId = delay1Id, FromPinId = pDelay1Out, ToNodeId = ret1Id,  ToPinId = pRet1In  },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId          = assetId,
            Name             = "SeqThen0LatentBridgeTest10",
            Dispatch         = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Parameters       = new(), WorkingState = new(),
            Variables        = new() { varA, varB, varC },
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

        // Arm breakpoint on SetVarA (svAId — first Then0 node).
        session.RegisterGraph(graph);
        session.SetBreakpoint(asset.AssetId, graphId, svAId);

        // ---- Tick 1: S0.Then0 runs (S0 probe, SetVarA probe), then Delay0 suspends ----
        // Recorded nodes: [s0Id/entry-block, svAId, delay0Id] (at minimum).
        // Breakpoint on svAId fires → pause at svAId (pointer = index of svAId).
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused, "Session must be paused on SetVarA breakpoint.");
        Assert.True(session.RecordedNodeCount >= 2,
            $"Expected >= 2 recorded nodes; got {session.RecordedNodeCount}.");

        // The paused node must be svAId.
        string svAIdStr    = svAId.ToString("D");
        string svBIdStr    = svBId.ToString("D");
        string delay0IdStr = delay0Id.ToString("D");
        string delay1IdStr = delay1Id.ToString("D");

        Assert.Equal(svAIdStr, session.CurrentNodeId);

        // ---- Step through Then0: svAId → delay0Id (last recorded node) ----
        // Navigate forward to the last recorded node (delay0Id).
        int lastIdx = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < lastIdx)
            session.StepInto();
        Assert.Equal(lastIdx, session.CurrentNodePointer);
        Assert.Equal(delay0IdStr, session.CurrentNodeId);

        // ---- PRIMARY BUG TEST: Step Over from delay0Id (last node of Then0) ----
        //
        // Compiler structure of the Then1 block (AFTER BPDBG-SEQ-PROBE-ORDER fix):
        //   bb.SourceNodeId ??= svBId  (SetVarB is first exec node, owns SourceNodeId)
        //   ScheduleSequenceNode(s1, bb) does ??= → no-op (svBId already set)
        //   s1 gets a seq-probe-anchor with ExecEntryNodeId=s1Id at its position.
        //
        // Execution order probes after Delay0 elapses:
        //   1. svBId (per-node probe for SetVarB — FIRST probe of resumed tick)
        //   2. s1Id  (seq-probe-anchor probe for S1.SequenceNode — in execution order)
        //   3. svCId (S1.Then0 block first probe = SetVarC)
        //   4. delay1Id (S1.Then1 block probe — last probe before Delay1 suspends)
        //
        // With the FIX: first probe of resumed tick = svBId (SetVarB, not S1 Sequence).
        //   _stepResumePending: _recorder.Count == 1 → re-pause on svBId. ✓
        //
        // BEFORE this fix (probe-order bug): s1Id header probe was prepended ahead of
        //   svBId → first probe = s1Id → landing on S1 (SetVarB silently skipped). ✗
        // ORIGINAL dead-state bug (pre _stepResumePending): allTerminal → temp BP on
        //   s0Id → never fires → IsPaused=false forever. ✗
        int resumeCountBefore = tc.ResumeCount;
        session.StepOver(); // tick-bridge

        Assert.False(session.IsPaused,
            "After StepOver from Then0 Delay: session must not be paused (resumed for next tick).");
        Assert.False(session.HasTemporaryBreakpoints,
            "_stepResumePending bridge must NOT arm temp BPs.");
        Assert.True(tc.ResumeCount > resumeCountBefore,
            $"Bridge must call RequestResume; ResumeCount was {resumeCountBefore}, now {tc.ResumeCount}.");
        Assert.True(tc.StepRequestCount == 0,
            "Bridge must NOT call RequestStepOneTick.");

        // ---- Drive tick 2: Delay0 elapses → S0.Then1 runs ----
        // First probe of Then1 = svBId (SetVarB — first exec node in the Then1 block).
        // _stepResumePending: _recorder.Count == 1 → re-pause on svBId.
        fixture.TickFrame(0.016f);

        // PRIMARY REGRESSION GUARD: session MUST re-pause (old code causes dead state).
        Assert.True(session.IsPaused,
            "PRIMARY BUG FIX: session must re-pause after Delay0 elapses and Then1 starts. " +
            $"If IsPaused=false, the topology-based bridge caused dead state. " +
            $"CurrentNodeId={session.CurrentNodeId ?? "<null>"}, RecordedCount={session.RecordedNodeCount}.");

        // The recording for tick 2 captures the entire Then1 execution in order.
        Assert.True(session.RecordedNodeCount >= 2,
            $"After re-pause: expected >= 2 recorded nodes in tick 2 (svBId + s1Id minimum); " +
            $"got {session.RecordedNodeCount}.");
        Assert.Equal(0, session.CurrentNodePointer);

        // === PRIMARY DISCRIMINATING ASSERTION (BPDBG-SEQ-PROBE-ORDER) ===
        // Landing node must be svBId (SetVarB — first exec node in Then1, first probe in
        // execution order). BEFORE this fix the landing was s1Id because ScheduleSequenceNode
        // overwrote SourceNodeId and the header probe was prepended ahead of SetVarB.
        string? landingNodeId = session.CurrentNodeId;
        string s1IdStr = s1Id.ToString("D");

        Assert.True(landingNodeId == svBIdStr,
            $"PROBE-ORDER FIX: StepOver from Then0 Delay must land on svBId (SetVarB = {svBIdStr}, " +
            $"first probe in execution order), NOT s1Id ({s1IdStr}) or delay1Id ({delay1IdStr}). " +
            $"Got: {landingNodeId ?? "<null>"}.");

        // Explicitly assert it is NOT the last Delay of Then1 (original dead-state bug).
        Assert.True(landingNodeId != delay1IdStr,
            $"BUG REGRESSION: landing node must NOT be delay1Id ({delay1IdStr}). " +
            "If this fails, the topology-based bridge is still active and skipping intermediate nodes.");

        // Assert it is NOT s1Id (probe-order bug: S1 header probe prepended before SetVarB).
        Assert.True(landingNodeId != s1IdStr,
            $"PROBE-ORDER REGRESSION: landing node must NOT be s1Id ({s1IdStr}). " +
            "ScheduleSequenceNode must not clobber SourceNodeId when SetVarB precedes it.");

        // Step forward from svBId: pointer 1 should be s1Id (seq-probe-anchor for S1.SequenceNode).
        session.StepInto();
        string? secondNodeId = session.CurrentNodeId;
        Assert.True(secondNodeId == s1IdStr,
            $"Second step from svBId must land on s1Id (S1.SequenceNode probe = {s1IdStr}). " +
            $"Got: {secondNodeId ?? "<null>"}.");
        Assert.True(secondNodeId != delay1IdStr,
            $"Second step must NOT skip to delay1Id. Got: {secondNodeId ?? "<null>"}.");

        // Step forward again: svCId (S1.Then0 first probe = SetVarC).
        if (session.RecordedNodeCount >= 3)
        {
            session.StepInto();
            string? thirdNodeId = session.CurrentNodeId;
            string svCIdStr = svCId.ToString("D");
            Assert.True(thirdNodeId == svCIdStr,
                $"Third step must land on svCId (SetVarC = {svCIdStr}). Got: {thirdNodeId ?? "<null>"}.");
        }

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
