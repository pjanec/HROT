using System.Numerics;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.Map.Common.Tests
{
    public class HrotEnvironmentTests
    {
        private const double CoordinateToleranceDeg = 0.001;

        [Fact]
        public void CreateTkb_RegistersCatalogTemplates()
        {
            var tkb = HrotEnvironment.CreateTkb();

            Assert.True(tkb.TryGetByType(TkbEntityTypes.Tank_M1Abrams, out _));
            Assert.True(tkb.TryGetByType(TkbEntityTypes.Infantry_Rifleman, out _));
        }

        [Fact]
        public void CreateGeoTransform_UsesBerlinOrigin()
        {
            var transform = HrotEnvironment.CreateGeoTransform();

            var (lat, lon, _) = transform.ToGeodetic(Vector3.Zero);

            Assert.InRange(lat, 52.52 - CoordinateToleranceDeg, 52.52 + CoordinateToleranceDeg);
            Assert.InRange(lon, 13.405 - CoordinateToleranceDeg, 13.405 + CoordinateToleranceDeg);
        }

        [Fact]
        public void CreateParticipant_UsesProvidedDomainId()
        {
            using var participant = HrotEnvironment.CreateParticipant(10);

            Assert.Equal(10u, participant.DomainId);
        }
    }
}