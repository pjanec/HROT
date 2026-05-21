using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Verifies that the full compiler pipeline produces byte-identical output
/// when run multiple times on the same asset.
/// </summary>
public sealed class CompilerDeterminismTests
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

    private static CompileResult Compile(string assetName)
    {
        var asset = TestData.LoadAsset(assetName);
        return new BlueprintCompiler().Compile(asset, DefaultOptions());
    }

    [Theory]
    [InlineData(TestData.SampleAssets.LibraryMath)]
    [InlineData(TestData.SampleAssets.InstanceCounter)]
    [InlineData(TestData.SampleAssets.HasVisibleTarget)]
    [InlineData(TestData.SampleAssets.MoveToAndFire)]
    public void FullPipeline_IsDeterministic(string assetName)
    {
        var r1 = Compile(assetName);
        var r2 = Compile(assetName);

        Assert.Equal(r1.Succeeded, r2.Succeeded);
        Assert.Equal(r1.GeneratedSource, r2.GeneratedSource);
    }

    [Theory]
    [InlineData(TestData.SampleAssets.LibraryMath)]
    [InlineData(TestData.SampleAssets.InstanceCounter)]
    [InlineData(TestData.SampleAssets.MoveToAndFire)]
    public void GeneratedFileName_IsIdenticalAcrossRuns(string assetName)
    {
        var r1 = Compile(assetName);
        var r2 = Compile(assetName);

        Assert.Equal(r1.GeneratedFileName, r2.GeneratedFileName);
    }

    [Fact]
    public void DifferentAssets_HaveDifferentGeneratedSources()
    {
        var r1 = Compile(TestData.SampleAssets.LibraryMath);
        var r2 = Compile(TestData.SampleAssets.InstanceCounter);

        Assert.NotEqual(r1.GeneratedSource, r2.GeneratedSource);
    }
}
