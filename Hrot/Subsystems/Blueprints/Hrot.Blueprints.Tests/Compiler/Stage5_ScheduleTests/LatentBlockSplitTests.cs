using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for latent-node basic-block splitting in Stage5.
/// Each latent node (LatentDelay, WaitForChannel, WaitForEvent) splits the
/// current block: the pre-suspend block ends with IrTerm_Suspend,
/// and a fresh resume block continues from the node's exec successor.
/// </summary>
public sealed class LatentBlockSplitTests
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

    [Fact]
    public void LatentDelay_SplitsIntoTwoBlocks_WithSuspendTerminator()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("DelayAction")
            .WithHostings(Hrot.Blueprints.Core.Assets.AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Delay(0.5f).Return())
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors);
        var graph = Assert.Single(ir.Graphs);
        // Entry block + resume block = at least 2 blocks.
        Assert.True(graph.Blocks.Count >= 2,
            $"Expected >= 2 blocks after LatentDelay split, got {graph.Blocks.Count}.");

        var preBlock = graph.Blocks[0];
        Assert.IsType<IrTerm_Suspend>(preBlock.Terminator);
    }

    [Fact]
    public void WaitForChannel_SplitsIntoTwoBlocks_WithSuspendTerminator()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("WaitAction")
            .WithHostings(Hrot.Blueprints.Core.Assets.AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().WaitForChannel("LocomotionChannel").Return())
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors);
        var graph = Assert.Single(ir.Graphs);
        Assert.True(graph.Blocks.Count >= 2,
            $"Expected >= 2 blocks after WaitForChannel split, got {graph.Blocks.Count}.");

        var preBlock = graph.Blocks[0];
        Assert.IsType<IrTerm_Suspend>(preBlock.Terminator);
    }

    [Fact]
    public void MultipleLatentNodes_ProducesMultipleResumeBlocks()
    {
        // Two latent nodes -> 3 blocks: entry, resume1, resume2.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("TwoWaits")
            .WithHostings(Hrot.Blueprints.Core.Assets.AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .WaitForChannel("Chan1")
                .WaitForChannel("Chan2")
                .Return())
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors);
        var graph = Assert.Single(ir.Graphs);
        Assert.True(graph.Blocks.Count >= 3,
            $"Expected >= 3 blocks for two latent nodes, got {graph.Blocks.Count}.");

        // First two blocks should be suspended.
        Assert.IsType<IrTerm_Suspend>(graph.Blocks[0].Terminator);
        Assert.IsType<IrTerm_Suspend>(graph.Blocks[1].Terminator);
    }

    [Fact]
    public void ResumeBlock_HasCorrectTerminator()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("WaitThenReturn")
            .WithHostings(Hrot.Blueprints.Core.Assets.AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .WaitForChannel("Chan")
                .Return(Hrot.Blueprints.Core.Assets.NodeStatus.Success))
            .Build();

        var (ir, sink) = RunSchedule(asset);

        Assert.False(sink.HasErrors);
        var graph = Assert.Single(ir.Graphs);
        Assert.True(graph.Blocks.Count >= 2);

        // Last block should end with ReturnStatus(Success).
        var resumeBlock = graph.Blocks[graph.Blocks.Count - 1];
        var term = Assert.IsType<IrTerm_ReturnStatus>(resumeBlock.Terminator);
        Assert.Equal(Hrot.Blueprints.Core.Assets.NodeStatus.Success, term.Status);
    }

    private static (IrAsset ir, DiagnosticSink sink) RunSchedule(
        Hrot.Blueprints.Core.Assets.BlueprintAsset asset)
    {
        var opts  = DefaultOptions();
        var sink  = new DiagnosticSink();
        var ctx   = new ValidationContext(sink, opts);
        // Bypass full validation/type-resolve for latent tests (catalog stubs empty).
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ir = Stage5_Schedule.Run(typed, ctx);
        return (ir, sink);
    }
}
