using System;
using System.Numerics;
using Bagira.Map.Common;

namespace Bagira.Map.Common.Tests
{
    public class BagiraEnvironmentTests
    {
        private const double CoordinateToleranceDeg = 0.001;

        [Fact]
        public void CreateTkb_RegistersCatalogTemplates()
        {
            var tkb = BagiraEnvironment.CreateTkb();

            Assert.True(tkb.TryGetByType(TkbEntityTypes.Tank_M1Abrams, out _));
            Assert.True(tkb.TryGetByType(TkbEntityTypes.Infantry_Rifleman, out _));
        }

        [Fact]
        public void CreateGeoTransform_UsesBerlinOrigin()
        {
            var transform = BagiraEnvironment.CreateGeoTransform();

            var (lat, lon, _) = transform.ToGeodetic(Vector3.Zero);

            Assert.InRange(lat, 52.52 - CoordinateToleranceDeg, 52.52 + CoordinateToleranceDeg);
            Assert.InRange(lon, 13.405 - CoordinateToleranceDeg, 13.405 + CoordinateToleranceDeg);
        }

        [Fact]
        public void CreateParticipant_UsesProvidedDomainId()
        {
            using var participant = BagiraEnvironment.CreateParticipant(10);

            Assert.Equal(10u, participant.DomainId);
        }
    }
}