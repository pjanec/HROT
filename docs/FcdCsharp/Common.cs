using System;
using CycloneDDS.Schema;

namespace Bagira.DDS.DM
{
    // Unique identifier of a participating node
    [DdsStruct]
    [DdsIdlFile("bdc-sst-common")]
    public partial struct NodeId
    {
        public int AppDomainId; // see DomainType

        // Individual node; unique within a domain
        public int AppInstanceId;
    }

    // position in geographical coordinates
    [DdsStruct]
    [DdsIdlFile("bdc-sst-common")]
    public partial struct GeoPosition
    {
        // latitude in degrees
        public double Latitude;

        // longitude in degrees
        public double Longitude;

        // altitude in meters above reference ellipsoid
        public double Altitude;
    }


    // position in geographical coordinates
    [DdsStruct]
    [DdsIdlFile("bdc-sst-common")]
    public partial struct OrientationHPR
    {
        // angles in degrees
        public float Heading;
        public float Pitch;
        public float Roll;
    }

    // "Direction Angles and Length" 3d vector defined by 2 directional angles and a length
    [DdsStruct]
    [DdsIdlFile("bdc-sst-common")]
    public partial struct DAL3
    {
        // angles in degrees
        float Azimuth;
        float Elevation;
        // length in meters
        float Length;
    }

}
