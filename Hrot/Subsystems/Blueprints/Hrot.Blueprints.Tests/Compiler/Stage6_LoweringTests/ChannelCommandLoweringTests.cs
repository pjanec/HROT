using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class ChannelCommandLoweringTests
{
    private static IrAsset RunLower(
        Hrot.Blueprints.Core.Assets.BlueprintAsset asset, DiagnosticSink sink)
    {
        var opts  = new CompileOptions(
            Mode: CompilerMode.Debug,
            NodeRegistry: BuiltInNodeRegistry.Instance,
            TypeRegistry: StaticTypeRegistry.Instance,
            EngineEvents: BuiltInEngineEventCatalog.Instance,
            ChannelCommands: BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives: BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
        var typed = new TypedAsset(
            asset,
            PinTypes:   new Dictionary<Guid, IrTypeRef>(),
            FieldTypes: new Dictionary<Guid, IrTypeRef>());
        var ctx = new ValidationContext(sink, opts);
        var ir  = Stage5_Schedule.Run(typed, ctx);
        return Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
    }

    [Fact]
    public void AiPrimitive_WithChannelCommand_PreservesIrOp_ChannelCommand()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAction")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ChannelCommand("LocomotionChannel", "MoveTo")
                .Return())
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        // IrOp_ChannelCommand should be present in the lowered IR.
        var allOps = lowered.Graphs
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Statements)
            .Select(s => s.Operation);

        Assert.Contains(allOps, op => op is IrOp_ChannelCommand cc
            && cc.ChannelComponentTypeFqn.Contains("LocomotionChannel")
            && cc.ActionIdConstantName.Contains("MoveTo"));
    }

    [Fact]
    public void AiPrimitive_WithWaitForChannel_ThenReturn_ProducesTwoBlocks()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveAndWait")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g
                .Entry()
                .ChannelCommand("LocomotionChannel", "MoveTo")
                .WaitForChannel("LocomotionChannel")
                .Return())
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors);
        var graph = Assert.Single(lowered.Graphs);

        // After lowering: dispatch + at least phase-0 + channel-check blocks.
        Assert.True(graph.Blocks.Count >= 3,
            $"Expected >= 3 blocks, got {graph.Blocks.Count}.");
    }
}
