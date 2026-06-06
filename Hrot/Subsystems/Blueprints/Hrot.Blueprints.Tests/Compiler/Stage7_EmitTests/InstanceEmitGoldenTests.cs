using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class InstanceEmitGoldenTests
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

    [Theory]
    [InlineData(TestData.SampleAssets.InstanceCounter)]
    [InlineData(TestData.SampleAssets.HealthRegen)]
    [InlineData(TestData.SampleAssets.DoorActor)]
    public void Instance_EmitMatchesGoldenSource(string assetName)
    {
        var (src, sink) = EmitAsset(assetName);

        Assert.False(sink.HasErrors,
            $"Compile errors for '{assetName}': {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");

        TestData.ReadOrRegenerateSnapshot($"Emit/{assetName}.cs.txt", src);
    }

    [Fact]
    public void Instance_EmitIsDeterministic()
    {
        var asset = TestData.LoadAsset(TestData.SampleAssets.HealthRegen);
        var (s1, _) = EmitAssetDirectly(asset);
        var (s2, _) = EmitAssetDirectly(asset);
        Assert.Equal(s1, s2);
    }

    // ---- AN2: Enum TypeRef EMIT round-trip (no double global::) ---------
    //
    // Regression guard for the AN2 emit bug: StaticTypeRegistry.TryResolve must strip the
    // "global::" sentinel from the synthesized enum IrTypeRef.FullName, because
    // StatementEmitter.TypeRefToCSharp re-adds "global::" on emit. If the prefix were kept,
    // an enum-typed Instance State field would emit "global::global::Ns.MyEnum" -> CS0234.
    //
    // CONTRACT: asset-level BlueprintTypeRef.TypeId carries "global::" + FQN (the enum sentinel);
    // compiler-internal IrTypeRef.FullName is the UNPREFIXED FQN; emit re-adds "global::" once.
    [Fact]
    public void Instance_EnumVariable_EmitsSingleGlobalPrefix()
    {
        const string enumFqn = "SomeNamespace.SomeEnum";

        var asset = BlueprintAssetBuilder
            .Instance("EnumEmitTest")
            .Build();

        // Stamp an enum-typed Instance State variable using the editor "global::" convention.
        asset.Variables.Add(new Hrot.Blueprints.Core.Assets.VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Mode",
            Type = new Hrot.Blueprints.Core.Assets.BlueprintTypeRef { TypeId = "global::" + enumFqn },
        });

        var (src, sink) = EmitAssetDirectly(asset);

        Assert.False(sink.HasErrors,
            $"Compile errors: {string.Join(", ", sink.All.Where(d => d.IsError).Select(d => d.Code))}");

        // The State struct field must emit the enum FQN with EXACTLY ONE global:: qualifier.
        Assert.Contains($"global::{enumFqn} Mode;", src);
        // And nowhere in the generated source may a doubled prefix appear.
        Assert.DoesNotContain("global::global::", src);
    }

    private static (string src, DiagnosticSink sink) EmitAsset(string assetName)
    {
        var asset = TestData.LoadAsset(assetName);
        return EmitAssetDirectly(asset);
    }

    private static (string src, DiagnosticSink sink) EmitAssetDirectly(
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
