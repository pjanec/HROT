using System.Numerics;
using System.Threading;
using Hrot.NED.Common;
using Hrot.SimHost.Network;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.Map.Common.Translators;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Xunit;

// Aliases to distinguish ECS navigation types from DDS wire types (dual-enum pattern).
using EcsNavigationIntent = FDP.Toolkit.Navigation.NavigationIntent;
using EcsNavigationStatus = FDP.Toolkit.Navigation.NavigationStatus;
using EcsNavigationMode   = FDP.Toolkit.Navigation.NavigationMode;
using EcsNavigationResult = FDP.Toolkit.Navigation.NavigationResult;
using DdsNavigationIntent = Hrot.NED.Descriptors.NavigationIntent;
using DdsNavigationStatus = Hrot.NED.Descriptors.NavigationStatus;
using ENavigationMode     = Hrot.NED.Descriptors.ENavigationMode;
using ENavigationResult   = Hrot.NED.Descriptors.ENavigationResult;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for the four concrete navigation translator classes (MOD1-P3T4):
    /// <see cref="NavigationIntentEgressTranslator"/>, <see cref="NavigationIntentIngressTranslator"/>,
    /// <see cref="NavigationStatusEgressTranslator"/>, and <see cref="NavigationStatusIngressTranslator"/>.
    /// </summary>
    [Collection("SimHostDds")]
    public class NavigationTranslatorTests
    {
        // ── Stubs ─────────────────────────────────────────────────────────────

        private sealed class IdentityGeoTransform : IGeographicTransform
        {
            public void SetOrigin(double lat, double lon, double alt) { }

            public Vector3 ToCartesian(double lat, double lon, double alt)
                => new Vector3((float)lon, (float)lat, (float)alt);

            public (double lat, double lon, double alt) ToGeodetic(Vector3 pos)
                => (pos.Y, pos.X, pos.Z);
        }

        /// <summary>
        /// Direct command buffer that applies ECS mutations immediately to a repo.
        /// </summary>
        private class DirectCommandBuffer : IEntityCommandBuffer
        {
            private readonly EntityRepository _repo;
            public DirectCommandBuffer(EntityRepository repo) => _repo = repo;

            public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
                => _repo.SetComponent(entity, component);

            public void AddComponent<T>(Entity entity, in T component) where T : unmanaged
                => _repo.AddComponent(entity, component);

            public void AddManagedComponent<T>(Entity entity, T? component) where T : class
                => _repo.SetManagedComponent(entity, component!);

            public Entity CreateEntity() => _repo.CreateEntity();

            public unsafe void SetComponentRaw(Entity entity, int typeId, void* ptr, int sizeBytes)
                => throw new System.NotImplementedException();

            public void SetManagedComponentRaw(Entity entity, int typeId, object component)
                => throw new System.NotImplementedException();

            public void DestroyEntity(Entity entity) => _repo.DestroyEntity(entity);
            public void PublishEvent<T>(in T evt) where T : unmanaged { }
            public void RemoveComponent<T>(Entity entity) where T : unmanaged => _repo.RemoveComponent<T>(entity);
            public void RemoveManagedComponent<T>(Entity entity) where T : class { }
            public void SetLifecycleState(Entity entity, EntityLifecycle state) { }
            public void SetManagedComponent<T>(Entity entity, T? component) where T : class
                => _repo.SetManagedComponent(entity, component!);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static EntityRepository CreateWorldForEgress()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<EcsNavigationIntent>();
            repo.RegisterComponent<EcsNavigationStatus>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            return repo;
        }

        private static EntityRepository CreateWorldForIngress()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<EcsNavigationIntent>();
            repo.RegisterComponent<EcsNavigationStatus>();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            return repo;
        }

        // ── NavigationIntentEgressTranslator ──────────────────────────────────

        [Fact]
        public void NavigationIntentEgressTranslator_WritesOnce_PerOwnedEntity()
        {
            const uint domainId = 220u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<DdsNavigationIntent>(participant, "NavigationIntent");
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();
            var translator   = new NavigationIntentEgressTranslator(participant, entityMap, geoTransform);

            using var repo = CreateWorldForEgress();
            var entity     = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(42));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            repo.AddComponent(entity, new EcsNavigationIntent
            {
                IntentId         = 7,
                Mode             = EcsNavigationMode.DirectPoint,
                TargetSpeed      = 10f,
                ArrivalRadius    = 5f,
                FinalDestination = new Vector2(100f, 200f),
            });

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);
            Thread.Sleep(200);

            using var loan = reader.Take();
            int count = 0;
            DdsNavigationIntent published = default;
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                count++;
                published = sample.Data;
            }

            Assert.Equal(1, count);
            Assert.Equal(42, published.EntityId);
            Assert.Equal(7u, published.IntentId);
            Assert.Equal(ENavigationMode.NAV_DIRECT_POINT, published.Mode);
            Assert.Equal(10f, published.TargetSpeed, precision: 2);
        }

        [Fact]
        public void NavigationIntentEgressTranslator_DoesNotPublish_ForNoneMode()
        {
            const uint domainId = 220u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<DdsNavigationIntent>(participant, "NavigationIntent");
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();
            var translator   = new NavigationIntentEgressTranslator(participant, entityMap, geoTransform);

            using var repo = CreateWorldForEgress();
            var entity     = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(43));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            repo.AddComponent(entity, new EcsNavigationIntent
            {
                Mode = EcsNavigationMode.None, // inactive — must be skipped
            });

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);
            Thread.Sleep(200);

            using var loan = reader.Take();
            bool found = false;
            foreach (var sample in loan)
            {
                if (sample.IsValid && sample.Data.EntityId == 43)
                    found = true;
            }

            Assert.False(found, "Entities with Mode=None must not be published.");
        }

        // ── NavigationIntentIngressTranslator ─────────────────────────────────

        [Fact]
        public void NavigationIntentIngressTranslator_Ignores_UnknownEntity()
        {
            // No entity is registered in the map → PollIngress must not throw.
            const uint domainId = 221u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();
            var translator   = new NavigationIntentIngressTranslator(participant, entityMap, geoTransform);

            using var repo = CreateWorldForIngress();
            var cmd        = new DirectCommandBuffer(repo);

            // No DDS data written → Take() returns empty → no throw.
            var ex = Record.Exception(() => translator.PollIngress(cmd, repo));
            Assert.Null(ex);
        }

        [Fact]
        public void NavigationIntentIngressTranslator_SetsComponent_WhenEntityKnown()
        {
            const uint domainId = 222u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<DdsNavigationIntent>(participant, "NavigationIntent");
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();
            var translator   = new NavigationIntentIngressTranslator(participant, entityMap, geoTransform);

            using var repo  = CreateWorldForIngress();
            var entity      = repo.CreateEntity();
            repo.AddComponent(entity, new EcsNavigationIntent());   // component must exist for SetComponent
            entityMap.Register(55, entity);

            Thread.Sleep(200); // let publications match

            writer.Write(new DdsNavigationIntent
            {
                EntityId         = 55,
                IntentId         = 3,
                Mode             = ENavigationMode.NAV_DIRECT_POINT,
                TargetSpeed      = 8f,
                ArrivalRadius    = 10f,
                FinalDestination = new GeoPoint { Latitude = 51.0, Longitude = 0.1, Altitude = 0.0 }
            });
            Thread.Sleep(200);

            var cmd = new DirectCommandBuffer(repo);
            translator.PollIngress(cmd, repo);

            var intent = repo.GetComponent<EcsNavigationIntent>(entity);
            Assert.Equal(3u, intent.IntentId);
            Assert.Equal(EcsNavigationMode.DirectPoint, intent.Mode);
            Assert.Equal(8f, intent.TargetSpeed, precision: 2);
        }

        // ── NavigationStatusEgressTranslator ──────────────────────────────────

        [Fact]
        public void NavigationStatusEgressTranslator_WritesOnce_PerOwnedEntity()
        {
            const uint domainId = 223u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<DdsNavigationStatus>(participant, "NavigationStatus");
            var entityMap  = new NetworkEntityMap();
            var translator = new NavigationStatusEgressTranslator(participant, entityMap);

            using var repo = CreateWorldForEgress();
            var entity     = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(77));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            repo.AddComponent(entity, new EcsNavigationStatus
            {
                IntentId = 5,
                Result   = EcsNavigationResult.Arrived,
            });

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);
            Thread.Sleep(200);

            using var loan = reader.Take();
            int count = 0;
            DdsNavigationStatus published = default;
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                count++;
                published = sample.Data;
            }

            Assert.Equal(1, count);
            Assert.Equal(77, published.EntityId);
            Assert.Equal(5u, published.IntentId);
            Assert.Equal(ENavigationResult.RES_ARRIVED, published.Result);
        }

        // ── NavigationStatusIngressTranslator ─────────────────────────────────

        [Fact]
        public void NavigationStatusIngressTranslator_Ignores_UnknownEntity()
        {
            const uint domainId = 224u;
            using var participant = new DdsParticipant(domainId);
            var entityMap  = new NetworkEntityMap();
            var translator = new NavigationStatusIngressTranslator(participant, entityMap);

            using var repo = CreateWorldForIngress();
            var cmd        = new DirectCommandBuffer(repo);

            var ex = Record.Exception(() => translator.PollIngress(cmd, repo));
            Assert.Null(ex);
        }

        [Fact]
        public void NavigationStatusIngressTranslator_SetsComponent_WhenEntityKnown()
        {
            const uint domainId = 225u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<DdsNavigationStatus>(participant, "NavigationStatus");
            var entityMap  = new NetworkEntityMap();
            var translator = new NavigationStatusIngressTranslator(participant, entityMap);

            using var repo  = CreateWorldForIngress();
            var entity      = repo.CreateEntity();
            repo.AddComponent(entity, new EcsNavigationStatus());   // must exist for SetComponent
            entityMap.Register(88, entity);

            Thread.Sleep(200);

            writer.Write(new DdsNavigationStatus
            {
                EntityId = 88,
                IntentId = 9,
                Result   = ENavigationResult.RES_ARRIVED,
            });
            Thread.Sleep(200);

            var cmd = new DirectCommandBuffer(repo);
            translator.PollIngress(cmd, repo);

            var status = repo.GetComponent<EcsNavigationStatus>(entity);
            Assert.Equal(9u, status.IntentId);
            Assert.Equal(EcsNavigationResult.Arrived, status.Result);
        }

        // ── KinematicTranslatorPack — correct type assertions ─────────────────

        [Fact]
        public void KinematicTranslatorPack_ContainsNavigationStatusEgressTranslator_TypeCheck()
        {
            const uint domainId = 210u;
            using var participant = new DdsParticipant(domainId);
            var entityMap    = new NetworkEntityMap();
            var geoTransform = new IdentityGeoTransform();

            bool found = false;
            foreach (var t in KinematicTranslatorPack.Create(participant, entityMap, geoTransform))
            {
                if (t is NavigationStatusEgressTranslator)
                    found = true;
            }

            Assert.True(found, "KinematicTranslatorPack must yield NavigationStatusEgressTranslator.");
        }

        // ── PACK-N001 SC-2: DDS wire struct has ProgressS field ───────────────

        /// <summary>
        /// PACK-N001 SC-2: The HROT NED DDS <see cref="DdsNavigationStatus"/> struct must
        /// expose a public instance field named <c>ProgressS</c> of type <c>float</c>.
        /// Verified via reflection so the test fails immediately on field removal.
        /// </summary>
        [Fact]
        public void NedNavigationStatus_HasProgressSField()
        {
            var fields = typeof(DdsNavigationStatus)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.Contains(fields, f => f.Name == "ProgressS" && f.FieldType == typeof(float));
        }

        // ── PACK-N003: Translator ProgressS mapping ───────────────────────────

        /// <summary>
        /// PACK-N003 SC-1: Egress translator must include <c>ProgressS</c> in the published
        /// DDS sample and must not zero out existing <c>IntentId</c>/<c>Result</c> values.
        /// </summary>
        [Fact]
        public void NavigationStatusEgress_MapsProgressS_ToWireFormat()
        {
            const uint domainId = 226u;
            using var participant = new DdsParticipant(domainId);
            using var reader = new DdsReader<DdsNavigationStatus>(participant, "NavigationStatus");
            var entityMap  = new NetworkEntityMap();
            var translator = new NavigationStatusEgressTranslator(participant, entityMap);

            using var repo = CreateWorldForEgress();
            var entity     = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(91));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            repo.AddComponent(entity, new EcsNavigationStatus
            {
                IntentId  = 3,
                Result    = EcsNavigationResult.InProgress,
                ProgressS = 0.4f,
            });

            Thread.Sleep(200);
            translator.ScanAndPublish(repo);
            Thread.Sleep(200);

            using var loan = reader.Take();
            DdsNavigationStatus published = default;
            bool found = false;
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Data.EntityId != 91) continue;
                published = sample.Data;
                found = true;
            }

            Assert.True(found, "Egress translator must publish a DDS sample for the entity.");
            Assert.Equal(3u, published.IntentId);
            Assert.Equal(ENavigationResult.RES_IN_PROGRESS, published.Result);
            Assert.Equal(0.4f, published.ProgressS, precision: 4);
        }

        /// <summary>
        /// PACK-N003 SC-2: Ingress translator must write <c>ProgressS</c> from the DDS sample
        /// into the ECS <see cref="EcsNavigationStatus"/> component.
        /// </summary>
        [Fact]
        public void NavigationStatusIngress_MapsProgressS_ToEcsComponent()
        {
            const uint domainId = 227u;
            using var participant = new DdsParticipant(domainId);
            using var writer = new DdsWriter<DdsNavigationStatus>(participant, "NavigationStatus");
            var entityMap  = new NetworkEntityMap();
            var translator = new NavigationStatusIngressTranslator(participant, entityMap);

            using var repo  = CreateWorldForIngress();
            var entity      = repo.CreateEntity();
            repo.AddComponent(entity, new EcsNavigationStatus());
            entityMap.Register(92, entity);

            Thread.Sleep(200);

            writer.Write(new DdsNavigationStatus
            {
                EntityId  = 92,
                IntentId  = 3,
                Result    = ENavigationResult.RES_ARRIVED,
                ProgressS = 0.9f,
            });
            Thread.Sleep(200);

            var cmd = new DirectCommandBuffer(repo);
            translator.PollIngress(cmd, repo);

            var status = repo.GetComponent<EcsNavigationStatus>(entity);
            Assert.Equal(0.9f, status.ProgressS, precision: 4);
            Assert.Equal(3u, status.IntentId);
            Assert.Equal(EcsNavigationResult.Arrived, status.Result);
        }
    }
}
