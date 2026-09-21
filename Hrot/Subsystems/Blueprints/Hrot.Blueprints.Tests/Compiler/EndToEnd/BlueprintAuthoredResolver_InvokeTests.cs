using System;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Tests.Builders;
using Hrot.Blueprints.Tests.Golden;
using Xunit;

using MoveToParams = Hrot.AI.Behaviors.Brains.CgfNodes.MoveToLocationParams;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐⭐ <b><c>Q43</c> end-to-end — a parameter resolver AUTHORED AS A BLUEPRINT actually runs.</b>
/// 📄 <c>Architect_Question_43</c> · <c>Behavior_Parameter_Resolver_Detailed_Design.md</c> §8.1 <c>R1</c>–<c>R2</c>.
///
/// <para>
/// ⭐⭐ <b>It drives the REAL corpus asset</b>, not a hand-built fixture: <c>ParamResolverDemo.bp.json</c>
/// is compiled by the production source generator and is covered by the golden sweep, so this rail and
/// the goldens cannot drift apart. ⛔ A builder-made asset here would have tested the emitter against a
/// shape no asset on disk has — which is exactly the blind spot that let <c>BP-297</c> ship.
/// </para>
///
/// <para>
/// ⚠ <b>What "end to end" means here:</b> compile → Roslyn-load → run the generated
/// <c>[BlueprintRegistrar]</c> → look the asset up by name → invoke through
/// <see cref="BlueprintDefinition.Resolvers"/> → read the refined DTO back out. ⛔ It does NOT yet cover
/// a BINDING (which behaviour's params this resolver refines) — that is deliberately the next slice,
/// because the authoring surface for it is <c>Q41-C2′</c>'s picker and the UI lane is held.
/// </para>
/// </summary>
public sealed class BlueprintAuthoredResolver_InvokeTests : IDisposable
{
    private readonly BlueprintTestFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void AConstructionGraph_IsRegisteredAsAResolver_AndRefinesTheDtoItIsGiven()
    {
        GoldenCorpus.EnsureBehaviorAssemblyLoaded();
        _fixture.CompileAndLoad(GoldenCorpus.Load("ParamResolverDemo"));

        Assert.True(_fixture.Registry.TryGetByName("ParamResolverDemo", out var def),
            "The resolver Library blueprint should be registered.");
        Assert.Equal(BlueprintDispatchKind.Library, def!.Kind);

        // ⭐⭐ Q43-A2′ — a Construction graph lands in `Resolvers`, NOT in `Functions`. The separation is
        //    the whole point: a binding site must be able to pick a resolver without a naming
        //    convention (Q43-A3 rejected exactly that).
        Assert.True(def.Resolvers.ContainsKey("ResolveMoveTo"),
            "The Construction graph must be published in BlueprintDefinition.Resolvers.");
        Assert.False(def.Functions.ContainsKey("ResolveMoveTo"),
            "A Construction graph must NOT also appear in Functions — the kind is what marks a resolver.");

        // ── invoke it ────────────────────────────────────────────────────────
        var authored = new MoveToParams { X = 12.5f, Y = -3.25f, Speed = 36f, ArrivalRadius = 4f };

        var entity = _fixture.CreateEntity();
        Span<byte> inputs  = stackalloc byte[Marshal.SizeOf<MoveToParams>()];
        Span<byte> outputs = stackalloc byte[Marshal.SizeOf<MoveToParams>()];
        MemoryMarshal.Write(inputs, in authored);

        def.Resolvers["ResolveMoveTo"](inputs, outputs, _fixture.View, entity, 0f);

        var usable = MemoryMarshal.Read<MoveToParams>(outputs);

        // ⭐ Q43-D — the resolver REFINES: the one member it rewires changes, the rest are PRESERVED
        //   because they were left unwired on the SetMembers node. ⛔ A resolver that merely PRODUCED a
        //   value would zero X/Y/ArrivalRadius here, which is the BP-275 defect in a new place.
        Assert.Equal(36f * 0.2777778f, usable.Speed, 4);
        Assert.Equal(authored.X, usable.X);
        Assert.Equal(authored.Y, usable.Y);
        Assert.Equal(authored.ArrivalRadius, usable.ArrivalRadius);
    }
}
