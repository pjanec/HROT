using System.Collections.Generic;
using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.SimHost.Util;
using CarKinem.Core;
using Fdp.Kernel;
using Fdp.Modules.Geographic;

namespace Bagira.SimHost.Tests
{
    public class DescriptorMapperTests
    {
        // ─── Stub geographic transform for unit tests ─────────────────────────

        private sealed class IdentityGeoTransform : IGeographicTransform
        {
            public void SetOrigin(double lat, double lon, double alt) { }

            /// <summary>Returns (lon, lat, alt) as a trivial Cartesian vector for test assertions.</summary>
            public Vector3 ToCartesian(double lat, double lon, double alt)
                => new Vector3((float)lon, (float)lat, (float)alt);

            public (double lat, double lon, double alt) ToGeodetic(Vector3 pos)
                => (pos.Y, pos.X, pos.Z);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static EntityDescriptorUnion MakeEntityMasterDescriptor(long tkbType) =>
            new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtEntityMaster,
                EntityMaster = new EntityMaster { EntityId = 0, TkbType = tkbType },
            };

        private static EntityDescriptorUnion MakeEntityInfoDescriptor() =>
            new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtEntityInfo,
                EntityInfo = new EntityInfo { EntityId = 0, Name = "TestUnit" },
            };

        private static EntityDescriptorUnion MakeGeoSpatialDescriptor(double lat, double lon) =>
            new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtGeoSpatial,
                GeoSpatial = new GeoSpatial
                {
                    EntityId = 0,
                    Pos = new GeoPosition { Latitude = lat, Longitude = lon, Altitude = 100.0 },
                    Rot = new OrientationHPR { Heading = 90f },
                },
            };

        // ─── Tests ────────────────────────────────────────────────────────────

        [Fact]
        public void DescriptorMapper_ExtractsTkbType_FromEntityMaster()
        {
            const long expectedTkbType = 42L;
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeEntityMasterDescriptor(expectedTkbType),
            };

            long result = DescriptorMapper.ExtractTkbType(descriptors);

            Assert.Equal(expectedTkbType, result);
        }

        [Fact]
        public void DescriptorMapper_EntityMasterDescriptor_AddsEntityMasterComponent()
        {
            const long tkbType = 10L;
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeEntityMasterDescriptor(tkbType),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

            // Should contain exactly one EntityMaster object with the correct TkbType
            Assert.Single(components);
            var master = Assert.IsType<EntityMaster>(components[0]);
            Assert.Equal(tkbType, master.TkbType);
        }

        /// <summary>
        /// SC1 (TASK-IF001): A non-vehicle GeoSpatial descriptor must NOT produce a
        /// <see cref="VehicleState"/> component — that responsibility belongs to the TKB template.
        /// </summary>
        [Fact]
        public void DescriptorMapper_GeoSpatialDescriptor_DoesNotAddVehicleState()
        {
            var geo = new IdentityGeoTransform();
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeGeoSpatialDescriptor(lat: 48.0, lon: 16.0),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geo);

            // Must produce exactly GeoSpatial + SimTransform (no VehicleState).
            Assert.Equal(2, components.Count);

            var geoSpatial = Assert.IsType<GeoSpatial>(components[0]);
            Assert.Equal(48.0, geoSpatial.Pos.Latitude, precision: 5);

            var simTransform = Assert.IsType<SimTransform>(components[1]);
            // IdentityGeoTransform returns (lon, lat, alt) → Position.X = lon, Position.Y = lat
            Assert.Equal(16f, simTransform.Position.X, precision: 3);
            Assert.Equal(48f, simTransform.Position.Y, precision: 3);

            // Explicitly assert no VehicleState is present — the TKB template owns it.
            Assert.DoesNotContain(components, c => c is VehicleState);
        }

        /// <summary>
        /// A GeoSpatial descriptor with no geo transform should produce only GeoSpatial
        /// (no SimTransform, no VehicleState).
        /// </summary>
        [Fact]
        public void DescriptorMapper_GeoSpatialDescriptor_NullTransform_AddsOnlyGeoSpatial()
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeGeoSpatialDescriptor(lat: 48.0, lon: 16.0),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

            Assert.Single(components);
            Assert.IsType<GeoSpatial>(components[0]);
            Assert.DoesNotContain(components, c => c is VehicleState);
        }

        [Fact]
        public void DescriptorMapper_UnknownDescriptor_IsSkippedWithoutException()
        {
            // dtGeoSpatialDR is recognised but any future unknown _d value should also be safe;
            // here we deliberately use a descriptor type that exists but verify the mapper
            // does not throw and simply skips items it cannot handle.
            // We achieve an "unrecognised" scenario by building a custom union with a made-up
            // discriminator value cast from an out-of-range int.
            var unknown = new EntityDescriptorUnion
            {
                _d = (EDescriptorType)999,  // Value outside the known enum range
            };

            var descriptors = new List<EntityDescriptorUnion> { unknown };

            // Must not throw; must return an empty list
            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

            Assert.Empty(components);
        }
    }
}
