using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hrot.Network.Orchestration;

/// <summary>
/// ⭐ CE-4210 MA-9 — the ONE encoding of a scenario-save request's <c>ClusterOpRequest.PayloadJson</c>: <c>{"ScenarioName": name}</c> as ever, plus — only when set — <c>"OnlyGuids"</c> (the entities to write) and
/// <c>"HeaderJson"</c> (the macro header fields the slice uses). The egress, the master translator and the in-process adapter all read and write it HERE, so the three can never disagree; a request without the optional
/// keys encodes byte-for-byte as before.
/// </summary>
public static class SaveScenarioRequestJson
{
    public static string Encode(string? scenarioName, Guid[]? onlyGuids = null, string? headerJson = null)
    {
        var o = new JsonObject { ["ScenarioName"] = scenarioName };
        if (onlyGuids is { Length: > 0 }) o["OnlyGuids"] = new JsonArray(onlyGuids.Select(g => (JsonNode?)JsonValue.Create(g.ToString("D"))).ToArray());
        if (!string.IsNullOrWhiteSpace(headerJson)) o["HeaderJson"] = headerJson;
        return o.ToJsonString();
    }

    public static (string? ScenarioName, Guid[]? OnlyGuids, string? HeaderJson) Decode(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return (null, null, null);
        try
        {
            var node = JsonNode.Parse(payloadJson);
            string? name = node?["ScenarioName"]?.GetValue<string>();
            Guid[]? guids = null;
            if (node?["OnlyGuids"] is JsonArray arr)
            {
                var list = arr.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) && Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToArray();
                if (list.Length > 0) guids = list;
            }
            string? header = node?["HeaderJson"] is JsonValue h && h.TryGetValue<string>(out var hs) && !string.IsNullOrWhiteSpace(hs) ? hs : null;
            return (name, guids, header);
        }
        catch (JsonException) { return (null, null, null); }
    }
}
