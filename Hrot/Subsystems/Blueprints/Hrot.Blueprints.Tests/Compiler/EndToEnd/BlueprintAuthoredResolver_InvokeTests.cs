using System;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Xunit;

using MoveToParams = Hrot.AI.Behaviors.Brains.CgfNodes.MoveToLocationParams;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b><c>Q43</c> + <c>R4</c> end-to-end — a parameter resolver AUTHORED AS A BLUEPRINT runs, and
/// it runs in the ONE currency every supply path speaks.</b>
/// 📄 <c>Architect_Question_43</c> · <c>DESIGN_Resolver_World_Reach.md</c> §4, §7.1.
///
/// <para>
/// ⭐⭐ <b>It drives the REAL corpus asset</b>, not a hand-built fixture: <c>ParamResolverDemo.bp.json</c>
/// is compiled by the production source generator and covered by the golden sweep, so this rail and the
/// goldens cannot drift apart. ⛔ A builder-made asset would have tested the emitter against a shape no
/// asset on disk has — the blind spot that let <c>BP-297</c> ship.
/// </para>
/// </summary>
public sealed class BlueprintAuthoredResolver_InvokeTests : IDisposable
{
    private readonly BlueprintTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private BlueprintDefinition LoadDemo()
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        _fixture.CompileAndLoad(GoldenCorpus.Load("ParamResolverDemo"));
        Assert.True(_fixture.Registry.TryGetByName("ParamResolverDemo", out var def),
            "The resolver Library blueprint should be registered.");
        return def!;
    }

    [Fact]
    public void AConstructionGraph_IsRegisteredAsAResolver_AndRefinesTheDtoItIsGiven()
    {
        var def = LoadDemo();
        Assert.Equal(BlueprintDispatchKind.Library, def.Kind);

        // ⭐⭐ Q43-A2′ — a Construction graph lands in `Resolvers`, NOT in `Functions`. The separation is
        //    the whole point: a binding site must pick a resolver without a naming convention.
        Assert.True(def.Resolvers.ContainsKey("ResolveMoveTo"),
            "The Construction graph must be published in BlueprintDefinition.Resolvers.");
        Assert.False(def.Functions.ContainsKey("ResolveMoveTo"),
            "A Construction graph must NOT also appear in Functions — the kind is what marks a resolver.");

        var entry = def.Resolvers["ResolveMoveTo"];

        // ⭐ R4 §7.1 — the declared type is carried BESIDE the delegate so a picker can type-filter
        //   without invoking or reflecting over it.
        Assert.Equal(typeof(MoveToParams), entry.DtoType);

        // ── invoke it through the universal currency ──────────────────────────
        // ⭐⭐⭐ THIS is the shape that matters: `ResolveParams<TDto>` is what BehaviorParams.FromJson
        //   composes for the four JSON-fed paths and what HostedParamResolvers stores directly for the
        //   fifth, which has no JSON at all. ⛔ No span round-trip, and `host` survives.
        var resolve = entry.As<MoveToParams>();

        var dto = new MoveToParams { X = 12.5f, Y = -3.25f, Speed = 36f, ArrivalRadius = 4f };
        var entity = _fixture.CreateEntity();

        resolve(ref dto, _fixture.World, entity, host: null);

        // ⭐ Q43-D — the resolver REFINES: the one member it rewires changes, the rest are PRESERVED
        //   because they were left unwired on the SetMembers node. ⛔ A resolver that merely PRODUCED a
        //   value would zero X/Y/ArrivalRadius here, which is the BP-275 defect in a new place.
        Assert.Equal(36f * 0.2777778f, dto.Speed, 4);
        Assert.Equal(12.5f,  dto.X);
        Assert.Equal(-3.25f, dto.Y);
        Assert.Equal(4f,     dto.ArrivalRadius);
    }

    /// <summary>
    /// ⭐⭐ <b><c>A5</c> — a mismatched <c>TDto</c> THROWS rather than reinterpreting bytes.</b>
    /// ⛔ It can only come from a binding that named this resolver for a params region of another
    /// shape; running it anyway is silent corruption, which is what <c>HostedParamResolvers.TryRun</c>
    /// already refuses for the same reason.
    /// </summary>
    [Fact]
    public void AskingAResolverForTheWrongDtoType_Throws()
    {
        var entry = LoadDemo().Resolvers["ResolveMoveTo"];

        var ex = Assert.Throws<InvalidOperationException>(() => entry.As<AnotherParams>());
        Assert.Contains("MoveToLocationParams", ex.Message);
        Assert.Contains("AnotherParams", ex.Message);
    }

    private struct AnotherParams { public int Whatever; }
}
