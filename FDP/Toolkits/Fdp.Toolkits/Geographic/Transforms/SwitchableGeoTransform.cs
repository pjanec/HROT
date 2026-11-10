using System.Numerics;

namespace Fdp.Modules.Geographic.Transforms
{
    /// <summary>
    /// ⭐ CE-4394 (docs/DESIGN_Terrain_Import_UTM.md §12.2 U-2) — THE one geo transform of a node (Geo Origin R-229): every holder keeps this
    /// object, and a terrain load selects what is INSIDE it — the tangent plane at the terrain's <c>origin</c> (every terrain until now) or
    /// the exact UTM frame a terrain declares (<c>frame.utm</c>). The <c>SwitchableNavmeshProvider</c> pattern: one reference, the
    /// implementation swapped in one write (⛔ replacing the object would leave the ~15 holders on the old one).
    /// </summary>
    public sealed class SwitchableGeoTransform : IGeographicTransform
    {
        private volatile IGeographicTransform _inner;

        /// <summary>The tangent plane at 0, 0, 0 until a terrain sets its frame (⛔ no default place — R-229).</summary>
        public SwitchableGeoTransform() => _inner = new WGS84Transform();

        /// <summary>The tangent plane at a caller-supplied origin (tests and tools).</summary>
        public SwitchableGeoTransform(double latDeg, double lonDeg, double altMeters) => _inner = new WGS84Transform(latDeg, lonDeg, altMeters);

        /// <summary>What is inside now.</summary>
        public IGeographicTransform Inner => _inner;

        /// <summary>The tangent plane at the origin — exactly today's <c>SetOrigin</c> when it already is one.</summary>
        public void UseTangentPlane(double latDeg, double lonDeg, double altMeters)
        {
            if (_inner is WGS84Transform tangent) tangent.SetOrigin(latDeg, lonDeg, altMeters);
            else _inner = new WGS84Transform(latDeg, lonDeg, altMeters);
        }

        /// <summary>The exact UTM frame: local (0, 0) at (<paramref name="e0"/>, <paramref name="n0"/>) in <paramref name="zone"/>.</summary>
        public void UseUtm(int zone, bool south, double e0, double n0, double h0 = 0.0)
            => _inner = new UtmTransform(zone, south, e0, n0, h0);

        public void SetOrigin(double latDeg, double lonDeg, double altMeters) => _inner.SetOrigin(latDeg, lonDeg, altMeters);
        public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters) => _inner.ToCartesian(latDeg, lonDeg, altMeters);
        public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos) => _inner.ToGeodetic(localPos);
        public (double lat, double lon, double alt) Origin => _inner.Origin;
        public double GridConvergenceDeg(Vector3 localPos) => _inner.GridConvergenceDeg(localPos);
    }
}
