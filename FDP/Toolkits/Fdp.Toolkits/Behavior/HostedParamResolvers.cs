using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b><c>Q41-C1′</c> for the HOSTED path — the RESOLVE stage, finally with somewhere to run.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.7 · <c>Architect_Question_41</c> <c>C1′</c>.
///
/// <para>🔴 <b>The hole <c>C1′</c> names.</b> <c>BehaviorParams.FromJson</c> specifies three stages —
/// <b>bake defaults → overlay JSON → RESOLVE → write</b> — and the generated <c>ParseParams</c>
/// implements bake, overlay and write. ⛔ <b>There is no resolve step</b>, which is why
/// <c>RegisterResolver</c> collides with the generated path: the generated path cannot EXPRESS the
/// middle stage. 🔒 <c>Q41</c> calls emitting it <i>"the enabling change — nothing else in C is
/// reachable without it."</i></para>
///
/// <para>⭐⭐⭐ <b>Why the HOSTED call site is the one that matters.</b> A resolver's signature is
/// <c>ResolveParams&lt;TDto&gt;(ref TDto, EntityRepository world, Entity self, IHostVariableAccess? host)</c>
/// — it reads from <b>wherever it has access</b>, so its result depends on the occurrence's CONTEXT.
/// 🔒 <b>User, <c>2026-09-21</c>:</b> <i>"isn't there something like function based param resolution,
/// allowing to take params from wherever the function/graph has access to? this would mean own
/// resolve pass."</i> ⇒ ⭐ running it once per behaviour and copying the result into every occurrence
/// would be wrong <b>by construction</b>, and <c>host</c> can only ever be non-null in a per-occurrence
/// pass.</para>
///
/// <para>⚠ <b>Resolve-once, not live binding</b> (§3.1): this runs on <c>freshlyAttached</c> — the
/// child's activation — and never on a steady-state dispatch. ⛔ A per-frame re-resolve is a different
/// feature and <c>R-84</c> rules it out.</para>
///
/// <para>⚠ <b>Startup registration, per-occurrence invocation.</b> Registration happens during the
/// registrar scan; <see cref="TryRun"/> runs inside a kernel dispatch and must not allocate beyond the
/// boxed host access, which is one per occurrence and not per frame.</para>
/// </summary>
public static class HostedParamResolvers
{
    // assetId -> the resolver, stored type-erased so this class is not generic. The cast in TryRun is
    // checked, and a mismatch is a registration error the rail names — never a silent reinterpret.
    private static readonly Dictionary<Guid, object> _byAsset = new();

    /// <summary>
    /// Registers the resolve stage for a hosted blueprint, keyed by its ASSET id.
    ///
    /// <para>⚠ <b>Per ASSET, not per site</b> — which is right, and not a limitation: the per-SITE
    /// difference is carried by <c>E3b-0</c>'s binding (WHICH variable seeds this occurrence) and by
    /// <c>host</c> (what the resolver can read). ⛔ A per-site resolver would be a second selection
    /// mechanism, and <c>Q41-C2′</c> already rules the authorable one is per VARIABLE.</para>
    /// </summary>
    public static void Register<TParams>(Guid assetId, ResolveParams<TParams> resolver)
        where TParams : unmanaged
    {
        if (resolver is null) throw new ArgumentNullException(nameof(resolver));
        _byAsset[assetId] = resolver;
    }

    /// <summary>
    /// ⭐⭐ Runs the asset's resolve stage over <paramref name="parameters"/>, if one is registered.
    ///
    /// <para>⛔ <b>Returns <c>false</c> when there is no resolver, and that is the COMMON case</b> —
    /// §3.1: for the overwhelmingly common shape the authored DTO and the usable params are the same,
    /// and the resolve step <i>is</i> the deserialize. ⚠ So "no resolver" must be free and silent, or
    /// every existing asset pays for a feature it does not use.</para>
    ///
    /// <para>⛔⛔ <b>A registered resolver of the WRONG type THROWS.</b> It can only come from a
    /// registration that named one asset and handed a resolver for another's <c>Params</c> — a build-
    /// time authoring error, and reinterpreting the bytes would corrupt the occurrence silently.</para>
    /// </summary>
    public static bool TryRun<TParams>(
        Guid assetId, ref TParams parameters, Fdp.Core.EntityRepository world, Fdp.Core.Entity self,
        IHostVariableAccess? host)
        where TParams : unmanaged
    {
        if (!_byAsset.TryGetValue(assetId, out object? stored)) return false;

        if (stored is not ResolveParams<TParams> resolver)
            throw new InvalidOperationException(
                $"A parameter resolver is registered for asset {assetId}, but it is a " +
                $"{stored.GetType().Name} rather than a ResolveParams<{typeof(TParams).Name}>. " +
                "The registration named the wrong asset, or the wrong Params type — running it would " +
                "reinterpret this occurrence's bytes as another asset's layout.");

        resolver(ref parameters, world, self, host);
        return true;
    }

    /// <summary>Drops every registration. ⚠ Hot reload and test isolation, as with the registries beside it.</summary>
    public static void ClearAll() => _byAsset.Clear();

    /// <summary>How many resolvers are registered. ⭐ For rails and diagnostics.</summary>
    public static int Count => _byAsset.Count;
}
