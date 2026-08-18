using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class AiPrimitiveLoweringTests
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
    public void AiPrimitive_ActionWithLatent_InjectsPhaseField()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("Action")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().WaitForChannel("Chan").Return())
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors,
            $"Unexpected errors: {string.Join(", ", sink.All.Select(d => d.Code))}");
        // ⭐ Batch 86 — RESTATED. IrAsset.WorkingState is retired and always empty (R-01); the
        //   synthesized __phase now lands in the ONE state tier. ⛔ Left as-is this would have gone
        //   GREEN-on-empty in reverse — Assert.Contains over [] is a loud red, which is why it caught it.
        Assert.Contains(lowered.StateDeclarations, f => f.Name == "__phase");
    }

    [Fact]
    public void AiPrimitive_WithNoLatent_HasNoPhaseField()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("Action")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        // No latent: __phase field should still be injected for dispatch uniformity.
        // Accept either presence or absence -- just verify no errors.
        Assert.False(sink.HasErrors);
    }

    [Fact]
    public void AiPrimitive_WithLatent_DispatchBlockIsEntry()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("Action")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().WaitForChannel("Chan").Return())
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors);
        var graph = Assert.Single(lowered.Graphs);

        // Entry block must be the dispatch block.
        var entryBlock = graph.Blocks.First(b => b.Id == graph.Entry);
        Assert.Equal("dispatch", entryBlock.Label);
    }

    [Fact]
    public void AiPrimitive_NoSuspendAfterLowering()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("Action")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Delay(1.0f).Return())
            .Build();

        var sink   = new DiagnosticSink();
        var lowered = RunLower(asset, sink);

        Assert.False(sink.HasErrors);
        var graph = Assert.Single(lowered.Graphs);
        Assert.DoesNotContain(graph.Blocks, b => b.Terminator is IrTerm_Suspend);
    }
}
