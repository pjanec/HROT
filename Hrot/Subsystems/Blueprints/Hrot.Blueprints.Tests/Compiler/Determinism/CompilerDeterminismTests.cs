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

    /// <summary>
    /// Per design §17.8: concurrent compilations of the same blueprint must produce
    /// byte-identical emitted source.  This test runs N=4 compilations in parallel and
    /// asserts all outputs are equal to the sequential reference output.
    /// </summary>
    [Theory]
    [InlineData(TestData.SampleAssets.LibraryMath)]
    [InlineData(TestData.SampleAssets.InstanceCounter)]
    [InlineData(TestData.SampleAssets.MoveToAndFire)]
    public void FullPipeline_IsParallelDeterministic(string assetName)
    {
        const int N = 4;

        // Sequential reference.
        var reference = Compile(assetName);
        Assert.True(reference.Succeeded, $"Reference compile failed for {assetName}.");

        // N concurrent compilations.
        var results = new CompileResult[N];
        System.Threading.Tasks.Parallel.For(0, N, i =>
        {
            results[i] = Compile(assetName);
        });

        for (int i = 0; i < N; i++)
        {
            Assert.True(results[i].Succeeded,
                $"Parallel compile {i} failed for {assetName}.");
            Assert.Equal(reference.GeneratedSource, results[i].GeneratedSource);
        }
    }
}
