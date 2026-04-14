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

    // Geographic position for BDC — latitude/longitude/altitude in degrees/meters
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcGeoPoint
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
    }

    // Orientation angles in degrees (heading, pitch, roll)
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcEulerOri
    {
        public float Heading;
        public float Pitch;
        public float Roll;
    }

    // Velocity/angular vector: azimuth (deg), elevation (deg), length (m/s)
    [DdsStruct]
    [DdsIdlFile("bdc-common")]
    public partial struct BdcAngularVector
    {
        public float Azimuth;
        public float Elevation;
        public float Length;
    }
}
