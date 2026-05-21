using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Lowering;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Tests for StructureHashComputation — sensitivity/stability properties.
/// </summary>
public sealed class StructureHashTests
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

    private static Hrot.Blueprints.Core.Compiler.Ir.IrAsset RunPipeline(
        BlueprintAsset asset)
    {
        var opts   = DefaultOptions();
        var sink   = new DiagnosticSink();
        var ctx    = new ValidationContext(sink, opts);
        Stage2_Validate.Run(asset, ctx);
        var norm   = Stage3_Normalize.Run(asset, ctx);
        var typed  = Stage4_TypeResolve.Run(norm, ctx);
        var ir     = Stage5_Schedule.Run(typed, ctx);
        return Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
    }

    [Fact]
    public void StructureHash_IsDeterministic()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.InstanceCounter);
        var h1 = StructureHashComputation.Compute(RunPipeline(asset));
        var h2 = StructureHashComputation.Compute(RunPipeline(asset));
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void StructureHash_IsNonZero_ForNonTrivialAsset()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        var hash  = StructureHashComputation.Compute(RunPipeline(asset));
        Assert.True(hash != 0UL, "StructureHash should be non-zero for a real asset.");
    }

    [Fact]
    public void StructureHash_ChangesWhenFieldNameChanges()
    {
        var asset1 = BlueprintAssetBuilder
            .Instance("Counter1")
            .WithVariable("count", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var asset2 = BlueprintAssetBuilder
            .Instance("Counter1")
            .WithVariable("tally", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var h1 = StructureHashComputation.Compute(RunPipeline(asset1));
        var h2 = StructureHashComputation.Compute(RunPipeline(asset2));

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void StructureHash_ChangesWhenFieldTypeChanges()
    {
        var asset1 = BlueprintAssetBuilder
            .Instance("Counter2")
            .WithVariable("value", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var asset2 = BlueprintAssetBuilder
            .Instance("Counter2")
            .WithVariable("value", typeof(float))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var h1 = StructureHashComputation.Compute(RunPipeline(asset1));
        var h2 = StructureHashComputation.Compute(RunPipeline(asset2));

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void StructureHash_DifferentAcrossDispatchKinds()
    {
        var lib = BlueprintAssetBuilder
            .Library("SameFields")
            .WithGraph("Fn", g => g.Entry().Return())
            .Build();

        var inst = BlueprintAssetBuilder
            .Instance("SameFields")
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var h1 = StructureHashComputation.Compute(RunPipeline(lib));
        var h2 = StructureHashComputation.Compute(RunPipeline(inst));

        Assert.NotEqual(h1, h2);
    }
}
