using System.Runtime.InteropServices;

namespace Fdp.Modules.Geographic.Components
{
    /// <summary>
    /// Geodetic position and orientation. Mirrors SimTransform in the geographic domain.
    ///
    /// Written by SimTransformBridgeSystem each tick for locally-owned entities.
    /// Read by application-layer egress translators (e.g. GeoSpatialEgressTranslator in SimHost).
    ///
    /// Convention:
    ///   Latitude/Longitude: WGS84 decimal degrees
    ///   Altitude:           meters (MSL or AGL — application decides)
    ///   HeadingDeg:         compass [0, 360), 0=North, 90=East, clockwise
    ///   PitchDeg:           +ve = nose up
    ///   RollDeg:            +ve = right wing down (clockwise looking forward)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GeoTransform
    {
        /// <summary>WGS84 geodetic latitude in decimal degrees.</summary>
        public double Latitude;

        /// <summary>WGS84 geodetic longitude in decimal degrees.</summary>
        public double Longitude;

        /// <summary>Altitude in meters.</summary>
        public float Altitude;

        /// <summary>Compass heading in degrees [0, 360). 0=North, 90=East, clockwise.</summary>
        public float HeadingDeg;

        /// <summary>Pitch in degrees. 0=level, positive=nose up.</summary>
        public float PitchDeg;

        /// <summary>Roll in degrees. 0=level, positive=right wing down.</summary>
        public float RollDeg;
    }
}
