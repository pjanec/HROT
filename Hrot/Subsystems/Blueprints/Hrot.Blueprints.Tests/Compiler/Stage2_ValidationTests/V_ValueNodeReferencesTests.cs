using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// BP-15 — reference checks for four node kinds that previously had <b>no validator at all</b>, so an
/// unset or mistyped reference passed Stage 2 silently and only misbehaved at runtime.
/// See <c>V_ValueNodeReferences</c>.
/// </summary>
public sealed class V_ValueNodeReferencesTests
{
    // ---- helpers --------------------------------------------------------

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, DefaultOptions()));
        return sink.All;
    }

    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>A minimal valid asset with one extra node appended to its only graph.</summary>
    private static BlueprintAsset WithNode(Node node)
    {
        var asset = BlueprintAssetBuilder
            .Instance("V")
            .WithGraph("G", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].Nodes.Add(node);
        return asset;
    }

    // ---- BP1403: CallCustomEvent.EventId --------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1403")]
    public void CallCustomEvent_EmptyEventId_EmitsBP1403()
    {
        var asset = WithNode(new CallCustomEventNode { Id = Guid.NewGuid(), EventId = "" });

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1403 && d.IsError);
    }

    /// <summary>
    /// The ordinary authoring shape must NOT error: Stage5's <c>FindCustomEventIndex</c> resolves an
    /// EventId against <c>asset.CustomEvents</c> by parsed Guid <b>or by Name</b>, so a plain name
    /// reference to a declared custom event is legal.
    /// </summary>
    [Fact]
    public void CallCustomEvent_DeclaredEventByName_DoesNotEmitBP1403()
    {
        var asset = BlueprintAssetBuilder
            .Instance("V")
            .WithCustomEvent("OnFire")
            .WithGraph("G", g => g.Entry().CallCustomEvent("OnFire").Return())
            .WithEventGraph("OnFire", g => g.Entry().Return())
            .Build();

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1403);
    }

    /// <summary>
    /// A dotted identity is a baked <c>[BlueprintEvent]</c> the compiler cannot verify
    /// (netstandard2.0 cannot reflect game assemblies), so it is trusted — mirroring
    /// <c>V_EventGraphReferences</c>.
    /// </summary>
    [Fact]
    public void CallCustomEvent_DottedFqn_IsTrusted()
    {
        var asset = WithNode(new CallCustomEventNode
        {
            Id      = Guid.NewGuid(),
            EventId = "Some.Game.Assembly.OnFireEvent",
        });

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1403);
    }

    // ---- BP1404: ScoreDecision.AssetId ----------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1404")]
    public void ScoreDecision_EmptyAssetId_EmitsBP1404()
    {
        var asset = WithNode(new ScoreDecisionNode { Id = Guid.NewGuid(), AssetId = "" });

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1404 && d.IsError);
    }

    /// <summary>
    /// Regression guard for the decision-asset id convention: these are <b>not</b> parseable Guids.
    /// The shipped <c>CombatPostureDecision</c> uses this exact human-readable pseudo-GUID, so a
    /// <c>Guid.TryParse</c> check here would reject real production assets.
    /// </summary>
    [Fact]
    public void ScoreDecision_PseudoGuidAssetId_IsAccepted()
    {
        var asset = WithNode(new ScoreDecisionNode
        {
            Id      = Guid.NewGuid(),
            AssetId = "3c6f9e42-5d10-6f3a-ac23-posture0000001",
        });

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1404);
    }

    // ---- BP1405: ReadRankedResult.Rank ----------------------------------

    [Fact]
    [CoversDiagnosticCode("BP1405")]
    public void ReadRankedResult_NegativeRank_EmitsBP1405()
    {
        var asset = WithNode(new ReadRankedResultNode { Id = Guid.NewGuid(), Rank = -1 });

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1405 && d.IsError);
    }

    [Fact]
    public void ReadRankedResult_ZeroRank_IsAccepted()
    {
        var asset = WithNode(new ReadRankedResultNode { Id = Guid.NewGuid(), Rank = 0 });

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1405);
    }

    // ---- BP1406: Cast.TargetTypeId --------------------------------------

    /// <summary>
    /// Only the <b>empty</b> case is checked here. An unresolvable target is already caught as BP1500
    /// by <c>V_TypeReferences</c>, because <c>BuiltInNodeRegistry</c> projects the Cast out-pin type
    /// from this field. Empty escapes that check — the registry substitutes <c>System.Object</c>,
    /// which resolves fine and makes the cast a silent no-op.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1406")]
    public void Cast_EmptyTargetTypeId_EmitsBP1406()
    {
        var asset = WithNode(new CastNode { Id = Guid.NewGuid(), TargetTypeId = "" });

        var diags = Validate(asset);

        Assert.Contains(diags, d => d.Code == DiagnosticCodes.BP1406 && d.IsError);
    }

    [Fact]
    public void Cast_WithTargetTypeId_DoesNotEmitBP1406()
    {
        var asset = WithNode(new CastNode { Id = Guid.NewGuid(), TargetTypeId = "System.Int32" });

        var diags = Validate(asset);

        Assert.DoesNotContain(diags, d => d.Code == DiagnosticCodes.BP1406);
    }
}
