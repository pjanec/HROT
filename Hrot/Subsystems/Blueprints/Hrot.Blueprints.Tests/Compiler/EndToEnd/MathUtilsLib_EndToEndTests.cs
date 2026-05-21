using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// End-to-end tests for LibraryMath (Library) blueprint.
/// </summary>
public sealed class MathUtilsLib_EndToEndTests
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
    public void LibraryMath_CompilesSuccessfully()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Expected success but got errors: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);
    }

    [Fact]
    public void LibraryMath_GeneratedSource_ContainsStaticClassWithBpSuffix()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded);

        var src = result.GeneratedSource!;
        // Library emits a static class.
        Assert.Contains("public static class", src);
        Assert.Contains("_Bp", src);
        Assert.Contains("public const int BlueprintId", src);
    }

    [Fact]
    public void LibraryMath_GeneratedSource_ContainsBlueprintRegistrar()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded);

        // Library blueprints emit a registrar class.
        Assert.Contains("BlueprintRegistrar_", result.GeneratedSource!);
    }

    [Fact]
    public void LibraryMath_IsDeterministic()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var opts  = DefaultOptions();
        var r1 = new BlueprintCompiler().Compile(asset, opts);
        var r2 = new BlueprintCompiler().Compile(asset, opts);
        Assert.Equal(r1.GeneratedSource, r2.GeneratedSource);
    }

    [Theory]
    [InlineData(CompilerMode.Debug)]
    [InlineData(CompilerMode.Release)]
    public void LibraryMath_Compiles_InBothModes(CompilerMode mode)
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var opts  = new CompileOptions(
            Mode:              mode,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

        var result = new BlueprintCompiler().Compile(asset, opts);
        Assert.True(result.Succeeded,
            $"Mode={mode}: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
    }
}
