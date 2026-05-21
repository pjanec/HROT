using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Lowering;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Tests covering Compiler Stage 6 (TASK-CP-003).
/// Test method names are suffixed with Stage6 so they can be filtered:
///   dotnet test --filter "Stage6"
/// </summary>
public sealed class Stage6Tests
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

    // Runs Stage5 then Stage6 and returns the Stage6 output.
    private static IrAsset RunStage5Then6(
        BlueprintAsset asset,
        DiagnosticSink sink,
        CompilerMode mode = CompilerMode.Debug)
    {
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());

        var ctx = new ValidationContext(sink, DefaultOptions());
        var ir  = Stage5_Schedule.Run(typed, ctx);
        return Stage6_Lower.Run(ir, mode, sink);
    }

    // ------------------------------------------------------------------
    // SC1: AiPrimitive WaitForChannel -- dispatch structure
    // ------------------------------------------------------------------

    [Fact]
    public void Stage6_AiPrimitive_WaitForChannel_ProducesDispatchBlock()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("AiPrimWait")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().WaitForChannel("TestChannel").Return())
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunStage5Then6(asset, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(lowered.Graphs);

        // No Suspend terminators remain after lowering.
        Assert.DoesNotContain(graph.Blocks, b => b.Terminator is IrTerm_Suspend);

        // Entry is the synthesized dispatch block.
        var dispatchBlock = graph.Blocks.First(b => b.Id == graph.Entry);
        Assert.Equal("dispatch", dispatchBlock.Label);
        Assert.Contains(dispatchBlock.Statements, s => s.Operation is IrOp_ReadWorkingStatePhase);
        Assert.IsType<IrTerm_Branch>(dispatchBlock.Terminator);

        // Phase-0 initial block: has WriteWorkingStatePhase(1) and ReturnStatus(Running).
        var branchTerm = (IrTerm_Branch)dispatchBlock.Terminator;
        var phase0Block = graph.Blocks.First(b => b.Id == branchTerm.IfTrue);
        Assert.Contains(phase0Block.Statements,
            s => s.Operation is IrOp_WriteWorkingStatePhase wsp && wsp.PhaseValue == 1);
        var phase0Term = Assert.IsType<IrTerm_ReturnStatus>(phase0Block.Terminator);
        Assert.Equal(NodeStatus.Running, phase0Term.Status);

        // Channel check block: has GetComponentRO + FieldRead("Status").
        var channelCheckBlock = graph.Blocks.First(b => b.Label == "phase1_channel_check");
        Assert.Contains(channelCheckBlock.Statements, s => s.Operation is IrOp_GetComponentRO);
        Assert.Contains(channelCheckBlock.Statements,
            s => s.Operation is IrOp_FieldRead fr && fr.FieldName == "Status");

        // WorkingState contains the synthesized __phase field.
        Assert.Contains(lowered.WorkingState, f => f.Name == "__phase");
    }

    // ------------------------------------------------------------------
    // SC2: Instance WaitForChannel -- cursor dispatch structure
    // ------------------------------------------------------------------

    [Fact]
    public void Stage6_Instance_WaitForChannel_ProducesCursorDispatch()
    {
        var asset = BlueprintAssetBuilder
            .Instance("InstWait")
            .WithGraph("Main", g => g.Entry().WaitForChannel("SomeChannel").Return())
            .Build();

        var sink    = new DiagnosticSink();
        var lowered = RunStage5Then6(asset, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(lowered.Graphs);

        // No Suspend terminators remain after lowering.
        Assert.DoesNotContain(graph.Blocks, b => b.Terminator is IrTerm_Suspend);

        // Entry is the synthesized cursor dispatch block.
        var dispatchBlock = graph.Blocks.First(b => b.Id == graph.Entry);
        Assert.Equal("cursor_dispatch", dispatchBlock.Label);
        Assert.Contains(dispatchBlock.Statements, s => s.Operation is IrOp_ReadCursorResumeAt);
        Assert.IsType<IrTerm_Branch>(dispatchBlock.Terminator);

        // Initial block (ResumeAt==0 branch): WriteCursorResumeAt(1) + WriteCursorInstanceVersion + IrTerm_Return(null).
        var branchTerm   = (IrTerm_Branch)dispatchBlock.Terminator;
        var initialBlock = graph.Blocks.First(b => b.Id == branchTerm.IfTrue);
        Assert.Contains(initialBlock.Statements,
            s => s.Operation is IrOp_WriteCursorResumeAt wra && wra.ResumeAtValue == 1);
        Assert.Contains(initialBlock.Statements, s => s.Operation is IrOp_WriteCursorInstanceVersion);
        var initTerm = Assert.IsType<IrTerm_Return>(initialBlock.Terminator);
        Assert.False(initTerm.Value.HasValue, "Initial block should return void.");

        // Resume check block: CheckCursorVersion + GetComponentRO + FieldRead("Status").
        var resumeCheckBlock = graph.Blocks.First(b => b.Label == "resume_1_channel_check");
        Assert.Contains(resumeCheckBlock.Statements, s => s.Operation is IrOp_CheckCursorVersion);
        Assert.Contains(resumeCheckBlock.Statements, s => s.Operation is IrOp_GetComponentRO);
        Assert.Contains(resumeCheckBlock.Statements,
            s => s.Operation is IrOp_FieldRead fr && fr.FieldName == "Status");
    }

    // ------------------------------------------------------------------
    // SC3: StructureHash changes when a field name changes
    // ------------------------------------------------------------------

    [Fact]
    public void Stage6_StructureHash_ChangesWhenFieldNameChanges()
    {
        var assetId   = Guid.NewGuid();
        var fieldType = new IrTypeRef { FullName = "System.Int32", IsUnmanaged = true, SizeBytes = 4 };

        var asset1 = BuildMinimalInstance(assetId, "Score",  fieldType);
        var asset2 = BuildMinimalInstance(assetId, "Points", fieldType);  // different name

        var hash1 = StructureHashComputation.Compute(asset1);
        var hash2 = StructureHashComputation.Compute(asset2);

        Assert.NotEqual(hash1, hash2);
    }

    // ------------------------------------------------------------------
    // SC4: StructureHash changes when a field type changes
    // ------------------------------------------------------------------

    [Fact]
    public void Stage6_StructureHash_ChangesWhenFieldTypeChanges()
    {
        var assetId   = Guid.NewGuid();
        var int32Type = new IrTypeRef { FullName = "System.Int32", IsUnmanaged = true, SizeBytes = 4 };
        var int64Type = new IrTypeRef { FullName = "System.Int64", IsUnmanaged = true, SizeBytes = 8 };

        var asset1 = BuildMinimalInstance(assetId, "Score", int32Type);
        var asset2 = BuildMinimalInstance(assetId, "Score", int64Type);  // different type

        var hash1 = StructureHashComputation.Compute(asset1);
        var hash2 = StructureHashComputation.Compute(asset2);

        Assert.NotEqual(hash1, hash2);
    }

    // ------------------------------------------------------------------
    // SC5: StructureHash does NOT change when only graph body changes
    // ------------------------------------------------------------------

    [Fact]
    public void Stage6_StructureHash_StableWhenOnlyGraphBodyChanges()
    {
        var assetId   = Guid.NewGuid();
        var fieldType = new IrTypeRef { FullName = "System.Int32", IsUnmanaged = true, SizeBytes = 4 };

        var baseAsset = BuildMinimalInstance(assetId, "Score", fieldType);
        var nullDebug = new IrDebugAnnotation { GraphId = Guid.Empty };

        // Asset 1: one-block graph.
        var graph1 = new IrGraph
        {
            Id     = Guid.NewGuid(),
            Name   = "Main",
            Kind   = IrGraphKind.Function,
            Entry  = new IrBlockId(0),
            Blocks = new[]
            {
                new IrBlock
                {
                    Id         = new IrBlockId(0),
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_Return(null) { Debug = nullDebug },
                },
            },
        };

        // Asset 2: two-block graph (different body, same fields).
        var graph2 = new IrGraph
        {
            Id     = Guid.NewGuid(),
            Name   = "Other",
            Kind   = IrGraphKind.Function,
            Entry  = new IrBlockId(0),
            Blocks = new[]
            {
                new IrBlock
                {
                    Id         = new IrBlockId(0),
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_Return(null) { Debug = nullDebug },
                },
                new IrBlock
                {
                    Id         = new IrBlockId(1),
                    Statements = Array.Empty<IrStatement>(),
                    Terminator = new IrTerm_Return(null) { Debug = nullDebug },
                },
            },
        };

        var asset1 = baseAsset with { Graphs = new[] { graph1 } };
        var asset2 = baseAsset with { Graphs = new[] { graph2 } };

        var hash1 = StructureHashComputation.Compute(asset1);
        var hash2 = StructureHashComputation.Compute(asset2);

        Assert.Equal(hash1, hash2);
    }

    // ------------------------------------------------------------------
    // SC6: Library asset with no function graphs emits BP5001
    // ------------------------------------------------------------------

    [Fact]
    public void Stage6_Library_NoFunctionGraphs_EmitsBP5001()
    {
        var asset = new IrAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "EmptyLib",
            Dispatch = AssetDispatchKind.Library,
            Graphs   = Array.Empty<IrGraph>(),
        };

        var sink = new DiagnosticSink();
        Stage6_Lower.Run(asset, CompilerMode.Debug, sink);

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP5001_LibraryHasNoFunctions);
    }

    // ------------------------------------------------------------------
    // SC7: Debug mode inserts IrOp_DebugProbe_NodeEnter at block start
    //      when first statement has a non-null NodeId.
    // ------------------------------------------------------------------

    [Fact]
    public void Stage6_DebugProbe_InsertsNodeEnterInDebugMode()
    {
        var nodeId   = Guid.NewGuid();
        var graphId  = Guid.NewGuid();
        var typeRef  = new IrTypeRef { FullName = "System.Int32", IsUnmanaged = true, SizeBytes = 4 };
        var val      = new IrValue(0, typeRef);
        var nullDbg  = new IrDebugAnnotation { GraphId = graphId };

        var blockWithNodeId = new IrBlock
        {
            Id    = new IrBlockId(0),
            Label = "entry",
            Statements = new[]
            {
                new IrStatement
                {
                    ResultValue = val,
                    Operation   = new IrOp_Const("0", typeRef),
                    Debug       = new IrDebugAnnotation { NodeId = nodeId, GraphId = graphId },
                },
            },
            Terminator = new IrTerm_Return(null) { Debug = nullDbg },
        };

        var asset = new IrAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "DebugTest",
            Dispatch = AssetDispatchKind.Library,
            Graphs   = new[]
            {
                new IrGraph
                {
                    Id     = graphId,
                    Name   = "Main",
                    Kind   = IrGraphKind.Function,
                    Entry  = new IrBlockId(0),
                    Blocks = new[] { blockWithNodeId },
                },
            },
        };

        var result = DebugProbeInsertion.Apply(asset, CompilerMode.Debug);

        var resultBlock = result.Graphs[0].Blocks[0];

        // First statement is the synthesized probe.
        Assert.IsType<IrOp_DebugProbe_NodeEnter>(resultBlock.Statements[0].Operation);
        var probe = (IrOp_DebugProbe_NodeEnter)resultBlock.Statements[0].Operation;
        Assert.Equal(nodeId, probe.NodeId);

        // Original statement is now at index 1.
        Assert.IsType<IrOp_Const>(resultBlock.Statements[1].Operation);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    // Builds a minimal Instance IrAsset with one variable field, with layout computed.
    private static IrAsset BuildMinimalInstance(Guid assetId, string fieldName, IrTypeRef fieldType)
    {
        var field = new IrField
        {
            Id   = Guid.NewGuid(),
            Name = fieldName,
            Type = fieldType,
        };

        var raw = new IrAsset
        {
            AssetId   = assetId,
            Name      = "Test",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = new[] { field },
        };

        // Run FieldLayout so Offset/Size are stamped in (StructureHash includes them).
        return FieldLayout.ComputeFieldLayouts(raw);
    }
}
