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

    // position in geographical coordinates
    [DdsStruct]
    [DdsIdlFile("hrot-common")]
    [DdsTypeFormat("[Lat:{Latitude:0.000000:Number}, Lon:{Longitude:0.000000:Number}, Alt:{Altitude:0.000000:Number}]")]
    public partial struct GeoPoint
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
    [DdsIdlFile("hrot-common")]
    [DdsTypeFormat("[Heading:{Heading:0.0:Number}, Pitch:{Pitch:0.0:Number}, Roll:{Roll:0.0:Number}]")]
    public partial struct EulerOri
    {
        // angles in degrees
        public float Heading;
        public float Pitch;
        public float Roll;
    }

    // "Direction Angles and Length" 3d vector defined by 2 directional angles and a length
    [DdsStruct]
    [DdsIdlFile("hrot-common")]
    [DdsTypeFormat("[Azimuth:{Azimuth:0.0:Number}, Elevation:{Elevation:0.0:Number}, Length:{Length:0.00:Number}]")]
    public partial struct AngularVector
    {
        // angles in degrees
        public float Azimuth;
        public float Elevation;
        // length in meters
        public float Length;
    }

    // Angular velocity (deg/s) — heading, pitch, roll rates
    [DdsStruct]
    [DdsIdlFile("hrot-common")]
    [DdsTypeFormat("[Heading:{Heading:0.0:Number}, Pitch:{Pitch:0.0:Number}, Roll:{Roll:0.0:Number}]")]
    public partial struct EulerRate
    {
        // rates in degrees per second
        public float Heading;
        public float Pitch;
        public float Roll;
    }

}
