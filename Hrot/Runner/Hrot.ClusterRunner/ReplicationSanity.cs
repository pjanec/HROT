using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Hrot.Editor.Director;

namespace Hrot.ClusterRunner;

/// <summary>
/// ⭐ RT-6 (R-4279, docs/DESIGN_Scenario_Regression.md §11.4) — replication sanity, judged in EVERY cluster run from the node's own
/// <c>/diagnostics/architecture</c> (one entry per subsystem of the <c>--mode all</c> process, each translator's sent / received samples):
/// <list type="bullet">
/// <item>a topic SENT by some egress (sent &gt; 0) that an INGRESS in the cluster subscribes to, and that no ingress RECEIVED ⇒ FAIL, named
/// (the samples left and nobody took them in);</item>
/// <item>a system that threw (<c>errors</c> &gt; 0) or a module that failed (<c>failureCount</c> &gt; 0) — what the module host swallowed ⇒ FAIL,
/// named.</item>
/// </list>
/// ⭐ A topic no ingress in this cluster subscribes to is NOT a failure (its reader is outside — a recorder, a monitor): it is listed as a note.
/// Pure over the JSON, so its rail needs no cluster.
/// </summary>
internal static class ReplicationSanity
{
    public sealed record Finding(List<string> Problems, List<string> Notes);

    public static Finding Judge(JsonNode? architecture)
    {
        var problems = new List<string>();
        var notes = new List<string>();
        if (architecture?["subsystems"] is not JsonArray subs)
        {
            problems.Add("replication sanity: /diagnostics/architecture gave no subsystems");
            return new Finding(problems, notes);
        }
        var sent = new Dictionary<string, (double Samples, List<string> By)>(StringComparer.Ordinal);
        var received = new Dictionary<string, (double Samples, List<string> By)>(StringComparer.Ordinal);
        foreach (var s in subs.OfType<JsonObject>())
        {
            string sub = s["subsystem"]?.ToString() ?? "?";
            if (s["error"] is { } err) { problems.Add($"replication sanity: subsystem {sub}: {err}"); continue; }
            foreach (var t in (s["translators"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                string topic = t["topic"]?.ToString() ?? "";
                if (topic.Length == 0) continue;
                string dir = t["direction"]?.ToString() ?? "";
                string who = $"{sub}/{t["systemName"]}";
                // TranslatorDirection: Ingress · Egress · Bidirectional (both)
                if (dir is "Egress" or "Bidirectional") Add(sent, topic, ConditionBook.Num(t["sentSamples"]) ?? 0, who);
                if (dir is "Ingress" or "Bidirectional") Add(received, topic, ConditionBook.Num(t["receivedSamples"]) ?? 0, who);
            }
            foreach (var y in (s["systems"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                if (ConditionBook.Num(y["errors"]) is { } e && e > 0)
                    problems.Add($"replication sanity: {sub}/{y["systemName"]} threw {e:0} time(s) — the module host swallowed it");
            foreach (var m in (s["modules"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                if (ConditionBook.Num(m["failureCount"]) is { } f && f > 0)
                    problems.Add($"replication sanity: module {sub}/{m["name"]} failed {f:0} time(s)");
        }
        foreach (var (topic, out_) in sent.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (out_.Samples <= 0) continue;
            if (!received.TryGetValue(topic, out var in_)) { notes.Add($"{topic}: {out_.Samples:0} sent, no ingress in this cluster subscribes"); continue; }
            if (in_.Samples <= 0)
                problems.Add($"replication sanity: {topic} — {out_.Samples:0} sample(s) sent by {string.Join(", ", out_.By)}, none received by {string.Join(", ", in_.By)}");
        }
        return new Finding(problems, notes);
    }

    private static void Add(Dictionary<string, (double Samples, List<string> By)> map, string topic, double samples, string who)
    {
        if (!map.TryGetValue(topic, out var v)) v = (0, new List<string>());
        v.By.Add(who);
        map[topic] = (v.Samples + samples, v.By);
    }
}
