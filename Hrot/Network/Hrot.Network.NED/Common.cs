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

    // ⭐ AQ107 B — NOT a wire type any more. Every position on the NED wire is local ENU metres (`Hrot.NED.Messages.Vec3d`,
    //   X east · Y north · Z up, at the terrain origin). This plain struct survives ONLY as the type of the JSON placement
    //   override `EntityPropertyPatch.GeoPosition` (an ExCon→IG authoring convenience, `{"GeoPosition":{"Latitude":…}}`); it is
    //   no longer a [DdsStruct], so it is not in `hrot-common.idl`. EulerOri / AngularVector / EulerRate (the Euler-degree pose
    //   of the old `WorldPos`) are gone with D: the pose is a quaternion + ENU velocities.
    public struct GeoPoint
    {
        // latitude in degrees
        public double Latitude;

        // longitude in degrees
        public double Longitude;

        // altitude in meters above reference ellipsoid
        public double Altitude;
    }
}
