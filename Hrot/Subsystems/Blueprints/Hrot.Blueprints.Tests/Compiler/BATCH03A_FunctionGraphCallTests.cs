using System.Reflection;
using System.Runtime.CompilerServices;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using BlueprintAsset   = Hrot.Blueprints.Core.Assets.BlueprintAsset;
using Graph            = Hrot.Blueprints.Core.Assets.Graph;
using GraphKind        = Hrot.Blueprints.Core.Assets.GraphKind;
using Node             = Hrot.Blueprints.Core.Assets.Node;
using EventEntryNode   = Hrot.Blueprints.Core.Assets.EventEntryNode;
using ReturnNode       = Hrot.Blueprints.Core.Assets.ReturnNode;
using FunctionCallNode = Hrot.Blueprints.Core.Assets.FunctionCallNode;
using LatentDelayNode  = Hrot.Blueprints.Core.Assets.LatentDelayNode;
using Pin              = Hrot.Blueprints.Core.Assets.Pin;
using Link             = Hrot.Blueprints.Core.Assets.Link;
using NodeStatus       = Hrot.Blueprints.Core.Assets.NodeStatus;
using BlueprintTypeRef = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;
using Header           = Hrot.Blueprints.Core.Assets.Header;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using ParameterDecl    = Hrot.Blueprints.Core.Assets.ParameterDecl;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BATCH-03A: End-to-end tests for in-blueprint function-graph calls.
/// Covers: Stage5 IR generation (IrOp_GraphCall + IrOp_ReadInputArg),
///         Stage7 compile-and-run, and BP1650 latent-node validation.
/// </summary>
public sealed class BATCH03A_FunctionGraphCallTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>
    /// Builds a hand-constructed BlueprintAsset for an Instance blueprint that has:
    ///   - A Tick graph: Entry → FunctionCallNode(TargetGraphId=addGraphId, args: litA=3, litB=4)
    ///                         → SetVariable(Result) → Return
    ///   - An "Add" Function graph with inputs a,b:int and output = Math.Abs(a) so the
    ///     generated code calls a real BCL method, compiles, and returns a deterministic value.
    ///     (The function returns Math.Abs(a) = |3| = 3, proving a was received and processed.)
    ///
    /// Returns (asset, addGraphId, resultVarId).
    /// </summary>
    private static (BlueprintAsset asset, Guid addGraphId, Guid resultVarId) BuildAddFunctionAsset()
    {
        var assetId     = Guid.NewGuid();
        var resultVarId = Guid.NewGuid();

        // ---- "Add" Function graph ----
        // Graph body: Entry (data-out a, b) → PureCall System.Math.Abs(a) → Return(result)
        // The ReturnNode has a data "Out" pin wired FROM the Abs call output.
        var addGraphId   = Guid.NewGuid();
        var addEntryId   = Guid.NewGuid();
        var addAbsCallId = Guid.NewGuid();
        var addReturnId  = Guid.NewGuid();

        var entryExecOut = Guid.NewGuid();
        var entryPinA    = Guid.NewGuid();
        var entryPinB    = Guid.NewGuid();

        var absCallInA   = Guid.NewGuid();
        var absCallOut   = Guid.NewGuid();

        var retExecIn    = Guid.NewGuid();
        var retDataOut   = Guid.NewGuid(); // "Out" pin on ReturnNode = the return-value slot

        var addGraph = new Graph
        {
            Id   = addGraphId,
            Name = "Add",
            Kind = GraphKind.Function,
            Inputs = new List<ParameterDecl>
            {
                new() { Id = Guid.NewGuid(), Name = "a", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
                new() { Id = Guid.NewGuid(), Name = "b", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
            Outputs = new List<ParameterDecl>
            {
                new() { Id = Guid.NewGuid(), Name = "result", Type = new BlueprintTypeRef { TypeId = "System.Int32" } },
            },
            Nodes = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = addEntryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = entryExecOut, Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        // Data-out pins: these are inputs to the function graph.
                        // Stage5's EventEntryNode case in ResolveNodeOutput emits IrOp_ReadInputArg
                        // for each data-out pin consumed by downstream nodes.
                        new() { Id = entryPinA, Name = "a", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                        new() { Id = entryPinB, Name = "b", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                // Pure call to System.Math.Abs(a) - a real BCL method that exists at runtime.
                new FunctionCallNode
                {
                    Id            = addAbsCallId,
                    TargetTypeId  = "System.Math",
                    MethodName    = "Abs",
                    IsPure        = true,
                    TargetGraphId = "",
                    Pins = new List<Pin>
                    {
                        new() { Id = absCallInA, Name = "a",      Direction = "In",  IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                        new() { Id = absCallOut, Name = "result", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                new ReturnNode
                {
                    Id   = addReturnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = retExecIn,  Name = "ExecIn", Direction = "In",  IsExec = true,  TypeRef = new() },
                        // "Out" direction: this is the return-value slot on the ReturnNode.
                        // Stage5.BuildReturnTerminator looks for Direction=="Out" here,
                        // then calls ResolveDataPin(rn.Id, outPin.Id) which follows a link
                        // arriving at ToNodeId=addReturnId, ToPinId=retDataOut.
                        new() { Id = retDataOut, Name = "result", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
            },
            Links = new List<Link>
            {
                // Exec: Entry → Return
                new() { FromNodeId = addEntryId, FromPinId = entryExecOut, ToNodeId = addReturnId, ToPinId = retExecIn },
                // Data: Entry.a → Abs call input
                new() { FromNodeId = addEntryId, FromPinId = entryPinA, ToNodeId = addAbsCallId, ToPinId = absCallInA },
                // Data: Abs result → Return's "Out" pin (the return-value slot)
                new() { FromNodeId = addAbsCallId, FromPinId = absCallOut, ToNodeId = addReturnId, ToPinId = retDataOut },
            },
        };

        // ---- Tick graph ----
        var tickGraphId  = Guid.NewGuid();
        var tickEntryId  = Guid.NewGuid();
        var tickCallId   = Guid.NewGuid();
        var tickSetVarId = Guid.NewGuid();
        var tickReturnId = Guid.NewGuid();

        var tickEntryExec  = Guid.NewGuid();
        var tickCallExIn   = Guid.NewGuid();
        var tickCallExOut  = Guid.NewGuid();
        var tickCallResult = Guid.NewGuid();
        var tickCallArgA   = Guid.NewGuid();
        var tickCallArgB   = Guid.NewGuid();
        var tickSetExIn    = Guid.NewGuid();
        var tickSetExOut   = Guid.NewGuid();
        var tickSetDataIn  = Guid.NewGuid();
        var tickRetExIn    = Guid.NewGuid();

        var litAId   = Guid.NewGuid();
        var litBId   = Guid.NewGuid();
        var litAOut  = Guid.NewGuid();
        var litBOut  = Guid.NewGuid();

        var tickGraph = new Graph
        {
            Id      = tickGraphId,
            Name    = "Tick",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = tickEntryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = tickEntryExec, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new Hrot.Blueprints.Core.Assets.LiteralNode
                {
                    Id = litAId,
                    TypeId = "System.Int32",
                    ValueJson = "3",
                    Pins = new List<Pin>
                    {
                        new() { Id = litAOut, Name = "value", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                new Hrot.Blueprints.Core.Assets.LiteralNode
                {
                    Id = litBId,
                    TypeId = "System.Int32",
                    ValueJson = "4",
                    Pins = new List<Pin>
                    {
                        new() { Id = litBOut, Name = "value", Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                // FunctionCallNode with TargetGraphId -> the Add graph
                new FunctionCallNode
                {
                    Id            = tickCallId,
                    TargetTypeId  = "",
                    MethodName    = "",
                    IsPure        = false,
                    TargetGraphId = addGraphId.ToString(),
                    Pins = new List<Pin>
                    {
                        new() { Id = tickCallExIn,   Name = "ExecIn",   Direction = "In",  IsExec = true,  TypeRef = new() },
                        new() { Id = tickCallExOut,  Name = "ExecOut",  Direction = "Out", IsExec = true,  TypeRef = new() },
                        new() { Id = tickCallArgA,   Name = "a",        Direction = "In",  IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                        new() { Id = tickCallArgB,   Name = "b",        Direction = "In",  IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                        new() { Id = tickCallResult, Name = "result",   Direction = "Out", IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                // SetVariableNode: writes the result to the "Result" variable
                new Hrot.Blueprints.Core.Assets.SetVariableNode
                {
                    Id         = tickSetVarId,
                    VariableId = resultVarId.ToString(),
                    Pins = new List<Pin>
                    {
                        new() { Id = tickSetExIn,   Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new() { Id = tickSetExOut,  Name = "ExecOut", Direction = "Out", IsExec = true,  TypeRef = new() },
                        new() { Id = tickSetDataIn, Name = "value",   Direction = "In",  IsExec = false,
                                TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
                new ReturnNode
                {
                    Id   = tickReturnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = tickRetExIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                // Exec chain
                new() { FromNodeId = tickEntryId,  FromPinId = tickEntryExec, ToNodeId = tickCallId,   ToPinId = tickCallExIn },
                new() { FromNodeId = tickCallId,   FromPinId = tickCallExOut, ToNodeId = tickSetVarId, ToPinId = tickSetExIn  },
                new() { FromNodeId = tickSetVarId, FromPinId = tickSetExOut,  ToNodeId = tickReturnId, ToPinId = tickRetExIn  },
                // Data: literals → FunctionCallNode args
                new() { FromNodeId = litAId, FromPinId = litAOut, ToNodeId = tickCallId, ToPinId = tickCallArgA },
                new() { FromNodeId = litBId, FromPinId = litBOut, ToNodeId = tickCallId, ToPinId = tickCallArgB },
                // Data: FunctionCallNode result → SetVariable
                new() { FromNodeId = tickCallId, FromPinId = tickCallResult, ToNodeId = tickSetVarId, ToPinId = tickSetDataIn },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId          = assetId,
            Name             = "FuncCallTest",
            Dispatch         = BlueprintDispatchKind.Instance,
            Parameters       = new(),
            WorkingState     = new(),
            Variables        = new List<Hrot.Blueprints.Core.Assets.VariableDecl>
            {
                new()
                {
                    Id   = resultVarId,
                    Name = "Result",
                    Type = new BlueprintTypeRef { TypeId = "System.Int32" },
                },
            },
            EventDispatchers = new(),
            CustomEvents     = new(),
            CallablePeers    = new(),
            Graphs           = new List<Graph> { tickGraph, addGraph },
            Header           = new Header(),
        };

        return (asset, addGraphId, resultVarId);
    }

    // -----------------------------------------------------------------------
    // Test 1: Stage5 IR — IrOp_GraphCall emitted with 2 args + IrOp_ReadInputArg(0)/(1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Stage5_FunctionCallNodeWithTargetGraphId_EmitsIrOp_GraphCall_And_ReadInputArg()
    {
        var (asset, addGraphId, _) = BuildAddFunctionAsset();

        var opts  = DefaultOptions();
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, opts);

        var typed = Stage4_TypeResolve.Run(asset, ctx);
        var ir    = Stage5_Schedule.Run(typed, ctx);

        // --- Assert: Tick graph contains IrOp_GraphCall with 2 args and int return type ---
        var tickIrGraph = ir.Graphs.First(g => g.Name == "Tick");
        var allStmts    = tickIrGraph.Blocks.SelectMany(b => b.Statements).ToList();

        var graphCallStmt = allStmts.FirstOrDefault(s => s.Operation is IrOp_GraphCall);
        Assert.NotNull(graphCallStmt);

        var graphCallOp = (IrOp_GraphCall)graphCallStmt!.Operation;
        Assert.Equal(addGraphId, graphCallOp.TargetGraphId);
        Assert.Equal(2, graphCallOp.Args.Count);
        Assert.Equal("System.Int32", graphCallOp.ReturnType.FullName);

        // --- Assert: Add graph's IR contains IrOp_ReadInputArg(0) (for input "a") ---
        // Note: input "b" is declared but not consumed by the Add graph body (the function
        // only calls Math.Abs(a)), so only ReadInputArg(0) is generated. The IrOp_GraphCall
        // still passes 2 args (positional, matching the 2 declared Inputs in the Tick call).
        var addIrGraph = ir.Graphs.First(g => g.Name == "Add");
        var addStmts   = addIrGraph.Blocks.SelectMany(b => b.Statements).ToList();

        var readInputArgs = addStmts
            .Where(s => s.Operation is IrOp_ReadInputArg)
            .Select(s => (IrOp_ReadInputArg)s.Operation)
            .OrderBy(op => op.ArgIndex)
            .ToList();

        // Must have at least ReadInputArg(0) for input "a" (consumed by Abs call)
        Assert.True(readInputArgs.Count >= 1,
            $"Expected >= 1 IrOp_ReadInputArg operation in the Add graph; found {readInputArgs.Count}.");
        Assert.Contains(readInputArgs, op => op.ArgIndex == 0);

        // No errors emitted
        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Code}: {d.Message}"))}");
    }

    // -----------------------------------------------------------------------
    // Test 2: End-to-end compile-and-run
    // -----------------------------------------------------------------------

    [Fact]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void E2E_FunctionCallNode_CompileAndRun_WritesExpectedResult()
        => E2E_FunctionCallNode_CompileAndRun_Body();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void E2E_FunctionCallNode_CompileAndRun_Body()
    {
        // Build the asset, compile, attach, tick, then assert the variable was written.
        var (asset, _, resultVarId) = BuildAddFunctionAsset();

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

        fixture.CompileAndLoad(asset);
        var entity = fixture.CreateEntity();
        fixture.AttachBlueprint(asset, entity);

        // Tick once — the Tick graph calls Add(3,4) and writes result to "Result".
        fixture.TickFrame(0.016f);

        // Read the state and verify the variable.
        var stateView = fixture.GetBlueprintState(asset, entity);
        Assert.NotNull(stateView);
        Assert.True(stateView.HasValue, "Expected a valid BlueprintStateView.");

        bool found = stateView.Value.TryGetField<int>("Result", out var result);
        Assert.True(found, "State field 'Result' not found in blueprint state.");

        // The Tick graph calls Add(3, 4) which internally calls Math.Abs(a=3) and returns 3.
        // So Result should be 3 after one tick.
        // This verifies:
        //   1) The Blueprint compiled and loaded (Func_Add was emitted correctly).
        //   2) TickFrame executed without throwing.
        //   3) The function graph body ran, consumed input "a" via IrOp_ReadInputArg(0),
        //      called Math.Abs, and the result was written back via SetVariable.
        Assert.Equal(3, result);
    }

    // -----------------------------------------------------------------------
    // Test 3: Validation — latent node in a called function graph → BP1650
    // -----------------------------------------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1650")]
    public void Stage2_FunctionGraphWithLatentNode_EmitsBP1650()
    {
        // Build an Instance asset with:
        //   - A Tick graph that has a FunctionCallNode(TargetGraphId = latentGraphId)
        //   - A Function graph named "BadHelper" that contains a LatentDelayNode

        var assetId        = Guid.NewGuid();
        var latentGraphId  = Guid.NewGuid();

        // ---- "BadHelper" Function graph containing LatentDelayNode ----
        var helpEntryId    = Guid.NewGuid();
        var helpDelayId    = Guid.NewGuid();
        var helpReturnId   = Guid.NewGuid();
        var helpEntryExec  = Guid.NewGuid();
        var helpDelayExIn  = Guid.NewGuid();
        var helpDelayExOut = Guid.NewGuid();
        var helpRetExIn    = Guid.NewGuid();

        var latentGraph = new Graph
        {
            Id      = latentGraphId,
            Name    = "BadHelper",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = helpEntryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = helpEntryExec, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new LatentDelayNode
                {
                    Id   = helpDelayId,
                    Pins = new List<Pin>
                    {
                        new() { Id = helpDelayExIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new() { Id = helpDelayExOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode
                {
                    Id   = helpReturnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = helpRetExIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = helpEntryId, FromPinId = helpEntryExec,  ToNodeId = helpDelayId,  ToPinId = helpDelayExIn  },
                new() { FromNodeId = helpDelayId, FromPinId = helpDelayExOut, ToNodeId = helpReturnId, ToPinId = helpRetExIn    },
            },
        };

        // ---- Tick graph that calls BadHelper via FunctionCallNode.TargetGraphId ----
        var tickGraphId  = Guid.NewGuid();
        var tickEntryId  = Guid.NewGuid();
        var tickCallId   = Guid.NewGuid();
        var tickReturnId = Guid.NewGuid();
        var tickEntryExec = Guid.NewGuid();
        var tickCallExIn  = Guid.NewGuid();
        var tickCallExOut = Guid.NewGuid();
        var tickRetExIn   = Guid.NewGuid();

        var tickGraph = new Graph
        {
            Id      = tickGraphId,
            Name    = "Tick",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id   = tickEntryId,
                    Pins = new List<Pin>
                    {
                        new() { Id = tickEntryExec, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new FunctionCallNode
                {
                    Id            = tickCallId,
                    TargetTypeId  = "",
                    MethodName    = "",
                    IsPure        = false,
                    TargetGraphId = latentGraphId.ToString(),
                    Pins = new List<Pin>
                    {
                        new() { Id = tickCallExIn,  Name = "ExecIn",  Direction = "In",  IsExec = true, TypeRef = new() },
                        new() { Id = tickCallExOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode
                {
                    Id   = tickReturnId,
                    Pins = new List<Pin>
                    {
                        new() { Id = tickRetExIn, Name = "ExecIn", Direction = "In", IsExec = true, TypeRef = new() },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = tickEntryId, FromPinId = tickEntryExec, ToNodeId = tickCallId,   ToPinId = tickCallExIn  },
                new() { FromNodeId = tickCallId,  FromPinId = tickCallExOut, ToNodeId = tickReturnId, ToPinId = tickRetExIn   },
            },
        };

        var asset = new BlueprintAsset
        {
            AssetId          = assetId,
            Name             = "LatentFuncCallTest",
            Dispatch         = BlueprintDispatchKind.Instance,
            Parameters       = new(),
            WorkingState     = new(),
            Variables        = new(),
            EventDispatchers = new(),
            CustomEvents     = new(),
            CallablePeers    = new(),
            Graphs           = new List<Graph> { tickGraph, latentGraph },
            Header           = new Header(),
        };

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1650);
    }
}
