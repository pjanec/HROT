using System.Linq;
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

    /// <summary><c>{ state, reason, lastRefusal, serverExists, station, respawnAt, offers[{unitNetId, side, slotName, holderStation, kind, count, max}], slot{state, unit} }</c> (S2: Dead, respawnAt, the role offers).</summary>
    public static JsonNode Get(DeployFlowSystem f)
    {
        var offers = new JsonArray();
        foreach (var o in f.Offers)
            offers.Add(new JsonObject { ["unitNetId"] = o.UnitNetId, ["side"] = o.Side.ToString(), ["slotName"] = o.SlotName, ["holderStation"] = o.HolderStation, ["kind"] = o.IsRole ? "Role" : o.IsArea ? "Area" : "Placed", ["count"] = o.Count, ["max"] = o.Max,
                ["kits"] = new JsonArray((o.Kits ?? System.Array.Empty<string>()).Select(k => (JsonNode?)k).ToArray()) });
        return new JsonObject
        {
            ["state"] = f.State.ToString(), ["reason"] = f.Reason, ["lastRefusal"] = f.LastRefusal, ["serverExists"] = f.ServerExists, ["station"] = f.Station,
            ["offers"] = offers,
            ["respawnAt"] = f.RespawnAtSim,
            ["slot"] = new JsonObject { ["state"] = f.State.ToString(), ["unit"] = f.UnitNetId },
        };
    }

    /// <summary><c>{ side, unitNetId, kit?, areaNetId? }</c> asks for a unit (a role's kit 0…3, a chosen spawn area); <c>{ side, observe:true }</c> asks to observe. Status: 200 sent · 400 bad body · 409 not choosing.</summary>
    public static (JsonNode? Result, string? Error, int Status) Post(DeployFlowSystem f, JsonNode? body)
    {
        if (body is not JsonObject o) return (null, "A body { side, unitNetId } or { side, observe:true } is required.", 400);
        if (!TrySide(o["side"], out var side)) return (null, "side must be Friend, Hostile, Neutral (or 1, 2, 0).", 400);
        bool observe = o["observe"] is JsonValue ov && ov.TryGetValue<bool>(out var b) && b;
        int unit = 0;
        if (!observe && !(o["unitNetId"] is JsonValue uv && uv.TryGetValue<int>(out unit) && unit != 0))
            return (null, "unitNetId (a non-zero network id) is required unless observe is true.", 400);
        // ⭐ S3 — optional kit (0…3) and area: a wrong JSON type or range is a 400, never a silent default
        int kit = 0, areaNetId = 0;
        if (o["kit"] is { } kn && !(kn is JsonValue kv && kv.TryGetValue<int>(out kit) && kit is >= 0 and <= 3)) return (null, "kit must be an integer 0…3.", 400);
        if (o["areaNetId"] is { } an && !(an is JsonValue av && av.TryGetValue<int>(out areaNetId))) return (null, "areaNetId must be an integer network id (0 = any area).", 400);
        bool sent = observe ? f.Observe(side) : f.Request(side, unit, (byte)kit, areaNetId);
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
