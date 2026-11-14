using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Replication;

namespace Fdp.Toolkit.Scenario
{
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐ THE ONE PARAM BINDER — docs/DESIGN_Scenario_Macros.md §5.1 (SR-159 … SR-163), §5.1.2 (SR-164 … SR-166), §14 SR-168.
    //
    // A binding lives where the TYPED round-trip carries it — three homes, one per kind of value:
    //   ① a reference / place value (EntityLink, PointLink, AreaLink) — RefSpec.Form = Param, JSON "^area"   (ParamBind on the S0 walker)
    //   ② a node of a managed DTO tree (a predicate node, a do entry) — its typed Bind map (BindableNode)  (the walker's Node hook)
    //   ③ a scalar / enum field of a component or a params struct — the entity's ParamBindings component    (ApplyBindings, by path)
    // ⭐ It runs AFTER deserialising, on the typed value (or the authored JSON), with ANY host's IParamSource (the macro expander, a
    //   scenario load, later a film take …) — ⛔ never a second binder (SR-164).
    // ⭐ NON-DESTRUCTIVE (SR-168): it fills the live value and KEEPS the binding — a "^area" keeps its form, a Bind stays on its node,
    //   ParamBindings stays on the entity — so a save writes the current value PLUS the binding, and the binding wins on the next load.
    //   Only a TRANSIENT macro child (never saved) has its ParamBindings dropped — by the expander, not here.
    // ════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ SR-164 — where a binding's value comes from: the macro instance's args over its header defaults, a scenario's params, …
    /// <see cref="TryGet"/> returns the param's value as <paramref name="target"/> (an <see cref="EntityLink"/> already RESOLVED in the
    /// scope outside the macro, a <see cref="float"/>, an enum, or the raw <see cref="JsonNode"/>); <c>false</c> when there is no such param.
    /// A value that cannot be read as <paramref name="target"/> throws, naming the param.
    /// </summary>
    public interface IParamSource
    {
        bool TryGet(string name, Type target, out object? value);

        /// <summary>A name for refusals (<c>"macro 'forces/RedQRF'"</c>).</summary>
        string Describe => "params";
    }

    /// <summary>
    /// ⭐ The JSON-backed source every host can use: name → value JSON (the header's defaults overlaid by the instance's args — see
    /// <see cref="Overlay"/>, the flat key-by-key overlay of SR-28). A reference value (<c>"@Village"</c>) is resolved by
    /// <paramref name="resolveOutside"/> — the <see cref="RefResolve"/> of the scope OUTSIDE the macro (SR-58: anything outside comes in
    /// through params) — before it is handed to the binding.
    /// </summary>
    public sealed class JsonParamSource : IParamSource
    {
        private static readonly JsonSerializerOptions _options = FdpJsonOptionsRegistry.DefaultRelaxed;
        private readonly IReadOnlyDictionary<string, JsonNode?> _values;
        private readonly LeafApplier? _resolveOutside;

        public JsonParamSource(IReadOnlyDictionary<string, JsonNode?> values, LeafApplier? resolveOutside = null, string describe = "params")
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
            _resolveOutside = resolveOutside;
            Describe = describe;
        }

        public string Describe { get; }

        /// <summary>The flat overlay (SR-28): every default, then every arg by key (case-insensitive), the arg winning. ⛔ No deep merge.</summary>
        public static Dictionary<string, JsonNode?> Overlay(IEnumerable<KeyValuePair<string, JsonNode?>> defaults, IEnumerable<KeyValuePair<string, JsonNode?>>? args)
        {
            var d = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in defaults) d[k] = v?.DeepClone();
            if (args != null) foreach (var (k, v) in args) d[k] = v?.DeepClone();
            return d;
        }

        public bool TryGet(string name, Type target, out object? value)
        {
            value = null;
            if (!_values.TryGetValue(name, out var node)) return false;
            if (target == typeof(JsonNode)) { value = node?.DeepClone(); return true; }
            try { value = node is null ? null : node.Deserialize(target, _options); }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
            { throw new InvalidOperationException($"the param '{name}' ({node?.ToJsonString()}) is not a {target.Name} ({Describe}): {ex.Message}", ex); }
            if (value is not null && _resolveOutside is not null) value = EntityRefRemap.ApplyTo(value, _resolveOutside);
            return true;
        }
    }

    /// <summary>
    /// ⭐⭐ SR-160 — the SHARED BASE of every node of a managed DTO tree that may bind a property to a param: a predicate node
    /// (<c>SearchPredicateDto</c>), a trigger's <c>do</c> entry. <see cref="Bind"/> maps THIS node's own property names to param names
    /// (<c>{"Min": "minDelay"}</c>) — so moving or reordering a node moves its bindings with it. The editor of the node writes it with the
    /// node; the binder applies it (the walker visits every node by its RUNTIME type, at any depth).
    /// </summary>
    public abstract class BindableNode
    {
        /// <summary>Property name (of this node) → param name. <c>null</c> when nothing is bound (and then not written to JSON).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? Bind { get; set; }
    }

    /// <summary>
    /// ⭐ SR-161 / SR-57 — the bindings of an entity's SCALAR fields (a component field, a params-struct field) the value type cannot
    /// carry: <c>"&lt;Component&gt;.&lt;field path&gt;" → param</c> (<c>"Behavior.Params.Speed": "speed"</c>, <c>"EntityInfo.ForceId": "side"</c>).
    /// Paths are by NAME — ⛔ an index in a path is refused. The params form and the component editor READ it to draw the chip and
    /// WRITE it on Bind / Unbind; they never put a binding into the params JSON.
    /// ⭐ Managed, immutable (CLAUDE.md "NEW COMPONENTS"): an edit builds a new instance. <c>NoReplay</c> (SR-165): an AUTHORING
    /// instruction — never recorded, never in a checkpoint. Saved by <c>ParamBindingsTranslator</c>.
    /// </summary>
    [ComponentId(GlobalComponentIds.ParamBindings)]
    [DataPolicy(DataPolicy.NoReplay)]
    public sealed record ParamBindings
    {
        /// <summary>The scenario-file component name.</summary>
        public const string ComponentName = "ParamBindings";

        public IReadOnlyDictionary<string, string> Bindings { get; init; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// ⭐ SR-159 / SR-160 — the binding OPERATION on the S0 walker: every <c>"^param"</c> leaf takes the param's value (form kept), every
    /// <see cref="BindableNode"/>'s <see cref="BindableNode.Bind"/> sets its properties. ⛔ A missing param is LOUD, naming it.
    /// </summary>
    public sealed class ParamBind : LeafApplier
    {
        private static readonly JsonSerializerOptions _options = FdpJsonOptionsRegistry.DefaultRelaxed;
        private readonly IParamSource _source;

        public ParamBind(IParamSource source) => _source = source ?? throw new ArgumentNullException(nameof(source));

        private object? Get(string name, Type target)
            => _source.TryGet(name, target, out var v) ? v
             : throw new InvalidOperationException($"no param '{name}' here ({_source.Describe})");

        private T GetValue<T>(string name) where T : struct
            => Get(name, typeof(T)) is T t ? t : default;

        public override EntityLink Link(EntityLink l)
            => l.Spec.Form == RefForm.Param ? l.BoundTo(GetValue<EntityLink>(l.Spec.ParamName)) : l;

        public override PointLink PointLink(PointLink p)
            => p.Spec.Form == RefForm.Param ? p.BoundTo(GetValue<PointLink>(p.Spec.ParamName)) : p;

        public override AreaLink Area(AreaLink a)
            => a.Spec.Form == RefForm.Param ? a.BoundTo(GetValue<AreaLink>(a.Spec.ParamName)) : a;

        public override bool Node(BindableNode node)
        {
            if (node.Bind is not { Count: > 0 } bind) return false;
            bool changed = false;
            var type = node.GetType();
            foreach (var (prop, param) in bind)
            {
                if (prop.IndexOf('.') > 0) { changed |= BindPathIn(node, type, prop, param); continue; }
                var p = PropertyOf(type, prop);
                var v = Get(param, p.PropertyType);
                if (Equals(p.GetValue(node), v)) continue;
                p.SetValue(node, v);   // a freshly deserialised node (an init-only member too): construction, not a live edit
                changed = true;
            }
            return changed;
        }

        public override bool NodeJson(Type nodeType, JsonObject obj)
        {
            if (ComponentJsonPath.FindKey(obj, nameof(BindableNode.Bind)) is not { } bindKey || obj[bindKey] is not JsonObject bind || bind.Count == 0) return false;
            bool changed = false;
            foreach (var (prop, paramNode) in bind)
            {
                if (paramNode is not JsonValue pv || !pv.TryGetValue<string>(out var param))
                    throw new InvalidOperationException($"'{nodeType.Name}.Bind.{prop}' must name a param (a string), saw {paramNode?.ToJsonString() ?? "null"}");
                if (prop.IndexOf('.') > 0)
                {
                    int dot = prop.IndexOf('.');
                    var holder = PropertyOf(nodeType, prop.Substring(0, dot));
                    var hkey = ComponentJsonPath.FindKey(obj, JsonNameOf(holder)) ?? JsonNameOf(holder);
                    if (obj[hkey] is not JsonObject inner) throw new InvalidOperationException($"'{nodeType.Name}.Bind.{prop}': '{holder.Name}' holds no JSON object to bind into");
                    changed |= SetNested(inner, prop.Substring(dot + 1), param, nodeType.Name, prop);
                    continue;
                }
                var p = PropertyOf(nodeType, prop);
                var key = ComponentJsonPath.FindKey(obj, JsonNameOf(p)) ?? JsonNameOf(p);
                var written = JsonSerializer.SerializeToNode(Get(param, p.PropertyType), p.PropertyType, _options);
                if (JsonNode.DeepEquals(obj[key], written)) continue;
                obj[key] = written;
                changed = true;
            }
            return changed;
        }

        /// <summary>⭐ SR-94 — a Bind key <c>"Args.text"</c>: a dotted PATH into a JSON-object property of the node (a step's args): the param's raw JSON lands there.</summary>
        private bool BindPathIn(BindableNode node, Type type, string path, string param)
        {
            int dot = path.IndexOf('.');
            var holder = PropertyOf(type, path.Substring(0, dot));
            if (!typeof(JsonObject).IsAssignableFrom(holder.PropertyType))
                throw new InvalidOperationException($"'{type.Name}.Bind.{path}': a dotted path goes into a JSON object property, '{holder.Name}' is a {holder.PropertyType.Name}");
            var inner = holder.GetValue(node) as JsonObject;
            if (inner is null) { inner = new JsonObject(); holder.SetValue(node, inner); }
            return SetNested(inner, path.Substring(dot + 1), param, type.Name, path);
        }

        private bool SetNested(JsonObject inner, string rest, string param, string typeName, string path)
        {
            var parts = rest.Split('.');
            var raw = (JsonNode?)Get(param, typeof(JsonNode));
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var k = ComponentJsonPath.FindKey(inner, parts[i]) ?? parts[i];
                if (inner[k] is JsonObject next) inner = next;
                else if (inner[k] is null) { var made = new JsonObject(); inner[k] = made; inner = made; }
                else throw new InvalidOperationException($"'{typeName}.Bind.{path}': '{parts[i]}' is not an object");
            }
            var last = ComponentJsonPath.FindKey(inner, parts[^1]) ?? parts[^1];
            if (JsonNode.DeepEquals(inner[last], raw)) return false;
            inner[last] = raw?.DeepClone();
            return true;
        }

        private static PropertyInfo PropertyOf(Type type, string name)
            => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { CanWrite: true } p
               && p.Name != nameof(BindableNode.Bind) ? p
             : throw new InvalidOperationException($"'{type.Name}' has no settable property '{name}' to bind (SR-160: Bind keys are the node's own property names)");

        private static string JsonNameOf(PropertyInfo p) => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name;
    }

    /// <summary>⭐⭐ SR-164 — the ONE entry point. Every host binds through these three calls; ⛔ none writes its own.</summary>
    public static class ParamBinder
    {
        /// <summary>① + ② on an in-memory value (a component, a params DTO, a predicate tree), in place. <c>true</c> when anything changed.</summary>
        public static bool Apply(object? value, IParamSource source)
            => value is not null && EntityRefRemap.WalkObject(value, new ParamBind(source));

        /// <summary>① + ② on a contract's JSON (a params blob, a component's JSON). The SAME string comes back when nothing changed.</summary>
        public static string? ApplyJson(Type contractType, string? json, IParamSource source)
            => EntityRefRemap.WalkJson(contractType, json, new ParamBind(source));

        /// <summary>
        /// ③ — an authored entity (component name → component JSON, the scenario file's shape): every path of its
        /// <see cref="ParamBindings"/> takes its param's value. A path walks objects by key (case-insensitive) and steps INTO a JSON
        /// string that holds an object (<c>"Behavior.Params.Speed"</c>: the slot's params blob), writing the string back. The
        /// <c>ParamBindings</c> component STAYS (SR-168). ⛔ An index, a missing component or a non-object step is LOUD.
        /// </summary>
        public static bool ApplyBindings(JsonObject entity, IParamSource source)
        {
            if (entity is null) throw new ArgumentNullException(nameof(entity));
            if (ComponentJsonPath.FindKey(entity, ParamBindings.ComponentName) is not { } pbKey
                || entity[pbKey] is not JsonObject pb
                || ComponentJsonPath.FindKey(pb, nameof(ParamBindings.Bindings)) is not { } bKey
                || pb[bKey] is not JsonObject bindings) return false;
            bool changed = false;
            foreach (var (path, paramNode) in bindings)
            {
                if (paramNode is not JsonValue pv || !pv.TryGetValue<string>(out var param))
                    throw new InvalidOperationException($"ParamBindings '{path}' must name a param (a string)");
                if (!source.TryGet(param, typeof(JsonNode), out var raw))
                    throw new InvalidOperationException($"no param '{param}' here ({source.Describe}) — bound by '{path}'");
                changed |= SetPath(entity, path, (JsonNode?)raw);
            }
            return changed;
        }

        /// <summary>The paths an entity's <see cref="ParamBindings"/> names (the validator reads them, SR-162).</summary>
        public static IReadOnlyList<(string Path, string Param)> BindingsOf(JsonObject entity)
        {
            var list = new List<(string, string)>();
            if (ComponentJsonPath.FindKey(entity, ParamBindings.ComponentName) is { } pbKey && entity[pbKey] is JsonObject pb
                && ComponentJsonPath.FindKey(pb, nameof(ParamBindings.Bindings)) is { } bKey && pb[bKey] is JsonObject bindings)
                foreach (var (path, p) in bindings)
                    if (p is JsonValue v && v.TryGetValue<string>(out var s)) list.Add((path, s));
            return list;
        }

        private static bool SetPath(JsonObject entity, string path, JsonNode? value)
        {
            if (path.IndexOf('[') >= 0)
                throw new InvalidOperationException($"ParamBindings '{path}': an index in a path is refused (SR-161 — bind a whole value, not an element)");
            var parts = path.Split('.');
            if (parts.Length < 2) throw new InvalidOperationException($"ParamBindings '{path}': a path is '<Component>.<field>'");
            var outcome = ComponentJsonPath.Set(entity, path, value, out var bad);
            return outcome switch
            {
                ComponentJsonPath.SetOutcome.Changed => true,
                ComponentJsonPath.SetOutcome.Unchanged => false,
                ComponentJsonPath.SetOutcome.MissingStep => throw new InvalidOperationException(bad == 0 ? $"ParamBindings '{path}': the entity has no component '{parts[0]}'"
                                                                                                          : $"ParamBindings '{path}': no '{parts[bad]}' at that path"),
                _ => throw new InvalidOperationException($"ParamBindings '{path}': '{parts[bad]}' is not an object"),
            };
        }
    }
}
