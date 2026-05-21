using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class HasVisibleTarget_EndToEndTests
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
    public void HasVisibleTarget_CompilesSuccessfully()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Expected success but got errors: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);
    }

    [Fact]
    public void HasVisibleTarget_GeneratedSource_ContainsExpectedStructures()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.HasVisibleTarget);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());
        Assert.True(result.Succeeded);

        var src = result.GeneratedSource!;
        // Condition AiPrimitive still emits Params and WorkingState.
        Assert.Contains("public struct Params",       src);
        Assert.Contains("public struct WorkingState", src);
        Assert.Contains("TickCore",                   src);
    }
}
