using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Scenario
{
    /// <summary>
    /// ⭐⭐ <b>R-1 (AR-5, first use) — ONE walker over a component's JSON by a dotted path.</b>
    ///
    /// <para>📄 <c>docs/UX/DESIGN_Review_Perspective.md</c> §6.13a (INVENTORY: the three copies — <c>ParamBind.FindKey</c> + <c>SetPath</c>, <c>ScenarioVariants.FindKey</c>,
    /// <c>ScenarioVerification.At</c>). The rules are theirs, copied, not invented: a key matches <b>ignoring case</b> (a client spells <c>position.x</c> or <c>Position.X</c>), a
    /// part that is a <b>number</b> indexes an array (<c>Weapons.0.Ammo</c>) — and on an object it is just a key (<c>{"Ammo":{"2":5}}</c>).</para>
    ///
    /// <para>⛔ R-1 only ADDS <see cref="Get"/> (the debug API's raw field read). ⏳ R-3 replaces the three copies' walks with this and adds <c>Set</c>
    /// (the param binder's "no index on write" rule stays its own check).</para>
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

        private static JsonNode? ValueOf(JsonObject o, string name)
        {
            if (o.TryGetPropertyValue(name, out var exact)) return exact;
            foreach (var (k, v) in o) if (k.Equals(name, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }
    }
}
