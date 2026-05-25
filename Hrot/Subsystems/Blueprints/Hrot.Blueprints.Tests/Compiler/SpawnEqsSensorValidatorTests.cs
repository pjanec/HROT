using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

public sealed class SpawnEqsSensorValidatorTests
{
    // ---- helpers --------------------------------------------------------

    private static CompileOptions DefaultOptions(IEqsTemplateCatalog? catalog = null) =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>(),
            EqsTemplates:      catalog);

    private static IReadOnlyList<Diagnostic> Validate(
        BlueprintAsset asset,
        IEqsTemplateCatalog? catalog = null)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions(catalog)));
        return sink.All;
    }

    // ---- Stub catalog for tests ----------------------------------------

    private sealed class StubEqsTemplateCatalog(params Guid[] knownIds) : IEqsTemplateCatalog
    {
        private readonly HashSet<Guid> _known = new(knownIds);
        public bool Contains(Guid assetId) => _known.Contains(assetId);
    }

    // ---- BP2030: unsupported dispatch ----------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2030")]
    public void Validate_UnsupportedDispatch_BP2030()
    {
        // Library dispatch -- forbidden
        var asset = BlueprintAssetBuilder
            .Library("LibTest")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SpawnEqsSensorNode
        {
            Id             = Guid.NewGuid(),
            TemplateAssetId = Guid.NewGuid(),
        });

        // Use a catalog so BP2031 is suppressed for a valid-looking template ID.
        var knownTemplateId = ((SpawnEqsSensorNode)asset.Graphs[0].Nodes.Last()).TemplateAssetId;
        var diags = Validate(asset, new StubEqsTemplateCatalog(knownTemplateId));
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2030);
    }

    // ---- BP2031: template not found ------------------------------------

    [Fact]
    [CoversDiagnosticCode("BP2031")]
    public void Validate_TemplateNotFound_BP2031()
    {
        var knownTemplateId = Guid.NewGuid();
        var unknownTemplateId = Guid.NewGuid();

        var asset = BlueprintAssetBuilder
            .Instance("InstanceTest")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SpawnEqsSensorNode
        {
            Id              = Guid.NewGuid(),
            TemplateAssetId  = unknownTemplateId,   // not in catalog
        });

        // Catalog knows only knownTemplateId, not unknownTemplateId.
        var diags = Validate(asset, new StubEqsTemplateCatalog(knownTemplateId));
        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP2031);
    }

    [Fact]
    public void Validate_NoCatalog_NoTemplateError()
    {
        // When EqsTemplates == null, BP2031 is suppressed regardless of TemplateAssetId.
        var asset = BlueprintAssetBuilder
            .Instance("InstanceTest")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SpawnEqsSensorNode
        {
            Id              = Guid.NewGuid(),
            TemplateAssetId  = Guid.NewGuid(),   // some arbitrary ID
        });

        var diags = Validate(asset, catalog: null);   // no catalog
        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP2031);
    }

    // ---- Happy path: valid Instance SpawnEqsSensorNode -----------------

    [Fact]
    public void Validate_ValidInstance_WithCatalog_NoErrors()
    {
        var knownTemplateId = Guid.NewGuid();

        var asset = BlueprintAssetBuilder
            .Instance("InstanceTest")
            .WithGraph("Main", GraphKind.Function, g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(new SpawnEqsSensorNode
        {
            Id              = Guid.NewGuid(),
            TemplateAssetId  = knownTemplateId,
        });

        var diags = Validate(asset, new StubEqsTemplateCatalog(knownTemplateId));
        Assert.DoesNotContain(diags, d => d.IsError);
    }
}
