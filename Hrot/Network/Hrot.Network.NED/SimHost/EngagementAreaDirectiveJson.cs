using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐ <c>CE-3261</c> (the <see cref="ReportingDirectiveJson"/> twin) — a <see cref="SetEngagementAreaEvent"/>'s fields as the JSON of a
    /// forwarded directive (<c>TacticalIntentWireKind.SetEngagementArea</c>):
    /// <c>{"Arc":1,"O":[x,y,z],"L":300,"R":60,"Min":0,"Max":400,"Allow":4711,"Watch":0,"W":1,"NoFire":[4712]}</c>. The entity and the origin
    /// ride the request's own <c>TargetEntityId</c> / <c>Origin</c>; the owner's <c>EngagementAreaSystem</c> applies it through the same
    /// origin gate as a local event (all empty withdraws it). Areas are network ids already.
    /// </summary>
    public static class EngagementAreaDirectiveJson
    {
        /// <summary>The JSON for <paramref name="evt"/> (everything but the entity and the origin).</summary>
        public static string Encode(in SetEngagementAreaEvent evt)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(160);
            sb.Append(c, $"{{\"Arc\":{evt.ArcSet},\"O\":[{evt.ArcOrigin.X:R},{evt.ArcOrigin.Y:R},{evt.ArcOrigin.Z:R}],");
            sb.Append(c, $"\"L\":{evt.LeftBearing:R},\"R\":{evt.RightBearing:R},\"Min\":{evt.MinRange:R},\"Max\":{evt.MaxRange:R},");
            sb.Append(c, $"\"Allow\":{evt.AllowArea},\"Watch\":{evt.WatchBearing:R},\"W\":{evt.WatchSet},\"NoFire\":[");
            int n = System.Math.Clamp(evt.NoFire.Count, 0, EngagementArea.MaxNoFire);
            for (int i = 0; i < n; i++) sb.Append(c, $"{(i > 0 ? "," : "")}{evt.NoFire.Items[i]}");
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>The event the owner raises again; <c>false</c> when the JSON is not a sector directive.</summary>
        public static bool TryDecode(string? json, Entity entity, BehaviorOrigin origin, out SetEngagementAreaEvent evt)
        {
            evt = default;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) return false;
                static float F(JsonElement r, string k) => r.TryGetProperty(k, out var v) && v.TryGetSingle(out var f) ? f : 0f;
                static byte B(JsonElement r, string k) => r.TryGetProperty(k, out var v) && v.TryGetByte(out var b) ? b : (byte)0;
                var o = Vector3.Zero;
                if (r.TryGetProperty("O", out var oa) && oa.ValueKind == JsonValueKind.Array && oa.GetArrayLength() == 3)
                    o = new Vector3(oa[0].GetSingle(), oa[1].GetSingle(), oa[2].GetSingle());
                evt = new SetEngagementAreaEvent
                {
                    Entity = entity, Origin = origin,
                    ArcSet = B(r, "Arc"), ArcOrigin = o, LeftBearing = F(r, "L"), RightBearing = F(r, "R"), MinRange = F(r, "Min"), MaxRange = F(r, "Max"),
                    AllowArea = r.TryGetProperty("Allow", out var a) && a.TryGetInt64(out var av) ? av : 0L,
                    WatchBearing = F(r, "Watch"), WatchSet = B(r, "W"),
                };
                if (r.TryGetProperty("NoFire", out var nf) && nf.ValueKind == JsonValueKind.Array)
                    foreach (var id in nf.EnumerateArray())
                        if (evt.NoFire.Count < EngagementArea.MaxNoFire && id.TryGetInt64(out var v)) evt.NoFire.Items[evt.NoFire.Count++] = v;
                return true;
            }
            catch (JsonException) { return false; }
            catch (System.InvalidOperationException) { return false; }
            catch (System.FormatException) { return false; }
        }
    }
}
