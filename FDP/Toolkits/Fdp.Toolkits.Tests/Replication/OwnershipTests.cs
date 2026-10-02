using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Messages;
using DescriptorAuthorityChanged = Fdp.Toolkit.Replication.Messages.DescriptorAuthorityChanged;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Replication.Services;

namespace Fdp.Toolkit.Replication.Tests
{
    class MockNetworkTopology : INetworkTopology
    {
        public int LocalNodeId { get; set; }
        public int GetOptimisticPeerCount(DISEntityType entityType) => 0;
        public IReadOnlyList<int> GetParticipatingPeers(DISEntityType entityType) => new List<int>();
        public bool IsPeerAlive(int nodeId) => true;

        // Missing implementations
        public IEnumerable<int> GetExpectedPeers(long tkbType) => new List<int>();
        public IEnumerable<int> GetAllNodes() => new List<int>();
    }

    public class OwnershipTests
    {
        [Fact]
        public void IngressSystem_UpdatesOwnership_WhenMessageReceived()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            repo.RegisterEvent<OwnershipUpdate>();

            var map = new NetworkEntityMap();
            var topo = new MockNetworkTopology { LocalNodeId = 1 };
            var sys = new OwnershipIngressSystem(map, topo);

            var entity = repo.CreateEntity();
            long netId = 999;
            repo.AddComponent(entity, new NetworkIdentity(netId));
            map.Register(netId, entity);

            var msg = new OwnershipUpdate
            {
                NetworkId = new NetworkIdentity(netId),
                PackedKey = PackedKey.Create(1, 0),
                NewOwnerNodeId = 5
            };
            repo.Bus.Publish(msg);
            repo.Bus.SwapBuffers();

            sys.Execute(repo, 0f);

            Assert.True(repo.HasManagedComponent<DescriptorOwnership>(entity));
            var ownership = repo.GetComponent<DescriptorOwnership>(entity);
            Assert.True(ownership.Map.ContainsKey(msg.PackedKey));
            Assert.Equal(5, ownership.Map[msg.PackedKey]);
        }
        
        [Fact]
        public void IngressSystem_FiresAuthorityChanged_WhenLocalNodeBecomesOwner()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            repo.RegisterEvent<OwnershipUpdate>();
            repo.RegisterEvent<DescriptorAuthorityChanged>();

            var map = new NetworkEntityMap();
            var topo = new MockNetworkTopology { LocalNodeId = 10 };
            var sys = new OwnershipIngressSystem(map, topo);

            var entity = repo.CreateEntity();
            long netId = 888;
            repo.AddComponent(entity, new NetworkIdentity(netId));
            map.Register(netId, entity);

            var msg = new OwnershipUpdate
            {
                NetworkId = new NetworkIdentity(netId),
                PackedKey = PackedKey.Create(2, 0),
                NewOwnerNodeId = 10
            };
            repo.Bus.Publish(msg);
            repo.Bus.SwapBuffers();

            sys.Execute(repo, 0f);

            repo.Bus.SwapBuffers(); // Make generated event visible

            var events = ((ISimulationView)repo).ReadEvents<DescriptorAuthorityChanged>();
            bool found = false;
            foreach (var e in events)
            {
                if (e.Entity == entity && e.PackedKey == msg.PackedKey && e.IsAuthoritative)
                    found = true;
            }
            Assert.True(found, "Should have fired DescriptorAuthorityChanged event");
        }

        // ── OQ12 / CE-275 ④ — BDC compliance: an EntityMaster OwnershipUpdate mirrors into
        //    NetworkAuthority.PrimaryOwnerId (the save-gate fact). The "master" descriptor is
        //    identified network-agnostically via DescriptorOwnershipMap.PrimaryOwnerDescriptorOrdinal.
        //    📄 docs/DESIGN_Distributed_Scenario_Persistence.md §6c.
        private const int MasterOrdinal = 7;   // stand-in for the NED dtEntityMaster ordinal

        [Fact]
        public void IngressSystem_MirrorsPrimaryOwner_OnMasterDescriptorTransfer_ToUs()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            repo.RegisterEvent<OwnershipUpdate>();

            var map    = new NetworkEntityMap();
            var ownMap = new DescriptorOwnershipMap { PrimaryOwnerDescriptorOrdinal = MasterOrdinal };
            var sys    = new OwnershipIngressSystem(map, localNodeId: 1, descriptorMap: ownMap);

            var entity = repo.CreateEntity();
            long netId = 555;
            repo.AddComponent(entity, new NetworkIdentity(netId));
            // Ghost on THIS node (1), currently owned by remote node 2.
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 2, localNodeId: 1));
            map.Register(netId, entity);

            // External EntityMaster OwnershipUpdate hands the entity to us (node 1).
            repo.Bus.Publish(new OwnershipUpdate
            {
                NetworkId      = new NetworkIdentity(netId),
                PackedKey      = PackedKey.Create(MasterOrdinal, 0),
                NewOwnerNodeId = 1
            });
            repo.Bus.SwapBuffers();

            sys.Execute(repo, 0f);

            var netAuth = ((ISimulationView)repo).GetComponentRO<NetworkAuthority>(entity);
            Assert.Equal(1, netAuth.PrimaryOwnerId);
            Assert.True(netAuth.HasAuthority);      // save gate now sees us as owner
        }

        [Fact]
        public void IngressSystem_MirrorsPrimaryOwner_OnMasterDescriptorTransfer_AwayFromUs()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            repo.RegisterEvent<OwnershipUpdate>();

            var map    = new NetworkEntityMap();
            var ownMap = new DescriptorOwnershipMap { PrimaryOwnerDescriptorOrdinal = MasterOrdinal };
            var sys    = new OwnershipIngressSystem(map, localNodeId: 1, descriptorMap: ownMap);

            var entity = repo.CreateEntity();
            long netId = 556;
            repo.AddComponent(entity, new NetworkIdentity(netId));
            // We (node 1) currently own it.
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            map.Register(netId, entity);

            // EntityMaster OwnershipUpdate hands the entity to node 2.
            repo.Bus.Publish(new OwnershipUpdate
            {
                NetworkId      = new NetworkIdentity(netId),
                PackedKey      = PackedKey.Create(MasterOrdinal, 0),
                NewOwnerNodeId = 2
            });
            repo.Bus.SwapBuffers();

            sys.Execute(repo, 0f);

            var netAuth = ((ISimulationView)repo).GetComponentRO<NetworkAuthority>(entity);
            Assert.Equal(2, netAuth.PrimaryOwnerId);
            Assert.False(netAuth.HasAuthority);     // save gate now excludes us
        }

        [Fact]
        public void IngressSystem_LeavesPrimaryOwner_OnNonMasterDescriptor()
        {
            // Axis separation (design fact 8): a per-component grant — e.g. a Muscle claiming
            // SimTransform — moves AuthorityMask only and must NOT change entity/save ownership.
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            repo.RegisterEvent<OwnershipUpdate>();

            var map    = new NetworkEntityMap();
            var ownMap = new DescriptorOwnershipMap { PrimaryOwnerDescriptorOrdinal = MasterOrdinal };
            var sys    = new OwnershipIngressSystem(map, localNodeId: 1, descriptorMap: ownMap);

            var entity = repo.CreateEntity();
            long netId = 557;
            repo.AddComponent(entity, new NetworkIdentity(netId));
            repo.AddComponent(entity, new NetworkAuthority(primaryOwnerId: 1, localNodeId: 1));
            map.Register(netId, entity);

            // A NON-master descriptor (ordinal 3) is granted to node 2.
            repo.Bus.Publish(new OwnershipUpdate
            {
                NetworkId      = new NetworkIdentity(netId),
                PackedKey      = PackedKey.Create(3, 0),
                NewOwnerNodeId = 2
            });
            repo.Bus.SwapBuffers();

            sys.Execute(repo, 0f);

            var netAuth = ((ISimulationView)repo).GetComponentRO<NetworkAuthority>(entity);
            Assert.Equal(1, netAuth.PrimaryOwnerId);   // entity/save ownership unchanged
            Assert.True(netAuth.HasAuthority);
        }

        [Fact]
        public void EgressSystem_PublishesEvent_WhenOwnershipChanged()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            repo.RegisterEvent<OwnershipUpdate>();

            var sys = new OwnershipEgressSystem(new MockNetworkTopology { LocalNodeId = 77 });

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(1));

            // Initial Set
            var own = new DescriptorOwnership();
            own.SetOwner(100, 5); // Key 100, Owner 5
            repo.SetManagedComponent(entity, own);

            // First Execute - detects everything as "new" relative to empty cache
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var events = ((ISimulationView)repo).ReadEvents<OwnershipUpdate>();
            Assert.Equal(1, events.Length);
            Assert.Equal(100, events[0].PackedKey);
            Assert.Equal(5, events[0].NewOwnerNodeId);
            Assert.Equal(77, events[0].OriginNodeId);

            // Execute again - No change
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            Assert.Equal(0, ((ISimulationView)repo).ReadEvents<OwnershipUpdate>().Length);

            // Update ownership and verify change is published
            own.SetOwner(100, 6);
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            events = ((ISimulationView)repo).ReadEvents<OwnershipUpdate>();
            Assert.Equal(1, events.Length);
            Assert.Equal(6, events[0].NewOwnerNodeId);
            Assert.Equal(77, events[0].OriginNodeId);
        }
    }
}
