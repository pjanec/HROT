using System;
using Fdp.Toolkit.Behavior;

namespace Fdp.Toolkit.Blueprints;

/// <summary>
/// ⭐⭐⭐ <b>One blueprint-authored parameter resolver, published in the ONE currency every supply path
/// speaks.</b> 📄 <c>DESIGN_Resolver_World_Reach.md</c> §7.1 · <c>Architect_Question_43</c> §8.6.
///
/// <para>
/// 🔴 <b>Why this type exists at all — the shape it replaces was measurably wrong.</b> <c>Q43</c> first
/// published resolvers as <see cref="LibraryFunctionDelegate"/>, reusing the table
/// <see cref="BlueprintDefinition.Functions"/> already had. 📐 That delegate is
/// <c>(inputs, outputs, ISimulationView, Entity, float)</c> and carries <b>no</b>
/// <see cref="IHostVariableAccess"/> ⇒ a resolver dispatched through it <b>silently loses
/// <c>host</c></b> — the one capability <c>Q41-C1′</c>/<c>E7a</c> exist to provide — and pays a span
/// round-trip besides.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The currency is <see cref="ResolveParams{TDto}"/>, measured against all five supply paths:</b>
/// hand-written curated · the BTree bridge · the HSM bridge · Blueprint Instance · and the hosted
/// occurrence. ⛔ <c>ParseParamsDelegate</c> cannot serve the last of those — it demands a <c>json</c>
/// argument that a <c>freshlyAttached</c> occurrence does not have — and <see cref="ResolveParams{TDto}"/>
/// serves all five, because <c>BehaviorParams.FromJson</c> composes it into a <c>ParseParamsDelegate</c>
/// for the four that are JSON-fed.
/// </para>
///
/// <para>
/// ⚠ <b>Type-erased, because <see cref="BlueprintDefinition"/> is not generic.</b> ⭐ That is not a new
/// pattern: <c>HostedParamResolvers</c> stores the same delegate as <c>object</c> and casts on read,
/// and for the same reason. Every consumer knows its <c>TDto</c> at its own call site, so the erasure
/// costs nothing — see <see cref="As{TDto}"/>.
/// </para>
/// </summary>
public sealed class BlueprintResolverEntry
{
    private readonly object _resolver;

    private BlueprintResolverEntry(Type dtoType, object resolver)
    {
        DtoType   = dtoType  ?? throw new ArgumentNullException(nameof(dtoType));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>
    /// ⭐⭐ The type this resolver refines, carried SEPARATELY from the delegate.
    ///
    /// <para>
    /// ⛔ <b>Not redundant with the erased delegate.</b> A picker must type-filter the resolvers it
    /// offers — <c>Q43-E</c>: <i>"one type-filtered picker, per variable"</i> — and it must do that
    /// <b>without</b> invoking or reflecting over the delegate. ⭐ Reading a <see cref="Type"/> is the
    /// cheap, total way to answer <i>"may this resolver fill that variable?"</i>.
    /// </para>
    /// </summary>
    public Type DtoType { get; }

    /// <summary>
    /// Builds an entry from a typed resolver. ⭐ The generic parameter is what gives the emitted lambda
    /// its target type, so the generated registrar needs no cast and no helper method.
    /// </summary>
    public static BlueprintResolverEntry For<TDto>(ResolveParams<TDto> resolver)
        where TDto : unmanaged
        => new(typeof(TDto), resolver);

    /// <summary>
    /// ⭐⭐ Recovers the typed resolver.
    ///
    /// <para>
    /// ⛔⛔ <b>A mismatched <typeparamref name="TDto"/> THROWS, and that is the point.</b> It can only
    /// arise from a binding that named this resolver for a params region of a different shape — an
    /// authoring error — and running it anyway would reinterpret that region's bytes as another type's
    /// layout, silently. ⭐ Same contract, same wording as <c>HostedParamResolvers.TryRun</c>, because
    /// it is the same hazard.
    /// </para>
    /// </summary>
    public ResolveParams<TDto> As<TDto>() where TDto : unmanaged
    {
        if (_resolver is ResolveParams<TDto> typed) return typed;

        throw new InvalidOperationException(
            $"This blueprint resolver refines {DtoType.Name}, but it was asked for as a "
            + $"ResolveParams<{typeof(TDto).Name}>. The binding named this resolver for a parameters "
            + "region of a different shape — running it would reinterpret that region's bytes as "
            + "another type's layout.");
    }
}
