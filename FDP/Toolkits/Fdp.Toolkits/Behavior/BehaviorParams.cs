using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐ <c>G1</c> — the RESOLVE half of a parameter supply, as a typed callback.
    ///
    /// <para>
    /// Runs after the authored DTO has been deserialized and before it is written into the params
    /// region. ⭐ For the overwhelmingly common case — the authored DTO and the usable params are the
    /// same shape — there is nothing to do and this is <c>null</c>; the resolve step then <i>is</i> the
    /// deserialize, exactly as 📄 <c>DESIGN_Parameter_Model.md</c> §3.1 says.
    /// </para>
    /// </summary>
    public delegate void ResolveParams<TDto>(
        ref TDto dto, EntityRepository world, Entity self, IHostVariableAccess? host)
        where TDto : unmanaged;

    /// <summary>
    /// ⭐⭐⭐ <b><c>G1</c> — deserialize and resolve, split apart and composed back into ONE delegate.</b>
    ///
    /// <para>
    /// 📄 <c>DESIGN_Parameter_Model.md</c> §3.1 names three data shapes and says the middle one — the
    /// usable params — is what the RESOLVER writes. ⛔ <b>The split did not exist:</b> every
    /// <see cref="ParseParamsDelegate"/> was hand-rolled or emitted as one opaque blob that did both,
    /// so <i>"deserialize the authored DTO"</i> had no single implementation and the identity case
    /// still had to be written out by hand.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>Still ONE supply mechanism</b> (§8, ruling 9) — and that is why this returns a
    /// <see cref="ParseParamsDelegate"/> rather than being a second path the ingress has to know
    /// about. ⛔ A parallel <c>Overrides</c>-style applier would fail the rail; a factory for the one
    /// delegate does not.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Parse-before-commit is the CALLER's guarantee and stays there.</b>
    /// <c>BehaviorIngressSystem</c> parses into a stack shadow and only commits on success, so a
    /// throwing deserialize leaves the entity 100% on its old behaviour. ⇒ this helper deliberately
    /// does NOT swallow — swallowing here would turn a failed parse into a silent all-zero params
    /// region, which is the failure the shadow-copy exists to prevent.
    /// </para>
    /// </summary>
    public static class BehaviorParams
    {
        /// <summary>
        /// The JSON options every authored params payload is read with.
        ///
        /// <para>
        /// ⛔⛔ <b>Deliberately the SHARED registry, not a local copy.</b> A hand-written copy here
        /// looked tidy and was wrong twice over: the generator's emitted resolver already uses
        /// <c>FdpJsonOptionsRegistry.DefaultRelaxed</c>, so a second set would let a hand-composed
        /// resolver and a generated one disagree about the same payload — and the local copy I first
        /// wrote omitted <c>IncludeFields</c>, which every blittable params DTO needs, so it silently
        /// deserialized nothing at all. 📐 Caught by the rail below, not by reading.
        /// </para>
        /// </summary>
        public static JsonSerializerOptions JsonOptions
            => Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;

        /// <summary>
        /// Builds the behaviour's <see cref="ParseParamsDelegate"/> from its DTO type: deserialize the
        /// authored JSON, optionally <paramref name="resolve"/> it against world/self/host, then write
        /// it at the start of the params region.
        ///
        /// <para>
        /// ⚠ <b>An absent or empty payload is <c>default(TDto)</c>, not a failure.</b> Defaults are
        /// baked and the scenario JSON only overlays them (architect-approved <c>2026-06-06</c>), so a
        /// behaviour with nothing to override supplies no JSON at all.
        /// </para>
        /// </summary>
        public static unsafe ParseParamsDelegate FromJson<TDto>(ResolveParams<TDto>? resolve = null)
            where TDto : unmanaged
        {
            return (string json, byte* memory, EntityRepository world, Entity self, IHostVariableAccess? host) =>
            {
                TDto dto = string.IsNullOrWhiteSpace(json)
                    ? default
                    : JsonSerializer.Deserialize<TDto>(json, JsonOptions);

                resolve?.Invoke(ref dto, world, self, host);

                Unsafe.Write(memory, dto);
            };
        }
    }
}
