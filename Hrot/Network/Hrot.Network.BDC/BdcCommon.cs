using CycloneDDS.Schema;

namespace Hrot.BDC.Common
{
    // Unique identifier of a BDC participating node
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcNodeId
    {
        public int AppDomainId;
        public int AppInstanceId;
    }

    // ⭐ AQ107 F — the BDC stack follows the NED wire's rules: every position / velocity is in the LOCAL ENU frame
    //   (X east, Y north, Z up, metres at the terrain origin — the ECS SimTransform frame), orientation is a quaternion.
    //   Replaces BdcGeoPoint (lat/lon), BdcEulerOri (Euler degrees) and BdcAngularVector (azimuth/elevation/length).

    // 3-component double vector: a position in metres, local ENU
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcVec3d
    {
        public double X;
        public double Y;
        public double Z;
    }

    // 3-component float vector: a velocity (m/s), an angular velocity (rad/s) or a quaternion's vector part
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcVec3f
    {
        public float X;
        public float Y;
        public float Z;
    }
}
