using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-2377</c> — THE walker over a scenario / macro document's behaviour slots: every <c>Behavior</c> and <c>Sop</c> object that
    /// carries a <c>Name</c>, wherever it sits in the entities tree. The scenario load's refusal and the sweep test both call this — no
    /// second walker. ⛔ Pure JSON: no registry, no world.
    /// </summary>
    public static class ScenarioBehaviorSlots
    {
        /// <summary>One behaviour slot: <see cref="Slot"/> is "Behavior" or "Sop"; <see cref="Params"/> is the params as JSON text (null when absent);
        /// <see cref="Entity"/> is the path of keys down to the slot's owner (empty at the top level).</summary>
        public readonly record struct Found(string Slot, string Name, string? Params, string Entity);

        /// <summary>Every slot under <paramref name="scenario"/>, depth first, in document order.</summary>
        public static IEnumerable<Found> Enumerate(JsonNode? scenario) => Walk(scenario, "");

        private static IEnumerable<Found> Walk(JsonNode? node, string entity)
        {
            if (node is JsonObject o)
            {
                foreach (var (key, value) in o)
                {
                    if ((key == "Behavior" || key == "Sop") && value is JsonObject slot && slot["Name"] is JsonValue nv && nv.TryGetValue<string>(out var name))
                    {
                        string? p = slot["Params"] switch { JsonValue pv when pv.TryGetValue<string>(out var s) => s, null => null, var other => other.ToJsonString() };
                        yield return new Found(key, name, p, entity);
                    }
                    else
                        foreach (var r in Walk(value, entity.Length == 0 && node.Parent is null && key == "entities" ? "" : entity.Length == 0 ? key : entity + "/" + key)) yield return r;
                }
            }
            else if (node is JsonArray a)
                foreach (var v in a) foreach (var r in Walk(v, entity)) yield return r;
        }

        /// <summary>⚠ A params blob with a param binding (<c>"^name"</c>, or a <c>Bind</c> map — macros SR-41/SR-161) is only whole AFTER the
        /// load applies the args, so its keys and values cannot be checked here: only the behaviour NAME is (ingress checks the rest).</summary>
        private static bool IsBound(string? p) => p != null && (p.Contains("\"^") || p.Contains("\"Bind\"") || p.Contains("\"bind\""));

        /// <summary>The refusal lines for every slot whose params do not validate — "&lt;entity key&gt; &lt;slot&gt; '&lt;name&gt;': &lt;problem&gt;". Empty = fine.</summary>
        public static List<string> Problems(BehaviorRegistry registry, JsonNode? scenario)
        {
            var lines = new List<string>();
            foreach (var s in Enumerate(scenario))
                foreach (var problem in BehaviorParamsValidation.Validate(registry, s.Name, IsBound(s.Params) ? null : s.Params))
                    lines.Add($"{(s.Entity.Length == 0 ? "(root)" : s.Entity)} {s.Slot} '{s.Name}': {problem}");
            return lines;
        }
    }
}
