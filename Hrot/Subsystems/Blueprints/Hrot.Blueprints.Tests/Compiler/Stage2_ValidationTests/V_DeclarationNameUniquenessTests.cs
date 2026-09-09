using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Hrot.Blueprints.Core;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐ <b><c>BP1673</c> — the rail <c>U-12</c> makes necessary by removing two others.</b>
///
/// <para>
/// ⛔ <b>The mechanism.</b> <c>Stage5.FindVariableRef</c> searches <c>Variables</c> →
/// <c>WorkingState</c> → <c>Parameters</c> and falls back to matching by <b>name</b> — the path
/// hand-authored assets take. Two declarations of one name bind to whichever kind that order reaches
/// first, silently. ⭐ It was unreachable only because <c>BP1024</c> and <c>BP1031</c>'s
/// <c>WorkingState</c> half made the mixture itself illegal, so at most one list was ever populated.
/// </para>
///
/// <para>
/// ⚠ <b><c>U-14</c> does not cover this.</b> That closed <c>BlueprintDocumentFactory.MakeUniqueName</c>
/// — the <b>editor's</b> auto-namer. A hand-authored <c>.bp.json</c> never goes near it, and the
/// corpus is hand-authored.
/// </para>
/// </summary>
public sealed class V_DeclarationNameUniquenessTests
{
    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>())));
        return sink.All;
    }

    /// <summary>
    /// ⭐⭐ <b>The exact shape <c>BP1024</c>'s retirement newly permits:</b> an AiPrimitive carrying
    /// both a <c>WorkingState</c> and a <c>Variable</c> called <c>Health</c>. ⛔ Before this rule, a
    /// <c>GetVariable</c> naming <c>Health</c> would have bound to the <c>Variable</c> and nothing
    /// would have said so.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1673")]
    public void AWorkingStateAndAVariableSharingANameIsRefused()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithWorkingStateField("Health", typeof(int))
            .WithVariable("Health", typeof(int))
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1673);
    }

    /// <summary>⭐ Case-insensitive, matching <c>U-14</c>'s <c>OrdinalIgnoreCase</c> namer — so the
    /// compiler refuses exactly what the editor refuses to create.</summary>
    [Fact]
    [CoversDiagnosticCode("BP1673")]
    public void TheComparisonIsCaseInsensitive()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithWorkingStateField("Health", typeof(int))
            .WithVariable("health", typeof(int))
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1673);
    }

    /// <summary>⭐ Same-kind duplicates too — equally undiagnosed before, and the same defect. A
    /// designer reading the error does not care which list the twin is in.</summary>
    [Fact]
    [CoversDiagnosticCode("BP1673")]
    public void TwoVariablesSharingANameAreRefused()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithVariable("Speed", typeof(int))
            .WithVariable("Speed", typeof(float))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1673);
    }

    /// <summary>
    /// ⛔⛔ <b>The rule must not fire on the shape <c>U-12</c> exists to allow.</b> Distinct names
    /// across the two <c>(State, Asset)</c> spellings are exactly what Pass 1 says must compile — if
    /// <c>BP1673</c> fired here it would have re-implemented <c>BP1024</c> under a new number.
    /// </summary>
    [Fact]
    public void DistinctNamesAcrossKindsAreFine()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("A")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithWorkingStateField("Health", typeof(int))
            .WithVariable("Speed", typeof(int))
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1673);
    }

    /// <summary>
    /// ⭐ <b>A graph local may still shadow an asset declaration.</b> That is ruled — <c>Q27-C1</c>:
    /// a local wins inside its own graph — and it is a different scope, so this asset-scope rule must
    /// stay out of it. ⚠ Without this test the obvious "just check every name" implementation would
    /// pass every other assertion here and break shadowing.
    /// </summary>
    [Fact]
    public void AGraphLocalMayShadowAnAssetDeclaration()
    {
        var asset = BlueprintAssetBuilder
            .Instance("I")
            .WithVariable("Health", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();
        asset.Graphs[0].LocalVariables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Health",
            Type = asset.Declarations.First().Type,
        });

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1673);
    }

    /// <summary>
    /// ⭐⭐ <b>And the corpus is clean</b> — all 58 shipped assets, asserted rather than assumed.
    /// ⛔ If this ever reddens, <c>BP1673</c> is not a new rail but a bug report about the corpus.
    /// </summary>
    [Fact]
    public void NoShippedAssetCarriesACollision()
    {
        var offenders = CorpusCanonicalisationTests.AllManagedFiles()
            .Select(f => (File: Path.GetFileName(f),
                          Asset: BlueprintJsonServices.Deserialize(File.ReadAllText(f))!))
            .Select(x => (x.File, Dupes: x.Asset.Declarations
                .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList()))
            .Where(x => x.Dupes.Count > 0)
            .ToList();

        Assert.True(offenders.Count == 0,
            "shipped assets carrying a duplicate declaration name:\n  "
            + string.Join("\n  ", offenders.Select(o => $"{o.File}: {string.Join(", ", o.Dupes)}")));
    }
}
