using System;
using System.Linq;
using Fdp.Toolkit.Replication.Services;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Xunit;

using HillAttackParams = Hrot.AI.Behaviors.Brains.PlatoonHillAttackParams;

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
/// </summary>
public sealed class ResolverWorldReachTests : IDisposable
{
    private const string DtoId = "global::Hrot.AI.Behaviors.Brains.PlatoonHillAttackParams";

    private readonly BlueprintTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    // ── A1 — the Function / Construction split ───────────────────────────────

    /// <summary>
    /// ⭐⭐ <b><c>A1</c> — the context is appended to a <c>Construction</c> graph and NOT to a
    /// <c>Function</c> graph on the same asset.</b>
    ///
    /// <para>
    /// ⛔ That asymmetry is the whole safety argument for <c>R4</c>: a Library asset's ordinary helper
    /// functions keep their exact pre-<c>R4</c> signature, which is why every golden except the two
    /// resolver assets stayed byte-identical. ⚠ Asserted on ONE asset carrying both kinds, so the two
    /// arms cannot drift apart unnoticed.
    /// </para>
    /// </summary>
    [Fact]
    public void OnlyAConstructionGraph_GetsTheWorldContext()
    {
        var src = Emit(BlueprintAssetBuilder
            .Library("MixedLib")
            .WithGraph("Helper", GraphKind.Function, g =>
            {
                g.WithInput("A", "System.Int32");
                g.Entry().Return();
            })
            .WithGraph("Resolve", GraphKind.Construction, g =>
            {
                g.WithInput("Dto", DtoId).WithOutput("Result", DtoId);
                g.Entry().Return();
            })
            .Build());

        const string ResolverSignature =
            "Resolve(global::Hrot.AI.Behaviors.Brains.PlatoonHillAttackParams Dto, "
            + "global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self, "
            + "global::Fdp.Toolkit.Behavior.IHostVariableAccess host)";

        Assert.Contains(ResolverSignature, src);

        // ⛔ The Function graph must be untouched — its parameters are its inputs and nothing else.
        Assert.Contains("Helper(int A)", src);
        Assert.DoesNotContain("Helper(int A, global::Fdp.Core.EntityRepository", src);
    }

    // ── A2 / A3 — self and the view reach the CLR escape hatch ───────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>A2</c> + <c>A3</c> — a CLR call inside a resolver receives <c>self</c> AND the view.</b>
    ///
    /// <para>
    /// 🔴 <b>RED BEFORE <c>R4</c>, and the red-proof is one line.</b> The gate was
    /// <c>Stage5_Schedule.ResolveFunctionCallTrailingContext</c>:
    /// <c>if (Dispatch == Library) return (false, false)</c> — a baked <c>TrailingContext</c> was
    /// DISCARDED for a Library asset, so the arguments were never appended and the emitted call had
    /// the wrong arity. ⛔ Restoring that line reddens this test.
    /// </para>
    ///
    /// <para>
    /// ⭐ <c>HasTarget(uint, Entity self, ISimulationView view)</c> is chosen deliberately: it takes
    /// BOTH, so one node covers both acceptance rows, and it is a real shipped helper rather than a
    /// fixture invented for the rail.
    /// </para>
    /// </summary>
    [Fact]
    public void ACallInsideAResolver_ReceivesSelfAndTheView()
    {
        var src = Emit(BlueprintAssetBuilder
            .Library("HatchLib")
            .WithGraph("Resolve", GraphKind.Construction, g =>
            {
                // ⚠ IN and OUT are the SAME type — BP1677 refuses anything else, and it refused an
                //   earlier draft of this very rail. ⭐ That is the validator doing its job: a resolver
                //   REFINES, so its shape is fixed, and a test may not quietly author a shape the
                //   product forbids.
                g.WithInput("Found", "System.Boolean").WithOutput("Found", "System.Boolean");
                g.Entry()
                 .PureCallReturning(
                     "Hrot.AI.Behaviors.Brains.HillAssault2TankOps", "HasTarget", "System.Boolean",
                     FunctionCallContextKind.SelfAndView, ("targetNetworkId", "System.UInt32"))
                 .Return();
            })
            .Build());

        Assert.Contains("HillAssault2TankOps.HasTarget(", src);
        // ⭐ `self` then the view, in that order — AppendContextArgs' documented contract.
        Assert.Contains(", self, world)", src);
    }

    // ── A4 — the world singleton, end to end, on the REAL corpus asset ───────

    /// <summary>
    /// ⭐⭐⭐ <b><c>A4</c> — the motivating case, run for real.</b> The corpus asset
    /// <c>ResolverWorldReachDemo</c> compiles, loads, and its resolver reads the
    /// <see cref="NetworkEntityMap"/> <b>world singleton</b> through the CLR hatch to turn a network id
    /// into an <c>Entity</c>, which lands in <c>PlatoonHillAttackParams.TargetAreaEntity</c>.
    ///
    /// <para>
    /// ⚠ <b>That DTO is not a random choice</b> — it is the one <c>R-132</c>'s silent failure was about
    /// (the platoon drove to <c>(0,0)</c> because the resolver that knew its shape never ran).
    /// </para>
    /// </summary>
    [Fact]
    public void AResolver_ReadsAWorldSingleton_AndRefinesTheDtoFromIt()
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        _fixture.CompileAndLoad(GoldenCorpus.Load("ResolverWorldReachDemo"));

        // ⛔ Before R4 the emitted call could not have carried a view at all, so this mapping was
        //    simply unreachable from a blueprint.
        var target = _fixture.CreateEntity();
        var map = new NetworkEntityMap();
        map.Register(4242, target);
        _fixture.World.SetSingletonManaged(map);

        Assert.True(_fixture.Registry.TryGetByName("ResolverWorldReachDemo", out var def));
        var resolve = def!.Resolvers["ResolveHillAttack"].As<HillAttackParams>();

        var dto = new HillAttackParams { StartX = 11f, TankSpacing = 7f };
        resolve(ref dto, _fixture.World, _fixture.CreateEntity(), host: null);

        Assert.Equal(target, dto.TargetAreaEntity);
        // ⭐ every unwired member is PRESERVED — the resolver REFINES (Q43-D).
        Assert.Equal(11f, dto.StartX);
        Assert.Equal(7f,  dto.TankSpacing);
    }

    /// <summary>
    /// ⚠ <b>The honest other half of <c>A4</c>:</b> with no singleton registered the helper returns
    /// <c>Entity.Null</c> rather than throwing, so a resolver run on a world that has not published the
    /// map degrades to "unresolved" instead of failing the whole behaviour switch.
    /// ⭐ Pinned because that is a property of <c>NetworkEntityMapOps</c>, not of this asset, and a
    /// change there would silently alter what every blueprint resolver does at ingress.
    /// </summary>
    [Fact]
    public void WithNoSingletonRegistered_TheResolverYieldsNullRatherThanThrowing()
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        _fixture.CompileAndLoad(GoldenCorpus.Load("ResolverWorldReachDemo"));

        Assert.True(_fixture.Registry.TryGetByName("ResolverWorldReachDemo", out var def));
        var resolve = def!.Resolvers["ResolveHillAttack"].As<HillAttackParams>();

        var dto = new HillAttackParams { StartX = 3f };
        var ex = Record.Exception(() => resolve(ref dto, _fixture.World, _fixture.CreateEntity(), null));

        Assert.Null(ex);
        Assert.Equal(Fdp.Core.Entity.Null, dto.TargetAreaEntity);
        Assert.Equal(3f, dto.StartX);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string Emit(BlueprintAsset asset)
    {
        var result = new BlueprintCompiler().Compile(asset, GoldenCorpus.Options());
        Assert.True(result.Succeeded,
            "compile failed: " + string.Join(", ", result.Diagnostics.Where(d => d.IsError)
                                                         .Select(d => d.Code + ": " + d.Message)));
        return result.GeneratedSource!;
    }
}
