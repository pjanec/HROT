using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Golden source snapshot tests for Stage7 emission.
/// Run with BLUEPRINT_REGENERATE_SNAPSHOTS=1 to (re)generate.
/// </summary>
public sealed class LibraryEmitGoldenTests
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
    public void Library_EmitMatchesGoldenSource()
    {
        var asset  = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
        var opts   = DefaultOptions();
        var sink   = new DiagnosticSink();
        var ctx    = new ValidationContext(sink, opts);

        Stage2_Validate.Run(asset, ctx);
        var normalized = Stage3_Normalize.Run(asset, ctx);
        var typed      = Stage4_TypeResolve.Run(normalized, ctx);
        var ir         = Stage5_Schedule.Run(typed, ctx);
        var lowered    = Stage6_Lower.Run(ir, CompilerMode.Debug, sink);
        var (src, _)   = Stage7_Emit.Run(lowered, CompilerMode.Debug, sink);

        Assert.False(sink.HasErrors,
            $"Compile errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");

        TestData.ReadOrRegenerateSnapshot($"Emit/LibraryMath.cs.txt", src);
    }

    [Fact]
    public void Library_EmitIsDeterministic()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);

        var (src1, _) = EmitAsset(asset);
        var (src2, _) = EmitAsset(asset);

        Assert.Equal(src1, src2);
    }

    private static (string source, DiagnosticSink sink) EmitAsset(
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
