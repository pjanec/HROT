using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-03 (Slice W1): validator coverage for <c>V_ComponentAccessRules</c> (BP2060-BP2062) --
/// <c>SetComponentNode</c>'s STRUCTURAL rules (ComponentTypeFqn well-formed, self-only). Deliberately
/// does NOT test a <c>[BlueprintWritable]</c> gate -- the compiler cannot reflect over the real
/// component type (see <c>V_ComponentAccessRules</c>'s doc comment), so no such check exists here;
/// that gate is enforced editor-side only (CA-04).
/// </summary>
public sealed class V_ComponentAccessValidatorTests
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

    // ---- BP2060: ComponentTypeFqn empty ----------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2060")]
    public void Validate_EmptyComponentTypeFqn_BP2060()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "",
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2060);
    }

    // ---- BP2061: ComponentTypeFqn not well-formed -------------------------

    [Fact]
    [CoversDiagnosticCode("BP2061")]
    public void Validate_MalformedComponentTypeFqn_BP2061()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            // Neither a StaticTypeRegistry primitive nor a well-formed dotted FQN -- unresolvable.
            ComponentTypeFqn = "not a type id",
        });

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2061);
    }

    // ---- BP2062: "Target" pin present -- self-only ------------------------

    [Fact]
    [CoversDiagnosticCode("BP2062")]
    public void Validate_TargetPinPresent_BP2062()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var setComp = new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
        };
        // SetComponent never gets a "Target" pin from Stage0 enrichment (self-only by
        // construction) -- author one directly to prove a hand-authored/legacy asset is caught.
        setComp.Pins.Add(new Pin
        {
            Id = Guid.NewGuid(), Name = "Target", Direction = "In", IsExec = false,
            TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" },
        });
        asset.Graphs[0].Nodes.Add(setComp);

        var diags = Validate(asset);
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2062);
    }

    // ---- Happy path: valid SetComponent, no errors ------------------------

    [Fact]
    public void Validate_ValidComponentTypeFqn_NoComponentAccessErrors()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Numerics.Vector3",
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d =>
            d.Code == DiagnosticCodes.BP2060
            || d.Code == DiagnosticCodes.BP2061
            || d.Code == DiagnosticCodes.BP2062);
    }

    // ---- Also accepts a primitive TypeId (e.g. System.Int32) --------------

    [Fact]
    public void Validate_PrimitiveComponentTypeFqn_NoBP2061()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("SetComponentTest")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = "System.Int32",
        });

        var diags = Validate(asset);
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2061);
    }
}
