using System.Numerics;
using System.Text.Json.Nodes;

namespace Hrot.Common.Orders
{
    /// <summary>
    /// ⭐ CE-4575 (X-9) — THE form of a point in an order or a designation: local metres <c>[x, y, z]</c> (a <c>PointLink</c>'s JSON, S0 item 5 /
    /// SR-135). Every sender (the RTS click, the command wheel, the possession station, the menu's pick, HTTP) writes it here and every reader
    /// (the designation overlay) reads it here. 📐 S0 item 5 retired the geodetic <c>[lat, lon]</c> (and <c>OrderGeo</c>'s <c>[y, x]</c>
    /// fallback) but three places kept writing or reading it, so an RTS / HTTP move was dropped on the brain ("params did not parse").
    /// </summary>
    public static class OrderPoints
    {
        /// <summary>An order's point at <paramref name="local"/> (metres).</summary>
        public static JsonArray Of(Vector3 local) => new(local.X, local.Y, local.Z);

        /// <summary>An order's point on the ground at <paramref name="local"/> (z = 0: the mover puts it on the surface).</summary>
        public static JsonArray Of(Vector2 local) => new(local.X, local.Y, 0f);

        /// <summary>The local point an order or designation names (<c>[x, y]</c> or <c>[x, y, z]</c>); false for anything else (an authored
        /// reference <c>"@Key"</c> is resolved by the receiver, not here).</summary>
        public static bool TryRead(JsonNode? value, out Vector3 local)
        {
            local = default;
            if (value is not JsonArray { Count: 2 or 3 } a) return false;
            if (!TryFloat(a[0], out float x) || !TryFloat(a[1], out float y)) return false;
            float z = 0f;
            if (a.Count == 3 && !TryFloat(a[2], out z)) return false;
            local = new Vector3(x, y, z);
            return true;

            static bool TryFloat(JsonNode? n, out float f)
            {
                f = 0f;
                if (n is not JsonValue v) return false;
                if (v.TryGetValue(out double d)) { f = (float)d; return true; }
                if (v.TryGetValue(out float s)) { f = s; return true; }
                if (v.TryGetValue(out int i)) { f = i; return true; }
                if (v.TryGetValue(out long l)) { f = l; return true; }
                return false;
            }
        }
    }
}
