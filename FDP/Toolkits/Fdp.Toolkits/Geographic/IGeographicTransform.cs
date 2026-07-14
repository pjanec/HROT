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
    }
}
