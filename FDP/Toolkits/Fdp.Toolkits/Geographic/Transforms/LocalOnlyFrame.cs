using System;
using System.Numerics;

namespace Fdp.Modules.Geographic.Transforms
{
    /// <summary>The refusal of a geodetic conversion on a frame with no place on the Earth — the debug API answers it as "this terrain has no geo frame".</summary>
    public sealed class NoGeoFrameException : InvalidOperationException
    {
        public NoGeoFrameException() : base("this terrain has no geo frame — its local metres have no place on the Earth (no 'origin' in terrain.json)") { }
    }

    /// <summary>
    /// ⭐ CE-4396 (docs/DESIGN_Geo_Origin.md §6, UE-2, R-4197) — a terrain that declares NO origin: positions are metres with no place on the
    /// Earth. <see cref="HasFrame"/> is false and every geodetic conversion is refused with <see cref="NoGeoFrameException"/> (⛔ no UTM frame at
    /// (0 N, 0 E) — a fake place in the Atlantic, the very bug CE-236 removed; R-229 "no default place"). The wire says <c>Frame = none</c>.
    /// </summary>
    public sealed class LocalOnlyFrame : IGeographicTransform
    {
        public bool HasFrame => false;
        public void SetOrigin(double latDeg, double lonDeg, double altMeters) => throw new NoGeoFrameException();
        public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters) => throw new NoGeoFrameException();
        public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos) => throw new NoGeoFrameException();
        public (double lat, double lon, double alt) Origin => (0.0, 0.0, 0.0);
        public double GridConvergenceDeg(Vector3 localPos) => 0.0;
    }
}
