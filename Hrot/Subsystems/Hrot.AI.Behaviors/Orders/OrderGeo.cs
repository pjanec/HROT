using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior.Params;

namespace Hrot.AI.Behaviors.Orders
{
    /// <summary>
    /// ⭐ <c>CE-2141</c> — an order's geodetic point (<see cref="PickableGeoPoint"/>: lat/lon, no height) ↔ local metres, through the
    /// world's <see cref="IGeographicTransform"/>. Without one (offline / test worlds) X = longitude and Y = latitude, as the
    /// hill-attack resolver does. 📄 docs/DESIGN_Orders_Params_Entity_Classes.md §7 (D2-F).
    /// </summary>
    public static class OrderGeo
    {
        /// <summary>The point in local metres at height <paramref name="z"/> (a geodetic click carries none: the caller supplies
        /// the unit's own height, and the navmesh snaps the rest).</summary>
        public static Vector3 ToLocal(EntityRepository world, PickableGeoPoint point, float z)
        {
            var geo = Transform(world);
            if (geo == null) return new Vector3((float)point.Longitude, (float)point.Latitude, z);
            var v = geo.ToCartesian(point.Latitude, point.Longitude, 0.0);
            return new Vector3(v.X, v.Y, z);
        }

        /// <summary>The geodetic point of local <paramref name="at"/>.</summary>
        public static PickableGeoPoint ToGeo(EntityRepository world, Vector3 at)
        {
            var geo = Transform(world);
            if (geo == null) return new PickableGeoPoint(at.Y, at.X);
            var (lat, lon, _) = geo.ToGeodetic(at);
            return new PickableGeoPoint(lat, lon);
        }

        private static IGeographicTransform? Transform(EntityRepository world)
            => world.HasSingletonManaged<IGeographicTransform>() ? world.GetSingletonManaged<IGeographicTransform>() : null;
    }
}
