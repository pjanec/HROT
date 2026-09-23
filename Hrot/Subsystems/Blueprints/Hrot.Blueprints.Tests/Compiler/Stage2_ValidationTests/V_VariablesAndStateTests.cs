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
        // ⭐ CE-326 (2026-09-23) — PREMISE CORRECTION, not a weakened claim. This rail used 26 ints
        //   (104 B) to clear a 100-byte limit. That 100 was the width of BrainBlackboard
        //   .BehaviorParameters, a component P4 DELETED; BP1200 now refuses only what no occurrence
        //   tier could hold (BlueprintTierLadder.Tier16384PayloadSize = 16096).
        // ⇒ 4025 System.Int32 = 16100 B, four bytes past the ceiling. ⚠ What the rail ASSERTS is
        //   untouched: params too wide to store emit BP1200. Only the width that counts as "too wide"
        //   moved, because the storage moved. 📄 DESIGN_Occurrence_Scoped_Storage.md §30.30.1.
        var builder = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction);

        for (int i = 0; i < 4025; i++)
            builder = builder.WithParameter($"p{i}", typeof(int));

        var asset = builder.Build();
        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1200);
    }

    [Fact]
    [CoversDiagnosticCode("BP1201")]
    public void AiPrimitive_WorkingStateOverLimit_EmitsBP1201()
    {
        // ⭐ CE-326 (2026-09-23) — PREMISE CORRECTION, as for BP1200 above. The old 1016 was
        //   `1024 - 8`: the payload of Blackboard1024, which P4 DELETED. BP1201 now refuses only what
        //   no tier could hold (16096).
        // ⇒ 2013 System.Int64 = 16104 B, eight bytes past the ceiling.
        var builder = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction);

        for (int i = 0; i < 2013; i++)
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
