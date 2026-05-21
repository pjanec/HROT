using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class InstanceLoweringTests
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
    public void Instance_WithLatentGraph_ProducesCursorDispatch()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithGraph("Main", g => g.Entry().WaitForChannel("Chan").Return())
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");

        var graph = Assert.Single(lowered.Graphs);
        var entryBlock = graph.Blocks.First(b => b.Id == graph.Entry);
        Assert.Equal("cursor_dispatch", entryBlock.Label);
    }

    [Fact]
    public void Instance_NoSuspendAfterLowering()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithGraph("Main", g => g.Entry().Delay(0.5f).Return())
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors);
        foreach (var graph in lowered.Graphs)
            Assert.DoesNotContain(graph.Blocks, b => b.Terminator is IrTerm_Suspend);
    }

    [Fact]
    public void Instance_WithVariables_FieldOffsetsAssigned()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithVariable("hp", typeof(float))
            .WithVariable("mana", typeof(float))
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors);
        // Variables should have non-negative offsets after FieldLayout.
        foreach (var v in lowered.Variables)
            Assert.True(v.Offset >= 0, $"Variable '{v.Name}' has negative offset {v.Offset}.");
    }

    [Fact]
    public void Instance_StructureHashIsNonZero()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithVariable("x", typeof(int))
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors);
        Assert.False(lowered.StructureHash == 0, "Expected non-zero StructureHash after lowering.");
    }
}
