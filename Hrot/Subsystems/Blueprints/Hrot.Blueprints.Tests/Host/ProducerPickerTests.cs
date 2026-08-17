using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Editor.ActionCatalog;
using Xunit;

namespace Hrot.Blueprints.Tests.Host;

/// <summary>
/// ⭐⭐⭐ <b><c>G7</c> + <c>W10</c> — ONE producer picker.</b>
///
/// <para>
/// 📄 Plan §4c, ruling 9: <c>G7</c>'s <i>"parameter resolver: None / Pick / Create"</i> and
/// <c>W10</c>'s <i>"initializer picker"</i> are both <b>"pick a named producer from a contributing
/// catalog"</b>. ⛔ Two catalogs would be exactly the duplication ruling 9 forbids — the rail for that
/// is <see cref="OneCatalogServesBothCallers"/>.
/// </para>
///
/// <para>
/// ⭐⭐ <b>Identity is the generated FQN, NOT the AssetId</b> — architect <c>AQ2</c>, non-negotiable.
/// ⚠ Asserted as the <b>STORED STRING</b>, because an AssetId would round-trip just as happily and
/// "reload works" would not tell the two apart.
/// </para>
///
/// <para>
/// ⛔ <b>Scope: the PICKER only.</b> "Create resolver" scaffolding (<c>E5</c>), divergence detection
/// (<c>E6</c>) and Library-asset authoring (<c>E1</c>) are out.
/// </para>
/// </summary>
public sealed class ProducerPickerTests
{
    private const string Float = "System.Single";
    private const string Int   = "System.Int32";

    private static readonly Guid ResolverAssetId = new("11110000-0000-0000-0000-000000000001");

    private static BlueprintSignature LibrarySig(
        string name, int blueprintId, params BlueprintFunctionSig[] fns)
        => new(Path: $"{name}.bp.json",
               AssetId: ResolverAssetId,
               Name: name,
               SanitizedName: name,
               BlueprintId: blueprintId,
               Dispatch: BlueprintDispatchKind.Library,
               ExportedFunctions: fns,
               Hostings: Array.Empty<AiPrimitiveHosting>(),
               DeclaredCallablePeers: Array.Empty<Guid>());

    private static BlueprintFunctionSig Fn(string name, string outType)
        => new(name,
               new[] { new BlueprintParamSig("x", Float) },
               new[] { new BlueprintParamSig("value", outType) });

    /// <summary>A hand-written producer — the resolver design's interim escape hatch (<c>E3</c>).</summary>
    internal static class ClrProducers
    {
        public static float SpeedFromRange(float range) => range * 2f;
    }

    private static MethodInfo ClrProducer
        => typeof(ClrProducers).GetMethod(nameof(ClrProducers.SpeedFromRange))!;

    private static ProducerCatalog Catalog(
        IReadOnlyList<BlueprintSignature>? sigs = null,
        IReadOnlyList<MethodInfo>?         clr  = null)
        => new(() => sigs ?? new[] { LibrarySig("AimResolvers", 0x0ABCDEF1,
                                                Fn("ComputeSpeed", Float),
                                                Fn("ComputeCount", Int)) },
               () => clr  ?? new[] { ClrProducer });

    // ── the offer ───────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The catalog offers a producer contributed by EACH source</b> — a Library Function graph
    /// and a hand-written CLR method. ⛔ There is deliberately no source member that nothing supplies:
    /// an unassigned member is a picker option that can never appear.
    /// </summary>
    [Fact]
    public void TheCatalogOffersAProducerFromEverySource()
    {
        var bySource = Catalog().GetProducers().GroupBy(p => p.Source).ToDictionary(g => g.Key, g => g.Count());

        foreach (var source in Enum.GetValues<ProducerSource>())
            Assert.True(bySource.TryGetValue(source, out int n) && n > 0,
                $"No producer was contributed for source {source} -- a source nothing supplies is a "
                + "picker option that can never appear.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>ONE catalog serves both callers.</b> The parameter resolver and the variable
    /// initializer ask the SAME catalog the SAME question — <i>"what can produce a <c>float</c>?"</i> —
    /// and get the same answer. ⛔ A second catalog is what ruling 9 forbids; if the two ever needed
    /// different answers, this rail is where that shows up.
    /// </summary>
    [Fact]
    public void OneCatalogServesBothCallers()
    {
        var catalog = Catalog();

        // G7: a parameter resolver for a float Parameter.
        var resolverOptions = new ProducerPicker(catalog)
            .OptionsFor(DeclarationKind.Parameter, Float);
        // W10: an initializer for a float Variable.
        var initializerOptions = new ProducerPicker(catalog)
            .OptionsFor(DeclarationKind.Variable, Float);

        Assert.Equal(resolverOptions.Select(o => o.Entry?.Fqn),
                     initializerOptions.Select(o => o.Entry?.Fqn));
        // ⭐ And it is a real offer, not two empty lists agreeing.
        Assert.True(resolverOptions.Count > 1);
    }

    /// <summary>
    /// ⭐ <b>Offered over the UNION</b> (📄 <c>PLAN_Cross_Host_Sequencing.md:176</c>) — not
    /// <c>Variables</c> alone. Since <c>U-12</c> the three kinds are one declaration list with a tag,
    /// and a picker offered on only one of them is the spelling rule the unification retired.
    /// </summary>
    [Fact]
    public void OffersOverTheWholeUnion()
    {
        var picker = new ProducerPicker(Catalog());

        foreach (var kind in Enum.GetValues<DeclarationKind>())
        {
            var options = picker.OptionsFor(kind, Float);
            Assert.True(options.Count > 1, $"Nothing offered for DeclarationKind.{kind}.");
        }
    }

    /// <summary>
    /// ⭐ <b>"None" is first and first-class.</b> It is the shipped answer for every declaration, so it
    /// has to be a row you can pick, not the absence of a row.
    /// </summary>
    [Fact]
    public void NoneIsTheFirstRowAndIsSelectable()
    {
        var picker  = new ProducerPicker(Catalog());
        var options = picker.OptionsFor(DeclarationKind.Parameter, Float);

        Assert.Equal(ProducerPicker.NoneLabel, options[0].Label);
        Assert.Null(options[0].Entry);

        picker.Select(options[0]);
        Assert.True(picker.Selected.IsNone);
        Assert.Null(picker.Persist());
    }

    /// <summary>⭐ Only producers of the RIGHT type are offered — that is what "can supply this" means.</summary>
    [Fact]
    public void OnlyProducersOfTheTargetTypeAreOffered()
    {
        var picker = new ProducerPicker(Catalog());

        var forFloat = picker.OptionsFor(DeclarationKind.Variable, Float).Skip(1).ToList();
        var forInt   = picker.OptionsFor(DeclarationKind.Variable, Int).Skip(1).ToList();

        Assert.All(forFloat, o => Assert.Equal(Float, o.Entry!.Outputs.Single().TypeId));
        Assert.All(forInt,   o => Assert.Equal(Int,   o.Entry!.Outputs.Single().TypeId));
        Assert.NotEmpty(forInt);
        Assert.DoesNotContain(forInt, o => forFloat.Any(f => f.Entry!.Fqn == o.Entry!.Fqn));
    }

    // ── identity + persistence ──────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>What is persisted is the FQN.</b> ⛔ Not the AssetId (architect <c>AQ2</c>) — and the
    /// assertion is on the STORED STRING, because an AssetId would round-trip too.
    /// </summary>
    [Fact]
    public void WhatIsPersistedIsTheGeneratedFqn_NotTheAssetId()
    {
        var picker = new ProducerPicker(Catalog());
        var entry  = picker.OptionsFor(DeclarationKind.Parameter, Float)
                           .Single(o => o.Entry?.Source == ProducerSource.LibraryFunction).Entry!;

        picker.Select(ProducerSelection.Of(entry));
        string stored = picker.Persist()!;

        Assert.Equal("Hrot.AI.Behaviors.Generated.AimResolvers_0ABCDEF1_Bp.ComputeSpeed", stored);
        Assert.DoesNotContain(ResolverAssetId.ToString(), stored, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐⭐ <b>The FQN is the EMITTER's class-name formula.</b> ⚠ The catalog rebuilds it from a
    /// <c>BlueprintSignature</c> while <c>LibraryEmitter</c> builds it from an <c>IrAsset</c> — two
    /// assemblies, one string. This pins them: both take <c>SanitizedName</c> and <c>BlueprintId</c>
    /// from the same parse, so the assertion is computed from the signature, never pasted.
    /// </summary>
    [Fact]
    public void TheLibraryFqn_MatchesTheEmittersClassNameFormula()
    {
        var sig = LibrarySig("AimResolvers", 0x0ABCDEF1, Fn("ComputeSpeed", Float));

        string emitterClassName = $"{sig.SanitizedName}_{sig.BlueprintId:X8}_Bp";
        Assert.Equal($"Hrot.AI.Behaviors.Generated.{emitterClassName}.ComputeSpeed",
                     ProducerCatalog.LibraryFunctionFqn(sig, "ComputeSpeed"));
    }

    /// <summary>⭐ <b>None → a producer → None</b>, round-tripping through the stored form each time.</summary>
    [Fact]
    public void TheSelectionRoundTrips_NoneToAProducerToNone()
    {
        var catalog = Catalog();
        var picker  = new ProducerPicker(catalog);

        Assert.True(picker.Selected.IsNone);
        Assert.Null(picker.Persist());

        var entry = catalog.GetProducersReturning(Float).First();
        picker.Select(ProducerSelection.Of(entry));
        string? stored = picker.Persist();

        var reloaded = new ProducerPicker(catalog);
        reloaded.Restore(stored);
        Assert.Equal(entry.Fqn, reloaded.Selected.Fqn);
        Assert.Same(entry, reloaded.SelectedEntry);

        reloaded.Select(ProducerSelection.None);
        Assert.Null(reloaded.Persist());

        var reloadedAgain = new ProducerPicker(catalog);
        reloadedAgain.Restore(reloaded.Persist());
        Assert.True(reloadedAgain.Selected.IsNone);
    }

    /// <summary>
    /// ⚠⚠ <b>A stored FQN whose producer vanished is KEPT and reported unresolvable</b> — ⛔ not
    /// silently reset to None. Resetting would turn a broken reference into a plausible-looking
    /// deliberate choice, which is the quiet-wrong-value shape this programme keeps filing.
    /// </summary>
    [Fact]
    public void ADanglingSelection_IsReportedRatherThanSilentlyCleared()
    {
        var picker = new ProducerPicker(Catalog());
        picker.Restore("Hrot.AI.Behaviors.Generated.Deleted_00000000_Bp.Gone");

        Assert.False(picker.Selected.IsNone);
        Assert.False(picker.IsResolvable);
        Assert.Null(picker.SelectedEntry);
        // ⭐ And it still persists what it was given -- reopening the asset does not lose the record.
        Assert.Equal("Hrot.AI.Behaviors.Generated.Deleted_00000000_Bp.Gone", picker.Persist());
    }

    /// <summary>⭐ A None selection is trivially resolvable — "no producer" is never broken.</summary>
    [Fact]
    public void NoneIsAlwaysResolvable()
        => Assert.True(new ProducerPicker(Catalog()).IsResolvable);

    // ── the catalog's own contract ──────────────────────────────────────────

    /// <summary>
    /// ⛔ <b>A non-Library asset contributes nothing.</b> An Instance or AiPrimitive is a behaviour,
    /// not a named pure producer; offering its graphs would be a different concept wearing this one's
    /// picker.
    /// </summary>
    [Fact]
    public void OnlyLibraryDispatchAssetsContributeFunctions()
    {
        var instanceSig = LibrarySig("NotALibrary", 0x0BAD0001, Fn("Tick", Float))
            with { Dispatch = BlueprintDispatchKind.Instance };

        var catalog = new ProducerCatalog(() => new[] { instanceSig }, () => Array.Empty<MethodInfo>());

        Assert.Empty(catalog.GetProducers());
    }

    /// <summary>
    /// ⛔⛔ <b>Both contributors are required.</b> A host with no CLR producers passes an explicit
    /// empty supplier. 📌 The repo's silent-default rule at construction: an optional supplier makes
    /// "this source contributes nothing" and "I forgot to wire it" the same observation.
    /// </summary>
    [Fact]
    public void BothContributorsAreRequired_NoSilentDefault()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ProducerCatalog(null!, () => Array.Empty<MethodInfo>()));
        Assert.Throws<ArgumentNullException>(
            () => new ProducerCatalog(() => Array.Empty<BlueprintSignature>(), null!));

        var ctor = typeof(ProducerCatalog).GetConstructors().Single();
        Assert.All(ctor.GetParameters(), p => Assert.False(p.IsOptional));
    }

    /// <summary>⭐ The offer order is deterministic, so the drop-down does not reshuffle on rebuild.</summary>
    [Fact]
    public void TheOfferOrderIsDeterministic()
    {
        var catalog = Catalog();
        var first   = catalog.GetProducers().Select(p => p.Fqn).ToList();
        catalog.Rebuild();

        Assert.Equal(first, catalog.GetProducers().Select(p => p.Fqn));
        Assert.Equal(first.OrderBy(f => f, StringComparer.Ordinal), first);
    }
}
