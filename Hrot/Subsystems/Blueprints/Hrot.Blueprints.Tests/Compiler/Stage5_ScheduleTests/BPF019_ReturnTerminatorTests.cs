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
/// Tests for BPF-019: BuildReturnTerminator must resolve the return value from the
/// current block's statements, not the last-allocated block's statements.
/// </summary>
public sealed class BPF019_ReturnTerminatorTests
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

    /// <summary>
    /// BPF-019: When an Instance blueprint has a Tick graph with a LatentDelay node,
    /// the scheduler allocates a resume block (bb1).  After allocating bb1, if there
    /// were any further blocks allocated after bb1, the bug would cause ReturnNode
    /// in bb1 to look in the wrong (empty) block for its terminator.
    /// This test uses a two-Delay chain: entry(bb0) -> delay(bb1) -> delay(bb2) ->
    /// Return in bb2.  With the bug, the Return in bb1-or-bb2 would use the LAST
    /// block's (empty) statements.  With the fix, it uses the current block.
    /// We verify the schedule produces no errors and the last block has a Return terminator.
    /// </summary>
    [Fact]
    public void ReturnInNonFinalBlock_SchedulesWithoutError()
    {
        // Build an Instance blueprint with two Delay nodes so that multiple blocks
        // are allocated before the final Return block is scheduled.
        var asset = BlueprintAssetBuilder
            .Instance("MultiDelayInst")
            .WithGraph("Tick", g => g
                .Entry()
                .Delay(0.5f)
                .Delay(1.0f)
                .Return())
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(ir.Graphs);
        // Entry, resume1, resume2 = 3 blocks minimum.
        Assert.True(graph.Blocks.Count >= 3,
            $"Expected >= 3 blocks, got {graph.Blocks.Count}.");

        // The final block's terminator must be a Return terminator for Instance dispatch.
        var lastBlock = graph.Blocks[graph.Blocks.Count - 1];
        Assert.IsType<IrTerm_Return>(lastBlock.Terminator);
    }

    /// <summary>
    /// BPF-019: The BranchNode introduces non-final blocks. Ensure that ReturnNode
    /// at the end of a branch does not accidentally use the last-allocated block's
    /// (empty) statement list when resolving its terminator.
    /// </summary>
    [Fact]
    public void BranchThenReturn_ProducesNonEmptyTerminatorInEachBranch()
    {
        // Build an AiPrimitive with a Branch node -- both branches end in Return.
        // The trueBlock is allocated before falseBlock, so with the bug,
        // trueBlock's ReturnNode would use falseBlock.Statements (empty).
        var asset = BlueprintAssetBuilder
            .AiPrimitive("BranchReturn")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .Branch(
                    "true",
                    trueBranch:  tb => tb.Return(NodeStatus.Success),
                    falseBranch: fb => fb.Return(NodeStatus.Failure)))
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors,
            $"Errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(ir.Graphs);
        // entry + trueBlock + falseBlock = 3 blocks
        Assert.Equal(3, graph.Blocks.Count);

        // Both branch blocks must have a ReturnStatus terminator (not FallThrough / null).
        var trueBlock  = graph.Blocks[1];
        var falseBlock = graph.Blocks[2];
        Assert.IsType<IrTerm_ReturnStatus>(trueBlock.Terminator);
        Assert.IsType<IrTerm_ReturnStatus>(falseBlock.Terminator);
    }
}
