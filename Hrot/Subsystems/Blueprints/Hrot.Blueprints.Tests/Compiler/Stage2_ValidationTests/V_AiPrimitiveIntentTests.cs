using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class V_AiPrimitiveIntentTests
{
    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        var opts = new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());
        Stage2_Validate.Run(asset, new ValidationContext(sink, opts));
        return sink.All;
    }

    [Fact]
    [CoversDiagnosticCode("BP1100")]
    public void Condition_WithReturnRunning_EmitsBP1100()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("C")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().Return(NodeStatus.Running))
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1100);
    }

    [Fact]
    [CoversDiagnosticCode("BP1101")]
    public void Condition_WithLatentDelay_EmitsBP1101()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("C")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().Delay(1.0f).Return())
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1101);
    }

    [Fact]
    [CoversDiagnosticCode("BP1101")]
    public void Condition_WithWaitForChannel_EmitsBP1101()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("C")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().WaitForChannel("AnyChannel").Return())
            .Build();

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1101);
    }

    [Fact]
    public void Action_WithReturnRunning_NoDiagnostics()
    {
        // Running return is valid for Actions.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return(NodeStatus.Running))
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d =>
            d.Code == DiagnosticCodes.BP1100 || d.Code == DiagnosticCodes.BP1101);
    }

    [Fact]
    public void Condition_WithReturnSuccess_NoDiagnostics()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("C")
            .WithIntent(AiPrimitiveIntent.Condition)
            .WithHostings(AiPrimitiveHosting.BTreeCondition)
            .WithGraph("Main", g => g.Entry().Return(NodeStatus.Success))
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d =>
            d.Code == DiagnosticCodes.BP1100 || d.Code == DiagnosticCodes.BP1101);
    }
}
