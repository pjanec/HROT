using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core.Serialization;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ <b>R-1 (AR-5, first use) — ONE walker over a component's JSON by a dotted path.</b>
    ///
    /// <para>📄 <c>docs/UX/DESIGN_Review_Perspective.md</c> §6.13a (INVENTORY: the three copies — <c>ParamBind.FindKey</c> + <c>SetPath</c>, <c>ScenarioVariants.FindKey</c>,
    /// <c>ScenarioVerification.At</c>). The rules are theirs, copied, not invented: a key matches <b>ignoring case</b> (a client spells <c>position.x</c> or <c>Position.X</c>), a
    /// part that is a <b>number</b> indexes an array (<c>Weapons.0.Ammo</c>) — and on an object it is just a key (<c>{"Ammo":{"2":5}}</c>).</para>
    ///
    /// <para>⭐ R-3 — <see cref="FindKey"/> + <see cref="Set"/> replace the other copies' walks; the param binder's "no index on write" and "&lt;Component&gt;.&lt;field&gt;" rules stay ITS checks, made before it calls <see cref="Set"/>.</para>
    /// </summary>
    public static class ComponentJsonPath
    {
        /// <summary>
        /// The value at <paramref name="path"/> (<c>A.B.0.C</c>) under <paramref name="node"/>, or <see langword="null"/> when any part is missing. An empty path is
        /// <paramref name="node"/> itself. The returned node is the live one (clone it before attaching it to another tree).
        /// </summary>
        public static JsonNode? Get(JsonNode? node, string? path)
        {
            if (node is null) return null;
            if (string.IsNullOrEmpty(path)) return node;
            foreach (var part in path.Split('.'))
            {
                node = node switch
                {
                    JsonObject o => ValueOf(o, part),
                    JsonArray a when int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < a.Count => a[i],
                    _ => null,
                };
                if (node is null) return null;
            }
            return node;
        }

        /// <summary>The key of <paramref name="o"/> that equals <paramref name="name"/> ignoring case (the exact spelling first), or <see langword="null"/>.</summary>
        public static string? FindKey(JsonObject o, string name)
        {
            if (o.ContainsKey(name)) return name;
            foreach (var (k, _) in o) if (k.Equals(name, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }

        /// <summary>What <see cref="Set"/> did.</summary>
        public enum SetOutcome { Changed, Unchanged, MissingStep, NotAnObject }

        /// <summary>
        /// Writes <paramref name="value"/> (cloned) at <paramref name="path"/> (<c>A.B.C</c>) under <paramref name="root"/>: keys match ignoring case, the LAST key is created when absent, a
        /// step through a JSON <b>string holding an object</b> (a brain slot's <c>Params</c> blob) is walked and the string written back. Every step is an OBJECT key (no array index on write).
        /// <paramref name="failedPart"/> is the index of the path part that stopped it for <see cref="SetOutcome.MissingStep"/> / <see cref="SetOutcome.NotAnObject"/>, else -1.
        /// </summary>
        public static SetOutcome Set(JsonObject root, string path, JsonNode? value, out int failedPart)
            => SetIn(root, path.Split('.'), 0, value, out failedPart);

        private static SetOutcome SetIn(JsonObject obj, string[] parts, int i, JsonNode? value, out int failedPart)
        {
            failedPart = -1;
            var key = FindKey(obj, parts[i]);
            if (i == parts.Length - 1)
            {
                key ??= parts[i];
                if (JsonNode.DeepEquals(obj[key], value)) return SetOutcome.Unchanged;
                obj[key] = value?.DeepClone();
                return SetOutcome.Changed;
            }
            if (key is null) { failedPart = i; return SetOutcome.MissingStep; }
            switch (obj[key])
            {
                case JsonObject child:
                    return SetIn(child, parts, i + 1, value, out failedPart);
                case JsonValue s when s.TryGetValue<string>(out var text) && TryParseObject(text, out var inner):
                    var r = SetIn(inner, parts, i + 1, value, out failedPart);
                    if (r == SetOutcome.Changed) obj[key] = inner.ToJsonString(FdpJsonOptionsRegistry.DefaultRelaxed);
                    return r;
                default:
                    failedPart = i;
                    return SetOutcome.NotAnObject;
            }
        }

        private static bool TryParseObject(string text, out JsonObject obj)
        {
            obj = null!;
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart()[0] != '{') return false;
            try { obj = JsonNode.Parse(text) as JsonObject ?? null!; } catch (JsonException) { return false; }
            return obj is not null;
        }

        private static JsonNode? ValueOf(JsonObject o, string name)
        {
            if (o.TryGetPropertyValue(name, out var exact)) return exact;
            return FindKey(o, name) is { } k ? o[k] : null;
        }
    }
}
