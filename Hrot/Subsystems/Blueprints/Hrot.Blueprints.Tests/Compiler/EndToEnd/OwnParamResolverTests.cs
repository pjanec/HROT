using System;
using System.Linq;
using Fdp.Toolkit.Behavior;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b><c>E8a</c> — an asset carries its OWN parameter resolver, so NOTHING has to name it.</b>
/// 📄 <c>DESIGN_Resolver_World_Reach.md</c> §7.2 · <c>R-149</c>.
///
/// <para>
/// ⭐⭐ <b>The measurement that makes this slice exist:</b> <c>HostedParamResolvers.Register</c> is keyed
/// by the hosted blueprint's OWN asset id, and <c>AiPrimitiveEmitter</c> already emits
/// <c>TryRun(AssetId, …)</c> in the thunk. ⇒ when the resolver is the asset's own <c>Construction</c>
/// graph, producer and consumer key on one value the asset already carries and there is <b>no binding
/// step at all</b> — which is why `E8a` needs no selection property.
/// </para>
/// </summary>
public sealed class OwnParamResolverTests
{
    // ── the emitted shape ────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ The own-asset resolver is emitted as <c>ResolveParams&lt;Params&gt;</c> and registered under
    /// <c>AssetId</c> — the SAME key the thunk's <c>TryRun</c> already uses.
    /// </summary>
    [Fact]
    public void AnOwnResolver_IsEmittedAndRegisteredUnderTheAssetsOwnId()
    {
        var src = Emit(GoldenCorpus.Load("OwnParamResolverDemo"));

        Assert.Contains("ResolveOwnParams(", src);
        Assert.Contains("ref Params p,", src);
        Assert.Contains("global::Fdp.Toolkit.Behavior.IHostVariableAccess host)", src);

        // ⭐ reads its own parameter and writes the refined value back INTO the params region —
        //   that write is the resolver's output, which is why V_ResolverPurity permits it here.
        Assert.Contains("var __t0 = p.SpeedKph;", src);
        Assert.Contains("p.SpeedMps = ", src);

        // ⭐⭐⭐ producer and consumer on ONE key ⇒ no binding.
        Assert.Contains("HostedParamResolvers.Register<", src);
        Assert.Contains(".AssetId,", src);
        Assert.Contains("HostedParamResolvers.TryRun(", src);
    }

    /// <summary>
    /// ⭐ <b>Still gated.</b> An asset with neither hosting nor a resolver emits no <c>AssetId</c> and
    /// no registration — which is what keeps the other 44 goldens byte-identical.
    /// </summary>
    [Fact]
    public void AnAssetWithoutAResolver_EmitsNoRegistration()
    {
        var src = Emit(GoldenCorpus.Load("ParamDemo"));

        Assert.DoesNotContain("HostedParamResolvers.Register<", src);
    }

    // ── BP1676 — there must be something to resolve ──────────────────────────

    /// <summary>
    /// ⭐⭐ <b><c>BP1676</c>, REVISED.</b> It used to mean <i>"Construction only on a Library asset"</i>.
    /// ⭐ Its real job survives: refuse a resolver nothing will ever call — which on a params-owning
    /// asset means one that has no parameters to refine.
    /// </summary>
    [Fact]
    [CoversDiagnosticCode("BP1676")]
    public void AnOwnResolver_OnAnAssetWithNoParameters_EmitsBP1676()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("NoParams")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .WithGraph("Resolve", GraphKind.Construction, g => g.Entry().Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1676);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>R-149</c> made structural:</b> an asset's parameters are ONE region, and a region
    /// names exactly one resolver ⇒ two <c>Construction</c> graphs on it cannot be authored.
    /// ⛔ This is the competition the selection model exists to make unrepresentable.
    /// </summary>
    [Fact]
    public void TwoOwnResolversOnOneAsset_EmitBP1676()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("TwoResolvers")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithParameter("Speed", typeof(float))
            .WithGraph("Main", g => g.Entry().Return())
            .WithGraph("ResolveA", GraphKind.Construction, g => g.Entry().Return())
            .WithGraph("ResolveB", GraphKind.Construction, g => g.Entry().Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1676);
    }

    /// <summary>
    /// ⭐ A LIBRARY may carry many — they are separately-named reusable resolvers, each bound by
    /// whoever names it. ⛔ Collapsing that to "one per asset" would forbid a resolver library.
    /// </summary>
    [Fact]
    public void TwoResolversOnALibraryAsset_AreLegal()
    {
        const string Dto = "global::Hrot.AI.Behaviors.Brains.PlatoonHillAttackParams";
        var asset = BlueprintAssetBuilder
            .Library("ResolverLib")
            .WithGraph("A", GraphKind.Construction, g =>
                { g.WithInput("D", Dto).WithOutput("R", Dto); g.Entry().Return(); })
            .WithGraph("B", GraphKind.Construction, g =>
                { g.WithInput("D", Dto).WithOutput("R", Dto); g.Entry().Return(); })
            .Build();

        Assert.DoesNotContain(Validate(asset), d => d.Code == DiagnosticCodes.BP1676);
    }

    // ── BP1677 — the two signatures ──────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>An own-asset resolver declares NOTHING</b>, because its subject is this asset's
    /// GENERATED <c>Params</c> struct — a type no authored <c>TypeId</c> could name without baking the
    /// emitted class name (which embeds the BlueprintId hash) into the asset.
    /// </summary>
    [Fact]
    public void AnOwnResolver_ThatDeclaresASignature_EmitsBP1677()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("DeclaredSig")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithParameter("Speed", typeof(float))
            .WithGraph("Main", g => g.Entry().Return())
            .WithGraph("Resolve", GraphKind.Construction, g =>
                { g.WithInput("Nope", "System.Single"); g.Entry().Return(); })
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1677);
    }

    // ── BP1675 — the ONE purity exemption ────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The exemption is narrow, and the negative half is what proves it.</b> A
    /// <c>SetVariable</c> targeting a PARAMETER is the resolver's output; one targeting STATE is still
    /// a side effect that would outlive a failed parse, and is still refused.
    /// </summary>
    [Fact]
    public void AnOwnResolver_MayWriteAParameter_ButNotState()
    {
        // ✅ the real corpus asset writes SpeedMps, a PARAMETER — and compiles.
        Assert.DoesNotContain(Validate(GoldenCorpus.Load("OwnParamResolverDemo")),
            d => d.Code == DiagnosticCodes.BP1675);

        // ⛔ the same node shape, targeting a VARIABLE, is refused.
        var asset = BlueprintAssetBuilder
            .AiPrimitive("WritesState")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithParameter("Speed", typeof(float))
            .WithVariable("Ticks", typeof(int))
            .WithGraph("Main", g => g.Entry().Return())
            .WithGraph("Resolve", GraphKind.Construction, g =>
                g.Entry().SetVariable("Ticks", "1").Return())
            .Build();

        Assert.Contains(Validate(asset), d => d.Code == DiagnosticCodes.BP1675);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string Emit(BlueprintAsset asset)
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded,
            "compile failed: " + string.Join(", ", result.Diagnostics.Where(d => d.IsError)
                                                         .Select(d => d.Code + ": " + d.Message)));
        return result.GeneratedSource!;
    }

    private static IReadOnlyList<Diagnostic> Validate(BlueprintAsset asset)
    {
        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, GoldenCorpus.Options()));
        return sink.All;
    }
}

/// <summary>
/// ⭐⭐⭐ <b><c>R-149</c> — two explicit bindings for one params region THROW, never race.</b>
/// 📄 <c>DESIGN_Resolver_World_Reach.md</c> §7.2.
/// </summary>
public sealed unsafe class ResolverDuplicateBindingTests
{
    /// <summary>
    /// 🔒 <c>R-132</c>'s own sentence applied to its successor: <i>"curated wins BY DECLARATION, not by
    /// arriving first."</i> ⇒ two CURATED bindings have no tie-break, and the silent last-writer-wins
    /// this replaced would have picked one by source order.
    /// </summary>
    [Fact]
    public void TwoResolversForOneBehaviourInOneScan_Throw()
    {
        var registry = new BehaviorRegistry();
        registry.RegisterResolver("Behaviour", Noop);

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.RegisterResolver("Behaviour", Noop));

        Assert.Contains("Behaviour", ex.Message);
        Assert.Contains("one resolver", ex.Message);
    }

    /// <summary>
    /// ⭐⭐ <b>The scope is ONE registry instance, and that is load-bearing.</b> 📐 Every scan builds a
    /// FRESH staging registry and the live registry is written by <c>MergeFrom</c> — a separate
    /// overwrite path. ⇒ re-registration across a hot reload never reaches the guard.
    /// ⛔ A throw without that distinction would have broken reload, which is why this rail exists
    /// beside the one above rather than instead of it.
    /// </summary>
    [Fact]
    public void MergeFrom_StillOverwrites_SoAHotReloadIsUnaffected()
    {
        var live = new BehaviorRegistry();
        live.RegisterResolver("Behaviour", Noop);

        var rescan = new BehaviorRegistry();
        rescan.RegisterResolver("Behaviour", Noop);   // a fresh scan: legal, it is a new registry

        var ex = Record.Exception(() => live.MergeFrom(rescan));

        Assert.Null(ex);
    }

    private static unsafe void Noop(
        string json, byte* memory, int capacity, Fdp.Core.EntityRepository world, Fdp.Core.Entity self,
        IHostVariableAccess? host)
    { }
}
