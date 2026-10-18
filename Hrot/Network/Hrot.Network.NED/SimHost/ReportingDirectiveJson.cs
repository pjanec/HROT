using System.Globalization;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad.Command;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐ <c>CE-3251</c> (BR-13, the <see cref="RoeDirectiveJson"/> twin) — a <see cref="SetReportingEvent"/>'s fields as the JSON of a
    /// forwarded directive (<c>TacticalIntentWireKind.SetReporting</c>): <c>{"Pir":3,"Ffir":1,"Sitrep":30,"Area":4711}</c>. The entity and
    /// the origin ride the request's own <c>TargetEntityId</c> / <c>Origin</c>; the owner's <c>ReportingRequirementsSystem</c> applies it
    /// through the same origin gate as a local event (all zero withdraws them).
    /// </summary>
    public static class ReportingDirectiveJson
    {
        /// <summary>The JSON for <paramref name="evt"/> (everything but the entity and the origin).</summary>
        public static string Encode(in SetReportingEvent evt)
            => string.Create(CultureInfo.InvariantCulture,
                $"{{\"Pir\":{(uint)evt.Pir},\"Ffir\":{(uint)evt.Ffir},\"Sitrep\":{evt.SitrepEverySeconds:R},\"Area\":{evt.PirArea}}}");

        /// <summary>The event the owner raises again; <c>false</c> when the JSON is not a reporting directive.</summary>
        public static bool TryDecode(string? json, Entity entity, BehaviorOrigin origin, out SetReportingEvent evt)
        {
            evt = default;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return false;
                evt = new SetReportingEvent
                {
                    Entity             = entity,
                    Origin             = origin,
                    Pir                = (PirFlags)(root.TryGetProperty("Pir", out var p) && p.TryGetUInt32(out var pv) ? pv : 0u),
                    Ffir               = (FfirFlags)(root.TryGetProperty("Ffir", out var f) && f.TryGetUInt32(out var fv) ? fv : 0u),
                    SitrepEverySeconds = root.TryGetProperty("Sitrep", out var s) && s.TryGetSingle(out var sv) ? sv : 0f,
                    PirArea            = root.TryGetProperty("Area", out var a) && a.TryGetInt64(out var av) ? av : 0L,
                };
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
