using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Map.Common.Replication.Utils;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Systems;

namespace Hrot.SimHost.Tests
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
                EntityInfo = new Hrot.NED.Descriptors.EntityInfo { EntityId = 0, Name = "TestUnit" },
            };

        private static EntityDescriptorUnion MakeGeoSpatialDescriptor(double lat, double lon) =>
            new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtWorldPos,
                WorldPos = new WorldPos
                {
                    EntityId = 0,
                    Pos = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = 100.0 },
                    Ori = new EulerOri { Heading = 90f },
                },
            };

        private static EntityDescriptorUnion MakeGeoSpatialDrDescriptor(float speedMs, float headingDeg) =>
            new EntityDescriptorUnion
            {
                _d = EDescriptorType.dtWorldPos,
                WorldPos = new WorldPos
                {
                    EntityId = 0,
                    Vel = new AngularVector { Azimuth = headingDeg, Elevation = 0f, Length = speedMs },
                    Acc = new AngularVector { Azimuth = 0f, Elevation = 0f, Length = 0f },
                    RotVel = new EulerRate { Heading = 0f, Pitch = 0f, Roll = 0f },
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

            long result = DescriptorMapper.ExtractTkbType(descriptors, out _);

            Assert.Equal(expectedTkbType, result);
        }

        [Fact]
        public void DescriptorMapper_EntityMasterDescriptor_ReturnsEmptyList()
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeEntityMasterDescriptor(tkbType: 10L),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

            Assert.Empty(components);
        }

        [Fact]
        public void DescriptorMapper_EntityMasterDescriptor_NoEntityMasterType()
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeEntityMasterDescriptor(tkbType: 10L),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

            Assert.DoesNotContain(components, c => c is EntityMaster);
        }

        [Fact]
        public void DescriptorMapper_EntityInfoDescriptor_ReturnsIgEntityData()
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeEntityInfoDescriptor(),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

            var igData = Assert.Single( components, c => c is Fdp.Core.EntityInfo );
            var entityInfo = (Fdp.Core.EntityInfo)igData;
            Assert.Equal("TestUnit", entityInfo.Name);
        }

        [Fact]
        public void DescriptorMapper_EntityInfoDescriptor_NoEntityInfoType()
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeEntityInfoDescriptor(),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

			Assert.DoesNotContain( components, c => c is Hrot.NED.Descriptors.EntityInfo );
        }

        [Fact]
        public void MapToComponents_GeoSpatialDescriptor_ContainsSimTransform()
        {
            var geo = new IdentityGeoTransform();
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeGeoSpatialDescriptor(lat: 48.0, lon: 16.0),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geo);

            var simTransform = Assert.Single(components.OfType<SimTransform>());
            // IdentityGeoTransform returns (lon, lat, alt) → Position.X = lon, Position.Y = lat
            Assert.Equal(16f, simTransform.Position.X, precision: 3);
            Assert.Equal(48f, simTransform.Position.Y, precision: 3);
        }

        [Fact]
        public void MapToComponents_GeoSpatialDescriptor_NoRawGeoSpatialType()
        {
            var geo = new IdentityGeoTransform();
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeGeoSpatialDescriptor(lat: 48.0, lon: 16.0),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geo);

            Assert.DoesNotContain(components, c => c is WorldPos);
        }

        [Fact]
        public void DescriptorMapper_GeoSpatialDescriptor_NullTransform_ProducesNoComponents()
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeGeoSpatialDescriptor(lat: 48.0, lon: 16.0),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

            Assert.Empty(components);
        }

        [Fact]
        public void DescriptorMapper_UnknownDescriptor_IsSkippedWithoutException()
        {
            // Any unknown _d value should be safe;
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

        [Fact]
        public void MapToComponents_GeoSpatialDRDescriptor_ContainsSimVelocity()
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeGeoSpatialDrDescriptor(speedMs: 15f, headingDeg: 90f),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

            var simVelocity = Assert.Single(components.OfType<SimVelocity>());
            Assert.Equal(15f, simVelocity.Linear.Length(), precision: 3);

            float heading = SimTransformBridgeSystem.VelocityToAzimuthDeg(simVelocity.Linear, fallback: 0f);
            Assert.Equal(90f, heading, precision: 1);
        }

        [Fact]
        public void MapToComponents_GeoSpatialDRDescriptor_NoRawGeoSpatialDRType()
        {
            var descriptors = new List<EntityDescriptorUnion>
            {
                MakeGeoSpatialDrDescriptor(speedMs: 15f, headingDeg: 90f),
            };

            var components = DescriptorMapper.MapToComponents(descriptors, geoTransform: null);

            Assert.DoesNotContain(components, c => c is WorldPos);
        }
    }
}
