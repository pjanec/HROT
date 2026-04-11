using System.Numerics;

namespace Fdp.Modules.Geographic
{
    public interface IGeographicTransform
    {
        void SetOrigin(double latDeg, double lonDeg, double altMeters);
        Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters);
        (double lat, double lon, double alt) ToGeodetic(Vector3 localPos);
    }
}
