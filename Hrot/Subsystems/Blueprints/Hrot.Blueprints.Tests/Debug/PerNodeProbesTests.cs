using Fdp.Core;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Mocks;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Debug;

// ════════════════════════════════════════════════════════════════════════════════
// BPDBG-PERNODE-PROBES: per-exec-node debug probe tests
//
// Prior to this fix, debug probes were per-block (one probe per IrBlock, keyed to
// block.SourceNodeId).  A SetVariable node fused with a following LatentDelayNode
// in the same block had NO probe → not breakpointable / steppable / recorded.
//
// The fix:
//   • Stage5_Schedule tags each exec-node's first statement with ExecEntryNodeId.
//   • DebugProbeInsertion inserts a NodeEnter probe before each tagged statement.
//   • BreakpointTargets is now one-to-one (nodeId → nodeId) rather than many-to-one.
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Gates the BPDBG-PERNODE-PROBES feature.  Tests run with the real BlueprintCompiler
/// + BlueprintTestFixture end-to-end pipeline.  All tests that touch DebugProbe.Sink
/// are in the serialised "DebugProbe" collection for test isolation.
/// </summary>
[Collection("DebugProbe")]
public sealed class PerNodeProbesTests : IDisposable
{
    private static readonly BlueprintTestFixtureOptions NoAlcCheck =
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false };

    // Save / restore DebugProbe.Sink.
    private readonly IBlueprintProbeSink? _savedSink = DebugProbe.Sink;
    public void Dispose() => DebugProbe.Sink = _savedSink;

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static CompileOptions DebugOptions => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static (BlueprintAsset asset, Guid graphId, Guid setVarId, Guid delayId)
        BuildSetVarThenDelayAsset(string name)
    {
        // Graph: Entry → SetVariable(X) → Delay(0.0f) → Return
        // Stage5 schedules these into ONE block (block 0):
        //   Entry (no statements) → SetVar statements → LatentOp (Delay) → suspend.
        // Before fix: only Delay gets a probe (SourceNodeId = Delay.Id overwrites Entry.Id).
        // After fix:  both SetVar and Delay get their own probes in execution order.
        var asset = BlueprintAssetBuilder
            .Instance(name)
            .WithVariable("X", typeof(int))
            .WithGraph("Tick", g => g
                .Entry()
                .SetVariable("X", "0")
                .Delay(0.0f)
                .Return())
            .Build();

        var graphId   = asset.Graphs[0].Id;
        var setVarId  = asset.Graphs[0].Nodes[1].Id; // [0]=Entry,[1]=SetVar,[2]=Delay,[3]=Return
        var delayId   = asset.Graphs[0].Nodes[2].Id;
        return (asset, graphId, setVarId, delayId);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 1 — One-to-one BreakpointTargets mapping
    //
    // DebugMap.BreakpointTargets[SetVarId] == SetVarId  (not Delay.Id)
    // DebugMap.BreakpointTargets[DelayId]  == DelayId
    // The two targets must be distinct (SetVar ≠ Delay).
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BreakpointTargets_SetVarAndDelay_AreDistinctAndSelfMapped()
    {
        var (asset, _, setVarId, delayId) = BuildSetVarThenDelayAsset("PerNodeMap1");

        var compiler = new BlueprintCompiler();
        var result   = compiler.Compile(asset, DebugOptions);
        Assert.True(result.Succeeded,
            "Compilation failed: " + string.Join("; ", result.Diagnostics.Select(d => d.Message)));

        var targets = result.DebugMap!.BreakpointTargets;

        // SetVar must map to itself (one-to-one).
        Assert.True(targets.ContainsKey(setVarId),
            $"BreakpointTargets must contain SetVar ({setVarId:D}).");
        Assert.Equal(setVarId, targets[setVarId]);

        // Delay must map to itself (one-to-one).
        Assert.True(targets.ContainsKey(delayId),
            $"BreakpointTargets must contain Delay ({delayId:D}).");
        Assert.Equal(delayId, targets[delayId]);

        // They must be distinct targets (pre-fix both would map to Delay.Id).
        Assert.NotEqual(targets[setVarId], targets[delayId]);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 2 — Breakpoint on SetVar (in SetVar+Delay block) hits before Delay runs
    //
    // The primary reported bug: SetVar has no probe → SetBreakpoint(SetVar) never fires.
    // After the fix: Probe(SetVar.Id) fires before Probe(Delay.Id) because SetVar
    // executes before the latent suspend.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Breakpoint_OnSetVar_InFusedSetVarDelayBlock_Hits()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (asset, graphId, setVarId, delayId) = BuildSetVarThenDelayAsset("PerNodeBP2");

        var tc      = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.Attach(); // overrides DebugProbe.Sink

        // Register debug map so SetBreakpoint resolves ProbeNodeId via BreakpointTargets.
        var compiler     = new BlueprintCompiler();
        var compileResult = compiler.Compile(asset, DebugOptions);
        Assert.True(compileResult.Succeeded,
            string.Join("; ", compileResult.Diagnostics.Select(d => d.Message)));
        session.RegisterDebugMap(compileResult.DebugMap!);

        // Set breakpoint on SetVar (NOT on Delay).
        session.SetBreakpoint(asset.AssetId, graphId, setVarId);

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Tick: blueprint runs; probe for SetVar fires → breakpoint should hit and pause.
        fixture.TickFrame(0.016f);

        Assert.True(tc.PauseRequestCount >= 1,
            $"Breakpoint on SetVar ({setVarId:D}) must fire. " +
            $"PauseRequestCount={tc.PauseRequestCount}. " +
            $"BreakpointTargets: {TargetsString(compileResult.DebugMap!.BreakpointTargets)}");

        session.Continue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 3 — Probe ordering: SetVar probe fires before Delay probe
    //
    // In the fused block the execution order is SetVar (synchronous) then Delay
    // (suspend).  The probe for SetVar must arrive before the probe for Delay in
    // the NodeEntries sequence recorded by CapturingDebugSession.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProbeOrder_SetVarProbeFiresBeforeDelayProbe_InFusedBlock()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (asset, _, setVarId, delayId) = BuildSetVarThenDelayAsset("PerNodeOrder3");

        // Use CapturingDebugSession (already installed as DebugProbe.Sink by fixture ctor).
        // It records every OnNodeEnter call in NodeEntries in arrival order.
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var entries    = fixture.DebugSession.NodeEntries;
        var setVarStr  = setVarId.ToString("D");
        var delayStr   = delayId.ToString("D");

        // Both probes must have fired.
        Assert.True(entries.Any(e => e.NodeId == setVarStr),
            $"SetVar probe ({setVarStr}) must fire. Recorded: [{string.Join(", ", entries.Select(e => e.NodeId.Substring(0, 8)))}]");
        Assert.True(entries.Any(e => e.NodeId == delayStr),
            $"Delay probe ({delayStr}) must fire. Recorded: [{string.Join(", ", entries.Select(e => e.NodeId.Substring(0, 8)))}]");

        // SetVar must appear BEFORE Delay in the recorded sequence.
        int setVarIdx = entries.ToList().FindIndex(e => e.NodeId == setVarStr);
        int delayIdx  = entries.ToList().FindIndex(e => e.NodeId == delayStr);
        Assert.True(setVarIdx < delayIdx,
            $"SetVar probe (index {setVarIdx}) must fire before Delay probe (index {delayIdx}). " +
            $"Sequence: [{string.Join(", ", entries.Select(e => e.NodeId.Substring(0, 8)))}]");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 4 — Straight-line synchronous: Entry → SetVar_A → SetVar_B → Return
    //
    // All four exec nodes share ONE block with no latent.
    // Both SetVar nodes must each get their own probe.
    // Breakpoint on SetVar_B must fire.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void StraightLine_TwoSetVars_BothBreakpointable()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        // Graph: Entry → SetVariable(A=0) → SetVariable(B=0) → Return
        // All four nodes schedule into one block (no latent, no branch, no sequence).
        var asset = BlueprintAssetBuilder
            .Instance("PerNodeStraightLine4")
            .WithVariable("A", typeof(int))
            .WithVariable("B", typeof(int))
            .WithGraph("Tick", g => g
                .Entry()
                .SetVariable("A", "0")
                .SetVariable("B", "0")
                .Return())
            .Build();

        var graphId  = asset.Graphs[0].Id;
        var setVarAId = asset.Graphs[0].Nodes[1].Id; // SetVar A
        var setVarBId = asset.Graphs[0].Nodes[2].Id; // SetVar B

        var tc      = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.Attach();

        var compiler     = new BlueprintCompiler();
        var compileResult = compiler.Compile(asset, DebugOptions);
        Assert.True(compileResult.Succeeded,
            string.Join("; ", compileResult.Diagnostics.Select(d => d.Message)));
        session.RegisterDebugMap(compileResult.DebugMap!);

        // Both SetVar nodes must appear in BreakpointTargets (one-to-one).
        var targets = compileResult.DebugMap!.BreakpointTargets;
        Assert.True(targets.ContainsKey(setVarAId),
            $"SetVar_A must be in BreakpointTargets. Targets: {TargetsString(targets)}");
        Assert.Equal(setVarAId, targets[setVarAId]);
        Assert.True(targets.ContainsKey(setVarBId),
            $"SetVar_B must be in BreakpointTargets. Targets: {TargetsString(targets)}");
        Assert.Equal(setVarBId, targets[setVarBId]);

        // Breakpoint on the SECOND SetVar must fire.
        session.SetBreakpoint(asset.AssetId, graphId, setVarBId);

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        Assert.True(tc.PauseRequestCount >= 1,
            $"Breakpoint on SetVar_B ({setVarBId:D}) must fire. " +
            $"PauseRequestCount={tc.PauseRequestCount}. " +
            $"Targets: {TargetsString(targets)}");

        session.Continue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 5 — Data nodes are NOT probed / NOT in BreakpointTargets
    //
    // LiteralNode and GetVariableNode are pure data nodes.  They must NOT appear
    // in BreakpointTargets and no Probe statement should be emitted for them.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DataNodes_AreNotInBreakpointTargets()
    {
        // Build manually to have explicit control over which nodes are data vs exec.
        // Graph: Entry → SetVar(X=literal(42)) → Return
        // Nodes: [0]=Entry, [1]=Literal(42), [2]=SetVar, [3]=Return
        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var litId    = Guid.NewGuid();
        var setVarId = Guid.NewGuid();
        var retId    = Guid.NewGuid();
        var varId    = Guid.NewGuid();

        var peOut   = Guid.NewGuid();
        var svIn    = Guid.NewGuid();
        var svOut   = Guid.NewGuid();
        var svVal   = Guid.NewGuid();
        var litOut  = Guid.NewGuid();
        var retIn   = Guid.NewGuid();

        var varX = new VariableDecl
        {
            Id   = varId,
            Name = "X",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        };

        var graph = new Graph
        {
            Id    = graphId,
            Name  = "Tick",
            Kind  = GraphKind.Function,
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } },
                },
                new LiteralNode
                {
                    Id        = litId,
                    TypeId    = "System.Int32",
                    ValueJson = "42",
                    Pins = new() { new Pin { Id = litOut, Name = "Value", Direction = "Out", IsExec = false, TypeRef = new() } },
                },
                new SetVariableNode
                {
                    Id         = setVarId,
                    VariableId = varId.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = svIn,  Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = svOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new Pin { Id = svVal, Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new() },
                    },
                },
                new ReturnNode
                {
                    Id     = retId,
                    Status = NodeStatus.Success,
                    Pins   = new() { new Pin { Id = retIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId,  FromPinId = peOut,  ToNodeId = setVarId, ToPinId = svIn  },
                new() { FromNodeId = litId,    FromPinId = litOut, ToNodeId = setVarId, ToPinId = svVal },
                new() { FromNodeId = setVarId, FromPinId = svOut,  ToNodeId = retId,    ToPinId = retIn },
            },
            Inputs  = new(),
            Outputs = new(),
        };

        var asset = new BlueprintAsset
        {
            AssetId          = Guid.NewGuid(),
            Name             = "PerNodeDataNodes5",
            Dispatch         = AssetDispatchKind.Instance,
            Parameters       = new(),
            WorkingState     = new(),
            Variables        = new() { varX },
            EventDispatchers = new(),
            CustomEvents     = new(),
            CallablePeers    = new(),
            Graphs           = new() { graph },
            Header           = new Header(),
        };

        var compiler      = new BlueprintCompiler();
        var compileResult = compiler.Compile(asset, DebugOptions);
        Assert.True(compileResult.Succeeded,
            string.Join("; ", compileResult.Diagnostics.Select(d => d.Message)));

        var targets = compileResult.DebugMap!.BreakpointTargets;

        // Exec nodes must be present (one-to-one).
        Assert.True(targets.ContainsKey(setVarId),
            $"SetVar must be in BreakpointTargets. Targets: {TargetsString(targets)}");

        // Pure data nodes must NOT appear in BreakpointTargets.
        Assert.False(targets.ContainsKey(litId),
            $"LiteralNode ({litId:D}) must NOT be in BreakpointTargets — it is a data node. " +
            $"Targets: {TargetsString(targets)}");

        // Also verify via the CapturingDebugSession's IsNodeBreakpointable helper.
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var session = fixture.DebugSession;
        session.RegisterDebugMap(compileResult.DebugMap!);

        Assert.False(session.IsNodeBreakpointable(asset.AssetId, graphId, litId),
            $"LiteralNode must not be breakpointable.");
        Assert.True(session.IsNodeBreakpointable(asset.AssetId, graphId, setVarId),
            $"SetVar must be breakpointable.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 6 — Probe sequence recorded: both SetVar and Delay recorded in order
    //
    // SubTickSnapshotRecorder integration: RecordedNodeCount reflects both nodes
    // when a breakpoint is armed and the fused block runs.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RecordedNodeCount_IncludesBothSetVarAndDelay_InFusedBlock()
    {
        using var fixture = new BlueprintTestFixture(NoAlcCheck);
        var (asset, graphId, setVarId, delayId) = BuildSetVarThenDelayAsset("PerNodeRecorder6");

        var tc      = new MockTimeController();
        var session = new BlueprintDebugSession(fixture.Registry, fixture.View, tc);
        session.SetLiveRepository(fixture.World);
        session.Attach();

        var compiler      = new BlueprintCompiler();
        var compileResult = compiler.Compile(asset, DebugOptions);
        Assert.True(compileResult.Succeeded,
            string.Join("; ", compileResult.Diagnostics.Select(d => d.Message)));
        session.RegisterDebugMap(compileResult.DebugMap!);

        // Arm breakpoint on the SetVar so recording is active.
        session.SetBreakpoint(asset.AssetId, graphId, setVarId);

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Tick: SetVar probe fires (BP pauses), Delay probe recorded but session stays paused.
        // The recorder captures a delta per NodeEnter; both SetVar and Delay must appear.
        fixture.TickFrame(0.016f);

        Assert.True(session.IsPaused,
            "Session must be paused after tick (breakpoint on SetVar).");
        Assert.True(session.RecordedNodeCount >= 2,
            $"RecordedNodeCount must be >= 2 (SetVar + Delay); got {session.RecordedNodeCount}.");

        // Verify the virtual pointer shows SetVar as the landing node (first recorded = paused at).
        // Pointer starts at the breakpoint node after pause.
        Assert.True(session.CurrentNodePointer >= 0,
            $"CurrentNodePointer must be valid (>= 0); got {session.CurrentNodePointer}.");

        // The node at pointer 0 must be SetVar (the first probe that fires).
        // Walk to pointer 0 to confirm.
        while (session.CurrentNodePointer > 0)
            session.StepBack();
        Assert.Equal(0, session.CurrentNodePointer);
        Assert.Equal(setVarId.ToString("D"), session.CurrentNodeId);

        // Walk to pointer 1: must be Delay (second probe in the fused block).
        session.StepInto();
        Assert.Equal(1, session.CurrentNodePointer);
        Assert.Equal(delayId.ToString("D"), session.CurrentNodeId);

        session.Continue();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 7 — Probe ORDER: SetVarB → Sequence S1 records svBId THEN s1Id
    //
    // BPDBG-SEQ-PROBE-ORDER gate: when an exec node (SetVarB) precedes a
    // SequenceNode (S1) in the same scheduled block, the probes must fire in
    // EXECUTION order (svBId first, s1Id second), NOT reversed.
    //
    // Before fix: ScheduleSequenceNode overwrote bb.SourceNodeId with s1Id
    //   (clobbering svBId) and emitted no ExecEntryNodeId statement, causing
    //   DebugProbeInsertion to prepend a header probe for s1Id BEFORE svBId's
    //   per-node probe.  Result: [s1Id, svBId, …] — execution order inverted.
    //
    // After fix: ??= preserves svBId as SourceNodeId; seq-probe-anchor emits
    //   ExecEntryNodeId=s1Id at the correct position.  Result: [svBId, s1Id, …].
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProbeOrder_SetVarBThenSequenceS1_RecordsSvBIdBeforeS1Id()
    {
        // Graph: Entry → SetVarB(svBId) → S1(s1Id) { Then0: SetVarC(svCId) → Return }
        // SetVarB and S1 land in the SAME IR block (S1 is the exec successor of SetVarB).
        // S1 has no exec-in → its Then0 block is reached via the Goto in the S1 block.
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var svBId   = Guid.NewGuid();
        var s1Id    = Guid.NewGuid();
        var svCId   = Guid.NewGuid();
        var retId   = Guid.NewGuid();

        var varB = new VariableDecl { Id = Guid.NewGuid(), Name = "B",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" } };
        var varC = new VariableDecl { Id = Guid.NewGuid(), Name = "C",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var peOut    = Guid.NewGuid();
        var pSvBIn   = Guid.NewGuid(); var pSvBOut = Guid.NewGuid();
        var ps1In    = Guid.NewGuid(); var ps1Then0 = Guid.NewGuid();
        var pSvCIn   = Guid.NewGuid(); var pSvCOut  = Guid.NewGuid();
        var pRetIn   = Guid.NewGuid();

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new System.Collections.Generic.List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SetVariableNode { Id = svBId, VariableId = varB.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvBIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pSvBOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new SequenceNode { Id = s1Id,
                    Pins = new()
                    {
                        new Pin { Id = ps1In,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = ps1Then0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new SetVariableNode { Id = svCId, VariableId = varC.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvCIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pSvCOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new System.Collections.Generic.List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = peOut,    ToNodeId = svBId,  ToPinId = pSvBIn   },
                new() { FromNodeId = svBId,   FromPinId = pSvBOut,  ToNodeId = s1Id,   ToPinId = ps1In    },
                new() { FromNodeId = s1Id,    FromPinId = ps1Then0, ToNodeId = svCId,  ToPinId = pSvCIn   },
                new() { FromNodeId = svCId,   FromPinId = pSvCOut,  ToNodeId = retId,  ToPinId = pRetIn   },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId          = Guid.NewGuid(),
            Name             = "ProbeOrderSetVarSeq7",
            Dispatch         = AssetDispatchKind.Instance,
            Parameters       = new(), WorkingState = new(),
            Variables        = new() { varB, varC },
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs           = new() { graph },
            Header           = new Header(),
        };

        // ---- Compile and check BreakpointTargets one-to-one ----
        var compiler      = new BlueprintCompiler();
        var compileResult = compiler.Compile(asset, DebugOptions);
        Assert.True(compileResult.Succeeded,
            string.Join("; ", compileResult.Diagnostics.Select(d => d.Message)));

        var targets = compileResult.DebugMap!.BreakpointTargets;

        // Both SetVarB and S1 must be in BreakpointTargets (one-to-one).
        Assert.True(targets.ContainsKey(svBId),
            $"SetVarB ({svBId:D}) must be in BreakpointTargets. Targets: {TargetsString(targets)}");
        Assert.Equal(svBId, targets[svBId]);

        Assert.True(targets.ContainsKey(s1Id),
            $"S1.Sequence ({s1Id:D}) must be in BreakpointTargets. Targets: {TargetsString(targets)}");
        Assert.Equal(s1Id, targets[s1Id]);

        // ---- Record probe arrival order ----
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var entries   = fixture.DebugSession.NodeEntries;
        var svBIdStr  = svBId.ToString("D");
        var s1IdStr   = s1Id.ToString("D");

        // Both probes must fire.
        Assert.True(entries.Any(e => e.NodeId == svBIdStr),
            $"SetVarB probe ({svBIdStr[..8]}) must fire. " +
            $"Recorded: [{string.Join(", ", entries.Select(e => e.NodeId[..8]))}]");
        Assert.True(entries.Any(e => e.NodeId == s1IdStr),
            $"S1.Sequence probe ({s1IdStr[..8]}) must fire. " +
            $"Recorded: [{string.Join(", ", entries.Select(e => e.NodeId[..8]))}]");

        // svBId MUST appear BEFORE s1Id (execution order, not header-probe order).
        int svBIdx = entries.ToList().FindIndex(e => e.NodeId == svBIdStr);
        int s1Idx  = entries.ToList().FindIndex(e => e.NodeId == s1IdStr);

        Assert.True(svBIdx < s1Idx,
            $"PROBE-ORDER FIX: SetVarB probe (index {svBIdx}) must fire BEFORE S1.Sequence probe " +
            $"(index {s1Idx}). Before the fix the header probe for S1 was prepended ahead of SetVarB. " +
            $"Recorded: [{string.Join(", ", entries.Select(e => e.NodeId[..8]))}]");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 8 — Single-sequence-first block has exactly ONE probe for the sequence
    //
    // BPDBG-SEQ-PROBE-ORDER gate: when the SequenceNode IS the first exec node
    // in a block (no preceding exec node), the seq-probe-anchor is the only way
    // the probe fires (ExecEntryNodeId path, not header path).  There must be
    // EXACTLY ONE NodeEnter probe for the sequence — no double probe.
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleSequenceFirstBlock_ExactlyOneProbeForSequence()
    {
        // Graph: Entry → SeqFirst(seqId) { Then0: SetVarA → Return }
        // SeqFirst IS the first exec node in the block (Entry produces no statements).
        // After fix: ??= sets SourceNodeId=seqId, anchor has ExecEntryNodeId=seqId.
        //   coveredByExecEntryId(seqId) = true → needsHeaderProbe = false.
        //   Exactly ONE probe for seqId (from the anchor) — no header probe doubling.
        using var fixture = new BlueprintTestFixture(NoAlcCheck);

        var graphId  = Guid.NewGuid();
        var entryId  = Guid.NewGuid();
        var seqId    = Guid.NewGuid();
        var svAId    = Guid.NewGuid();
        var retId    = Guid.NewGuid();

        var varA = new VariableDecl { Id = Guid.NewGuid(), Name = "A",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

        var peOut    = Guid.NewGuid();
        var psIn     = Guid.NewGuid(); var psThen0 = Guid.NewGuid();
        var pSvAIn   = Guid.NewGuid(); var pSvAOut = Guid.NewGuid();
        var pRetIn   = Guid.NewGuid();

        var graph = new Graph
        {
            Id = graphId, Name = "Tick", Kind = GraphKind.Function,
            Inputs = new(), Outputs = new(),
            Nodes = new System.Collections.Generic.List<Node>
            {
                new EventEntryNode { Id = entryId,
                    Pins = new() { new Pin { Id = peOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() } } },
                new SequenceNode { Id = seqId,
                    Pins = new()
                    {
                        new Pin { Id = psIn,    Name = "ExecIn", Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = psThen0, Name = "Then0",  Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new SetVariableNode { Id = svAId, VariableId = varA.Id.ToString(),
                    Pins = new()
                    {
                        new Pin { Id = pSvAIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new Pin { Id = pSvAOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    }},
                new ReturnNode { Id = retId, Status = NodeStatus.Success,
                    Pins = new() { new Pin { Id = pRetIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() } } },
            },
            Links = new System.Collections.Generic.List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = peOut,    ToNodeId = seqId,  ToPinId = psIn    },
                new() { FromNodeId = seqId,   FromPinId = psThen0,  ToNodeId = svAId,  ToPinId = pSvAIn  },
                new() { FromNodeId = svAId,   FromPinId = pSvAOut,  ToNodeId = retId,  ToPinId = pRetIn  },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId          = Guid.NewGuid(),
            Name             = "SingleSeqFirstBlock8",
            Dispatch         = AssetDispatchKind.Instance,
            Parameters       = new(), WorkingState = new(),
            Variables        = new() { varA },
            EventDispatchers = new(), CustomEvents = new(), CallablePeers = new(),
            Graphs           = new() { graph },
            Header           = new Header(),
        };

        // Compile and verify the generated source has EXACTLY ONE probe for seqId.
        var compiler      = new BlueprintCompiler();
        var compileResult = compiler.Compile(asset, DebugOptions);
        Assert.True(compileResult.Succeeded,
            string.Join("; ", compileResult.Diagnostics.Select(d => d.Message)));

        var source   = compileResult.GeneratedSource ?? string.Empty;
        var seqIdStr = seqId.ToString("D");
        var pattern  = $@"DebugProbe\.NodeEnter\s*\(\s*self\s*,\s*""{System.Text.RegularExpressions.Regex.Escape(seqIdStr)}""\s*\)";
        int count    = System.Text.RegularExpressions.Regex.Matches(source, pattern).Count;

        Assert.True(count == 1,
            $"Sequence-first block must have EXACTLY ONE probe for seqId ({seqIdStr[..8]}), " +
            $"got {count}. Double probe = seq-probe-anchor + header probe both emitted (regression).");

        // Also verify BreakpointTargets one-to-one (seqId maps to itself).
        var targets = compileResult.DebugMap!.BreakpointTargets;
        Assert.True(targets.ContainsKey(seqId),
            $"SequenceNode ({seqId:D}) must be in BreakpointTargets.");
        Assert.Equal(seqId, targets[seqId]);

        // Record probe order: seqId fires before svAId (sequence's anchor before SetVarA's block).
        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        fixture.TickFrame(0.016f);

        var entries  = fixture.DebugSession.NodeEntries;
        var svAIdStr = svAId.ToString("D");

        // seqId probe must fire exactly once at runtime.
        int seqProbeCount = entries.Count(e => e.NodeId == seqIdStr);
        Assert.True(seqProbeCount == 1,
            $"seqId probe must fire exactly once at runtime; fired {seqProbeCount} times.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string TargetsString(IReadOnlyDictionary<Guid, Guid> targets)
        => string.Join(", ", targets.Select(kv =>
            $"{kv.Key.ToString("D")[..8]}→{kv.Value.ToString("D")[..8]}"));
}
