using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// End-to-end tests for DoorActor (Instance) and DoorSensor (Instance) blueprints.
/// Verifies both compile successfully and produce distinct output.
/// </summary>
public sealed class DoorActor_DoorSensor_EndToEndTests
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
    public void DoorActor_CompilesSuccessfully()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.DoorActor);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Expected success but got errors: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
    }

    [Fact]
    public void DoorSensor_CompilesSuccessfully()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.DoorSensor);
        var result = new BlueprintCompiler().Compile(asset, DefaultOptions());

        Assert.True(result.Succeeded,
            $"Expected success but got errors: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
    }

    [Fact]
    public void DoorActor_And_DoorSensor_HaveDifferentGeneratedSources()
    {
        var actor  = new BlueprintCompiler().Compile(
            TestData.LoadAsset(TestData.SampleAssets.DoorActor),   DefaultOptions());
        var sensor = new BlueprintCompiler().Compile(
            TestData.LoadAsset(TestData.SampleAssets.DoorSensor),  DefaultOptions());

        Assert.True(actor.Succeeded && sensor.Succeeded);
        Assert.NotEqual(actor.GeneratedSource, sensor.GeneratedSource);
    }

    [Fact]
    public void DoorActor_And_DoorSensor_HaveDifferentBlueprintIds()
    {
        var actor  = new BlueprintCompiler().Compile(
            TestData.LoadAsset(TestData.SampleAssets.DoorActor),   DefaultOptions());
        var sensor = new BlueprintCompiler().Compile(
            TestData.LoadAsset(TestData.SampleAssets.DoorSensor),  DefaultOptions());

        Assert.True(actor.Succeeded && sensor.Succeeded);
        Assert.NotEqual(actor.GeneratedFileName, sensor.GeneratedFileName);
    }
}
