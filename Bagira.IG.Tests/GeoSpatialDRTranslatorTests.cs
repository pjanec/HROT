using System.Numerics;
using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.IG.Translators;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Bagira.IG.Tests
{
    public class GeoSpatialDRTranslatorTests
    {
        private const long KnownId = 1L;
        private const long UnknownId = 99L;

        [Fact]
        public void Decode_KnownEntity_SetsNetworkVelocity()
        {
            using var participant = new DdsParticipant(0);
            var repo = new EntityRepository();
            var entityMap = new NetworkEntityMap();
            var entity = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var translator = new TestGeoSpatialDRTranslator(participant, entityMap, new StubGeoTransform());
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new GeoSpatialDR
            {
                EntityId = (int)KnownId,
                Vel = new DAL3 { Azimuth = 0, Elevation = 0, Length = 10 }
            }, cmd, repo);

            Assert.True(cmd.SetNetworkVelocityCalled);
            var velocity = cmd.LastNetworkVelocity!.Value.Value;
            Assert.Equal(0f, velocity.X, 3);
            Assert.Equal(10f, velocity.Y, 3);
            Assert.Equal(0f, velocity.Z, 3);
        }

        [Fact]
        public void Decode_UnknownEntity_IsSkipped()
        {
            using var participant = new DdsParticipant(0);
            var repo = new EntityRepository();
            var entityMap = new NetworkEntityMap();

            var translator = new TestGeoSpatialDRTranslator(participant, entityMap, new StubGeoTransform());
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new GeoSpatialDR
            {
                EntityId = (int)UnknownId,
                Vel = new DAL3 { Azimuth = 0, Elevation = 0, Length = 10 }
            }, cmd, repo);

            Assert.False(cmd.SetNetworkVelocityCalled);
        }

        private sealed class TestGeoSpatialDRTranslator : GeoSpatialDRTranslator
        {
            public TestGeoSpatialDRTranslator(
                DdsParticipant participant,
                NetworkEntityMap entityMap,
                IGeographicTransform geoTransform)
                : base(participant, entityMap, geoTransform)
            {
            }

            public void DecodeForTest(in GeoSpatialDR data, IEntityCommandBuffer cmd, ISimulationView view)
            {
                base.Decode(data, cmd, view);
            }
        }

        private sealed class StubGeoTransform : IGeographicTransform
        {
            public void SetOrigin(double latDeg, double lonDeg, double altMeters) { }
            public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters) => Vector3.Zero;
            public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos) => (0, 0, 0);
        }

        private sealed class RecordingCommandBuffer : IEntityCommandBuffer
        {
            public bool SetNetworkVelocityCalled { get; private set; }
            public NetworkVelocity? LastNetworkVelocity { get; private set; }

            public Entity CreateEntity() => new Entity();
            public void DestroyEntity(Entity entity) { }
            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged { }
            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
            {
                if (component is NetworkVelocity velocity)
                {
                    SetNetworkVelocityCalled = true;
                    LastNetworkVelocity = velocity;
                }
            }
            public void RemoveComponent<T>(Entity entity) where T : unmanaged { }
            public void AddManagedComponent<T>(Entity entity, T? component) where T : class { }
            public void SetManagedComponent<T>(Entity entity, T? component) where T : class { }
            public void RemoveManagedComponent<T>(Entity entity) where T : class { }
            public void PublishEvent<T>(in T evt) where T : unmanaged { }
            public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int size) { }
            public void SetManagedComponentRaw(Entity entity, int typeId, object obj) { }
            public void SetLifecycleState(Entity entity, EntityLifecycle state) { }
        }
    }
}
