using CycloneDDS.Runtime;
using Fdp.Modules.Geographic.Transforms;
using Bagira.Map.Definitions.Tkb;
using Fdp.Toolkit.Tkb;

namespace Bagira.Map.Common
{
    /// <summary>
    /// Shared stateless factory for common Bagira runtime primitives.
    /// </summary>
    public static class BagiraEnvironment
    {
        private const double BerlinLatitudeDeg = 52.52;
        private const double BerlinLongitudeDeg = 13.405;
        private const double BerlinAltitudeMeters = 0.0;

        public static TkbDatabase CreateTkb()
        {
            var tkb = new TkbDatabase();
            BdcTkbCatalog.RegisterAll(tkb);
            RouteTkbExtensions.ApplyRoutePlanToBlueprint(tkb);
            return tkb;
        }

        public static WGS84Transform CreateGeoTransform()
        {
            var transform = new WGS84Transform();
            transform.SetOrigin(BerlinLatitudeDeg, BerlinLongitudeDeg, BerlinAltitudeMeters);
            return transform;
        }

        public static DdsParticipant CreateParticipant(int domainId)
        {
            return new DdsParticipant((uint)domainId);
        }
    }
}