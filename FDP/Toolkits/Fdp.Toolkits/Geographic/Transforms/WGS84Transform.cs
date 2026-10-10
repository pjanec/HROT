using System;
using System.Numerics;
using Fdp.Modules.Geographic;

namespace Fdp.Modules.Geographic.Transforms
{
    /// <summary>
    /// WGS84 ellipsoid implementation using East-North-Up tangent plane.
    /// Accurate for distances < 100km from origin.
    ///
    /// <para>⭐ <c>CE-3126</c> (R-229) — the origin can CHANGE while the node runs: a terrain commit sets it from the terrain's
    /// own <c>origin</c> (<c>TerrainResidency.Commit</c>), a replay sets it from the recording. So every value derived from it
    /// lives in ONE immutable <see cref="State"/> swapped by a single reference write — a reader on another thread sees the old
    /// origin or the new one, never the new origin with the old matrix. A new instance starts at 0,0,0 with valid matrices
    /// (before, its matrices were all zeros until <see cref="SetOrigin"/> ran, so it mapped everything to the origin).
    /// 📄 docs/DESIGN_Geo_Origin.md §2 C.</para>
    /// </summary>
    public class WGS84Transform : IGeographicTransform
    {
        private const double WGS84_A = 6378137.0; // Semi-major axis (m)
        private const double WGS84_F = 1.0 / 298.257223563; // Flattening
        private const double WGS84_E2 = WGS84_F * (2.0 - WGS84_F); // Eccentricity²

        /// <summary>Everything derived from one origin, built once and never mutated.</summary>
        private sealed class State
        {
            public double OriginLat, OriginLon, OriginAlt;          // radians, radians, metres
            public double OriginX, OriginY, OriginZ;                // the origin in ECEF
            public Matrix4x4 EcefToLocal, LocalToEcef;
        }

        private volatile State _state;

        public WGS84Transform() { _state = Build(0.0, 0.0, 0.0); }

        /// <summary>A transform already set to the given origin (degrees, degrees, metres).</summary>
        public WGS84Transform(double latDeg, double lonDeg, double altMeters) { _state = Build(latDeg, lonDeg, altMeters); }

        /// <summary>
        /// The geodetic origin in DEGREES latitude/longitude and metres altitude. The fields are stored
        /// in radians (see <see cref="SetOrigin"/>), so they are converted back for this getter.
        /// </summary>
        public (double lat, double lon, double alt) Origin
        {
            get
            {
                var s = _state;
                return (s.OriginLat * 180.0 / Math.PI, s.OriginLon * 180.0 / Math.PI, s.OriginAlt);
            }
        }

        public void SetOrigin(double latDeg, double lonDeg, double altMeters) => _state = Build(latDeg, lonDeg, altMeters);

        private static State Build(double latDeg, double lonDeg, double altMeters)
        {
            if (latDeg < -90.0 || latDeg > 90.0)
                throw new ArgumentOutOfRangeException(nameof(latDeg), "Latitude must be between -90 and 90 degrees.");

            var s = new State
            {
                OriginLat = latDeg * Math.PI / 180.0,
                OriginLon = lonDeg * Math.PI / 180.0,
                OriginAlt = altMeters,
            };

            // Compute ECEF origin
            (s.OriginX, s.OriginY, s.OriginZ) = GeodeticToECEF(s.OriginLat, s.OriginLon, s.OriginAlt);

            double sinLat = Math.Sin(s.OriginLat);
            double cosLat = Math.Cos(s.OriginLat);
            double sinLon = Math.Sin(s.OriginLon);
            double cosLon = Math.Cos(s.OriginLon);

            // Build ECEF→ENU rotation matrix for use with Vector3.Transform(v, M).
            //
            // C# Vector3.Transform computes:
            //   result.X = v.X*M11 + v.Y*M21 + v.Z*M31
            //   result.Y = v.X*M12 + v.Y*M22 + v.Z*M32
            //   result.Z = v.X*M13 + v.Y*M23 + v.Z*M33
            //
            // We need enu = R * ecef_delta where:
            //   East  = -sinLon*dX + cosLon*dY + 0*dZ
            //   North = -sinLat*cosLon*dX - sinLat*sinLon*dY + cosLat*dZ
            //   Up    =  cosLat*cosLon*dX + cosLat*sinLon*dY + sinLat*dZ
            //
            // The Matrix4x4 constructor takes (M11,M12,M13,M14, M21,M22,M23,M24, M31,M32,M33,M34, ...)
            // Matching result.X (East):  M11=-sinLon,       M21=cosLon,          M31=0
            // Matching result.Y (North): M12=-sinLat*cosLon, M22=-sinLat*sinLon, M32=cosLat
            // Matching result.Z (Up):    M13=cosLat*cosLon,  M23=cosLat*sinLon,  M33=sinLat
            s.EcefToLocal = new Matrix4x4(
                // M11            M12                       M13                      M14
                (float)-sinLon,   (float)(-sinLat*cosLon),  (float)(cosLat*cosLon),  0,
                // M21            M22                       M23                      M24
                (float)cosLon,    (float)(-sinLat*sinLon),  (float)(cosLat*sinLon),  0,
                // M31            M32                       M33                      M34
                0f,               (float)cosLat,             (float)sinLat,           0,
                0, 0, 0, 1
            );

            Matrix4x4.Invert(s.EcefToLocal, out s.LocalToEcef);
            return s;
        }

        public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters)
        {
            if (latDeg < -90.0 || latDeg > 90.0)
                throw new ArgumentOutOfRangeException(nameof(latDeg), "Latitude must be between -90 and 90 degrees.");

            double lat = latDeg * Math.PI / 180.0;
            double lon = lonDeg * Math.PI / 180.0;
            
            var st = _state;
            var (x, y, z) = GeodeticToECEF(lat, lon, altMeters);

            // Difference in doubles (high precision delta)
            double dx = x - st.OriginX;
            double dy = y - st.OriginY;
            double dz = z - st.OriginZ;

            // Now safe to cast to float for local rotation (since delta is relatively small)
            var delta = new Vector3((float)dx, (float)dy, (float)dz);
            return Vector3.Transform(delta, st.EcefToLocal);
        }
        
        public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos)
        {
            var st = _state;

            // Rotate back to ECEF delta
            var delta = Vector3.Transform(localPos, st.LocalToEcef);

            // Add delta to origin (in doubles)
            double x = st.OriginX + delta.X;
            double y = st.OriginY + delta.Y;
            double z = st.OriginZ + delta.Z;
            
            return ECEFToGeodetic(x, y, z);
        }
        
        // WGS84 conversion helpers
        private static (double x, double y, double z) GeodeticToECEF(double lat, double lon, double alt)
        {
            double N = WGS84_A / Math.Sqrt(1.0 - WGS84_E2 * Math.Sin(lat) * Math.Sin(lat));
            double x = (N + alt) * Math.Cos(lat) * Math.Cos(lon);
            double y = (N + alt) * Math.Cos(lat) * Math.Sin(lon);
            double z = (N * (1.0 - WGS84_E2) + alt) * Math.Sin(lat);
            return (x, y, z);
        }
        
        private static (double, double, double) ECEFToGeodetic(double x, double y, double z)
        {
            // Iterative solution
            
            double lon = Math.Atan2(y, x);
            double p = Math.Sqrt(x * x + y * y);
            double lat = Math.Atan2(z, p * (1.0 - WGS84_E2));
            
            for (int i = 0; i < 5; i++)
            {
                double N = WGS84_A / Math.Sqrt(1.0 - WGS84_E2 * Math.Sin(lat) * Math.Sin(lat));
                double alt = p / Math.Cos(lat) - N;
                lat = Math.Atan2(z, p * (1.0 - WGS84_E2 * N / (N + alt)));
            }
            
            double N_final = WGS84_A / Math.Sqrt(1.0 - WGS84_E2 * Math.Sin(lat) * Math.Sin(lat));
            double alt_final = p / Math.Cos(lat) - N_final;
            
            return (lat * 180.0 / Math.PI, lon * 180.0 / Math.PI, alt_final);
        }
    }
}
