using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐⭐ CE-4222 (rules view R5c, RE-9 + A2; docs/DESIGN_Rules_View_UI.md §7.3) — <b>drag to connect</b>, PURE: what dropping row X on a trigger writes into the trigger's <c>when</c>. The condition is
/// <c>OnEntity { ref: X, predicate: PropertyMatch on ObjectiveStatus }</c> — the shape the authored triggers use (<i>Counter-attack</i>'s) — and, <b>A2, by X's kind</b>: an objective or measure ⇒ <c>State == Achieved</c>; a trigger ⇒ <c>TriggerState == Fired</c>.
/// It is ANDed into the existing <c>when</c> (an existing AND compound gets one more condition; any other condition is wrapped in a new AND compound); a trigger with no <c>when</c> gets it as its <c>when</c>.
/// </summary>
public static class RuleConnect
{
    /// <summary>
    /// ⭐ CE-4225 — the STABLE form of a link to row <paramref name="x"/>: the EntityLink the entity picker writes (<see cref="ReferenceLeafDrawers.PickToLink"/> — <c>Exact</c> = the entity's GUID) through its own JSON converter, i.e.
    /// <c>{"NetworkId": n, "Ref": "&lt;guid&gt;"}</c>, the shape a scenario's authored triggers carry. ⛔ A bare network id is NOT stable across a save and reload (measured: it named another rule). An entity with no GUID gets the bare id (as the picker does).
    /// </summary>
    public static JsonNode LinkOf(RuleRow x, LiveRefScope? scope)
        => JsonSerializer.SerializeToNode(ReferenceLeafDrawers.PickToLink(new EntityRef(x.NetworkId), scope), BehaviorParams.JsonOptions) ?? new JsonObject { ["NetworkId"] = x.NetworkId };

    /// <summary>The GUID a link names (an object's <c>Ref</c> or the text itself), or null.</summary>
    private static Guid? GuidOf(JsonNode? link) => link switch
    {
        JsonObject o when o.TryGetPropertyValue("Ref", out var r) => GuidOf(r),
        JsonValue v when v.TryGetValue<string>(out var t) && Guid.TryParse(t, out var g) && g != Guid.Empty => g,
        _ => null,
    };

    /// <summary>The predicate that says "X is done": by X's kind (A2). <paramref name="link"/> = <see cref="LinkOf"/>.</summary>
    public static JsonObject DonePredicateOf(RuleRow x, JsonNode link)
    {
        bool trigger = x.Status.Role == ObjectiveRole.Trigger;
        double value = trigger ? (double)(int)TriggerState.Fired : (double)(int)ObjectiveState.Achieved;
        return new JsonObject
        {
            ["$type"] = "OnEntity",
            ["ref"] = link.DeepClone(),
            ["predicate"] = new JsonObject
            {
                ["$type"] = "PropertyMatch",
                ["ComponentType"] = nameof(ObjectiveStatus),
                ["PropertyPath"] = trigger ? nameof(ObjectiveStatus.TriggerState) : nameof(ObjectiveStatus.State),
                ["Operator"] = "Equals",
                ["Predicate"] = new JsonObject { ["$type"] = "Numeric", ["MinValue"] = value, ["MaxValue"] = value },
            },
        };
    }

    /// <summary>Does <paramref name="when"/> already wait on <paramref name="x"/> (an <c>OnEntity</c> naming it, anywhere in the tree)? By GUID when the link has one — the stable identity — else by network id.</summary>
    public static bool Names(JsonNode? when, int networkId, JsonNode? link = null)
    {
        var guid = GuidOf(link);
        switch (when)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    if (k.Equals("ref", StringComparison.OrdinalIgnoreCase) && Same(v, networkId, guid)) return true;
                    if (Names(v, networkId, link)) return true;
                }
                return false;
            case JsonArray a: foreach (var i in a) if (Names(i, networkId, link)) return true; return false;
            default: return false;
        }
    }

    private static bool Same(JsonNode? v, int networkId, Guid? guid)
        => GuidOf(v) is { } g ? guid == g : LinkIdOf(v) == networkId;

    /// <summary>An authored entity link as the form or a scenario writes it: a number · <c>{"NetworkId": n}</c> · a numeric string (the same forms <c>AuthoredRules.LinkId</c> reads).</summary>
    private static int LinkIdOf(JsonNode? v) => v switch
    {
        JsonObject o when o.TryGetPropertyValue("NetworkId", out var n) => LinkIdOf(n),
        JsonValue j when j.TryGetValue<int>(out int i) => i,
        JsonValue j when j.TryGetValue<string>(out var s) && int.TryParse(s, out int i) => i,
        _ => 0,
    };

    /// <summary>
    /// <paramref name="paramsJson"/> (a trigger's params) with "X is done" ANDed into its <c>when</c>; <see langword="null"/> and a <paramref name="why"/> when it cannot be (not an object, or it already waits on X).
    /// </summary>
    public static string? AddedTo(string paramsJson, RuleRow x, JsonNode link, out string? why)
    {
        why = null;
        if (JsonNode.Parse(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) is not JsonObject root)
        { why = "the trigger's params are not an object"; return null; }   // ⭐ parsed key-insensitively: the stored JSON is the params contract's — a scenario writes camelCase, the form PascalCase
        string key = "when";
        foreach (var (k, _) in root) if (k.Equals("when", StringComparison.OrdinalIgnoreCase)) { key = k; break; }
        var existing = root[key];
        if (Names(existing, x.NetworkId, link)) { why = $"it already waits on {x.Title}"; return null; }
        var done = DonePredicateOf(x, link);
        if (existing is null) root[key] = done;
        else if (existing is JsonObject c && string.Equals((string?)c["$type"], "Compound", StringComparison.Ordinal) && IsAnd(c) && c["Conditions"] is JsonArray list) list.Add(done);
        else
        {
            root.Remove(key);
            var wrapped = new JsonObject { ["$type"] = "Compound", ["Operator"] = "And", ["Conditions"] = new JsonArray(existing, done) };
            // keep `when` where it was among the params: rebuild the object in order
            var rebuilt = new JsonObject();
            bool placed = false;
            foreach (var (k, v) in root.ToArray()) { root.Remove(k); rebuilt[k] = v; if (k.Equals("common", StringComparison.OrdinalIgnoreCase)) { rebuilt[key] = wrapped; placed = true; } }
            if (!placed) rebuilt[key] = wrapped;
            root = rebuilt;
        }
        return root.ToJsonString();
    }

    private static bool IsAnd(JsonObject compound)
        => compound["Operator"] is null || (compound["Operator"] is JsonValue v && ((v.TryGetValue<string>(out var s) && s.Equals("And", StringComparison.OrdinalIgnoreCase)) || (v.TryGetValue<int>(out int n) && n == 0)));
}
