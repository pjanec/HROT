using CycloneDDS.Runtime;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Tkb;
using System;
using System.Reflection;

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
        private const string TkbCatalogTypeName = "Bagira.Map.Definitions.Tkb.BdcTkbCatalog, Bagira.Map.Definitions";

        public static TkbDatabase CreateTkb()
        {
            var tkb = new TkbDatabase();
            RegisterBdcCatalog(tkb);
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

        private static void RegisterBdcCatalog(TkbDatabase tkb)
        {
            var catalogType = Type.GetType(TkbCatalogTypeName, throwOnError: false);
            if (catalogType == null)
                throw new InvalidOperationException("Could not load Bagira.Map.Definitions TKB catalog type.");

            var registerAll = catalogType.GetMethod(
                "RegisterAll",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(TkbDatabase) },
                modifiers: null);

            if (registerAll == null)
                throw new InvalidOperationException("Could not find BdcTkbCatalog.RegisterAll(TkbDatabase) method.");

            registerAll.Invoke(null, new object[] { tkb });
        }
    }
}