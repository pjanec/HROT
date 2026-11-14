using System;
using System.Numerics;

namespace Fdp.Modules.Geographic.Transforms
{
    /// <summary>
    /// ⭐ CE-4394 (docs/DESIGN_Terrain_Import_UTM.md §12.2 U-1) — the EXACT UTM frame of an imported terrain: local = (E − E₀, N − N₀, h − h₀),
    /// grid north = +Y, over the shared <see cref="Utm"/>. The second implementation of <see cref="IGeographicTransform"/> beside the tangent
    /// plane (the retired tangent plane); a terrain that declares <c>frame.utm</c> selects it (<see cref="SwitchableGeoTransform"/>).
    /// <para>⭐ Any zone, either hemisphere; a point beyond the zone's band stays in the DECLARED zone (transverse Mercator holds well past
    /// it — the local frame never jumps at a zone edge). Immutable state swapped in one write, as the retired tangent plane.</para>
    /// </summary>
    public sealed class UtmTransform : IGeographicTransform
    {
        private sealed record State(int Zone, bool South, double E0, double N0, double H0, double Lat0, double Lon0);

        private volatile State _state;

        /// <param name="zone">The UTM zone (1–60).</param>
        /// <param name="south">The southern hemisphere (false northing 10 000 km).</param>
        /// <param name="e0">The easting of local (0, 0), metres.</param>
        /// <param name="n0">The northing of local (0, 0), metres.</param>
        /// <param name="h0">The height of local z = 0, metres.</param>
        public UtmTransform(int zone, bool south, double e0, double n0, double h0 = 0.0)
        {
            if (zone < 1 || zone > 60) throw new ArgumentOutOfRangeException(nameof(zone), zone, "a UTM zone is 1–60");
            _state = Build(zone, south, e0, n0, h0);
        }

        private UtmTransform(State state) => _state = state;

        /// <summary>The UTM zone (1–60) of a longitude: its 6° band.</summary>
        public static int ZoneOf(double lonDeg) => Math.Clamp((int)Math.Floor((lonDeg + 180.0) / 6.0) + 1, 1, 60);

        /// <summary>
        /// ⭐ CE-4396 (docs/DESIGN_Geo_Origin.md §6.1a) — THE rule: the stored lat / lon origin IS the frame. zone = the origin's longitude band
        /// (or <paramref name="zoneOverride"/> when a terrain uses another one), hemisphere = the sign of its latitude (or
        /// <paramref name="southOverride"/>), (E0, N0, H0) = the origin projected into that zone.
        /// </summary>
        public static UtmTransform AtOrigin(double latDeg, double lonDeg, double altMeters, int? zoneOverride = null, bool? southOverride = null)
        {
            int zone = zoneOverride ?? ZoneOf(lonDeg);
            bool south = southOverride ?? latDeg < 0;
            var (e, n) = Utm.FromGeodetic(latDeg, lonDeg, zone, south);
            return new UtmTransform(new State(zone, south, e, n, altMeters, latDeg, lonDeg));   // the origin as given, exactly (the stored lat / lon IS the frame)
        }

        private static State Build(int zone, bool south, double e0, double n0, double h0)
        {
            var (lat, lon) = Utm.ToGeodetic(e0, n0, zone, south);
            return new State(zone, south, e0, n0, h0, lat, lon);
        }

        public int Zone => _state.Zone;
        public bool South => _state.South;
        public double E0 => _state.E0;
        public double N0 => _state.N0;
        public double H0 => _state.H0;
        public (int Zone, bool South)? UtmZone => (_state.Zone, _state.South);

        /// <summary>Re-anchors local (0, 0) at (<paramref name="latDeg"/>, <paramref name="lonDeg"/>) IN THIS ZONE — the interface's meaning kept.</summary>
        public void SetOrigin(double latDeg, double lonDeg, double altMeters)
        {
            var s = _state;
            var (e, n) = Utm.FromGeodetic(latDeg, lonDeg, s.Zone, s.South);
            _state = new State(s.Zone, s.South, e, n, altMeters, latDeg, lonDeg);
        }

        public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters)
        {
            var s = _state;
            var (e, n) = Utm.FromGeodetic(latDeg, lonDeg, s.Zone, s.South);
            return new Vector3((float)(e - s.E0), (float)(n - s.N0), (float)(altMeters - s.H0));
        }

        public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos)
        {
            var s = _state;
            var (lat, lon) = Utm.ToGeodetic(s.E0 + localPos.X, s.N0 + localPos.Y, s.Zone, s.South);
            return (lat, lon, s.H0 + localPos.Z);
        }

        public (double lat, double lon, double alt) Origin { get { var s = _state; return (s.Lat0, s.Lon0, s.H0); } }

        /// <inheritdoc/>
        public double GridConvergenceDeg(Vector3 localPos)
        {
            var s = _state;
            var (lat, lon) = Utm.ToGeodetic(s.E0 + localPos.X, s.N0 + localPos.Y, s.Zone, s.South);
            return Utm.ConvergenceDeg(lat, lon, s.Zone);
        }
    }
}
