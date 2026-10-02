using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-427</c> — <b>the ROOT behaviour's resolve stage over its whole block.</b> 📄 <c>Q76</c>
    /// §12.4c, <c>Behavior_Parameter_Resolver_Detailed_Design</c> §3.3's logical signature
    /// <i>"resolve(in TAuthored authored, ref TUsable usable, …)"</i>.
    ///
    /// <para>⭐ <paramref name="authored"/> is the scenario's JSON deserialized into the behaviour's
    /// AUTHORED shape — read-only, <c>in</c>. <paramref name="block"/> arrives already BAKED with every
    /// default (<c>CE-426</c>), so the resolver CONVERTS and MODIFIES (<c>R-152</c>: <i>"Resolves does
    /// conversion if needed"</i>) and may write any field of the block, State included (<c>R-151</c> ③).</para>
    ///
    /// </summary>
    /// <para>⚠ <typeparamref name="TAuthored"/> is deliberately UNCONSTRAINED — refines <c>Q76</c> §12.4c,
    /// which wrote <c>unmanaged</c>. 📐 The two shipped two-shape authored DTOs
    /// (<c>PlatoonHillAttackParamsJsonDto</c>, <c>MoveToLocationParamsJsonDto</c>) are CLASSES: they are
    /// JSON contracts carrying geographic points, never stored in a slot. Only the BLOCK is memory, so
    /// only <typeparamref name="TBlock"/> must be unmanaged. With an empty payload a class-typed
    /// <paramref name="authored"/> is <c>null</c> — the resolver decides what an absent intent means.</para>
    public delegate void ResolveBlock<TAuthored, TBlock>(
        in TAuthored authored, ref TBlock block, EntityRepository world, Entity self)
        where TBlock : unmanaged;

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

        // ⛔ CE-416 ③ (2026-10-02) — FromJson<TDto>(ResolveParams<TDto>?) and the ResolveParams delegate are RETIRED.
        //   📐 Zero production callers since CE-426/CE-427: every behaviour's supply is the bake + FromBlockResolver's
        //   typed resolve stage, and FromJson wrote the DTO OVER the baked defaults — a second, older supply shape.
        //   Its identity case is FromBlockResolver<T, T>((in a, ref b, w, s) => b = a).

        /// <summary>
        /// ⭐⭐⭐ <c>CE-427</c> — <b>SUPPLY + RESOLVE for a typed block resolver</b>, as the one
        /// <see cref="ParseParamsDelegate"/> the ingress already calls (ruling 9 — still ONE supply
        /// mechanism). Deserializes the JSON into <typeparamref name="TAuthored"/> and hands it, with the
        /// ALREADY-BAKED block, to <paramref name="resolve"/>.
        ///
        /// <para>⛔ It does NOT bake and does NOT clear: the memory it is given is the ingress shadow the
        /// bake just wrote (<c>BehaviorRegistry.ApplyResolverOverlay</c> composes the bake in front of it).
        /// Clearing here would discard every default — the draft-1 mistake <c>Q76</c> §12.9c records.</para>
        ///
        /// <para>⚠ The width guard is the parse's own <c>capacity</c>: a buffer narrower than
        /// <typeparamref name="TBlock"/> is a stale layout and is refused, never overrun.</para>
        /// </summary>
        public static unsafe ParseParamsDelegate FromBlockResolver<TAuthored, TBlock>(ResolveBlock<TAuthored, TBlock> resolve)
            where TBlock : unmanaged
        {
            if (resolve is null) throw new ArgumentNullException(nameof(resolve));
            return (string json, byte* memory, int capacity, EntityRepository world, Entity self) =>
            {
                if (capacity < sizeof(TBlock))
                    throw new InvalidOperationException(
                        $"CE-427: the parse buffer is {capacity} bytes but the resolver's block " +
                        $"'{typeof(TBlock).Name}' needs {sizeof(TBlock)} — a stale layout; refusing to overrun it.");

                TAuthored authored = string.IsNullOrWhiteSpace(json)
                    ? default!
                    : JsonSerializer.Deserialize<TAuthored>(json, JsonOptions)!;

                resolve(in authored, ref Unsafe.AsRef<TBlock>(memory), world, self);
            };
        }

        /// <summary>
        /// ⭐⭐ <c>CE-443</c> (absorbs <c>CE-438</c>) — the FROM-BYTES arm of a typed block resolver: what a HOSTED
        /// child runs, its source being the bound host variable (<c>DESIGN_Parameter_Model.md</c> §P.2). Returns
        /// <c>null</c> when <typeparamref name="TAuthored"/> is not unmanaged — a class-typed authored contract is
        /// a JSON shape and cannot come from host bytes.
        /// <para>⛔ A source whose width is not <c>sizeof(TAuthored)</c> THROWS — it can only be a wrong binding.
        /// A <c>null</c> source (an unbound child) hands the resolver <c>default(TAuthored)</c>.</para>
        /// </summary>
        public static unsafe ResolveStageDelegate? FromBlockResolverSource<TAuthored, TBlock>(ResolveBlock<TAuthored, TBlock> resolve)
            where TBlock : unmanaged
        {
            if (resolve is null) throw new ArgumentNullException(nameof(resolve));
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TAuthored>()) return null;
            int authoredBytes = Unsafe.SizeOf<TAuthored>();
            return (byte* source, int sourceBytes, byte* block, int capacity, EntityRepository world, Entity self) =>
            {
                if (capacity < sizeof(TBlock))
                    throw new InvalidOperationException(
                        $"CE-443: the child block is {capacity} bytes but the resolver's block " +
                        $"'{typeof(TBlock).Name}' needs {sizeof(TBlock)} — a stale layout; refusing to overrun it.");
                TAuthored authored = default!;
                if (source != null)
                {
                    if (sourceBytes != authoredBytes)
                        throw new InvalidOperationException(
                            $"CE-443: the host variable is {sourceBytes} bytes but the resolver's authored type " +
                            $"'{typeof(TAuthored).Name}' is {authoredBytes}. The bound variable must be that type.");
                    authored = Unsafe.ReadUnaligned<TAuthored>(source);
                }
                resolve(in authored, ref Unsafe.AsRef<TBlock>(block), world, self);
            };
        }
    }
}
