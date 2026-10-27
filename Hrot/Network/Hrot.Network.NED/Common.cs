using System;
using CycloneDDS.Schema;

namespace Hrot.NED.Common
{
    // Unique identifier of a participating node
    [DdsStruct]
    [DdsIdlFile("hrot-common")]
    public partial struct NodeId
    {
        public int AppDomainId; // see DomainType

        // Individual node; unique within a domain
        public int AppInstanceId;
    }

    // ⭐ AQ107 B / CE-4073 — there is NO geodetic point type here any more. Every position on the NED wire is local ENU metres
    //   (`Hrot.NED.Messages.Vec3d`, X east · Y north · Z up, at the terrain origin); `GeoPoint` stopped being a [DdsStruct] with B and its
    //   last user, the JSON placement override `EntityPropertyPatch.GeoPosition`, went with CE-4073. EulerOri / AngularVector / EulerRate
    //   (the Euler-degree pose of the old `WorldPos`) are gone with D: the pose is a quaternion + ENU velocities.
}
