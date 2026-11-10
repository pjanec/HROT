using System.Numerics;

namespace Fdp.Modules.Geographic.Transforms
{
    /// <summary>
    /// ⭐ CE-4394 / CE-4396 (docs/DESIGN_Terrain_Import_UTM.md §12.2 U-2, docs/DESIGN_Geo_Origin.md §6) — THE one geo transform of a node (Geo Origin
    /// R-229): every holder keeps this object, and a terrain load selects what is INSIDE it — the UTM frame of the terrain's origin (R-4197: the stored
    /// lat / lon origin IS the frame) or <see cref="LocalOnlyFrame"/> when the terrain has none. ⛔ The tangent plane is retired. The
    /// <c>SwitchableNavmeshProvider</c> pattern: one reference, the implementation swapped in one write (replacing the object would leave the ~15
    /// holders on the old one). ⭐ Before any terrain the frame is LOCAL-ONLY (no default place).
    /// </summary>
    public sealed class SwitchableGeoTransform : IGeographicTransform
    {
        private volatile IGeographicTransform _inner;

        /// <summary>Local-only until a terrain sets its frame (⛔ no default place — R-229).</summary>
        public SwitchableGeoTransform() => _inner = new LocalOnlyFrame();

        /// <summary>The UTM frame of a caller-supplied origin (tests and tools) — by the rule.</summary>
        public SwitchableGeoTransform(double latDeg, double lonDeg, double altMeters) => _inner = UtmTransform.AtOrigin(latDeg, lonDeg, altMeters);

        /// <summary>What is inside now.</summary>
        public IGeographicTransform Inner => _inner;

        public bool HasFrame => _inner.HasFrame;
        public (int Zone, bool South)? UtmZone => _inner.UtmZone;

        /// <summary>The exact UTM frame: local (0, 0) at (<paramref name="e0"/>, <paramref name="n0"/>) in <paramref name="zone"/>.</summary>
        public void UseUtm(int zone, bool south, double e0, double n0, double h0 = 0.0)
            => _inner = new UtmTransform(zone, south, e0, n0, h0);

        /// <summary>The frame the stored lat / lon origin gives (§6.1a), with the optional zone override.</summary>
        public void UseOrigin(double latDeg, double lonDeg, double altMeters, int? zoneOverride = null, bool? southOverride = null)
            => _inner = UtmTransform.AtOrigin(latDeg, lonDeg, altMeters, zoneOverride, southOverride);

        /// <summary>No frame: the local metres have no place on the Earth (geodetic conversions are refused).</summary>
        public void UseLocalOnly()
        {
            if (_inner is not LocalOnlyFrame) _inner = new LocalOnlyFrame();
        }

        /// <summary>Re-sets the origin BY THE RULE (the zone of the new longitude) — the interface's meaning kept for tests and tools.</summary>
        public void SetOrigin(double latDeg, double lonDeg, double altMeters) => UseOrigin(latDeg, lonDeg, altMeters);

        public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters) => _inner.ToCartesian(latDeg, lonDeg, altMeters);
        public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos) => _inner.ToGeodetic(localPos);
        public (double lat, double lon, double alt) Origin => _inner.Origin;
        public double GridConvergenceDeg(Vector3 localPos) => _inner.GridConvergenceDeg(localPos);
    }
}
