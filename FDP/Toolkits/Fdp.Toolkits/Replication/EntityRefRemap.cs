using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Fdp.Toolkit.Replication
{
    /// <summary>
    /// ⭐⭐ Rewrites every <see cref="EntityRef"/> inside a value by an old→new network-id map — the ONE remap of authored
    /// entity references at scenario load. 📄 <c>docs/blueprints/DESIGN_Entity_Reference.md</c> D3.
    /// <para>
    /// The PLAN is a property of the TYPE, built once per type: members of type <see cref="EntityRef"/>, arrays and lists of
    /// it, and nested classes, structs, arrays and lists that contain it. Two appliers share it:
    /// <list type="bullet">
    ///   <item><see cref="CompileJson"/> — a behaviour's params JSON, rewritten IN PLACE (the original string comes back when
    ///     nothing changed, so an absent key is never written back as 0 over a declared default — S8o);</item>
    ///   <item><see cref="RemapObject"/> — an in-memory component (the scenario genesis intents), mutated in place.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class EntityRefRemap
    {
        // ── the plan ──────────────────────────────────────────────────────────────────────────────────────────────

        private enum Kind : byte { Ref, RefSequence, Nested, NestedSequence }

        private sealed class Entry
        {
            public Kind Kind;
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
        }

        private static readonly ConditionalWeakTable<Type, StrongBox<Plan?>> _plans = new();
        private static readonly object _gate = new();

        /// <summary><c>true</c> when values of <paramref name="type"/> can hold an <see cref="EntityRef"/> anywhere inside.</summary>
        public static bool HoldsRefs(Type type) => type == typeof(EntityRef) || IsRefSequence(type) || PlanOf(type) != null;

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
                return plan;
            }
            finally { visiting.Remove(type); }
        }

        private static void Add(ref Plan? plan, MemberInfo member, Type memberType, HashSet<Type> visiting)
        {
            Entry? e = null;
            if (memberType == typeof(EntityRef))
                e = new Entry { Kind = Kind.Ref };
            else if (IsRefSequence(memberType))
                e = new Entry { Kind = Kind.RefSequence };
            else if (ElementType(memberType) is { } elem && (Existing(elem, visiting) ?? Build(elem, visiting)) is { } elemPlan)
                e = new Entry { Kind = Kind.NestedSequence, Nested = elemPlan };
            else if ((Existing(memberType, visiting) ?? Build(memberType, visiting)) is { } nested)
                e = new Entry { Kind = Kind.Nested, Nested = nested };
            if (e is null) return;

            e.Member      = member;
            e.MemberType  = memberType;
            e.JsonKey     = member.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: true)?.Name ?? member.Name;
            e.JsonIgnored = member.IsDefined(typeof(JsonIgnoreAttribute), inherit: true);
            (plan ??= new Plan()).Entries.Add(e);
        }

        /// <summary>An already-built plan (outside a cycle), so a shared nested type is planned once.</summary>
        private static Plan? Existing(Type type, HashSet<Type> visiting)
            => !visiting.Contains(type) && _plans.TryGetValue(type, out var box) ? box.Value : null;

        private static bool IsRefSequence(Type t) => ElementType(t) == typeof(EntityRef);

        /// <summary>The element type of an array or a generic list/collection, else <c>null</c>.</summary>
        private static Type? ElementType(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && typeof(IList).IsAssignableFrom(t)) return t.GetGenericArguments()[0];
            return null;
        }

        /// <summary>A user class or struct that can carry references in its members — not a primitive or a framework type.</summary>
        private static bool IsWalkable(Type type)
            => !type.IsPrimitive && !type.IsEnum && !type.IsPointer && !type.IsArray && type != typeof(string)
               && type != typeof(decimal) && !type.IsGenericTypeDefinition && type != typeof(EntityRef)
               && type.Namespace != "System"
               && !(type.Namespace?.StartsWith("System.", StringComparison.Ordinal) ?? false);

        // ── the JSON applier ──────────────────────────────────────────────────────────────────────────────────────

        private static readonly ConditionalWeakTable<Type, Func<string?, IReadOnlyDictionary<long, long>, string?>> _json = new();
        private static readonly Func<string?, IReadOnlyDictionary<long, long>, string?> _identity = (json, _) => json;

        private static readonly System.Text.Json.JsonSerializerOptions _writeOptions = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Number of JSON delegates built (a cache probe for tests).</summary>
        internal static int JsonCompileCount { get; private set; }

        /// <summary>
        /// The cached delegate that rewrites every <see cref="EntityRef"/> in <paramref name="contractType"/>'s JSON.
        /// <c>null</c>, empty, malformed or non-object JSON, and JSON with no id in the map, come back as the SAME string.
        /// </summary>
        public static Func<string?, IReadOnlyDictionary<long, long>, string?> CompileJson(Type contractType)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (_json.TryGetValue(contractType, out var cached)) return cached;
            lock (_gate)
            {
                if (_json.TryGetValue(contractType, out cached)) return cached;
                JsonCompileCount++;
                var plan  = PlanOf(contractType);
                var built = plan is null ? _identity : (json, map) => RemapJson(json, map, plan);
                _json.Add(contractType, built);
                return built;
            }
        }

        private static string? RemapJson(string? json, IReadOnlyDictionary<long, long> map, Plan plan)
        {
            if (string.IsNullOrEmpty(json) || map.Count == 0) return json;
            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (System.Text.Json.JsonException) { return json; }   // malformed: the parser downstream reports it
            if (root is not JsonObject obj) return json;
            return WalkObject(obj, map, plan) ? root.ToJsonString(_writeOptions) : json;
        }

        private static Dictionary<string, Entry> Keys(Plan plan)
        {
            if (plan.ByJsonKey != null) return plan.ByJsonKey;
            var d = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in plan.Entries) if (!e.JsonIgnored) d[e.JsonKey] = e;
            return plan.ByJsonKey = d;
        }

        private static bool WalkObject(JsonObject obj, IReadOnlyDictionary<long, long> map, Plan plan)
        {
            var keys = Keys(plan);
            bool changed = false;
            List<(string Key, long Id)>? rewrites = null;
            foreach (var (key, node) in obj)
            {
                if (node is null || !keys.TryGetValue(key, out var e)) continue;
                switch (e.Kind)
                {
                    case Kind.Ref:
                        if (TryRemapNumber(node, map, out long id)) (rewrites ??= new()).Add((key, id));
                        break;
                    case Kind.RefSequence when node is JsonArray arr:
                        for (int i = 0; i < arr.Count; i++)
                            if (arr[i] is { } item && TryRemapNumber(item, map, out long nid)) { arr[i] = nid; changed = true; }
                        break;
                    case Kind.Nested when node is JsonObject child:
                        changed |= WalkObject(child, map, e.Nested!);
                        break;
                    case Kind.NestedSequence when node is JsonArray seq:
                        foreach (var item in seq)
                            if (item is JsonObject o) changed |= WalkObject(o, map, e.Nested!);
                        break;
                }
            }
            if (rewrites is not null)
            {
                foreach (var (key, id) in rewrites) obj[key] = id;
                changed = true;
            }
            return changed;
        }

        private static bool TryRemapNumber(JsonNode node, IReadOnlyDictionary<long, long> map, out long newId)
        {
            newId = 0;
            return node is JsonValue v && v.TryGetValue(out long oldId) && oldId != 0
                && map.TryGetValue(oldId, out newId) && newId != oldId;
        }

        // ── the object applier ────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Rewrites, in place, every <see cref="EntityRef"/> inside <paramref name="value"/> (a class instance, or a BOXED
        /// struct — mutations land in the box). Returns <c>true</c> when any changed.
        /// </summary>
        public static bool RemapObject(object? value, IReadOnlyDictionary<long, long> map)
        {
            if (value is null || map.Count == 0) return false;
            var plan = PlanOf(value.GetType());
            return plan != null && Apply(value, map, plan);
        }

        private static bool Apply(object target, IReadOnlyDictionary<long, long> map, Plan plan)
        {
            bool changed = false;
            foreach (var e in plan.Entries)
            {
                object? v = Get(e.Member, target);
                if (v is null) continue;
                switch (e.Kind)
                {
                    case Kind.Ref:
                        var r = (EntityRef)v;
                        var nr = r.Remap(map);
                        if (nr != r && TrySet(e.Member, target, nr)) changed = true;
                        break;
                    case Kind.RefSequence:
                        var list = (IList)v;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var item = (EntityRef)list[i]!;
                            var ni = item.Remap(map);
                            if (ni != item) { list[i] = ni; changed = true; }
                        }
                        break;
                    case Kind.Nested:
                        // A struct comes back BOXED: rewrite the box, then store it back into the member.
                        if (Apply(v, map, e.Nested!) && (!e.MemberType.IsValueType || TrySet(e.Member, target, v))) changed = true;
                        break;
                    case Kind.NestedSequence:
                        var seq = (IList)v;
                        for (int i = 0; i < seq.Count; i++)
                        {
                            var item = seq[i];
                            if (item is null || !Apply(item, map, e.Nested!)) continue;
                            if (item.GetType().IsValueType) seq[i] = item;   // store the rewritten box back
                            changed = true;
                        }
                        break;
                }
            }
            return changed;
        }

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
}
