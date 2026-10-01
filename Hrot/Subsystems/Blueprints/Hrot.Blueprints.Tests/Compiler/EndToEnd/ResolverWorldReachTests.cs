using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Xunit;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b><c>R4</c> — a resolver graph REACHES THE WORLD.</b>
/// 📄 <c>DESIGN_Resolver_World_Reach.md</c> §4, §9 acceptance <c>A1</c>–<c>A6</c>.
///
/// <para>
/// 🔴 <b>What was impossible before.</b> <c>EmissionContext.HasSelfInScope</c> was
/// <c>Dispatch != Library</c>, so a <c>Construction</c> graph compiled to a static method whose ONLY
/// parameters were the graph's inputs — no <c>self</c>, no world, no singletons. ⇒ the case the whole
/// resolver feature exists for (geo-authored params needing <c>IGeographicTransform</c>, a network id
/// needing <c>NetworkEntityMap</c>) could not be authored at all.
/// </para>
/// <para>
/// ⛔ <b>CE-448 (2026-09-30):</b> the reusable Library resolver these rails were first written against is
/// RETIRED, with its corpus assets <c>ParamResolverDemo</c>/<c>ResolverWorldReachDemo</c> and the
/// <c>BlueprintDefinition.Resolvers</c> index. ⭐ The claims are re-homed onto the ONE resolver kind left — a
/// behaviour resolver asset (a Library with a <c>ResolverSubject</c>, <c>DESIGN_Parameter_Model.md</c> §P.7).
/// ⛔ <c>A4</c> (a runtime singleton read through the index) is deleted, not re-homed: the behaviour path's
/// runtime proof is <c>T40</c>'s (<c>CE443_*</c>), and <c>NetworkEntityMapOps</c>' null-not-throw — a property of that helper,
/// not of any asset — is re-pinned directly below.
/// </para>
/// </summary>
public sealed class ResolverWorldReachTests
{
    private const string BlockTypeId = "Demo.SomeBehaviour_Block";

    // ── A1 — the Function / Construction split ───────────────────────────────

    /// <summary>
    /// ⭐⭐ <b><c>A1</c> — the context is appended to the resolver (<c>Construction</c>) graph and NOT to a
    /// <c>Function</c> graph on the same asset.</b>
    /// <para>⚠ Asserted on ONE asset carrying both kinds, so the two arms cannot drift apart unnoticed.</para>
    /// </summary>
    [Fact]
    public void OnlyAConstructionGraph_GetsTheWorldContext()
    {
        var src = Emit(Subject(b => b
            .WithGraph("Helper", GraphKind.Function, g =>
            {
                g.WithInput("A", "System.Int32");
                g.Entry().Return();
            })
            .WithGraph("Resolve", GraphKind.Construction, g => g.Entry().Return())));

        // ⭐ CE-443/CE-445: injected subjects, then world and self — no host.
        Assert.Contains("Resolve(in Params authored, ref global::" + BlockTypeId + " block, "
            + "global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self)", src);

        // ⛔ The Function graph must be untouched — its parameters are its inputs and nothing else.
        Assert.Contains("Helper(int A)", src);
        Assert.DoesNotContain("Helper(int A, global::Fdp.Core.EntityRepository", src);
    }

    // ── A2 / A3 — self and the view reach the CLR escape hatch ───────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>A2</c> + <c>A3</c> — a CLR call inside a resolver receives <c>self</c> AND the view.</b>
    /// <para>
    /// ⭐ <c>BlueprintWorldLibrary.RandomInt(int, int, int, Entity self, ISimulationView view)</c> takes BOTH, so one
    /// node covers both rows, and it is a real shipped built-in. Its result lands in a block Variable — a resolver
    /// declares no outputs (BP1677). (Was the hill-attack <c>HillAssault2TankOps.HasTarget</c>, retired with the
    /// <c>HillAssault2_*</c> twins, <c>2026-10-01</c>.)
    /// </para>
    /// </summary>
    [Fact]
    public void ACallInsideAResolver_ReceivesSelfAndTheView()
    {
        var src = Emit(Subject(b => b
            .WithGraph("Resolve", GraphKind.Construction, g => g
                .Entry()
                .PureCallReturning(
                    "Hrot.AI.Behaviors.StandardLibrary.BlueprintWorldLibrary", "RandomInt", "System.Int32",
                    FunctionCallContextKind.SelfAndView,
                    ("minInclusive", "System.Int32"), ("maxExclusive", "System.Int32"), ("salt", "System.Int32"))
                .SetVariable("Roll", "")
                .Return())));

        Assert.Contains("BlueprintWorldLibrary.RandomInt(", src);
        // ⭐ `self` then the view, in that order — AppendContextArgs' documented contract.
        Assert.Contains(", self, world)", src);
    }

    // ── the helper a resolver reaches the world through ──────────────────────

    /// <summary>
    /// ⚠ <b>With no <c>NetworkEntityMap</c> singleton the helper returns <c>Entity.Null</c> rather than throwing</b>, so
    /// a resolver run on a world that has not published the map degrades to "unresolved" instead of failing the
    /// behaviour switch. ⭐ Pinned on the helper itself (it was pinned through the retired <c>ResolverWorldReachDemo</c>).
    /// </summary>
    [Fact]
    public void WithNoSingletonRegistered_TheWorldReachHelperYieldsNullRatherThanThrowing()
    {
        using var world = new global::Fdp.Core.EntityRepository();
        global::Fdp.Core.Entity result = default;

        var ex = Record.Exception(() => result = global::Hrot.AI.Behaviors.Brains.NetworkEntityMapOps.ResolveTarget(4242, world));

        Assert.Null(ex);
        Assert.Equal(global::Fdp.Core.Entity.Null, result);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>A behaviour resolver asset: a Library with a <c>ResolverSubject</c> and one block Variable.</summary>
    private static BlueprintAsset Subject(Func<BlueprintAssetBuilder, BlueprintAssetBuilder> graphs)
    {
        var asset = graphs(BlueprintAssetBuilder.Library("WorldReachResolver").WithVariable("Found", typeof(bool)).WithVariable("Roll", typeof(int)))
            .Build();
        asset.ResolverSubject = new ResolverSubjectDecl { BehaviorName = "SomeBehaviour", BlockTypeId = BlockTypeId };
        return asset;
    }

    private static string Emit(BlueprintAsset asset)
    {
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded,
            "compile failed: " + string.Join(", ", result.Diagnostics.Where(d => d.IsError)
                                                         .Select(d => d.Code + ": " + d.Message)));
        return result.GeneratedSource!;
    }
}
