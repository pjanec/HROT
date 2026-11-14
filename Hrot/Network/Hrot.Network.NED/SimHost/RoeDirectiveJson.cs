using System.Globalization;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐ <c>CE-3251</c> (BR-13) — a <see cref="SetRoeEvent"/>'s fields as the JSON of a forwarded directive
    /// (<c>TacticalIntentWireKind.SetRoe</c>): <c>{"Fire":1,"Reactions":0,"Window":0,"Force":0}</c>. The entity and the origin
    /// ride the request's own <c>TargetEntityId</c> / <c>Origin</c>. A field left unset stays unset, so the owner's
    /// <c>RoeSystem</c> keeps the current value exactly as it would for a local event.
    /// </summary>
    public static class RoeDirectiveJson
    {
        /// <summary>The JSON for <paramref name="evt"/> (everything but the entity and the origin).</summary>
        public static string Encode(in SetRoeEvent evt)
            => string.Create(CultureInfo.InvariantCulture,
                $"{{\"Fire\":{(byte)evt.Fire},\"Reactions\":{(byte)evt.Reactions},\"Window\":{evt.ReturnFireWindowSeconds:R},\"Force\":{(byte)evt.ForceLevel}}}");

        /// <summary>The event the owner raises again; <c>false</c> when the JSON is not an ROE directive.</summary>
        public static bool TryDecode(string? json, Entity entity, BehaviorOrigin origin, out SetRoeEvent evt)
        {
            evt = default;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                evt = new SetRoeEvent
                {
                    Entity                  = entity,
                    Origin                  = origin,
                    Fire                    = (RoeFire)Byte(root, "Fire"),
                    Reactions               = (RoeReactions)Byte(root, "Reactions"),
                    ReturnFireWindowSeconds = root.TryGetProperty("Window", out var w) && w.TryGetSingle(out var f) ? f : 0f,
                    ForceLevel              = (RoeForceLevel)Byte(root, "Force"),
                };
                return true;
            }
            catch (JsonException) { return false; }
        }

        private static byte Byte(JsonElement root, string name)
            => root.TryGetProperty(name, out var v) && v.TryGetByte(out var b) ? b : (byte)0;
    }
}
