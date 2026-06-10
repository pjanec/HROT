using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for BPC-IMPLICIT-RETURN: implicit Return at end of an exec chain.
/// Stage 5 SealFallThrough now synthesizes the dispatch-appropriate implicit return
/// instead of emitting a bare IrTerm_FallThrough at genuine end-of-chain.
/// </summary>
public sealed class BPC_ImplicitReturnTests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static (IrAsset ir, DiagnosticSink sink) RunSchedule(BlueprintAsset asset)
    {
        var opts  = DefaultOptions();
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, opts);
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir = Stage5_Schedule.Run(typed, ctx);
        return (ir, sink);
    }

    // -----------------------------------------------------------------------
    // Test 1: Void Instance graph, no Return
    // Entry -> SetVariable(X=7), no ReturnNode.  Implicit void return at end.
    // -----------------------------------------------------------------------

    [Fact]
    public void Instance_VoidGraphNoReturn_EmitsImplicitVoidReturn()
    {
        var asset = BlueprintAssetBuilder
            .Instance("InstNoRet")
            .WithVariable("X", typeof(int))
            .WithGraph("Tick", g => g
                .Entry()
                .SetVariable("X", "7"))
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(ir.Graphs);
        var lastBlock = graph.Blocks[graph.Blocks.Count - 1];

        // Implicit void return for Instance dispatch.
        var retTerm = Assert.IsType<IrTerm_Return>(lastBlock.Terminator);
        Assert.Null(retTerm.Value); // void
    }

    // -----------------------------------------------------------------------
    // Test 2: AiPrimitive action, no Return
    // An AiPrimitive graph whose chain ends without a Return emits implicit
    // IrTerm_ReturnStatus(NodeStatus.Success).
    // -----------------------------------------------------------------------

    [Fact]
    public void AiPrimitive_NoReturn_EmitsImplicitSuccessReturn()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("APNoRet")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry())
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(ir.Graphs);
        var lastBlock = graph.Blocks[graph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_ReturnStatus>(lastBlock.Terminator);
        Assert.Equal(NodeStatus.Success, retTerm.Status);
    }

    // -----------------------------------------------------------------------
    // Test 3: Explicit early-exit Return still works
    // Branch: true path hits explicit Return mid-chain; false path falls off
    // the end (implicit return).  Both compile, each has correct terminator.
    // -----------------------------------------------------------------------

    [Fact]
    public void Branch_EarlyExitReturn_AndImplicitFallOff_CompileCorrectly()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("BranchEarlyRet")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .Branch(
                    "true",
                    trueBranch:  tb => tb.Return(NodeStatus.Failure),   // explicit early exit
                    falseBranch: _ => { }                                // falls off — implicit return
                ))
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(ir.Graphs);
        // entry + trueBlock + falseBlock = 3 blocks
        Assert.Equal(3, graph.Blocks.Count);

        var entryBlock     = graph.Blocks[0];
        var trueBranchBlock  = graph.Blocks[1];
        var falseBranchBlock = graph.Blocks[2];

        // Entry block has a Branch terminator (not a return).
        Assert.IsType<IrTerm_Branch>(entryBlock.Terminator);

        // True branch block: explicit Return(Failure).
        var trueTerm = Assert.IsType<IrTerm_ReturnStatus>(trueBranchBlock.Terminator);
        Assert.Equal(NodeStatus.Failure, trueTerm.Status);

        // False branch block: implicit ReturnStatus(Success) from SealFallThrough.
        var falseTerm = Assert.IsType<IrTerm_ReturnStatus>(falseBranchBlock.Terminator);
        Assert.Equal(NodeStatus.Success, falseTerm.Status);
    }

    // -----------------------------------------------------------------------
    // Test 4: Explicit non-default status still honored
    // An AiPrimitive with an explicit Return Failure emits Failure (not
    // overridden by the implicit-Success default).
    // -----------------------------------------------------------------------

    [Fact]
    public void AiPrimitive_ExplicitFailureReturn_NotOverriddenByImplicit()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("APFailRet")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .Return(NodeStatus.Failure))
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(ir.Graphs);
        var lastBlock = graph.Blocks[graph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_ReturnStatus>(lastBlock.Terminator);
        Assert.Equal(NodeStatus.Failure, retTerm.Status);
    }

    // -----------------------------------------------------------------------
    // Test 5: Instance graph with output value + explicit Return
    // ReturnNode with an output data pin connected to a SetVariable's value —
    // the Return terminator should carry the resolved output value.
    // Regression: implicit return must not interfere with explicit value returns.
    // -----------------------------------------------------------------------

    [Fact]
    public void Instance_ExplicitValueReturn_PreservesReturnValue()
    {
        var assetId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var retId   = Guid.NewGuid();

        var pinEntryOut  = Guid.NewGuid();
        var pinRetIn     = Guid.NewGuid();
        var pinRetOutVal = Guid.NewGuid();

        var graph = new Graph
        {
            Id      = graphId,
            Name    = "ValueRet",
            Kind    = GraphKind.Function,
            Inputs  = new(),
            Outputs = new(),
            Nodes   = new List<Node>
            {
                new EventEntryNode
                {
                    Id = entryId,
                    Pins = new()
                    {
                        new Pin { Id = pinEntryOut, Name = "ExecOut", Direction = "Out", IsExec = true, TypeRef = new() },
                    },
                },
                new ReturnNode
                {
                    Id     = retId,
                    Status = NodeStatus.Success,
                    Pins = new()
                    {
                        new Pin { Id = pinRetIn,     Name = "ExecIn",  Direction = "In",  IsExec = true,  TypeRef = new() },
                        new Pin { Id = pinRetOutVal, Name = "Result",  Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32" } },
                    },
                },
            },
            Links = new List<Link>
            {
                new() { FromNodeId = entryId, FromPinId = pinEntryOut, ToNodeId = retId, ToPinId = pinRetIn },
            },
        };

        var bp = new BlueprintAsset
        {
            AssetId          = assetId,
            Name             = "ValueRetBP",
            Dispatch         = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Instance,
            Parameters       = new(),
            WorkingState     = new(),
            Variables        = new(),
            EventDispatchers = new(),
            CustomEvents     = new(),
            CallablePeers    = new(),
            Graphs           = new() { graph },
            Header           = new Header(),
        };

        var (ir, sink) = RunSchedule(bp);

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var irGraph = Assert.Single(ir.Graphs);
        var lastBlock = irGraph.Blocks[irGraph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_Return>(lastBlock.Terminator);
        // The return should reference a value (the resolved output pin).
        Assert.NotNull(retTerm.Value);
    }

    // -----------------------------------------------------------------------
    // Test 6: Library dispatch also gets implicit ReturnStatus
    // -----------------------------------------------------------------------

    [Fact]
    public void Library_NoReturn_EmitsImplicitSuccessReturn()
    {
        var asset = BlueprintAssetBuilder
            .Library("LibNoRet")
            .WithGraph("G", g => g
                .Entry())
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(ir.Graphs);
        var lastBlock = graph.Blocks[graph.Blocks.Count - 1];

        var retTerm = Assert.IsType<IrTerm_ReturnStatus>(lastBlock.Terminator);
        Assert.Equal(NodeStatus.Success, retTerm.Status);
    }
}
