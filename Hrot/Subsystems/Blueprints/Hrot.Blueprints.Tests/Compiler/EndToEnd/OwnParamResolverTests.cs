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
        Assert.Contains("ref WorkingState ws,", src);   // ⭐ CE-432: the whole block, state included
        Assert.Contains("global::Fdp.Toolkit.Behavior.IHostVariableAccess host)", src);

        // ⭐ reads its own parameter and writes the refined value back INTO the params region —
        //   that write is the resolver's output, which is why V_ResolverPurity permits it here.
        Assert.Contains("var __t0 = p.SpeedKph;", src);
        Assert.Contains("p.SpeedMps = ", src);

        // ⭐⭐⭐ producer and consumer on ONE key ⇒ no binding.
        Assert.Contains("HostedParamResolvers.Register<", src);
        Assert.Contains(".WorkingState>(", src);        // ⭐ CE-432: registered as ResolveOccurrence<Params, WorkingState>
        Assert.Contains(".AssetId,", src);
        Assert.Contains("HostedParamResolvers.TryRun(", src);
        Assert.Contains("ref *__params, ref ws,", src); // ⭐ CE-426: the seam resolves the whole block
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-426</c> — <b>BAKE → SUPPLY → RESOLVE, in that order, at the occurrence's activation.</b>
    /// 📄 <c>Q76</c> §12.3. The working state's defaults are baked BEFORE the resolver runs, so it
    /// MODIFIES a pre-seeded block (§12.9c).
    ///
    /// <para>⚠ Inverse-edit red-proof: move <c>InitDefaultWorkingState</c> back after
    /// <c>EmitHostedResolve</c> in <c>AiPrimitiveEmitter.EmitParamSeed</c> — the order it had until
    /// <c>CE-426</c> — and the index assertion fails. ⛔ In that order every state value a resolver
    /// wrote was wiped one line later.</para>
    /// </summary>
    [Fact]
    public void TheSeedBakesTheStateBeforeTheResolverRuns()
    {
        var src = Emit(GoldenCorpus.Load("OwnParamResolverDemo"));

        int supply  = src.IndexOf("*__params = global::System.Runtime.CompilerServices.Unsafe.As<byte, Params>(", StringComparison.Ordinal);
        int bake    = src.IndexOf("InitDefaultWorkingState((WorkingState*)", StringComparison.Ordinal);
        int resolve = src.IndexOf("HostedParamResolvers.TryRun(", StringComparison.Ordinal);

        Assert.True(supply >= 0 && bake >= 0 && resolve >= 0, "the seed must emit all three stages");
        Assert.True(supply < resolve && bake < resolve,
            $"the resolver must run AFTER the supply and the state bake (supply {supply}, bake {bake}, resolve {resolve})");
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
    /// ⭐⭐⭐ <c>CE-432</c> — <b>an own resolver may write its WHOLE block — and nothing outside it.</b>
    ///
    /// <para>⛔⛔ <b>This rail FLIPPED, deliberately.</b> It was <c>AnOwnResolver_MayWriteAParameter_ButNotState</c>:
    /// a state write was refused because state lived outside what the resolve owned and survived a
    /// failed parse. 🔒 <c>R-151</c> ③ — <i>"a custom resolver may write the whole block"</i> — and
    /// <c>CE-426</c> now runs the resolver on SHADOW copies of params AND state, committed only when it
    /// returns ⇒ the state write can no longer escape. ⚠ The two changes landed in ONE commit
    /// (<c>Q76</c> §12.12b: shipping the exemption first would re-introduce the corruption).</para>
    ///
    /// <para>⭐ <b>The negative half still proves the exemption is narrow:</b> a write to a name that is
    /// NOT one of the asset's declarations is refused, and so is a state write on a non-AiPrimitive
    /// asset — no shadowed resolve stands behind it.</para>
    /// </summary>
    [Fact]
    public void AnOwnResolver_MayWriteItsWholeBlock_ButNothingOutsideIt()
    {
        // ✅ the real corpus asset writes SpeedMps, a PARAMETER — and compiles.
        Assert.DoesNotContain(Validate(GoldenCorpus.Load("OwnParamResolverDemo")),
            d => d.Code == DiagnosticCodes.BP1675);

        // ✅ CE-432: the same node shape targeting a STATE variable is now legal, and it emits a write
        //    through the injected `ws`.
        var writesState = BlueprintAssetBuilder
            .AiPrimitive("WritesState")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithParameter("Speed", typeof(float))
            .WithVariable("Ticks", typeof(int))
            .WithGraph("Main", g => g.Entry().Return())
            .WithGraph("Resolve", GraphKind.Construction, g =>
                g.Entry().SetVariable("Ticks", "1").Return())
            .Build();
        Assert.DoesNotContain(Validate(writesState), d => d.Code == DiagnosticCodes.BP1675);
        // ⚠ The builder's SetVariable is UNWIRED (it ignores its value argument), so it validates but
        //   emits nothing — the EMISSION is proven on the corpus demo, whose resolver really writes Ticks.
        Assert.Contains("ws.Ticks = ", Emit(GoldenCorpus.Load("OwnParamResolverDemo")));

        // ⛔ a write to a name that is none of this asset's declarations — outside the block.
        var writesElsewhere = BlueprintAssetBuilder
            .AiPrimitive("WritesElsewhere")
            .WithIntent(AiPrimitiveIntent.Action)
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithParameter("Speed", typeof(float))
            .WithGraph("Main", g => g.Entry().Return())
            .WithGraph("Resolve", GraphKind.Construction, g =>
                g.Entry().SetVariable("NotDeclaredHere", "1").Return())
            .Build();
        Assert.Contains(Validate(writesElsewhere), d => d.Code == DiagnosticCodes.BP1675);

        // ⛔ a state write on an INSTANCE asset — it has no shadowed resolve behind it.
        var instanceWritesState = BlueprintAssetBuilder
            .Instance("InstanceWritesState")
            .WithParameter("Speed", typeof(float))
            .WithVariable("Ticks", typeof(int))
            .WithGraph("Tick", g => g.Entry().Return())
            .WithGraph("Resolve", GraphKind.Construction, g =>
                g.Entry().SetVariable("Ticks", "1").Return())
            .Build();
        Assert.Contains(Validate(instanceWritesState), d => d.Code == DiagnosticCodes.BP1675);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <c>CE-428</c> — shape ③: the shipped BEHAVIOUR resolver asset <c>T40Resolver</c> compiles to the
    /// injected signature and writes the behaviour's block through its two halves. 📄 <c>Q76</c> §12.20.
    /// <para>⚠ Read from the corpus FILE through <c>BlueprintJsonServices</c> — the generator's own read path —
    /// so a <c>ResolverSubject</c> lost in (de)serialisation reds here rather than as a build-time BP1011.</para>
    /// </summary>
    [Fact]
    public void CE428_ABehaviourResolverAsset_EmitsTheInjectedBlockSignature()
    {
        var asset = GoldenCorpus.Load("T40Resolver");
        Assert.NotNull(asset.ResolverSubject);
        Assert.Equal(new[] { "Doubled" }, asset.ResolverSubject!.StateVariables);

        var src = Emit(asset);
        Assert.Contains("public static void ResolveBehavior(in global::Hrot.AI.Behaviors.Trees.T40_BehaviorResolverAsset_Blackboard authored, ref global::Hrot.AI.Behaviors.Trees.T40_BehaviorResolverAsset_Block block,", src);
        Assert.Contains("block.In.Speed", src);          // a Variable NOT in StateVariables ⇒ the In half
        Assert.Contains("block.St.Doubled = ", src);     // a Variable in StateVariables ⇒ the St half
    }

    /// <summary>
    /// ⛔⛔ <c>CE-428</c> — <b>the compiler's working copy of the asset must carry EVERY public settable property.</b>
    /// <c>BlueprintCompiler.Compile</c> rebuilds the asset field by field; 📌 measured: the new
    /// <c>ResolverSubject</c> was not on that list, so a resolver asset compiled as a plain Library (BP1011) while
    /// every Stage-2-only rail stayed green. ⭐ Reflection, so the NEXT new property reds here too.
    /// </summary>
    [Fact]
    public void TheCompilersAssetCopy_CarriesEveryPublicProperty()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root, "IOS-IG-SimHost.sln")))
            root = System.IO.Path.GetDirectoryName(root)!;
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(root!,
            "Hrot", "Subsystems", "Blueprints", "Hrot.Blueprints.Compiler", "Compiler", "BlueprintCompiler.cs"));
        var copied = System.Text.RegularExpressions.Regex.Matches(src, @"^\s+(\w+)\s+=\s+asset\.", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet();
        var missing = typeof(BlueprintAsset).GetProperties()
            .Where(p => p.CanWrite && p.SetMethod!.IsPublic && p.Name != "Graphs")
            .Where(p => p.Name is not ("Parameters" or "WorkingState" or "Variables"))   // views over the store, copied via DeclarationStore
            .Select(p => p.Name).Where(n => !copied.Contains(n)).ToList();
        Assert.True(missing.Count == 0, "BlueprintCompiler's asset copy drops: " + string.Join(", ", missing));
    }

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
/// ⭐⭐⭐ <c>CE-432</c> — <b>the real own-resolver RUNS over its whole block through the widened seam.</b>
/// The emission rails prove the text; this compiles and loads <c>OwnParamResolverDemo</c>, lets its
/// generated registrar register the resolver, and runs it through
/// <c>HostedParamResolvers.TryRun&lt;Params, WorkingState&gt;</c> — the exact call the emitted seed makes.
/// </summary>
public sealed class OwnParamResolverRuntimeTests : IDisposable
{
    private readonly BlueprintTestFixture _fixture = new();

    public void Dispose()
    {
        HostedParamResolvers.ClearAll();
        _fixture.Dispose();
    }

    [Fact]
    public void TheDemoResolver_WritesItsParameterAndItsState_ThroughTheSeam()
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        var asm = _fixture.CompileAndLoad(GoldenCorpus.Load("OwnParamResolverDemo"));

        Type cls = asm.GetTypes().Single(t => t.GetNestedType("Params") != null
                                           && t.GetNestedType("WorkingState") != null
                                           && t.Name.StartsWith("OwnParamResolverDemo", StringComparison.Ordinal));
        Type pT = cls.GetNestedType("Params")!;
        Type sT = cls.GetNestedType("WorkingState")!;
        var assetId = (Guid)cls.GetField("AssetId")!.GetValue(null)!;

        object p = Activator.CreateInstance(pT)!;
        pT.GetField("SpeedKph")!.SetValue(p, 36f);
        object st = Activator.CreateInstance(sT)!;

        var tryRun = typeof(HostedParamResolvers).GetMethods()
            .Single(m => m.Name == nameof(HostedParamResolvers.TryRun) && m.GetGenericArguments().Length == 2)
            .MakeGenericMethod(pT, sT);
        object?[] args = { assetId, p, st, _fixture.World, _fixture.CreateEntity(), null };

        Assert.True((bool)tryRun.Invoke(null, args)!,
            "the generated registrar must register the demo's own resolver under its AssetId");

        // ⭐ the parameter it has always refined…
        Assert.Equal(36f * 0.2777778f, (float)pT.GetField("SpeedMps")!.GetValue(args[1])!, 4);
        // ⭐⭐ …and, since CE-432, its STATE — refused by V_ResolverPurity before this slice.
        Assert.Equal(1, (int)sT.GetField("Ticks")!.GetValue(args[2])!);
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
