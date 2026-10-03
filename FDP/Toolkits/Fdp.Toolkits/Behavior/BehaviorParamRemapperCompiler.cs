using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Toolkit.Behavior.Attributes;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// Compiles and caches a remapping delegate for a behavior-param contract TYPE.
    ///
    /// <para>
    /// The returned delegate replaces every network identifier in the contract's JSON according to a caller-supplied
    /// old-to-new ID map. A member is an identifier when it is a <c>long</c> or <c>int</c> field or property tagged
    /// <see cref="RemapNetworkIdAttribute"/>; a member whose own type holds identifiers is walked as a nested object.
    /// Everything else is left exactly as authored.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <c>CE-2054</c> (S8o, <c>DESIGN_Unified_Behaviour_Run.md</c>) — <b>the plan is a property of the TYPE, so it
    /// nests.</b> A blueprint behaviour's contract is its generated <c>Params</c> struct, whose FIELDS are curated
    /// contracts (<c>Fire : FireAtTargetParamsJsonDto</c>); the id lives one object down. ⛔ The previous compiler read
    /// only top-level properties and round-tripped the whole DTO through the serializer — on a generated <c>Params</c>
    /// that writes every member back, so an absent key would arrive as <c>0</c> and override the Parameter's declared
    /// default. ⭐ The walk rewrites the JSON IN PLACE and returns the ORIGINAL string when no id changed.
    /// </para>
    ///
    /// <para>Reflection runs once per type, in <see cref="CompileFor"/>; the delegate only walks JSON.</para>
    /// </summary>
    public static class BehaviorParamRemapperCompiler
    {
        // ⭐ Weak per type: a contract type from a collectible (hot-reload) load context must not be pinned by this cache.
        private static readonly ConditionalWeakTable<Type, Func<string?, Dictionary<long, long>, string?>> _cache = new();
        private static readonly object _gate = new();

        /// <summary>Writes untouched strings back as authored rather than \u-escaping every non-ASCII character.</summary>
        private static readonly System.Text.Json.JsonSerializerOptions _writeOptions = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>The identity delegate, shared by every type that carries no identifier.</summary>
        private static readonly Func<string?, Dictionary<long, long>, string?> _identity = (json, _) => json;

        /// <summary>
        /// Number of times a delegate was built. A value of 1 after N calls to <see cref="Compile{TDto}"/> with the same
        /// type confirms that caching is effective.
        /// </summary>
        internal static int CompileCallCount { get; private set; }

        /// <summary>
        /// Returns a cached <c>Func&lt;string?, Dictionary&lt;long, long&gt;, string?&gt;</c> that remaps every
        /// network identifier in <typeparamref name="TDto"/>'s JSON (see <see cref="CompileFor"/>).
        /// </summary>
        /// <typeparam name="TDto">A JSON contract (class or struct) with a public parameterless constructor.</typeparam>
        public static Func<string?, Dictionary<long, long>, string?> Compile<TDto>()
            where TDto : new()   // ⭐ CE-2023 ③ — a class or a STRUCT contract ("S8n")
            => CompileFor(typeof(TDto));

        /// <summary>
        /// ⭐ <c>CE-2054</c> — the remap delegate for a contract <paramref name="contractType"/> known only at run time
        /// (a behaviour's <c>JsonParamsDtoType</c>).
        /// <list type="bullet">
        ///   <item>A type with no identifier anywhere gets the identity delegate <c>(json, _) =&gt; json</c>.</item>
        ///   <item><c>null</c>, empty, malformed or non-object JSON is returned unchanged.</item>
        ///   <item>An identifier absent from the map is left unchanged; when nothing changes, the SAME string is
        ///     returned.</item>
        /// </list>
        /// </summary>
        public static Func<string?, Dictionary<long, long>, string?> CompileFor(Type contractType)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));

            if (_cache.TryGetValue(contractType, out var cached)) return cached;
            lock (_gate)
            {
                if (_cache.TryGetValue(contractType, out cached)) return cached;
                CompileCallCount++;
                var plan = PlanFor(contractType, new HashSet<Type>());
                var built = plan is null ? _identity : (json, map) => Remap(json, map, plan);
                _cache.Add(contractType, built);
                return built;
            }
        }

        // ── the plan ──────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>One JSON key of a contract: an identifier, or an object whose own plan holds identifiers.</summary>
        private sealed record Entry(bool IsInt, Dictionary<string, Entry>? Nested);

        /// <summary>
        /// The JSON keys of <paramref name="type"/> that hold identifiers, or <c>null</c> when none do. Keys compare
        /// case-insensitively, as the parsers read them (<c>PropertyNameCaseInsensitive</c>, the emitted overlay).
        /// </summary>
        private static Dictionary<string, Entry>? PlanFor(Type type, HashSet<Type> visiting)
        {
            if (!IsWalkable(type) || !visiting.Add(type)) return null;
            try
            {
                Dictionary<string, Entry>? plan = null;
                const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;

                foreach (var p in type.GetProperties(Public))
                    if (p.CanRead && p.GetIndexParameters().Length == 0)
                        Add(ref plan, p, p.PropertyType, visiting);
                foreach (var f in type.GetFields(Public))
                    Add(ref plan, f, f.FieldType, visiting);

                return plan;
            }
            finally
            {
                visiting.Remove(type);
            }
        }

        private static void Add(ref Dictionary<string, Entry>? plan, MemberInfo member, Type memberType, HashSet<Type> visiting)
        {
            if (member.IsDefined(typeof(JsonIgnoreAttribute), inherit: true)) return;

            Entry? entry = null;
            if (member.IsDefined(typeof(RemapNetworkIdAttribute), inherit: true)
                && (memberType == typeof(long) || memberType == typeof(int)))
            {
                entry = new Entry(memberType == typeof(int), null);
            }
            else if (PlanFor(memberType, visiting) is { } nested)
            {
                entry = new Entry(false, nested);
            }
            if (entry is null) return;

            string key = member.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: true)?.Name ?? member.Name;
            plan ??= new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            plan[key] = entry;
        }

        /// <summary>A type that can carry identifiers in its members: a user class or struct, not a primitive or a
        /// framework type.</summary>
        private static bool IsWalkable(Type type)
            => !type.IsPrimitive && !type.IsEnum && !type.IsPointer && !type.IsArray && type != typeof(string)
               && type != typeof(decimal) && !type.IsGenericTypeDefinition
               && type.Namespace != "System"
               && !(type.Namespace?.StartsWith("System.", StringComparison.Ordinal) ?? false);

        // ── the walk ──────────────────────────────────────────────────────────────────────────────────────────────

        private static string? Remap(string? json, Dictionary<long, long> map, Dictionary<string, Entry> plan)
        {
            if (string.IsNullOrEmpty(json) || map.Count == 0) return json;

            JsonNode? root;
            try { root = JsonNode.Parse(json); }
            catch (System.Text.Json.JsonException) { return json; }   // malformed: the parser downstream reports it

            if (root is not JsonObject obj) return json;
            return Walk(obj, map, plan) ? root.ToJsonString(_writeOptions) : json;
        }

        /// <summary>Rewrites the identifiers of <paramref name="obj"/> in place; <c>true</c> when any changed.</summary>
        private static bool Walk(JsonObject obj, Dictionary<long, long> map, Dictionary<string, Entry> plan)
        {
            bool changed = false;
            List<(string Key, long Value)>? rewrites = null;

            foreach (var (key, node) in obj)
            {
                if (node is null || !plan.TryGetValue(key, out var entry)) continue;

                if (entry.Nested is { } nested)
                {
                    if (node is JsonObject child) changed |= Walk(child, map, nested);
                }
                else if (node is JsonValue v && v.TryGetValue(out long oldId) && map.TryGetValue(oldId, out long newId)
                         && newId != oldId)
                {
                    (rewrites ??= new()).Add((key, entry.IsInt ? (int)newId : newId));
                }
            }

            if (rewrites is not null)
            {
                foreach (var (key, value) in rewrites) obj[key] = value;
                changed = true;
            }
            return changed;
        }
    }
}
