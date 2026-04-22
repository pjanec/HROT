using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D.Components;
using Hrot.Common.Serializers;
using Hrot.Map.Common.Components;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="GenesisMaterializationSystem"/> — TASK-S404.
    /// </summary>
    public sealed class GenesisMaterializationSystemTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly NetworkEntityMap _entityMap;

        public GenesisMaterializationSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<PassengerBuffer>();
            _repo.RegisterComponent<IsEmbarkedTag>();
            _repo.RegisterComponent<VisHierarchyNode>();
            _repo.RegisterComponent<PersonalRouteRef>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterManagedComponent<InitialPassengersIntent>();
            _repo.RegisterManagedComponent<InitialVehicleIntent>();
            _repo.RegisterManagedComponent<InitialHierarchyIntent>();
            _repo.RegisterManagedComponent<InitialRouteIntent>();
            _repo.RegisterManagedComponent<InitialTargetsIntent>();
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _repo.Dispose();

        private GenesisMaterializationSystem CreateAndStartSystem()
        {
            var sys = new GenesisMaterializationSystem(_entityMap);
            sys.Create(_repo);
            return sys;
        }

        // ── Helper: create an entity with NetworkIdentity and register in map ──

        private Entity CreateNetworkedEntity(long netId)
        {
            var e = _repo.CreateEntity();
            _repo.SetComponent(e, new NetworkIdentity { Value = netId });
            _entityMap.Register(netId, e);
            return e;
        }

        // ── Test 1: Passengers deferred until referenced entity alive ──────────

        [Fact]
        public void Passengers_DeferredWhenReferencedEntityNotInMap()
        {
            var vehicle = _repo.CreateEntity();
            var intent = new InitialPassengersIntent();
            intent.PassengerNetworkIds.Add(99L); // not in map yet
            _repo.RegisterManagedComponent<InitialPassengersIntent>();
            _repo.SetManagedComponent(vehicle, intent);

            var sys = CreateAndStartSystem();
            sys.Run();

            // Not yet resolved — intent must remain
            Assert.True(_repo.HasManagedComponent<InitialPassengersIntent>(vehicle));
            Assert.False(_repo.HasComponent<PassengerBuffer>(vehicle));
        }

        // ── Test 2: Passengers materialized once referenced entity appears ──────

        [Fact]
        public void Passengers_MaterializedOnceEntityAppearsInMap()
        {
            var passenger = CreateNetworkedEntity(42L);

            var vehicle = _repo.CreateEntity();
            var intent = new InitialPassengersIntent();
            intent.PassengerNetworkIds.Add(42L);
            _repo.SetManagedComponent(vehicle, intent);

            var sys = CreateAndStartSystem();
            sys.Run();

            Assert.False(_repo.HasManagedComponent<InitialPassengersIntent>(vehicle));
            Assert.True(_repo.HasComponent<PassengerBuffer>(vehicle));
            var buf = _repo.GetComponent<PassengerBuffer>(vehicle);
            Assert.Equal(1, buf.Count);
            Assert.Equal(passenger, buf.Passengers[0]);
        }

        // ── Test 3: IsEmbarkedTag written from InitialVehicleIntent ───────────

        [Fact]
        public void Vehicle_MaterializesIsEmbarkedTagOnceVehicleInMap()
        {
            var vehicle = CreateNetworkedEntity(77L);

            var soldier = _repo.CreateEntity();
            _repo.SetManagedComponent(soldier, new InitialVehicleIntent { VehicleNetworkId = 77L });

            var sys = CreateAndStartSystem();
            sys.Run();

            Assert.False(_repo.HasManagedComponent<InitialVehicleIntent>(soldier));
            Assert.True(_repo.HasComponent<IsEmbarkedTag>(soldier));
            Assert.Equal(vehicle, _repo.GetComponent<IsEmbarkedTag>(soldier).VehicleEntity);
        }

        // ── Test 4: VisHierarchyNode written from InitialHierarchyIntent ───────

        [Fact]
        public void Hierarchy_MaterializesVisHierarchyNodeOnceAllEntitiesInMap()
        {
            var parent     = CreateNetworkedEntity(10L);
            var firstChild = CreateNetworkedEntity(20L);

            var entity = _repo.CreateEntity();
            _repo.SetManagedComponent(entity, new InitialHierarchyIntent
            {
                ParentNetworkId     = 10L,
                FirstChildNetworkId = 20L,
                NextSiblingNetworkId = 0L, // null
            });

            var sys = CreateAndStartSystem();
            sys.Run();

            Assert.False(_repo.HasManagedComponent<InitialHierarchyIntent>(entity));
            Assert.True(_repo.HasComponent<VisHierarchyNode>(entity));
            var node = _repo.GetComponent<VisHierarchyNode>(entity);
            Assert.Equal(parent,     node.Parent);
            Assert.Equal(firstChild, node.FirstChild);
            Assert.Equal(Entity.Null, node.NextSibling);
        }

        // ── Test 5: PersonalRouteRef written from InitialRouteIntent ──────────

        [Fact]
        public void Route_MaterializesPersonalRouteRefOnceRouteInMap()
        {
            var route = CreateNetworkedEntity(55L);

            var vehicle = _repo.CreateEntity();
            _repo.SetManagedComponent(vehicle, new InitialRouteIntent { RouteNetworkId = 55L });

            var sys = CreateAndStartSystem();
            sys.Run();

            Assert.False(_repo.HasManagedComponent<InitialRouteIntent>(vehicle));
            Assert.True(_repo.HasComponent<PersonalRouteRef>(vehicle));
            Assert.Equal(route, _repo.GetComponent<PersonalRouteRef>(vehicle).RouteEntity);
        }

        // ── Test 6: TargetMemory partial materialization ───────────────────────

        [Fact]
        public void Targets_PartialMaterialization_UnresolvedEntriesDropped_IntentAlwaysRemoved()
        {
            // Only entity 111 is registered; 222 is not
            var target1 = CreateNetworkedEntity(111L);
            _repo.RegisterComponent<TargetMemory>();

            var entity = _repo.CreateEntity();
            var intent = new InitialTargetsIntent();
            intent.Entries.Add(new TargetEntry { NetworkId = 111L, PosX = 1f, PosY = 2f, Score = 0.5f });
            intent.Entries.Add(new TargetEntry { NetworkId = 222L, PosX = 3f, PosY = 4f, Score = 0.9f });
            _repo.SetManagedComponent(entity, intent);

            var sys = CreateAndStartSystem();
            sys.Run();

            // Intent always removed after first tick
            Assert.False(_repo.HasManagedComponent<InitialTargetsIntent>(entity));

            // TargetMemory: only entry 111 resolved
            Assert.True(_repo.HasComponent<TargetMemory>(entity));
            var mem = _repo.GetComponent<TargetMemory>(entity);
            Assert.Equal(1, mem.Count);
        }
    }
}
