using System.Numerics;
using Fdp.Core;

namespace Fdp.Modules.Geographic
{
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
    }
}
