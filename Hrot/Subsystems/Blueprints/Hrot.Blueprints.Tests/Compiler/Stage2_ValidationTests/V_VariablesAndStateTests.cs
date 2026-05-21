using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class V_VariablesAndStateTests
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
    [CoversDiagnosticCode("BP1200")]
    public void AiPrimitive_ParamsOverLimit_EmitsBP1200()
    {
        // 101 bytes of parameters -- just over the 100-byte limit.
        // Use 26 System.Int32 parameters (26 * 4 = 104 bytes after alignment).
        var builder = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction);

        for (int i = 0; i < 26; i++)
            builder = builder.WithParameter($"p{i}", typeof(int));

        var asset = builder.Build();
        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1200);
    }

    [Fact]
    [CoversDiagnosticCode("BP1201")]
    public void AiPrimitive_WorkingStateOverLimit_EmitsBP1201()
    {
        // Max is 1016 bytes. Use 128 System.Int64 fields (128 * 8 = 1024 bytes).
        var builder = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction);

        for (int i = 0; i < 128; i++)
            builder = builder.WithWorkingStateField($"w{i}", typeof(long));

        var asset = builder.Build();
        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1201);
    }

    [Fact]
    [CoversDiagnosticCode("BP1210")]
    public void Instance_VariablesTooLargeForAnyTier_EmitsBP1210()
    {
        // Max tier is 16096 bytes. Use 2020 System.Int64 fields (2020 * 8 = 16160 bytes).
        var builder = BlueprintAssetBuilder
            .Instance("I");

        for (int i = 0; i < 2020; i++)
            builder = builder.WithVariable($"v{i}", typeof(long));

        var asset = builder.Build();
        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1210);
    }

    [Fact]
    [CoversDiagnosticCode("BP1211")]
    public void Instance_ForcedTierTooSmall_EmitsBP1211()
    {
        // Force tier 1024 (928-byte budget) but add 120 int fields (120 * 4 = 480 bytes after alignment > 928? no...)
        // Force1024 = 928 bytes budget. Add 120 System.Int64 fields (960 bytes) > 928.
        var builder = BlueprintAssetBuilder
            .Instance("I")
            .WithTierHint(BlackboardTierHint.Force1024);

        for (int i = 0; i < 120; i++)
            builder = builder.WithVariable($"v{i}", typeof(long));

        var asset = builder.Build();
        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1211);
    }

    [Fact]
    public void Instance_SmallVariables_NoDiagnostics()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithVariable("hp", typeof(float))
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d =>
            d.Code == DiagnosticCodes.BP1210 || d.Code == DiagnosticCodes.BP1211);
    }
}
