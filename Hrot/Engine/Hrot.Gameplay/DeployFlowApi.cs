using System.Text.Json.Nodes;
using Fdp.Core;

namespace Hrot.Gameplay;

/// <summary>
/// ⭐ Player Join S1 — the debug API's view of a Player's <see cref="DeployFlowSystem"/> (<c>GET /deploy</c>, <c>POST /deploy</c>): the JSON lives HERE so the route stays a thin lookup and a rail
/// can drive it without an HTTP host. The route finds the flow through <see cref="DeployFlowSystem.Of"/>; a node with none answers 404 <see cref="NoFlow"/>.
/// </summary>
public static class DeployFlowApi
{
    public const string NoFlow = "no deploy flow on this node";

    /// <summary><c>{ state, reason, lastRefusal, serverExists, station, offers[{unitNetId, side, slotName, holderStation}], slot{state, unit} }</c>.</summary>
    public static JsonNode Get(DeployFlowSystem f)
    {
        var offers = new JsonArray();
        foreach (var o in f.Offers)
            offers.Add(new JsonObject { ["unitNetId"] = o.UnitNetId, ["side"] = o.Side.ToString(), ["slotName"] = o.SlotName, ["holderStation"] = o.HolderStation });
        return new JsonObject
        {
            ["state"] = f.State.ToString(), ["reason"] = f.Reason, ["lastRefusal"] = f.LastRefusal, ["serverExists"] = f.ServerExists, ["station"] = f.Station,
            ["offers"] = offers,
            ["slot"] = new JsonObject { ["state"] = f.State.ToString(), ["unit"] = f.UnitNetId },
        };
    }

    /// <summary><c>{ side, unitNetId }</c> asks for a unit; <c>{ side, observe:true }</c> asks to observe. Status: 200 sent · 400 bad body · 409 not choosing.</summary>
    public static (JsonNode? Result, string? Error, int Status) Post(DeployFlowSystem f, JsonNode? body)
    {
        if (body is not JsonObject o) return (null, "A body { side, unitNetId } or { side, observe:true } is required.", 400);
        if (!TrySide(o["side"], out var side)) return (null, "side must be Friend, Hostile, Neutral (or 1, 2, 0).", 400);
        bool observe = o["observe"] is JsonValue ov && ov.TryGetValue<bool>(out var b) && b;
        int unit = 0;
        if (!observe && !(o["unitNetId"] is JsonValue uv && uv.TryGetValue<int>(out unit) && unit != 0))
            return (null, "unitNetId (a non-zero network id) is required unless observe is true.", 400);
        bool sent = observe ? f.Observe(side) : f.Request(side, unit);
        if (!sent) return (null, $"The deploy flow is {f.State}; a request is only taken while Choosing (an observe also while Observing with a server).", 409);
        return (Get(f), null, 200);
    }

    private static bool TrySide(JsonNode? n, out ForceId side)
    {
        side = ForceId.Neutral;
        if (n is not JsonValue v) return false;
        if (v.TryGetValue<string>(out var s)) return Enum.TryParse(s, ignoreCase: true, out side) && Enum.IsDefined(side);
        if (v.TryGetValue<int>(out var i) && i is >= 0 and <= 2) { side = (ForceId)i; return true; }
        return false;
    }
}
