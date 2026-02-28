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
    public class GeoSpatialTranslatorTests
    {
        private const long KnownId = 5L;

        [Fact]
        public void Decode_KnownEntity_SetsNetworkPosition()
        {
            using var participant = new DdsParticipant(0);
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            var entityMap = new NetworkEntityMap();
            var entity = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var geoTransform = new StubGeoTransform(new Vector3(1f, 2f, 3f));
            var translator = new TestGeoSpatialTranslator(participant, entityMap, geoTransform);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new GeoSpatial
            {
                EntityId = (int)KnownId,
                Pos = new GeoPosition { Latitude = 51, Longitude = 0, Altitude = 0 }
            }, cmd, repo);

            Assert.True(cmd.SetNetworkPositionCalled);
            Assert.Equal(new Vector3(1f, 2f, 3f), cmd.LastNetworkPosition!.Value.Value);
        }

        [Fact]
        public void Decode_KnownEntity_DoesNotSetSimTransformDirectly()
        {
            using var participant = new DdsParticipant(0);
            var repo = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            var entityMap = new NetworkEntityMap();
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform());
            entityMap.Register(KnownId, entity);

            var geoTransform = new StubGeoTransform(new Vector3(4f, 5f, 6f));
            var translator = new TestGeoSpatialTranslator(participant, entityMap, geoTransform);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new GeoSpatial
            {
                EntityId = (int)KnownId,
                Pos = new GeoPosition { Latitude = 1, Longitude = 2, Altitude = 3 }
            }, cmd, repo);

            Assert.False(cmd.SetSimTransformCalled);
        }

        private sealed class TestGeoSpatialTranslator : GeoSpatialTranslator
        {
            public TestGeoSpatialTranslator(
                DdsParticipant participant,
                NetworkEntityMap entityMap,
                IGeographicTransform geoTransform)
                : base(participant, entityMap, geoTransform)
            {
            }

            public void DecodeForTest(in GeoSpatial data, IEntityCommandBuffer cmd, ISimulationView view)
            {
                base.Decode(data, cmd, view);
            }
        }

        private sealed class StubGeoTransform : IGeographicTransform
        {
            private readonly Vector3 _result;

            public StubGeoTransform(Vector3 result) => _result = result;

            public void SetOrigin(double latDeg, double lonDeg, double altMeters) { }

            public Vector3 ToCartesian(double latDeg, double lonDeg, double altMeters) => _result;

            public (double lat, double lon, double alt) ToGeodetic(Vector3 localPos)
                => (0, 0, 0);
        }

        private sealed class RecordingCommandBuffer : IEntityCommandBuffer
        {
            public bool SetNetworkPositionCalled { get; private set; }
            public bool SetSimTransformCalled { get; private set; }
            public NetworkPosition? LastNetworkPosition { get; private set; }

            public Entity CreateEntity() => new Entity();
            public void DestroyEntity(Entity entity) { }
            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged { }
            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
            {
                if (component is NetworkPosition position)
                {
                    SetNetworkPositionCalled = true;
                    LastNetworkPosition = position;
                }

                if (component is SimTransform)
                    SetSimTransformCalled = true;
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
