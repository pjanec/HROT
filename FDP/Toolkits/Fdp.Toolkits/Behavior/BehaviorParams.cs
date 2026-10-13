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
    /// which wrote <c>unmanaged</c>. Only the BLOCK is memory, so only <typeparamref name="TBlock"/> must be unmanaged.
    /// ⭐ But only an UNMANAGED <typeparamref name="TAuthored"/> gets the from-bytes arm
    /// (<see cref="FromBlockResolverSource{TAuthored, TBlock}"/>) — i.e. can be started by a HOST with parameters.
    /// ⭐ <c>CE-2023</c> ③ (<c>DESIGN_Unified_Behaviour_Run.md</c> "S8n"): <c>MoveToLocationParamsJsonDto</c> and
    /// <c>FireAtTargetParamsJsonDto</c> are STRUCTS for exactly that reason. ⛔ SUPERSEDED: <i>"the two shipped two-shape
    /// authored DTOs … are CLASSES … never stored in a slot"</i> (<c>PlatoonHillAttackParamsJsonDto</c> still is one).
    /// With an empty payload a class-typed <paramref name="authored"/> is <c>null</c> and a struct one is <c>default</c> —
    /// the resolver decides what an absent intent means.</para>
    public delegate void ResolveBlock<TAuthored, TBlock>(
        in TAuthored authored, ref TBlock block, EntityRepository world, Entity self)
        where TBlock : unmanaged;

    /// <summary>
    /// ⭐⭐ <c>CE-3194</c> (R-3001 / R-3002, <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10a) — a params DTO's defaults, ONE
    /// answer for the bake, the curated parse and the editor. The DTO's own default is its field INITIALISERS — <c>new T()</c> with a
    /// parameterless constructor (C# applies initialisers only through one); an asset's <c>ParamsDefaultJson</c> overrides single
    /// fields on top; an order's JSON overrides on top of that (<see cref="BehaviorParams.OverlayFlat{T}"/>).
    /// <para>⛔ Never <c>default(T)</c> or zeroed memory: neither runs a constructor, so both lose the initialisers.</para>
    /// </summary>
    public static class ParamsDefaults
    {
        /// <summary>The DTO's own default: a value type through its parameterless constructor; a class through its parameterless
        /// constructor when it has one, else <c>null</c> (a class-shaped JSON contract with no default).</summary>
        public static T New<T>()
        {
            if (typeof(T).IsValueType) return Activator.CreateInstance<T>();
            return typeof(T).GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance<T>() : default!;
        }

        /// <summary><see cref="New{T}"/> for a type known at run time (the editor's form, a host). Boxed.</summary>
        public static object? DefaultOf(Type dto)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));
            if (dto.IsValueType) return Activator.CreateInstance(dto);
            return dto.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(dto) : null;
        }

        /// <summary>
        /// The EFFECTIVE value an asset bakes: <see cref="DefaultOf"/>, then <paramref name="overridesJson"/> (field → value; an absent
        /// field keeps the DTO's default; keys case-insensitive). Malformed or non-object JSON throws — an asset bug.
        /// </summary>
        public static object? Effective(Type dto, string? overridesJson)
        {
            var baseline = DefaultOf(dto);
            if (string.IsNullOrWhiteSpace(overridesJson) || baseline is null) return baseline;
            var opts = BehaviorParams.JsonOptions;
            var incoming = System.Text.Json.Nodes.JsonNode.Parse(overridesJson!) as System.Text.Json.Nodes.JsonObject
                ?? throw new JsonException($"CE-3194: ParamsDefaultJson for '{dto.Name}' must be a JSON object (field → value).");
            var merged = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(baseline, dto, opts)) as System.Text.Json.Nodes.JsonObject
                ?? new System.Text.Json.Nodes.JsonObject();
            foreach (var (key, value) in incoming)
            {
                string? existing = null;
                foreach (var (k, _) in merged)
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) { existing = k; break; }
                merged[existing ?? key] = value?.DeepClone();
            }
            return JsonSerializer.Deserialize(merged, dto, opts);
        }
    }

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
        /// ⭐⭐ <c>CE-2133</c> (B1, <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §1 P2) — the params JSON IS the params DTO's
        /// fields, FLAT, overlaid onto the already-baked <paramref name="target"/>: a key present in <paramref name="json"/>
        /// replaces that field, an absent key keeps its baked default, an unknown key is ignored (the curated path's own rule),
        /// keys match case-insensitively (<see cref="JsonOptions"/>), and malformed JSON THROWS (parse-before-commit is the
        /// ingress's guarantee). Empty / whitespace <paramref name="json"/> leaves <paramref name="target"/> as baked.
        /// <para>The generated <c>ParseParams</c> of a behaviour whose one Input variable is its params DTO calls this.</para>
        /// </summary>
        public static void OverlayFlat<T>(ref T target, string? json, JsonSerializerOptions? options = null) where T : struct
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            var opts = options ?? JsonOptions;
            var incoming = System.Text.Json.Nodes.JsonNode.Parse(json!) as System.Text.Json.Nodes.JsonObject
                ?? throw new JsonException($"CE-2133: params for '{typeof(T).Name}' must be a JSON object (the DTO's fields, flat).");
            if (incoming.Count == 0) return;
            var merged = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(target, opts)) as System.Text.Json.Nodes.JsonObject
                ?? new System.Text.Json.Nodes.JsonObject();
            foreach (var (key, value) in incoming)
            {
                string? existing = null;
                foreach (var (k, _) in merged)
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) { existing = k; break; }
                merged[existing ?? key] = value?.DeepClone();
            }
            target = merged.Deserialize<T>(opts);
        }

        /// <summary>
        /// ⭐ <c>CE-2133</c> — a behaviour that NAMES a params DTO it does not store (no Input variable of that type and no
        /// generated resolver): an empty payload is fine (an empty DTO such as <c>HoldOrder</c> carries nothing), a payload
        /// with fields THROWS unless a curated resolver has replaced this supply — ⛔ never drop authored params silently.
        /// </summary>
        public static void RequireNoStoredFields(string? json, string behaviour, string paramsDto)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            if (System.Text.Json.Nodes.JsonNode.Parse(json!) is System.Text.Json.Nodes.JsonObject { Count: 0 }) return;
            throw new InvalidOperationException(
                $"CE-2133: behaviour '{behaviour}' names params DTO '{paramsDto}' but stores none of it (no Input variable of " +
                "that type) and no resolver converts it, so these params would be dropped. Declare the Input variable, or " +
                "register a resolver.");
        }

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

                // ⭐ CE-3194 (R-3002) — no JSON: a STRUCT contract is its own default (`new T()`, its initialisers — it was
                //   default(T), which skips them); ⚠ a CLASS contract stays null, "nothing was sent" — a resolver keeps the bake on
                //   it (DangerAreaNodes.ResolveDangerCrossing), and a fresh instance would overwrite the tuning with zeros.
                TAuthored authored = string.IsNullOrWhiteSpace(json)
                    ? (typeof(TAuthored).IsValueType ? ParamsDefaults.New<TAuthored>() : default!)
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
                TAuthored authored = ParamsDefaults.New<TAuthored>();   // ⭐ CE-3194 — an unbound child gets the DTO's own default
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
