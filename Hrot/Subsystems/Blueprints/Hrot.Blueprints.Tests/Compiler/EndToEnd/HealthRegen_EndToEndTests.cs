using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class HealthRegen_EndToEndTests
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
    public void HealthRegen_CompilesSuccessfully()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Expected success but got errors: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);
    }

    [Fact]
    public void HealthRegen_GeneratedSource_ContainsTickSignature()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded);

        // Instance blueprints emit a Tick method.
        Assert.Contains("Tick", result.GeneratedSource!);
    }

    [Fact]
    public void HealthRegen_GeneratedSource_ContainsBlueprintIdConstant()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded);

        Assert.Contains("public const int BlueprintId", result.GeneratedSource!);
    }

    [Fact]
    public void HealthRegen_IsDeterministic()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        var opts  = DefaultOptions();
        var r1 = new BlueprintCompiler().Compile(asset, opts);
        var r2 = new BlueprintCompiler().Compile(asset, opts);
        Assert.Equal(r1.GeneratedSource, r2.GeneratedSource);
    }
}
