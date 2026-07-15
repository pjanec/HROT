using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// Slice 2a-2: validator coverage for <c>V_SharedStateRules</c> (BP2040-BP2042) --
/// GetSharedNode/SetSharedNode's SharedTypeId rules.
/// </summary>
public sealed class V_SharedStateValidatorTests
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

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));
        return sink.All;
    }

    // ---- BP2040: SharedTypeId empty -------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2040")]
    public void Validate_EmptySharedTypeId_BP2040()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedStateTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new GetSharedNode
        {
            Id           = Guid.NewGuid(),
            VariableId   = "rally",
            SharedTypeId = "",
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2040);
    }

    // ---- BP2041: SharedTypeId does not resolve --------------------------

    [Fact]
    [CoversDiagnosticCode("BP2041")]
    public void Validate_UnresolvableSharedTypeId_BP2041()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedStateTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetSharedNode
        {
            Id           = Guid.NewGuid(),
            VariableId   = "rally",
            // Neither a StaticTypeRegistry primitive nor "global::"-prefixed -- unresolvable.
            SharedTypeId = "not a type id",
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2041);
    }

    // ---- BP2042: unsupported (Library) dispatch -------------------------

    [Fact]
    [CoversDiagnosticCode("BP2042")]
    public void Validate_LibraryDispatch_BP2042()
    {
        var asset = BlueprintAssetBuilder
            .Library("SharedStateLibTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new GetSharedNode
        {
            Id           = Guid.NewGuid(),
            VariableId   = "rally",
            SharedTypeId = "Hrot.AI.Behaviors.Brains.SquadRallyState",
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2042);
    }

    // ---- Happy path: valid GetShared/SetShared, no errors --------------

    [Fact]
    public void Validate_ValidSharedTypeId_NoErrors()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedStateTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new GetSharedNode
        {
            Id           = Guid.NewGuid(),
            VariableId   = "rally",
            SharedTypeId = "Hrot.AI.Behaviors.Brains.SquadRallyState",
        });
        asset.Graphs[0].Nodes.Add(new SetSharedNode
        {
            Id           = Guid.NewGuid(),
            VariableId   = "rally",
            SharedTypeId = "Hrot.AI.Behaviors.Brains.SquadRallyState",
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d =>
            d.Code == DiagnosticCodes.BP2040
            || d.Code == DiagnosticCodes.BP2041
            || d.Code == DiagnosticCodes.BP2042);
    }

    // ---- Also accepts a primitive TypeId (e.g. System.Int32) ------------

    [Fact]
    public void Validate_PrimitiveSharedTypeId_NoBP2041()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SharedStateTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new GetSharedNode
        {
            Id           = Guid.NewGuid(),
            VariableId   = "counter",
            SharedTypeId = "System.Int32",
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2041);
    }
}
