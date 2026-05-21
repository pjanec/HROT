using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Tests covering Compiler Stages 1-5 (TASK-CP-002).
/// Test method names are suffixed with StageN so the SC7 filter works:
///   dotnet test --filter "Stage1|Stage2|Stage3|Stage4|Stage5"
/// </summary>
public sealed class Stage1To5Tests
{
    // Helper: build a minimal CompileOptions using built-in stubs.
    private static CompileOptions DefaultOptions(
        IReadOnlyList<BlueprintSignature>? siblings = null) =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: siblings ?? Array.Empty<BlueprintSignature>());

    // ------------------------------------------------------------------
    // SC1: Stage1_Parse
    // ------------------------------------------------------------------

    [Fact]
    public void Stage1_Parse_ValidJson_ReturnsNonNull()
    {
        var src = BlueprintAssetBuilder
            .Library("TestLib")
            .WithGraph("G", g => g.Entry().Return())
            .Build();

        var json = BlueprintJsonServices.Serialize(src);
        var sink = new DiagnosticSink();

        var result = Stage1_Parse.Run(json, sink);

        Assert.NotNull(result);
        Assert.False(sink.HasErrors);
    }

    [Fact]
    public void Stage1_Parse_MalformedJson_EmitsBP0002()
    {
        var sink = new DiagnosticSink();

        var result = Stage1_Parse.Run("{ not-valid-json !!!", sink);

        Assert.Null(result);
        Assert.True(sink.HasErrors);
        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP0002_JsonParseError);
    }

    // ------------------------------------------------------------------
    // SC2: Stage2_Validate -- V_AiPrimitiveIntent
    // ------------------------------------------------------------------

    [Fact]
    public void Stage2_Validate_ConditionWithReturnRunning_EmitsBP1100()
    {
        // AiPrimitive Condition graph with ReturnNode(Running) -- forbidden.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("CondPrim")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().Return(NodeStatus.Running))
            .Build();

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1100);
    }

    [Fact]
    public void Stage2_Validate_ConditionWithLatentDelay_EmitsBP1101()
    {
        // AiPrimitive Condition graph with LatentDelayNode -- forbidden.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("CondPrim2")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().Delay(1.0f).Return())
            .Build();

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1101);
    }

    // ------------------------------------------------------------------
    // SC3: Stage2_Validate -- V_VariablesAndState: state exceeds max tier
    // ------------------------------------------------------------------

    [Fact]
    public void Stage2_Validate_InstanceExceedsMaxTier_EmitsBP1210()
    {
        // 2013 x System.Int64 (8 bytes each) = 16104 bytes > 16096 (max tier).
        var variables = Enumerable.Range(0, 2013)
            .Select(i => new VariableDecl
            {
                Id   = new Guid(i, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                Name = $"V{i}",
                Type = new BlueprintTypeRef { TypeId = "System.Int64" },
            })
            .ToList();

        var asset = new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "HeavyStateInstance",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = variables,
        };

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1210);
    }

    // ------------------------------------------------------------------
    // SC4: Stage2_Validate -- V_PeerReferences: peer absent from sibling signatures
    // ------------------------------------------------------------------

    [Fact]
    public void Stage2_Validate_PeerAbsentFromSiblings_EmitsBP1301()
    {
        var peerId = Guid.NewGuid();

        // Build asset manually with all required node/link IDs to pass structural validators.
        var assetId          = Guid.NewGuid();
        var graphId          = Guid.NewGuid();
        var entryId          = Guid.NewGuid();
        var callId           = Guid.NewGuid();
        var returnId         = Guid.NewGuid();
        var entryExecOut     = Guid.NewGuid();
        var callExecIn       = Guid.NewGuid();
        var callExecOut      = Guid.NewGuid();
        var returnExecIn     = Guid.NewGuid();

        var asset = new BlueprintAsset
        {
            AssetId       = assetId,
            Name          = "PeerTest",
            Dispatch      = AssetDispatchKind.Instance,
            CallablePeers = new List<Guid> { peerId },
            Graphs        = new List<Graph>
            {
                new Graph
                {
                    Id   = graphId,
                    Name = "Main",
                    Kind = GraphKind.Function,
                    Nodes = new List<Node>
                    {
                        new EventEntryNode
                        {
                            Id   = entryId,
                            Pins = new List<Pin>
                            {
                                new Pin { Id = entryExecOut, Name = "ExecOut", Direction = "Out", IsExec = true },
                            },
                        },
                        new CallPeerBlueprintNode
                        {
                            Id              = callId,
                            PeerBlueprintId = peerId.ToString(),
                            FunctionRef     = "SomeFunction",
                            Pins = new List<Pin>
                            {
                                new Pin { Id = callExecIn,  Name = "ExecIn",  Direction = "In",  IsExec = true },
                                new Pin { Id = callExecOut, Name = "ExecOut", Direction = "Out", IsExec = true },
                            },
                        },
                        new ReturnNode
                        {
                            Id   = returnId,
                            Pins = new List<Pin>
                            {
                                new Pin { Id = returnExecIn, Name = "ExecIn", Direction = "In", IsExec = true },
                            },
                        },
                    },
                    Links = new List<Link>
                    {
                        new Link { FromNodeId = entryId, FromPinId = entryExecOut, ToNodeId = callId,   ToPinId = callExecIn  },
                        new Link { FromNodeId = callId,  FromPinId = callExecOut,  ToNodeId = returnId, ToPinId = returnExecIn },
                    },
                },
            },
        };

        // No sibling signatures provided --> peerId absent.
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        Stage2_Validate.Run(asset, ctx);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1301);
    }

    // ------------------------------------------------------------------
    // SC5: Stage5_Schedule -- WaitForChannelNode produces IrTerm_Suspend
    // ------------------------------------------------------------------

    [Fact]
    public void Stage5_Schedule_WaitForChannelNode_ProducesSuspendAndResumeBlock()
    {
        // Build asset: AiPrimitive Action with Entry -> WaitForChannel -> Return(Success)
        var asset = BlueprintAssetBuilder
            .AiPrimitive("WaitTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().WaitForChannel("TestChannel").Return())
            .Build();

        // Bypass Stage 2 (catalog stubs are empty; WaitForChannel would emit BP1402).
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());

        var ir    = Stage5_Schedule.Run(typed, ctx);
        var graph = Assert.Single(ir.Graphs);

        // Expect at least two blocks: pre-suspend and resume.
        Assert.True(graph.Blocks.Count >= 2,
            $"Expected >= 2 blocks, got {graph.Blocks.Count}.");

        var entryBlock = graph.Blocks[0];
        Assert.IsType<IrTerm_Suspend>(entryBlock.Terminator);

        var resumeBlock = graph.Blocks[1];
        Assert.IsType<IrTerm_ReturnStatus>(resumeBlock.Terminator);

        var retTerm = (IrTerm_ReturnStatus)resumeBlock.Terminator;
        Assert.Equal(NodeStatus.Success, retTerm.Status);
    }

    // ------------------------------------------------------------------
    // SC6: IrPrinter.PrettyPrint is deterministic (Stage5 output)
    // ------------------------------------------------------------------

    [Fact]
    public void Stage5_IrPrinter_PrettyPrint_IsDeterministic()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("PrintTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().WaitForChannel("Chan").Return())
            .Build();

        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());

        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, DefaultOptions());
        var ir   = Stage5_Schedule.Run(typed, ctx);

        var text1 = IrPrinter.PrettyPrint(ir);
        var text2 = IrPrinter.PrettyPrint(ir);

        Assert.Equal(text1, text2);
        Assert.NotEmpty(text1);
    }
}
