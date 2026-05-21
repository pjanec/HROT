using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class AiPrimitiveEmitGoldenTests
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

    [Theory]
    [InlineData(TestData.SampleAssets.HasVisibleTarget)]
    [InlineData(TestData.SampleAssets.MoveToAndFire)]
    public void AiPrimitive_EmitMatchesGoldenSource(string assetName)
    {
        var (src, sink) = EmitAsset(assetName);

        Assert.False(sink.HasErrors,
            $"Compile errors for '{assetName}': {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");

        TestData.ReadOrRegenerateSnapshot($"Emit/{assetName}.cs.txt", src);
    }

    [Fact]
    public void AiPrimitive_EmitIsDeterministic()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        var (s1, _) = EmitAssetDirectly(asset);
        var (s2, _) = EmitAssetDirectly(asset);
        Assert.Equal(s1, s2);
    }

    private static (string src, DiagnosticSink sink) EmitAsset(string assetName)
    {
        var asset = TestData.LoadAsset(assetName);
        return EmitAssetDirectly(asset);
    }

    private static (string src, DiagnosticSink sink) EmitAssetDirectly(
        Hrot.Blueprints.Core.Assets.BlueprintAsset asset)
    {
        var opts   = DefaultOptions();
        var sink   = new DiagnosticSink();
        var ctx    = new ValidationContext(sink, opts);
        Stage2_Validate.Run(asset, ctx);
        var norm   = Stage3_Normalize.Run(asset, ctx);
        var typed  = Stage4_TypeResolve.Run(norm, ctx);
        var ir     = Stage5_Schedule.Run(typed, ctx);
        var low    = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _) = Stage7_Emit.Run(low, CompilerMode.Debug, sink);
        return (src, sink);
    }
}
