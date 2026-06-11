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

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string TargetsString(IReadOnlyDictionary<Guid, Guid> targets)
        => string.Join(", ", targets.Select(kv =>
            $"{kv.Key.ToString("D")[..8]}→{kv.Value.ToString("D")[..8]}"));
}
