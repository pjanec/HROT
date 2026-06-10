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

// ---- Collection declaration (reuses the existing serialised DebugProbe collection) ----
// Test 1 and Test 3 do not touch DebugProbe.Sink, but Tests 2 and 4 do.
// All tests in this class run in the existing "DebugProbe" collection so they
// serialise with the rest of the probe tests for correctness.

/// <summary>
/// NGS-2.0 integration tests: wiring <see cref="SubTickSnapshotRecorder"/> into
/// <see cref="BlueprintDebugSession"/> via the live debug pipeline.
///
/// <para>These tests exercise the full compile → tick → record → restore path
/// using a real compiled blueprint and assert REAL restored runtime values.</para>
/// </summary>
[Collection("DebugProbe")]
public sealed class SubTickRecorderIntegrationTests : IDisposable
{
    // ── shared test constants ────────────────────────────────────────────────

    private static readonly BlueprintTestFixtureOptions NoAlcCheck =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    // Save / restore DebugProbe.Sink for test isolation.
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;

    public void Dispose() => DebugProbe.Sink = _savedSink;

    // =========================================================================
    // Test 1 — Recording OFF when unarmed
    // RecordedNodeCount == 0 and GlobalVersion/SimulationTick advance in lockstep.
    // =========================================================================

    [Fact]
    public void RecordingOff_WhenUnarmed_ZeroRecordedNodes_AndVersionsInLockstep()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset = BlueprintAssetBuilder
            .Instance("UnarmedTest")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var session = new BlueprintDebugSession(
            fixture.Registry, fixture.View, new MockTimeController());
        session.SetLiveRepository(fixture.World);
        session.Attach(); // sets DebugProbe.Sink = session

        // NO breakpoint armed → RecordingActive is false.

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        uint stBefore = fixture.World.SimulationTick;
        uint gvBefore = fixture.World.GlobalVersion;

        fixture.TickFrame(0.016f);

        uint stAfter = fixture.World.SimulationTick;
        uint gvAfter = fixture.World.GlobalVersion;

        // No recorder work: recorded node count must be zero.
        Assert.Equal(0, session.RecordedNodeCount);

        // In the test harness, EntityRepository.Tick() is never called by TickFrame:
        // MockSimulationView tracks its own _tick counter, not _repo's simulation clock.
        // Therefore both _repo.SimulationTick and _repo.GlobalVersion stay at their
        // baseline values — no BumpMemoryVersion was called because RecordingActive=false.
        // "Lockstep" = zero extra memory-version bumps; GV === ST (no divergence).
        Assert.Equal(stBefore, stAfter);                    // ST unchanged (no _repo.Tick)
        Assert.Equal(gvBefore, gvAfter);                    // GV unchanged (no BumpMemoryVersion)
        Assert.Equal(stAfter, gvAfter);                     // GV and ST stay in sync (lockstep)
    }

    // =========================================================================
    // Test 2 — Recording ON when armed: per-node values differ within one tick
    // Two SetVariable nodes increment a counter; restore shows pre-increment state
    // at each node boundary.
    // =========================================================================

    [Fact]
    public unsafe void RecordingOn_WhenArmed_PerNodeValuesAreDifferentWithinOneTick()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        // Build: Entry → Sequence(Then0: LiteralInt(10) → SetVariable(A=10) → [fall-through],
        //                          Then1: LiteralInt(20) → SetVariable(A=20) → Return)
        //
        // A Sequence node causes the compiler to allocate SEPARATE IR blocks for each
        // branch, each with its own SourceNodeId → its own probe. Both branches execute
        // sequentially in ONE tick, so at least 3 probes fire:
        //   probe 0: Entry/Sequence dispatch block
        //   probe 1: Then0 block (SetVar A=10)
        //   probe 2: Then1 block (SetVar A=20)
        // After the tick, A=20 (Then1 overwrites Then0).
        var asset = BuildTwoSeqVarAsset("SequencedIncrements");

        var tc = new MockTimeController();
        var session = new BlueprintDebugSession(
            fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        // Compile in Debug mode so all exec blocks get probes.
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm a breakpoint on the entry node so RecordingActive = true.
        var graphId     = asset.Graphs[0].Id;
        var entryNodeId = asset.Graphs[0].Nodes[0].Id; // EventEntry (owns the dispatch block)
        session.SetBreakpoint(asset.AssetId, graphId, entryNodeId);

        // Tick: blueprints run; OnNodeEnter fires for each exec block;
        // recorder captures a delta per block.
        fixture.TickFrame(0.016f);
        session.Continue();

        // We must have at least 2 recorded entries (Sequence dispatch + at least one Then branch).
        Assert.True(session.RecordedNodeCount >= 2,
            $"Expected >= 2 recorded nodes (Sequence produces multiple exec blocks) but got {session.RecordedNodeCount}. " +
            "Check that DebugProbeInsertion inserts probes in each SequenceNode branch block.");

        // Build a scratch repo with the same component registrations for restore.
        using var scratch = BuildScratchRepo(fixture);

        // The whole-tick final value should be 20 (Then1 overwrites Then0).
        var liveState = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(liveState);
        liveState!.Value.TryGetField("A", out int finalCount);
        Assert.Equal(20, finalCount);

        // Restore to node 0 (before any SetVariable ran): A must be the initial default = 0.
        session.RestoreRecordedNode(0, scratch);
        int countAtNode0 = ReadBlueprintIntField(scratch, entity, fixture.Registry, asset, "A");
        Assert.Equal(0, countAtNode0);

        // Restore to the last recorded node (before the last block's effect was applied).
        // With Sequence(SetVar A=10, SetVar A=20), state before last block = A=10
        // (Then0 wrote 10, Then1 hasn't written yet).
        int lastIdx = session.RecordedNodeCount - 1;
        session.RestoreRecordedNode(lastIdx, scratch);
        int countAtLastNode = ReadBlueprintIntField(scratch, entity, fixture.Registry, asset, "A");

        // CT0b (BATCH-03): Assert EXACT intermediate value at the last node before-entry.
        // With Sequence(SetVar A=10, SetVar A=20), execution order: Entry→Seq→Then0(A=10)→Then1(A=20).
        // "Last recorded node" is the Then1 branch; state BEFORE Then1 ran = A=10 (Then0 already ran).
        // This is the precise proof: the same paused tick shows A=0 (node0), A=10 (node2 pre-Then1),
        // A=20 (post-tick final) — proving exact sub-tick granularity.
        Assert.Equal(10, countAtLastNode);
    }

    // =========================================================================
    // Test 3 — SimulationTick frozen during recorded tick; GlobalVersion advances
    // =========================================================================

    [Fact]
    public void SimulationTickFrozen_GlobalVersionAdvances_DuringRecordedTick()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset = BlueprintAssetBuilder
            .Instance("SimTickTest")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var session = new BlueprintDebugSession(
            fixture.Registry, fixture.View, new MockTimeController());
        session.SetLiveRepository(fixture.World);
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm a breakpoint to enable recording.
        var graphId     = asset.Graphs[0].Id;
        var entryNodeId = asset.Graphs[0].Nodes[0].Id;
        session.SetBreakpoint(asset.AssetId, graphId, entryNodeId);

        uint stBefore = fixture.World.SimulationTick;
        uint gvBefore = fixture.World.GlobalVersion;

        fixture.TickFrame(0.016f);
        session.Continue();

        uint stAfter = fixture.World.SimulationTick;
        uint gvAfter = fixture.World.GlobalVersion;

        int nodeCount = session.RecordedNodeCount;

        // In the test harness, EntityRepository.Tick() is never called by TickFrame:
        // MockSimulationView owns a separate _tick counter, so _repo.SimulationTick stays frozen.
        // SimulationTick must remain at stBefore (no _repo.Tick() call in fixture).
        Assert.Equal(stBefore, stAfter);

        // GlobalVersion advances by exactly nodeCount (one BumpMemoryVersion per RecordNodeEntry).
        // There must be at least 1 recorded node (the entry node), so GV must have advanced.
        Assert.True(nodeCount >= 1, "Expected at least the entry node to be recorded.");
        Assert.Equal(gvBefore + (uint)nodeCount, gvAfter);

        // The semantic tick clock (SimulationTick) is frozen while GV advanced: they diverge.
        Assert.True(gvAfter > stAfter,
            $"GlobalVersion ({gvAfter}) must exceed SimulationTick ({stAfter}) when recording is active, " +
            "proving sub-tick BumpMemoryVersion bumps GV without advancing the semantic tick clock.");
    }

    // =========================================================================
    // Test 4 — Null safety: no live repo → recording silently off, no NPE
    // =========================================================================

    [Fact]
    public void NoLiveRepo_RecordingDisabled_NoPExceptionOnTick()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var asset = BlueprintAssetBuilder
            .Instance("NullRepoTest")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var session = new BlueprintDebugSession(
            fixture.Registry, fixture.View, new MockTimeController());
        // NOTE: SetLiveRepository is NOT called → _liveRepo = null.
        session.Attach();

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Arm a breakpoint — but recording should stay off because no live repo.
        var graphId     = asset.Graphs[0].Id;
        var entryNodeId = asset.Graphs[0].Nodes[0].Id;
        session.SetBreakpoint(asset.AssetId, graphId, entryNodeId);

        // Should not throw; session.Continue() after pause to let test clean up.
        fixture.TickFrame(0.016f);
        session.Continue();

        // Recording must be zero (silently off, no crash).
        Assert.Equal(0, session.RecordedNodeCount);
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

        // Variable A (the one we'll observe across sub-tick snapshots).
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
                // Then0: Literal(10) → SetVariable(A=10)  [no Return: falls through to Then1]
                new LiteralNode
                {
                    Id       = litAId,
                    TypeId   = "System.Int32",
                    ValueJson = "10",
                    Pins = new()
                    {
                        new Pin { Id = pLitAOut, Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() },
                    },
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
                // Then1: Literal(20) → SetVariable(A=20) → Return
                new LiteralNode
                {
                    Id       = litBId,
                    TypeId   = "System.Int32",
                    ValueJson = "20",
                    Pins = new()
                    {
                        new Pin { Id = pLitBOut, Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() },
                    },
                },
                new SetVariableNode
                {
                    Id         = svBId,
                    VariableId = varA.Id.ToString(), // reuse same var A (overwrite with 20)
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
                // Then0 ends without explicit Return — falls through to Then1 per Sequence semantics.
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
    /// Builds a scratch <see cref="EntityRepository"/> with the same component types
    /// as the fixture's live repo (BB1024 tier is sufficient for test blueprints).
    /// The caller is responsible for disposing it.
    /// </summary>
    private static EntityRepository BuildScratchRepo(BlueprintTestFixture fixture)
    {
        var scratch = new EntityRepository();
        MockTestComponents.Register(scratch);
        scratch.RegisterComponent<BlueprintBlackboard1024>();
        scratch.RegisterComponent<BlueprintBlackboard4096>();
        return scratch;
    }

    /// <summary>
    /// Reads a named <c>int</c> blueprint variable from an entity in an arbitrary repo.
    /// Returns 0 if the slot or field is not found.
    /// </summary>
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

        // Try BB1024 tier (test assets always use BB1024 in the fixture).
        if (!repo.HasComponent<BlueprintBlackboard1024>(entity))
            return 0;

        ref var bb = ref repo.GetComponentRW<BlueprintBlackboard1024>(entity);
        ref byte memRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb);
        byte* memory = (byte*)Unsafe.AsPointer(ref memRef);

        if (!BlueprintBlackboardPartitions.TryGetSlotOffset(memory, blueprintId, out int payloadOffset))
            return 0;

        var view = new BlueprintStateView(memory + payloadOffset, def.StateSize, def);
        return view.TryGetField(fieldName, out int val) ? val : 0;
    }
}
