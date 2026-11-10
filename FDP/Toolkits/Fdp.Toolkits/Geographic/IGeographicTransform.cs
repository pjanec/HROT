using System.Numerics;
using Fdp.Core;

namespace Fdp.Modules.Geographic
{
    // ⭐ D7 (restore design) — content / service / index, never world state: a keyframe must not carry it, and an apply must
    //   not replace the loaded instance (R-3043).
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    [ComponentId(GlobalComponentIds.IGeographicTransform)]
    public interface IGeographicTransform
    {
        void SetOrigin(double latDeg, double lonDeg, double altMeters);
        Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters);
        (double lat, double lon, double alt) ToGeodetic(Vector3 localPos);

        /// <summary>
        /// The geodetic origin (degrees latitude/longitude, metres altitude) most recently set via
        /// <see cref="SetOrigin"/>. Ported from the AI-debug API (the <c>/world/info</c> endpoint).
        /// A default of (0,0,0) is provided so lightweight test doubles need not implement it.
        /// </summary>
        (double lat, double lon, double alt) Origin => (0.0, 0.0, 0.0);

        /// <summary>
        /// ⭐ CE-4394 (docs/DESIGN_Terrain_Import_UTM.md §12.2 U-3) — the angle (degrees) from TRUE north to the LOCAL frame's north (+Y) at
        /// <paramref name="localPos"/>: a local (grid) heading becomes a true heading as <c>true = local + this</c>. 0 for the tangent plane
        /// (its +Y is true north at the origin, the convention this engine has always used) and for every test double; the UTM frame returns
        /// the grid convergence. ⛔ Applied only at a gateway that speaks a TRUE-north heading (the debug API's geo conversions, the Player's
        /// geodetic ghost) — every heading inside the engine is local.
        /// </summary>
        double GridConvergenceDeg(Vector3 localPos) => 0.0;
    }
}
