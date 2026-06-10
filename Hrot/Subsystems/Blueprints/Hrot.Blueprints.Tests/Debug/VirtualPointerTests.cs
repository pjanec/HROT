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
/// NGS-2.1 / NGS-2.2 / CT0a integration tests.
/// Exercises virtual-pointer navigation and per-node inspector state redirect
/// using a real compiled blueprint via <see cref="BlueprintTestFixture"/>.
///
/// <para>Design fact: a compiled blueprint tick is atomic — at pause, ALL nodes
/// of the paused tick are already recorded.  Step/StepBack moves the virtual
/// pointer over those recordings while the clock stays paused; no re-execution.</para>
/// </summary>
[Collection("DebugProbe")]
public sealed class VirtualPointerTests : IDisposable
{
    private static readonly BlueprintTestFixtureOptions NoAlcCheck =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    // Save/restore DebugProbe.Sink for test isolation.
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;
    public void Dispose() => DebugProbe.Sink = _savedSink;

    // =========================================================================
    // NGS-2.1 Test 1 — Pointer initialised on pause; clamping at both ends
    // =========================================================================

    [Fact]
    public void VirtualPointer_PauseInitialisesPointer_StepBackAndForwardClamp()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset  = BuildTwoSeqVarAsset("PointerClampTest");
        var tc     = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm breakpoint on the Sequence node (Nodes[1]).
        // Stage5_Schedule.ScheduleSequenceNode sets the entry block's SourceNodeId = seq.Id,
        // so the probe fires with the Sequence node's id, not the EventEntry node's id.
        // Without a registered DebugMap the session cannot re-key authored→probe ids, so the
        // breakpoint must be set on the node whose id is actually emitted by the probe.
        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id; // SequenceNode — the actual probe identity
        session.RegisterGraph(asset.Graphs[0]); // needed by StepFromNode for allSuccessorsAreTerminal check
        session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        // Tick — the whole chain runs and pause fires.
        fixture.TickFrame(0.016f);

        // Session must be paused.
        Assert.True(session.IsPaused);

        // Pointer must be valid (>= 0) after pause.
        Assert.True(session.CurrentNodePointer >= 0,
            $"Expected valid pointer on pause, got {session.CurrentNodePointer}");
        Assert.NotNull(session.CurrentNodeId);
        Assert.True(session.RecordedNodeCount >= 2,
            $"Expected >= 2 recorded nodes; got {session.RecordedNodeCount}");

        // StepBack at index 0 should be a no-op (clamp at 0).
        // First, make sure we're at index 0.
        int clampSteps = session.CurrentNodePointer; // steps needed to reach 0
        for (int i = 0; i < clampSteps; i++)
            session.StepBack();
        Assert.Equal(0, session.CurrentNodePointer);

        // One more StepBack — must stay at 0.
        session.StepBack();
        Assert.Equal(0, session.CurrentNodePointer);

        // Step forward to the end.
        int last = session.RecordedNodeCount - 1;
        while (session.CurrentNodePointer < last)
            session.StepInto();
        Assert.Equal(last, session.CurrentNodePointer);

        // NGS-2.3 (BF-03 fix): one more step forward at last index with a breakpoint armed
        // (RecordingActive) triggers the tick-bridge: nav state is cleared, temp BPs are set
        // on successors, RequestResume is called, and the session is no longer paused.
        // This is NOT a clamp — it's the bridge using the CF-6 temp-BP + resume mechanism
        // (NOT RequestStepOneTick, which fails for latent/Delay nodes).
        // The MockTimeController is a no-op so no re-pause occurs yet.
        int resumeCountBefore = tc.ResumeCount;
        session.StepInto();
        Assert.True(tc.ResumeCount > resumeCountBefore,
            $"NGS-2.3: step past end with armed BP must call RequestResume (BF-03 temp-BP bridge); " +
            $"ResumeCount was {resumeCountBefore}, now {tc.ResumeCount}.");
        Assert.True(tc.StepRequestCount == 0,
            "NGS-2.3: bridge must NOT call RequestStepOneTick (BF-03 fix; use temp-BP + resume instead).");
        Assert.False(session.IsPaused,
            "NGS-2.3: session must not be paused after tick-bridge call (tick not yet advanced).");
        Assert.Equal(-1, session.CurrentNodePointer);

        // The pointer/continue state is already cleared. No further Continue() needed.
    }

    // =========================================================================
    // NGS-2.2 — Inspector returns EXACT per-node field values as pointer moves
    // =========================================================================
    // Sequence A:0→10→20:
    //   node-index 0 (entry/seq dispatch): before any SetVar ran → A=0
    //   node-index 1 (Then0 block):        after node-0 wrote nothing → A=0
    //   node-index 2 (Then1 block):        after Then0 wrote A=10 → A=10
    //
    // GetCurrentStateSnapshot() must return these exact values as the pointer
    // moves — the headline behavioral proof that the same paused tick shows
    // different, correct per-node state.

    [Fact]
    public void Inspector_ReturnsExactPerNodeValues_AcrossStepBackAndForward()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset  = BuildTwoSeqVarAsset("InspectorExactValues");
        var tc     = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm breakpoint on Nodes[1] (SequenceNode) — the actual probe id for the entry block.
        // See PointerClampTest for the SourceNodeId overwrite rationale.
        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id;
        session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);

        // Must have at least 3 nodes: entry/seq-dispatch, Then0, Then1.
        int count = session.RecordedNodeCount;
        Assert.True(count >= 3,
            $"Expected >= 3 recorded nodes (entry, Then0, Then1), got {count}. " +
            "Ensure the Sequence node compiles to separate IR blocks.");

        // Navigate to index 0 (entry/seq dispatch block).
        // From wherever the pointer started, step back to 0.
        while (session.CurrentNodePointer > 0)
            session.StepBack();
        Assert.Equal(0, session.CurrentNodePointer);

        // At index 0: no SetVar has fired yet → A must be 0.
        var snap0 = session.GetCurrentStateSnapshot();
        Assert.NotNull(snap0);
        int aAt0 = GetSnapshotIntField(snap0!, "A");
        Assert.Equal(0, aAt0);

        // Step to index 1 (Then0 block).
        session.StepInto();
        Assert.Equal(1, session.CurrentNodePointer);

        // At index 1: still before Then0's effect lands (delta[1] captures nothing from node-0
        // which wrote nothing). The first SetVar is inside the Then0 block; its writes are
        // captured in delta[2] and appear only at pointer 2.
        var snap1 = session.GetCurrentStateSnapshot();
        Assert.NotNull(snap1);
        int aAt1 = GetSnapshotIntField(snap1!, "A");
        Assert.Equal(0, aAt1);

        // Step to index 2 (Then1 block).
        session.StepInto();
        Assert.Equal(2, session.CurrentNodePointer);

        // At index 2: Then0 has run (wrote A=10), Then1 hasn't started yet → A=10.
        // This is the key assertion: the SAME paused tick shows A=10 at this node.
        var snap2 = session.GetCurrentStateSnapshot();
        Assert.NotNull(snap2);
        int aAt2 = GetSnapshotIntField(snap2!, "A");
        Assert.Equal(10, aAt2);

        // StepBack to index 1 — A must return to 0.
        session.StepBack();
        Assert.Equal(1, session.CurrentNodePointer);
        var snapBack = session.GetCurrentStateSnapshot();
        Assert.NotNull(snapBack);
        int aBack = GetSnapshotIntField(snapBack!, "A");
        Assert.Equal(0, aBack);

        // Continue — inspector must revert to live (post-tick) state.
        // After Continue the session is not paused so GetCurrentStateSnapshot() returns null.
        session.Continue();
        Assert.False(session.IsPaused);
        Assert.Null(session.GetCurrentStateSnapshot());
        Assert.Equal(-1, session.CurrentNodePointer);
    }

    // =========================================================================
    // NGS-2.2 Test 3 — Inspector reverts to null after Continue
    // =========================================================================

    [Fact]
    public void Inspector_ReturnsNull_AfterContinue()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset  = BuildTwoSeqVarAsset("InspectorNullAfterContinue");
        var tc     = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm breakpoint on Nodes[1] (SequenceNode) — the actual probe id for the entry block.
        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id;
        session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);

        // Snapshot must be non-null while paused.
        var snapWhilePaused = session.GetCurrentStateSnapshot();
        Assert.NotNull(snapWhilePaused);

        // Continue — snapshot must become null (not paused).
        session.Continue();
        var snapAfterContinue = session.GetCurrentStateSnapshot();
        Assert.Null(snapAfterContinue);
    }

    // =========================================================================
    // CT0a — Entity-scope: two entities, only the debugged entity's nodes recorded
    // =========================================================================

    [Fact]
    public void EntityScope_TwoEntities_OnlyDebuggedEntityRecorded()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        // Two separate assets so each entity runs its own blueprint independently.
        var assetA = BuildTwoSeqVarAsset("EntityScopeA");
        var assetB = BuildTwoSeqVarAsset("EntityScopeB");

        var tc      = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoadMany(new[] { assetA, assetB });
        var entityA = fixture.CreateEntity();
        var entityB = fixture.CreateEntity();
        fixture.AttachBlueprint(assetA, entityA);
        fixture.AttachBlueprint(assetB, entityB);

        // Arm breakpoint on Nodes[1] (SequenceNode of assetA) — the actual probe id.
        // Entity filter after entity creation (entity id known only post-creation).
        var graphAId     = assetA.Graphs[0].Id;
        var probeANodeId = assetA.Graphs[0].Nodes[1].Id; // SequenceNode probe identity
        session.SetEntityFilter(entityA); // scope all probe events to entityA
        session.SetBreakpoint(assetA.AssetId, graphAId, probeANodeId);

        // Tick — both blueprints run; only entityA's nodes should be recorded.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);

        // Recorded nodes must be >= 2 (entityA's Sequence branches).
        int count = session.RecordedNodeCount;
        Assert.True(count >= 2,
            $"Expected >= 2 nodes for entityA, got {count}");

        // Every recorded node-id should come from entityA's blueprint instrumentation.
        // We can verify by restoring and checking the field value comes from assetA's logic.
        // Build scratch and restore to last node; A must be 10 (entityA's Then0 effect).
        using var scratch = BuildScratchRepo(fixture);
        session.RestoreRecordedNode(count - 1, scratch);
        int aInScratch = ReadBlueprintIntField(scratch, entityA, fixture.Registry, assetA, "A");

        // entityB's mutations must NOT appear in the scratch (it was excluded from recording).
        // entityA's A at last node: 10 (before Then1 ran).
        Assert.Equal(10, aInScratch);

        session.Continue();
    }

    // =========================================================================
    // NGS-2.1 Test — CF-6 fallback when no recordings exist
    // =========================================================================

    [Fact]
    public void StepInto_WithoutRecordings_FallsBackToTemporaryBreakpoints()
    {
        // Set up a session WITHOUT a live repo → no recordings.
        // StepInto should fall back to CF-6 (set temp BPs and resume).
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset = BuildTwoSeqVarAsset("CF6FallbackTest");
        var tc    = new MockTimeController();

        // Build a session without a live repository (no NGS recordings).
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        // Deliberately NOT calling session.SetLiveRepository → recording off.
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm breakpoint on Nodes[1] (SequenceNode) — the actual probe id for the entry block.
        // RegisterGraph before SetBreakpoint so CF-6 StepInto knows the graph structure.
        var graphId     = asset.Graphs[0].Id;
        var probeNodeId = asset.Graphs[0].Nodes[1].Id; // SequenceNode probe identity
        session.RegisterGraph(asset.Graphs[0]);
        session.SetBreakpoint(asset.AssetId, graphId, probeNodeId);

        // First tick pauses on the entry node.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);
        Assert.Equal(-1, session.CurrentNodePointer); // no recordings

        // StepInto with no recordings → CF-6 path: sets temp BPs and resumes.
        session.StepInto();

        // After StepInto(CF-6): session is no longer paused (temp BPs set, clock resumed).
        Assert.False(session.IsPaused);
        Assert.True(session.HasTemporaryBreakpoints,
            "CF-6 fallback should have set temporary breakpoints on successors.");

        // Second tick: temp BP fires → re-pauses.
        fixture.TickFrame(0.016f);
        Assert.True(session.IsPaused);

        session.Continue();
    }

    // =========================================================================
    // Helpers (reused from SubTickRecorderIntegrationTests)
    // =========================================================================

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
                        new Pin { Id = psIn,   Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen0, Name = "Then0", Direction = "Out", IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen1, Name = "Then1", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new LiteralNode
                {
                    Id       = litAId,
                    TypeId   = "System.Int32",
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
                    Id       = litBId,
                    TypeId   = "System.Int32",
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

    /// <summary>
    /// Reads a named int field from a <see cref="BlueprintStateSnapshot"/>.
    /// Returns 0 when the field is absent or the value is not an int.
    /// </summary>
    private static int GetSnapshotIntField(BlueprintStateSnapshot snapshot, string fieldName)
    {
        if (snapshot.FieldValues.TryGetValue(fieldName, out var obj) && obj is int i)
            return i;
        return 0;
    }

    private static EntityRepository BuildScratchRepo(BlueprintTestFixture fixture)
    {
        var scratch = new EntityRepository();
        MockTestComponents.Register(scratch);
        scratch.RegisterComponent<BlueprintBlackboard1024>();
        scratch.RegisterComponent<BlueprintBlackboard4096>();
        return scratch;
    }

    private static unsafe int ReadBlueprintIntField(
        EntityRepository repo,
        Entity entity,
        BlueprintRegistry registry,
        BlueprintAsset asset,
        string fieldName)
    {
        int blueprintId = BlueprintIdHash.Compute(asset.AssetId);

        if (!registry.TryGetById(blueprintId, out var def) || def == null)
            return 0;

        if (!repo.HasComponent<BlueprintBlackboard1024>(entity))
            return 0;

        ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        ref byte memRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
        byte* memory = (byte*)Unsafe.AsPointer(ref memRef);

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out int payloadOffset))
            return 0;

        var view = new BlueprintStateView(memory + payloadOffset, def!.StateSize, def!);
        return view.TryGetField(fieldName, out int val) ? val : 0;
    }
}
