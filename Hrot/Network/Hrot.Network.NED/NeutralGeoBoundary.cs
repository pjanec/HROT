using Fdp.Modules.Geographic;
using Hrot.Map.Common.Replication.Utils;
using Hrot.NED.Messages;

namespace Hrot.Network.NED
{
    /// <summary>
    /// ⭐ AQ107 B — the ONE place where the protocol-NEUTRAL layer's geodetic positions meet the NED wire's local ENU metres.
    ///
    /// <para>The NED wire is local ENU only (<see cref="Vec3d"/>); but the neutral Core DTOs/commands that ExCon and the IG exchange
    /// (<c>MapClickEventDto</c>, <c>CreateEntityCommand</c>, the <c>WorldPos</c> update JSON, the route-creation waypoints) still carry
    /// latitude / longitude / altitude — that layer is the mission / authoring convention and is NOT part of this refactor
    /// (a follow-up: make it local ENU too, or give ExCon the published origin — AQ107 C). So the NED adapters convert HERE, with the
    /// node's one <see cref="IGeographicTransform"/> (the factory holds it). A conversion that exists for the neutral layer, not for
    /// the wire: the wire side of it is a copy.</para>
    ///
    /// <para>With no transform (offline / headless unit tests) the long-standing offline convention applies —
    /// X = longitude, Y = latitude, Z = altitude — in both directions, so a round trip is the identity.</para>
    /// </summary>
    internal static class NeutralGeoBoundary
    {
        /// <summary>A neutral (lat, lon, alt) as a wire local-ENU position.</summary>
        public static Vec3d ToWire(IGeographicTransform? transform, double latDeg, double lonDeg, double altM)
        {
            if (transform == null) return new Vec3d { X = lonDeg, Y = latDeg, Z = altM };
            return EnuWire.ToWire(transform.ToCartesian(latDeg, lonDeg, altM));
        }

        /// <summary>A wire local-ENU position as a neutral (lat, lon, alt).</summary>
        public static (double Lat, double Lon, double Alt) ToNeutral(IGeographicTransform? transform, in Vec3d position)
        {
            if (transform == null) return (position.Y, position.X, position.Z);
            return transform.ToGeodetic(EnuWire.FromWire(position));
        }
    }
}
