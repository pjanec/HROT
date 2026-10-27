using System.Numerics;
using Hrot.NED.Messages;

namespace Hrot.Map.Common.Replication.Utils
{
    /// <summary>
    /// ⭐ AQ107 B — the whole "conversion" between the ECS and a position on the NED wire: a COPY.
    /// Every position, destination and waypoint on the wire is local ENU metres (X east, Y north, Z up) — the same frame as
    /// <c>SimTransform.Position</c> — so there is no geodetic maths left on the wire path, and this is the one place that says
    /// how a <see cref="Vector3"/> becomes a <see cref="Vec3d"/> and back. Double on the wire, float in the ECS.
    /// </summary>
    public static class EnuWire
    {
        /// <summary>An ECS local position as the wire's <see cref="Vec3d"/>.</summary>
        public static Vec3d ToWire(Vector3 v) => new Vec3d { X = v.X, Y = v.Y, Z = v.Z };

        /// <summary>A wire <see cref="Vec3d"/> as an ECS local position.</summary>
        public static Vector3 FromWire(in Vec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        /// <summary>An ECS velocity (m/s, rad/s) as the wire's <see cref="Vec3f"/>.</summary>
        public static Vec3f ToWireF(Vector3 v) => new Vec3f { X = v.X, Y = v.Y, Z = v.Z };

        /// <summary>A wire <see cref="Vec3f"/> as an ECS velocity.</summary>
        public static Vector3 FromWireF(in Vec3f v) => new Vector3(v.X, v.Y, v.Z);
    }
}
