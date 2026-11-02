using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Scenario;

namespace Fdp.Toolkit.Replication
{
    /// <summary>
    /// ⭐⭐ THE ONE WALKER of referencing and spatial values — finds every leaf of the reference and place types BY TYPE inside a value,
    /// and applies one operation to each. 📄 <c>docs/blueprints/DESIGN_Entity_Reference.md</c> D3 (the remap it began as);
    /// <c>docs/DESIGN_Scenario_Rules.md</c> §9.8.0 / SR-128 / SR-136 (S0: generalised).
    /// <para>
    /// The PLAN is a property of the TYPE, built once per type: members of a LEAF type (<see cref="EntityRef"/>, <see cref="EntityLink"/>,
    /// <see cref="WorldPoint"/>, <see cref="PointLink"/>, <see cref="AreaLink"/>, <see cref="WorldRotation"/>, <see cref="Bearing"/>),
    /// arrays and lists of them, and nested classes, structs, arrays and lists that contain them. Each OPERATION is a
    /// <see cref="LeafApplier"/> — <see cref="RefRemap"/> (old → new network ids, scenario load), <see cref="RefResolve"/> (an authored
    /// <c>@Key</c> / GUID → its answer), <see cref="Relocation"/> (authored anchor → instance anchor) — over either target:
    /// <list type="bullet">
    ///   <item><see cref="WalkJson"/> — a params / predicate JSON, rewritten IN PLACE (the original string comes back when nothing
    ///     changed, so an absent key is never written back as a default — S8o; a present leaf is rewritten through its own converter);</item>
    ///   <item><see cref="WalkObject"/> — an in-memory value (a component, a boxed struct), mutated in place.</item>
    /// </list>
    /// ⛔ No second plan or walker beside this one (CLAUDE.md, shared and unified).
    /// </para>
    /// </summary>
    public static class EntityRefRemap
    {
        // ── the plan ──────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The leaf types the plan finds.</summary>
        private enum LeafKind : byte { EntityRef, EntityLink, WorldPoint, PointLink, AreaLink, WorldRotation, Bearing }

        private enum Kind : byte { Leaf, LeafSequence, Nested, NestedSequence, Poly, PolySequence }

        private sealed class Entry
        {
            public Kind Kind;
            public LeafKind Leaf;
            public string JsonKey = "";
            public bool JsonIgnored;
            public MemberInfo Member = null!;
            public Type MemberType = null!;
            public Plan? Nested;             // Nested / NestedSequence
        }

        private sealed class Plan
        {
            public readonly List<Entry> Entries = new();
            public Dictionary<string, Entry>? ByJsonKey;   // case-insensitive, built on demand
            public bool HoldsIds;                          // any leaf that carries a network id (EntityRef / EntityLink / AreaLink), here or nested
            public string[]? Retired;                      // [RetiredJsonKeys]: keys an old form used — refused by a strict applier
            public Type Type = null!;                      // the contract this plan was built for
            public bool Bindable;                          // ⭐ SR-160/SR-164: the type is a BindableNode — the applier's Node hook sees it
        }

        private static readonly ConditionalWeakTable<Type, StrongBox<Plan?>> _plans = new();
        private static readonly object _gate = new();

        private static LeafKind? LeafOf(Type t)
            => t == typeof(EntityRef)     ? LeafKind.EntityRef
             : t == typeof(EntityLink)    ? LeafKind.EntityLink
             : t == typeof(WorldPoint)    ? LeafKind.WorldPoint
             : t == typeof(PointLink)     ? LeafKind.PointLink
             : t == typeof(AreaLink)      ? LeafKind.AreaLink
             : t == typeof(WorldRotation) ? LeafKind.WorldRotation
             : t == typeof(Bearing)       ? LeafKind.Bearing
             : null;

        private static bool CarriesIds(LeafKind leaf) => leaf is LeafKind.EntityRef or LeafKind.EntityLink or LeafKind.AreaLink;

        /// <summary><c>true</c> when values of <paramref name="type"/> can hold a network id anywhere inside (an <see cref="EntityRef"/>,
        /// <see cref="EntityLink"/> or <see cref="AreaLink"/>) — what a scenario load must remap.</summary>
        public static bool HoldsRefs(Type type)
            => LeafOf(type) is { } l ? CarriesIds(l)
             : ElementType(type) is { } e && LeafOf(e) is { } el ? CarriesIds(el)
             : PlanOf(type)?.HoldsIds == true;

        /// <summary><c>true</c> when values of <paramref name="type"/> hold ANY leaf (a reference, a place, an orientation, a bearing).</summary>
        public static bool HoldsLeaves(Type type)
            => LeafOf(type) != null || (ElementType(type) is { } e && LeafOf(e) != null) || PlanOf(type) != null;

        private static Plan? PlanOf(Type type)
        {
            if (_plans.TryGetValue(type, out var box)) return box.Value;
            lock (_gate)
            {
                if (_plans.TryGetValue(type, out box)) return box.Value;
                var plan = Build(type, new HashSet<Type>());
                _plans.Add(type, new StrongBox<Plan?>(plan));
                return plan;
            }
        }

        private static Plan? Build(Type type, HashSet<Type> visiting)
        {
            if (!IsWalkable(type) || !visiting.Add(type)) return null;
            try
            {
                Plan? plan = null;
                const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;
                foreach (var p in type.GetProperties(Public))
                    if (p.CanRead && p.GetIndexParameters().Length == 0)
                        Add(ref plan, p, p.PropertyType, visiting);
                foreach (var f in type.GetFields(Public))
                    Add(ref plan, f, f.FieldType, visiting);
                // ⭐ SR-164 — a BindableNode always has a plan (its Bind map is applied even when it holds no leaf).
                if (plan == null && typeof(BindableNode).IsAssignableFrom(type)) plan = new Plan();
                if (plan != null) plan.Bindable = typeof(BindableNode).IsAssignableFrom(type);
                if (plan != null)
                {
                    plan.Type = type;
                    if (type.GetCustomAttribute<RetiredJsonKeysAttribute>() is { } retired) plan.Retired = retired.Keys;
                }
                return plan;
            }
            finally { visiting.Remove(type); }
        }

        private static void Add(ref Plan? plan, MemberInfo member, Type memberType, HashSet<Type> visiting)
        {
            Entry? e = null;
            bool ids = false;
            // ⭐ SR-164 — a member typed as a BindableNode (a predicate, a do entry) is POLYMORPHIC: walked by the RUNTIME type's plan
            //   (the JSON's discriminator), so a tree of predicates is reached at any depth.
            if (typeof(BindableNode).IsAssignableFrom(memberType))
            { e = new Entry { Kind = Kind.Poly }; ids = PolyHoldsIds(memberType, visiting); }
            else if (ElementType(memberType) is { } pe && typeof(BindableNode).IsAssignableFrom(pe))
            { e = new Entry { Kind = Kind.PolySequence }; ids = PolyHoldsIds(pe, visiting); }
            else if (LeafOf(memberType) is { } leaf)
            { e = new Entry { Kind = Kind.Leaf, Leaf = leaf }; ids = CarriesIds(leaf); }
            else if (ElementType(memberType) is { } elem && LeafOf(elem) is { } elemLeaf)
            { e = new Entry { Kind = Kind.LeafSequence, Leaf = elemLeaf }; ids = CarriesIds(elemLeaf); }
            else if (ElementType(memberType) is { } elem2 && (Existing(elem2, visiting) ?? Build(elem2, visiting)) is { } elemPlan)
            { e = new Entry { Kind = Kind.NestedSequence, Nested = elemPlan }; ids = elemPlan.HoldsIds; }
            else if ((Existing(memberType, visiting) ?? Build(memberType, visiting)) is { } nested)
            { e = new Entry { Kind = Kind.Nested, Nested = nested }; ids = nested.HoldsIds; }
            if (e is null) return;

            e.Member      = member;
            e.MemberType  = memberType;
            e.JsonKey     = member.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: true)?.Name ?? member.Name;
            e.JsonIgnored = member.IsDefined(typeof(JsonIgnoreAttribute), inherit: true);
            plan ??= new Plan();
            plan.Entries.Add(e);
            plan.HoldsIds |= ids;
        }

        // ── polymorphic BindableNode members (SR-164) ─────────────────────────────────────────────────────────────

        private static readonly ConditionalWeakTable<Type, Dictionary<string, Type>> _derived = new();

        /// <summary>The concrete types a member declared as <paramref name="declared"/> may hold: its <c>[JsonDerivedType]</c> list
        /// (declared on it or a base), plus itself when concrete. Keyed by the discriminator text.</summary>
        private static Dictionary<string, Type> DerivedOf(Type declared)
        {
            if (_derived.TryGetValue(declared, out var map)) return map;
            map = new Dictionary<string, Type>(StringComparer.Ordinal);
            for (var t = declared; t != null && t != typeof(object); t = t.BaseType)
            {
                var attrs = t.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false);
                bool any = false;
                foreach (var a in attrs)
                {
                    any = true;
                    if (a.TypeDiscriminator is { } d && declared.IsAssignableFrom(a.DerivedType)) map[d.ToString()!] = a.DerivedType;
                }
                if (any) break;
            }
            lock (_gate) { if (!_derived.TryGetValue(declared, out var raced)) _derived.Add(declared, map); else map = raced; }
            return map;
        }

        private static string Discriminator(Type declared)
        {
            for (var t = declared; t != null && t != typeof(object); t = t.BaseType)
                if (t.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false) is { } p)
                    return p.TypeDiscriminatorPropertyName ?? "$type";
            return "$type";
        }

        /// <summary>The runtime type a JSON object of a member declared as <paramref name="declared"/> holds — by its discriminator,
        /// else the declared type itself when concrete; <c>null</c> when it cannot be told (left as is).</summary>
        private static Type? RuntimeTypeOf(Type declared, JsonObject obj)
        {
            if (obj[Discriminator(declared)] is JsonValue v && v.TryGetValue<string>(out var disc)
                && DerivedOf(declared).TryGetValue(disc, out var t)) return t;
            return declared.IsAbstract || declared.IsInterface ? null : declared;
        }

        private static bool PolyHoldsIds(Type declared, HashSet<Type> visiting)
        {
            bool ids = false;
            if (!declared.IsAbstract && !declared.IsInterface)
                ids |= (Existing(declared, visiting) ?? Build(declared, visiting))?.HoldsIds == true;
            foreach (var d in DerivedOf(declared).Values)
                ids |= (Existing(d, visiting) ?? Build(d, visiting))?.HoldsIds == true;
            return ids;
        }

        /// <summary>An already-built plan (outside a cycle), so a shared nested type is planned once.</summary>
        private static Plan? Existing(Type type, HashSet<Type> visiting)
            => !visiting.Contains(type) && _plans.TryGetValue(type, out var box) ? box.Value : null;

        /// <summary>The element type of an array or a generic list/collection, else <c>null</c>.</summary>
        private static Type? ElementType(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && typeof(IList).IsAssignableFrom(t)) return t.GetGenericArguments()[0];
            return null;
        }

        /// <summary>A user class or struct that can carry leaves in its members — not a primitive, a leaf or a framework type.</summary>
        private static bool IsWalkable(Type type)
            => !type.IsPrimitive && !type.IsEnum && !type.IsPointer && !type.IsArray && type != typeof(string)
               && type != typeof(decimal) && !type.IsGenericTypeDefinition && LeafOf(type) == null
               && type != typeof(RefSpec) && type != typeof(FixedString32)
               && type.Namespace != "System"
               && !(type.Namespace?.StartsWith("System.", StringComparison.Ordinal) ?? false);

        // ── the JSON applier ──────────────────────────────────────────────────────────────────────────────────────

        // CE-3187: the shared options plus one delta — the relaxed encoder, so untouched strings in a params blob keep their bytes.
        private static readonly JsonSerializerOptions _options = FdpJsonOptionsRegistry.Derive(o =>
            o.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

        private static readonly ConditionalWeakTable<Type, Func<string?, IReadOnlyDictionary<int, int>, string?>> _json = new();
        private static readonly Func<string?, IReadOnlyDictionary<int, int>, string?> _identity = (json, _) => json;

        /// <summary>Number of remap delegates built (a cache probe for tests).</summary>
        internal static int JsonCompileCount { get; private set; }

        /// <summary>
        /// The cached delegate that rewrites every network id in <paramref name="contractType"/>'s JSON by an old → new map
        /// (<see cref="RefRemap"/> over <see cref="WalkJson"/>). <c>null</c>, empty, malformed or non-object JSON, and JSON with no id in
        /// the map, come back as the SAME string.
        /// </summary>
        public static Func<string?, IReadOnlyDictionary<int, int>, string?> CompileJson(Type contractType)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (_json.TryGetValue(contractType, out var cached)) return cached;
            lock (_gate)
            {
                if (_json.TryGetValue(contractType, out cached)) return cached;
                JsonCompileCount++;
                var plan  = PlanOf(contractType);
                var built = plan is null || !plan.HoldsIds ? _identity
                          : (json, map) => map.Count == 0 ? json : WalkJson(json, plan, new RefRemap(map));
                _json.Add(contractType, built);
                return built;
            }
        }

        /// <summary>
        /// ⭐ Applies <paramref name="applier"/> to every leaf of <paramref name="contractType"/> inside <paramref name="json"/> (an
        /// object). Returns the SAME string when nothing changed (or the JSON is null, malformed or not an object).
        /// </summary>
        public static string? WalkJson(Type contractType, string? json, LeafApplier applier)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            var plan = LeafOf(contractType) == null ? PlanOf(contractType) : null;
            return plan is null ? json : WalkJson(json, plan, applier);
        }

        private static string? WalkJson(string? json, Plan plan, LeafApplier applier)
        {
            if (string.IsNullOrEmpty(json)) return json;
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (JsonException) { return json; }   // malformed: the parser downstream reports it
            if (root is not JsonObject obj) return json;
            return WalkObject(obj, plan, applier) ? root.ToJsonString(_options) : json;
        }

        private static Dictionary<string, Entry> Keys(Plan plan)
        {
            if (plan.ByJsonKey != null) return plan.ByJsonKey;
            var d = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in plan.Entries) if (!e.JsonIgnored) d[e.JsonKey] = e;
            return plan.ByJsonKey = d;
        }

        private static bool WalkObject(JsonObject obj, Plan plan, LeafApplier applier)
        {
            if (applier.RefusesOldForms && plan.Retired is { } retired)
                foreach (var (key, _) in obj)
                    if (Array.Exists(retired, r => r.Equals(key, StringComparison.OrdinalIgnoreCase)))
                        throw new JsonException($"'{key}' is a retired params key (CE-4153 S10: no backward compatibility) — migrate the file with hrot-content-migrate");
            bool changed = plan.Bindable && applier.NodeJson(plan.Type, obj);   // ⭐ SR-164: a node's Bind before its leaves
            var keys = Keys(plan);
            List<(string Key, JsonNode? Node)>? rewrites = null;
            foreach (var (key, node) in obj)
            {
                if (node is null || !keys.TryGetValue(key, out var e)) continue;
                applier.Visiting(key);   // ⭐ CE-4210 W-M6 — a params BLOB names its field too
                switch (e.Kind)
                {
                    case Kind.Leaf:
                        if (TryApply(node, e.Leaf, applier, key, out var replaced)) (rewrites ??= new()).Add((key, replaced));
                        break;
                    case Kind.Poly when node is JsonObject pc:
                        if (RuntimeTypeOf(e.MemberType, pc) is { } pt && PlanOf(pt) is { } pp) changed |= WalkObject(pc, pp, applier);
                        break;
                    case Kind.PolySequence when node is JsonArray pa:
                        var pd = ElementType(e.MemberType)!;
                        foreach (var item in pa)
                            if (item is JsonObject po && RuntimeTypeOf(pd, po) is { } it && PlanOf(it) is { } ip) changed |= WalkObject(po, ip, applier);
                        break;
                    case Kind.LeafSequence when node is JsonArray arr:
                        for (int i = 0; i < arr.Count; i++)
                            if (arr[i] is { } item && TryApply(item, e.Leaf, applier, key, out var ni)) { arr[i] = ni; changed = true; }
                        break;
                    case Kind.Nested when node is JsonObject child:
                        changed |= WalkObject(child, e.Nested!, applier);
                        break;
                    case Kind.NestedSequence when node is JsonArray seq:
                        foreach (var item in seq)
                            if (item is JsonObject o) changed |= WalkObject(o, e.Nested!, applier);
                        break;
                }
            }
            if (rewrites is not null)
            {
                foreach (var (key, n) in rewrites) obj[key] = n;
                changed = true;
            }
            return changed;
        }

        // ── the raw visitor (S0 item 9: the content migration reads the SAME plan) ─────────────────────────────────────

        /// <summary>
        /// ⭐ S0 item 9 — visits every leaf of <paramref name="contractType"/>'s plan inside <paramref name="obj"/> as its RAW JSON node (not read
        /// through the leaf's converter, so a value in a retired form — a <c>[lat, lon]</c> pair where a <see cref="PointLink"/> is — is still seen),
        /// with the leaf's type and its key path; a non-null return REPLACES the node. Returns <c>true</c> when any was replaced.
        /// <paramref name="visitObject"/>, when given, first sees every OBJECT the plan reaches (the root included) with its contract type and
        /// path — where a contract's renamed keys live, at any depth (a blueprint's params nesting a <c>MoveToLocation</c>'s).
        /// ⛔ For migrating content only — the load path reads leaves through their converters (<see cref="WalkJson(Type, string?, LeafApplier)"/>).
        /// </summary>
        public static bool VisitLeaves(Type contractType, JsonObject obj, Func<Type, JsonNode, string, JsonNode?> visit,
                                       Action<Type, JsonObject, string>? visitObject = null)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (LeafOf(contractType) != null) return false;
            return PlanOf(contractType) is { } plan && VisitObject(obj, plan, visit, visitObject, "");
        }

        private static bool VisitObject(JsonObject obj, Plan plan, Func<Type, JsonNode, string, JsonNode?> visit,
                                        Action<Type, JsonObject, string>? visitObject, string path)
        {
            visitObject?.Invoke(plan.Type, obj, path);
            var keys = Keys(plan);
            bool changed = false;
            List<(string Key, JsonNode? Node)>? rewrites = null;
            foreach (var (key, node) in obj)
            {
                if (node is null || !keys.TryGetValue(key, out var e)) continue;
                string at = path.Length == 0 ? key : path + "." + key;
                switch (e.Kind)
                {
                    case Kind.Leaf:
                        if (visit(TypeOf(e.Leaf), node, at) is { } r) (rewrites ??= new()).Add((key, r));
                        break;
                    case Kind.LeafSequence when node is JsonArray arr:
                        for (int i = 0; i < arr.Count; i++)
                            if (arr[i] is { } item && visit(TypeOf(e.Leaf), item, $"{at}[{i}]") is { } ri) { arr[i] = ri; changed = true; }
                        break;
                    case Kind.Nested when node is JsonObject child:
                        changed |= VisitObject(child, e.Nested!, visit, visitObject, at);
                        break;
                    case Kind.NestedSequence when node is JsonArray seq:
                        for (int i = 0; i < seq.Count; i++)
                            if (seq[i] is JsonObject o) changed |= VisitObject(o, e.Nested!, visit, visitObject, $"{at}[{i}]");
                        break;
                    case Kind.Poly when node is JsonObject pc:
                        if (RuntimeTypeOf(e.MemberType, pc) is { } pt && PlanOf(pt) is { } pp) changed |= VisitObject(pc, pp, visit, visitObject, at);
                        break;
                    case Kind.PolySequence when node is JsonArray pa:
                        var pd = ElementType(e.MemberType)!;
                        for (int i = 0; i < pa.Count; i++)
                            if (pa[i] is JsonObject po && RuntimeTypeOf(pd, po) is { } it && PlanOf(it) is { } ip)
                                changed |= VisitObject(po, ip, visit, visitObject, $"{at}[{i}]");
                        break;
                }
            }
            if (rewrites is not null) { foreach (var (key, n) in rewrites) obj[key] = n; changed = true; }
            return changed;
        }

        private static Type TypeOf(LeafKind leaf) => leaf switch
        {
            LeafKind.EntityRef => typeof(EntityRef),
            LeafKind.EntityLink => typeof(EntityLink),
            LeafKind.WorldPoint => typeof(WorldPoint),
            LeafKind.PointLink => typeof(PointLink),
            LeafKind.AreaLink => typeof(AreaLink),
            LeafKind.WorldRotation => typeof(WorldRotation),
            LeafKind.Bearing => typeof(Bearing),
            _ => typeof(object),
        };

        /// <summary>One JSON leaf through its own converter: read, apply, and — only when the value changed — write it back.</summary>
        private static bool TryApply(JsonNode node, LeafKind leaf, LeafApplier applier, string key, out JsonNode? replaced)
        {
            replaced = null;
            switch (leaf)
            {
                case LeafKind.EntityRef:     return Step<EntityRef>(node, applier.Ref, applier.RefusesOldForms ? key : null, out replaced);
                case LeafKind.EntityLink:    return Step<EntityLink>(node, applier.Link, applier.RefusesOldForms ? key : null, out replaced);
                case LeafKind.WorldPoint:    return Step<WorldPoint>(node, applier.Point, applier.RefusesOldForms ? key : null, out replaced);
                case LeafKind.PointLink:     return Step<PointLink>(node, applier.PointLink, applier.RefusesOldForms ? key : null, out replaced);
                case LeafKind.AreaLink:      return Step<AreaLink>(node, applier.Area, applier.RefusesOldForms ? key : null, out replaced);
                case LeafKind.WorldRotation: return Step<WorldRotation>(node, applier.Rotation, applier.RefusesOldForms ? key : null, out replaced);
                case LeafKind.Bearing:       return Step<Bearing>(node, applier.Bearing, applier.RefusesOldForms ? key : null, out replaced);
                default:                 return false;
            }
        }

        private static bool Step<T>(JsonNode node, Func<T, T> op, string? refuseAt, out JsonNode? replaced) where T : struct, IEquatable<T>
        {
            replaced = null;
            T before;
            // A value the leaf type cannot read is left as it is (the remap always skipped a non-number): the params parse that
            // follows reads it with the same converter and reports it loudly there. ⭐ An authored reference that names NOTHING is the
            // applier's to refuse (RefResolve), not this read's.
            // ⛔ CE-4153 S10 — except for an applier that refuses old forms (RefResolve: authored content at LOAD): a value its leaf cannot
            //   read there is a retired form (a [lat, lon] place) and the load fails, naming the key.
            try { before = node.Deserialize<T>(_options); }
            catch (Exception ex) when (refuseAt is not null)
            { throw new JsonException($"'{refuseAt}': {node.ToJsonString()} is not a {typeof(T).Name} (CE-4153 S10: no backward compatibility) — migrate the file with hrot-content-migrate", ex); }
            catch (Exception) { return false; }
            T after = op(before);
            if (after.Equals(before)) return false;
            replaced = JsonSerializer.SerializeToNode(after, _options);
            return true;
        }

        // ── the object applier ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rewrites, in place, every network id inside <paramref name="value"/> (a class instance, or a BOXED struct — mutations land in
        /// the box) by an old → new map. Returns <c>true</c> when any changed.
        /// </summary>
        public static bool RemapObject(object? value, IReadOnlyDictionary<int, int> map)
            => map.Count != 0 && WalkObject(value, new RefRemap(map));

        /// <summary>⭐ Applies <paramref name="applier"/> to every leaf inside <paramref name="value"/> (a class instance or a BOXED struct),
        /// in place. Returns <c>true</c> when any changed.</summary>
        public static bool WalkObject(object? value, LeafApplier applier)
        {
            if (value is null) return false;
            var plan = PlanOf(value.GetType());
            return plan != null && Apply(value, applier, plan);
        }

        private static bool Apply(object target, LeafApplier applier, Plan plan)
        {
            bool changed = plan.Bindable && target is BindableNode node && applier.Node(node);   // ⭐ SR-164: a node's Bind before its leaves
            foreach (var e in plan.Entries)
            {
                object? v = Get(e.Member, target);
                if (v is null) continue;
                applier.Visiting(e.JsonKey);
                switch (e.Kind)
                {
                    case Kind.Leaf:
                        var nv = ApplyLeaf(v, e.Leaf, applier);
                        if (!Equals(nv, v) && TrySet(e.Member, target, nv)) changed = true;
                        break;
                    case Kind.LeafSequence:
                        var list = (IList)v;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var item = list[i]!;
                            var ni = ApplyLeaf(item, e.Leaf, applier);
                            if (!Equals(ni, item)) { list[i] = ni; changed = true; }
                        }
                        break;
                    case Kind.Nested:
                        // A struct comes back BOXED: rewrite the box, then store it back into the member.
                        if (Apply(v, applier, e.Nested!) && (!e.MemberType.IsValueType || TrySet(e.Member, target, v))) changed = true;
                        break;
                    case Kind.Poly:
                        if (PlanOf(v.GetType()) is { } pp && Apply(v, applier, pp)) changed = true;   // a class: mutated in place
                        break;
                    case Kind.PolySequence:
                        foreach (var item in (IList)v)
                            if (item is not null && PlanOf(item.GetType()) is { } ip && Apply(item, applier, ip)) changed = true;
                        break;
                    case Kind.NestedSequence:
                        var seq = (IList)v;
                        for (int i = 0; i < seq.Count; i++)
                        {
                            var item = seq[i];
                            if (item is null || !Apply(item, applier, e.Nested!)) continue;
                            if (item.GetType().IsValueType) seq[i] = item;   // store the rewritten box back
                            changed = true;
                        }
                        break;
                }
            }
            return changed;
        }

        /// <summary>⭐ Applies <paramref name="applier"/> to ONE value — a leaf itself (an <see cref="EntityLink"/>, a <see cref="WorldPoint"/> …) or
        /// anything holding leaves (a struct is walked boxed) — and returns the new value. (A param's value resolved in the scope
        /// outside a macro, SR-164.)</summary>
        public static object? ApplyTo(object? value, LeafApplier applier)
        {
            if (value is null) return null;
            if (LeafOf(value.GetType()) is { } leaf) return ApplyLeaf(value, leaf, applier);
            WalkObject(value, applier);
            return value;
        }

        private static object ApplyLeaf(object v, LeafKind leaf, LeafApplier a) => leaf switch
        {
            LeafKind.EntityRef     => a.Ref((EntityRef)v),
            LeafKind.EntityLink    => a.Link((EntityLink)v),
            LeafKind.WorldPoint    => a.Point((WorldPoint)v),
            LeafKind.PointLink     => a.PointLink((PointLink)v),
            LeafKind.AreaLink      => a.Area((AreaLink)v),
            LeafKind.WorldRotation => a.Rotation((WorldRotation)v),
            LeafKind.Bearing       => a.Bearing((Bearing)v),
            _                  => v,
        };

        private static object? Get(MemberInfo m, object target)
            => m is PropertyInfo p ? p.GetValue(target) : ((FieldInfo)m).GetValue(target);

        private static bool TrySet(MemberInfo m, object target, object value)
        {
            switch (m)
            {
                case PropertyInfo p when p.CanWrite: p.SetValue(target, value); return true;
                case FieldInfo f when !f.IsLiteral:  f.SetValue(target, value); return true;
                default: return false;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // The operations — each maps a leaf to its new value (the identity by default). One applier per concept; ⛔ no per-field code.
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>An operation the walker applies to every leaf (<see cref="EntityRefRemap.WalkJson"/> / <see cref="EntityRefRemap.WalkObject"/>).
    /// Each method returns the leaf's new value; the default leaves it as is.</summary>
    public abstract class LeafApplier
    {
        public virtual EntityRef Ref(EntityRef r) => r;
        public virtual EntityLink Link(EntityLink l) => l;
        public virtual WorldPoint Point(WorldPoint p) => p;
        public virtual PointLink PointLink(PointLink p) => p;
        public virtual AreaLink Area(AreaLink a) => a;
        public virtual WorldRotation Rotation(WorldRotation r) => r;
        public virtual Bearing Bearing(Bearing b) => b;

        /// <summary>⭐ SR-164 — every <see cref="BindableNode"/> the walk reaches (an in-memory value), before its leaves. <c>true</c> = changed.</summary>
        public virtual bool Node(BindableNode node) => false;

        /// <summary>⭐ CE-4210 MA-9 — called by the walker before it visits each member (its JSON key), so a collecting applier can say WHICH field a leaf came from. Does nothing by default; never changes a value.</summary>
        public virtual void Visiting(string member) { }

        /// <summary>⭐ CE-4210 W-M6 — called by <c>ScenarioEntityWalk</c> before it walks a params BLOB, naming the slot it belongs to (<c>"Behavior.Params"</c>, <c>"Sop.Params"</c>, <c>"task 2"</c>). Does nothing by default; never changes a value.</summary>
        public virtual void Entering(string slot) { }
        /// <summary>⭐ SR-164 — every <see cref="BindableNode"/> the walk reaches in JSON (<paramref name="nodeType"/> = its runtime type), before its leaves.</summary>
        public virtual bool NodeJson(Type nodeType, JsonObject obj) => false;

        /// <summary>⛔ CE-4153 S10 — <c>true</c> for an applier over AUTHORED content at load (<see cref="RefResolve"/>): a leaf its converter cannot
        /// read, or a key the contract marks <see cref="RetiredJsonKeysAttribute"/>, is a LOUD error instead of being left for later.</summary>
        public virtual bool RefusesOldForms => false;
    }

    /// <summary>⭐ Old → new network ids (scenario load, D3): every <see cref="EntityRef"/>, a link's answer, an area entity.</summary>
    public sealed class RefRemap : LeafApplier
    {
        private readonly IReadOnlyDictionary<int, int> _map;
        public RefRemap(IReadOnlyDictionary<int, int> oldToNew) => _map = oldToNew ?? throw new ArgumentNullException(nameof(oldToNew));
        public override EntityRef Ref(EntityRef r) => r.Remap(_map);
        public override EntityLink Link(EntityLink l) => l.Ref.IsNone ? l : l.WithRef(l.Ref.Remap(_map));
        public override AreaLink Area(AreaLink a) => a.IsShape || a.Area.IsNone ? a : a.WithArea(a.Area.Remap(_map));
    }

    /// <summary>What resolution needs from the world it resolves in (a scenario's entities while it loads — S0 item 3; a macro scope later).</summary>
    public interface IRefScope
    {
        /// <summary>The entity a <see cref="RefForm.Key"/> / <see cref="RefForm.Exact"/> spec names here; <c>false</c> when none does.</summary>
        bool TryResolve(in RefSpec spec, out EntityRef entity);
        /// <summary>An entity's position (a <see cref="PointLink"/> written as a reference takes it); <c>false</c> when it has none.</summary>
        bool TryPositionOf(EntityRef entity, out WorldPoint position);
        /// <summary>A name for the scope in a refusal (<c>"scenario 'hill-attack'"</c>).</summary>
        string Describe { get; }
    }

    /// <summary>
    /// ⭐ RESOLVE (SR-126 / SR-130): every authored <c>@Key</c> / GUID becomes its answer — an <see cref="EntityLink"/>'s entity, a
    /// <see cref="PointLink"/>'s position (a snapshot; it does not follow the entity), an <see cref="AreaLink"/>'s area entity. The
    /// authored form is KEPT. ⛔ A reference that names nothing here is a LOUD error: <c>no entity with key 'X' here</c>.
    /// </summary>
    public sealed class RefResolve : LeafApplier
    {
        private readonly IRefScope _scope;
        public RefResolve(IRefScope scope) => _scope = scope ?? throw new ArgumentNullException(nameof(scope));

        /// <inheritdoc/>
        public override bool RefusesOldForms => true;

        private EntityRef Find(in RefSpec spec)
            => _scope.TryResolve(spec, out var r) && !r.IsNone ? r
             : throw new InvalidOperationException(spec.Form == RefForm.Key
                   ? $"no entity with key '{spec.KeyText}' here ({_scope.Describe})"
                   : $"no entity with GUID {spec.Exact:D} here ({_scope.Describe})");

        public override EntityLink Link(EntityLink l)
            => l.Spec.Form is RefForm.Key or RefForm.Exact ? l.WithRef(Find(l.Spec)) : l;

        public override PointLink PointLink(PointLink p)
        {
            if (p.Spec.Form is not (RefForm.Key or RefForm.Exact)) return p;
            var e = Find(p.Spec);
            return _scope.TryPositionOf(e, out var at) ? p.WithLocal(at)
                 : throw new InvalidOperationException($"the point '{p.Spec}' names an entity with no position ({_scope.Describe})");
        }

        public override AreaLink Area(AreaLink a)
            => a.Spec.Form is RefForm.Key or RefForm.Exact ? a.WithArea(Find(a.Spec)) : a;
    }

    /// <summary>
    /// ⭐ RELOCATE (SR-127): moves every LITERAL place from the authored anchor to the instance anchor — translate X / Y, turn about the
    /// anchor by the heading difference (compass, clockwise), Z by the anchors' height difference. ⭐ A REFERENCE never moves (a
    /// <see cref="PointLink"/> / <see cref="AreaLink"/> written as an entity is resolved, not relocated — SR-131). Orientations turn,
    /// bearings add the heading difference. 📌 Unused by the runtime until S3's expander; S0 proves it with rails.
    /// </summary>
    public sealed class Relocation : LeafApplier
    {
        private readonly WorldPoint _from, _to;
        private readonly float _turnDeg, _cos, _sin;
        private readonly Quaternion _turn;

        /// <param name="from">The authored anchor (where the content was built) and its heading.</param>
        /// <param name="to">The instance anchor and its heading.</param>
        public Relocation(WorldPoint from, Bearing fromHeading, WorldPoint to, Bearing toHeading)
        {
            _from = from; _to = to;
            _turnDeg = toHeading.Degrees - fromHeading.Degrees;
            float rad = _turnDeg * (MathF.PI / 180f);
            _cos = MathF.Cos(rad); _sin = MathF.Sin(rad);
            _turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -rad);   // compass clockwise = mathematically negative about +Z (up)
        }

        /// <summary>The heading difference applied, compass degrees.</summary>
        public float TurnDeg => _turnDeg;

        public override WorldPoint Point(WorldPoint p)
        {
            float dx = p.X - _from.X, dy = p.Y - _from.Y;
            // clockwise by the turn: east → south for +90
            return new WorldPoint(_to.X + dx * _cos + dy * _sin, _to.Y - dx * _sin + dy * _cos, p.Z + (_to.Z - _from.Z));
        }

        public override PointLink PointLink(PointLink p) => p.Spec.IsNone ? p.WithLocal(Point(p.Local)) : p;

        public override AreaLink Area(AreaLink a) => a.IsLiteralShape ? a.WithPlacement(Point(a.Center), Bearing(a.Heading)) : a;   // a param-bound shape stays (SR-131)

        public override WorldRotation Rotation(WorldRotation r)
            => ((Quaternion)r).LengthSquared() < 1e-6f ? r : (WorldRotation)Quaternion.Normalize(_turn * (Quaternion)r);

        public override Bearing Bearing(Bearing b) => new Bearing(b.Degrees + _turnDeg).Normalized;
    }

    /// <summary>
    /// ⛔ CE-4153 S10 — keys an OLD form of this params contract used (a renamed field: <c>targetLat</c> → <c>target</c>). An applier that
    /// refuses old forms (<see cref="RefResolve"/>, scenario load) fails on them, naming the key; every other read ignores them as any unknown
    /// key. ⭐ The migration (<c>hrot-content-migrate</c>) is what rewrites them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public sealed class RetiredJsonKeysAttribute : Attribute
    {
        public RetiredJsonKeysAttribute(params string[] keys) => Keys = keys ?? Array.Empty<string>();
        public string[] Keys { get; }
    }
}
