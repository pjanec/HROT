using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core.Abstractions;
using Xunit;

namespace Hrot.IG.Tests
{
    public class GeoSpatialTranslatorTests
    {
        private const long KnownId   = 5L;
        private const long UnknownId = 88L;

        [Fact]
        public void Decode_KnownEntity_SetsNetworkPosition()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            var entityMap = new NetworkEntityMap();
            var entity    = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var geoTransform = new StubGeoTransform(new Vector3(1f, 2f, 3f));
            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new TestGeoSpatialIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new WorldPos
            {
                EntityId = (int)KnownId,
                Pos = new GeoPoint { Latitude = 51, Longitude = 0, Altitude = 0 }
            }, cmd, repo);

            Assert.True(cmd.SetNetworkPositionCalled);
            Assert.Equal(new Vector3(1f, 2f, 3f), cmd.LastNetworkPosition!.Value.LastPosition);
        }

        /// <summary>
        /// Bug fix (Task 11): when the entity already has a <see cref="SimTransform"/>, the
        /// translator must still call SetComponent to update it so the map renders the entity
        /// at its new position rather than the original spawn position.
        /// </summary>
        [Fact]
        public void Decode_KnownEntity_UpdatesSimTransformWhenAlreadyPresent()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            var entityMap = new NetworkEntityMap();
            var entity    = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform
            {
                Position = new Vector3(0f, 0f, 0f),
                Rotation = Quaternion.Identity
            });
            entityMap.Register(KnownId, entity);

            var newPos       = new Vector3(10f, 20f, 30f);
            var geoTransform = new StubGeoTransform(newPos);
            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new TestGeoSpatialIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new WorldPos
            {
                EntityId = (int)KnownId,
                Pos = new GeoPoint { Latitude = 51, Longitude = 0, Altitude = 0 }
            }, cmd, repo);

            Assert.True(cmd.SetSimTransformCalled,
                "SetComponent<SimTransform> must be called to move the entity on the map");
            Assert.Equal(newPos, cmd.LastSetSimTransform!.Value.Position);
        }

        /// <summary>
        /// When the entity already has a <see cref="SimTransform"/>, the translator must
        /// NOT add another copy via the command buffer — it only enqueues SetComponent for
        /// the existing one (and also for <see cref="NetworkTransform"/>).
        /// </summary>
        [Fact]
        public void Decode_KnownEntity_DoesNotAddSimTransformIfAlreadyPresent()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            repo.RegisterComponent<SimTransform>();
            var entityMap = new NetworkEntityMap();
            var entity    = repo.CreateEntity();
            repo.AddComponent(entity, new SimTransform()); // entity already has SimTransform
            entityMap.Register(KnownId, entity);

            var geoTransform = new StubGeoTransform(new Vector3(4f, 5f, 6f));
            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new TestGeoSpatialIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new WorldPos
            {
                EntityId = (int)KnownId,
                Pos = new GeoPoint { Latitude = 1, Longitude = 2, Altitude = 3 }
            }, cmd, repo);

            Assert.False(cmd.AddSimTransformCalled,
                "AddComponent<SimTransform> must not be called when entity already has a SimTransform");
        }

        /// <summary>
        /// When the entity is not yet in the map, the translator must create a ghost and
        /// still enqueue the <see cref="NetworkTransform"/> component update.
        /// </summary>
        [Fact]
        public void Decode_UnknownEntity_CreatesGhostAndSetsNetworkPosition()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>(); // required by GhostCreationSystem
            repo.RegisterComponent<GhostStateTracker>(); // required by GhostCreationSystem
            repo.RegisterComponent<SimTransform>();
            var entityMap = new NetworkEntityMap();

            var geoTransform = new StubGeoTransform(new Vector3(7f, 8f, 9f));
            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new TestGeoSpatialIngressTranslator(participant, entityMap, geoTransform, ghostCreationSystem);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new WorldPos
            {
                EntityId = (int)UnknownId,
                Pos = new GeoPoint { Latitude = 10, Longitude = 20, Altitude = 0 }
            }, cmd, repo);

            Assert.True(entityMap.TryGetEntity(UnknownId, out _),
                "Ghost must be registered in entityMap after encountering unknown entity");
            Assert.True(cmd.SetNetworkPositionCalled,
                "SetComponent<NetworkTransform> must be called even for freshly created ghost entities");
        }

        private sealed class TestGeoSpatialIngressTranslator : GeoSpatialIngressTranslator
        {
            public TestGeoSpatialIngressTranslator(
                DdsParticipant participant,
                NetworkEntityMap entityMap,
                IGeographicTransform geoTransform,
                GhostCreationSystem ghostCreationSystem)
                : base(participant, entityMap, geoTransform, ghostCreationSystem, localNodeId: 0)
            {
            }

            public void DecodeForTest(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view)
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
            public bool AddSimTransformCalled    { get; private set; }
            public bool SetSimTransformCalled    { get; private set; }
            public NetworkTransform? LastNetworkPosition  { get; private set; }
            public SimTransform?     LastSetSimTransform  { get; private set; }

            public Entity CreateEntity() => new Entity();
            public void DestroyEntity(Entity entity) { }
            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
            {
                if (component is SimTransform)
                    AddSimTransformCalled = true;
            }
            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
            {
                if (component is NetworkTransform position)
                {
                    SetNetworkPositionCalled = true;
                    LastNetworkPosition = position;
                }
                if (component is SimTransform st)
                {
                    SetSimTransformCalled = true;
                    LastSetSimTransform = st;
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
