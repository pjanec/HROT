using System.Numerics;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Hrot.Map.Common.Replication.Ingress;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Abstractions;
using Xunit;
using Fdp.Interfaces;

namespace Hrot.IG.Tests
{
    /// <summary>
    /// Tests for the velocity plane of the unified <see cref="GeoSpatialIngressTranslator"/>.
    /// These scenarios previously lived in a separate GeoSpatialDRIngressTranslator which was
    /// merged into the unified translator.
    /// </summary>
    public class GeoSpatialDRTranslatorTests
    {
        private const long KnownId   = 1L;
        private const long UnknownId = 99L;

        [Fact]
        public void Decode_KnownEntity_SetsNetworkVelocity()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            var entityMap = new NetworkEntityMap();
            var entity    = repo.CreateEntity();
            entityMap.Register(KnownId, entity);

            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new TestGeoSpatialIngressTranslator(participant, entityMap, new StubGeoTransform(), ghostCreationSystem);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new WorldPos
            {
                EntityId = (int)KnownId,
                Vel = new AngularVector { Azimuth = 0, Elevation = 0, Length = 10 }
            }, cmd, repo);

            Assert.True(cmd.SetNetworkVelocityCalled);
            var velocity = cmd.LastNetworkVelocity!.Value.Value;
            Assert.Equal(0f,  velocity.X, 3);
            Assert.Equal(10f, velocity.Y, 3);
            Assert.Equal(0f,  velocity.Z, 3);
        }

        [Fact]
        public void Decode_UnknownEntity_CreatesGhostAndSetsNetworkVelocity()
        {
            using var participant = new DdsParticipant(0);
            var repo      = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            var entityMap = new NetworkEntityMap();

            var ghostCreationSystem = new GhostCreationSystem(entityMap);
            var translator = new TestGeoSpatialIngressTranslator(participant, entityMap, new StubGeoTransform(), ghostCreationSystem);
            var cmd = new RecordingCommandBuffer();

            translator.DecodeForTest(new WorldPos
            {
                EntityId = (int)UnknownId,
                Vel = new AngularVector { Azimuth = 0, Elevation = 0, Length = 10 }
            }, cmd, repo);

            Assert.True(entityMap.TryGetEntity(UnknownId, out _),
                "Ghost must be registered in entityMap after encountering unknown entity");
            Assert.True(cmd.SetNetworkVelocityCalled,
                "SetComponent<NetworkVelocity> must be called even for freshly created ghost entities");
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

