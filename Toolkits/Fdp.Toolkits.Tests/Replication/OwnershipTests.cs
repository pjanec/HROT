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

            var events = ((ISimulationView)repo).ConsumeEvents<DescriptorAuthorityChanged>();
            bool found = false;
            foreach (var e in events)
            {
                if (e.Entity == entity && e.PackedKey == msg.PackedKey && e.IsAuthoritative)
                    found = true;
            }
            Assert.True(found, "Should have fired DescriptorAuthorityChanged event");
        }

        [Fact]
        public void EgressSystem_PublishesEvent_WhenOwnershipChanged()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterManagedComponent<DescriptorOwnership>();
            repo.RegisterEvent<OwnershipUpdate>();

            var sys = new OwnershipEgressSystem();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(1));

            // Initial Set
            var own = new DescriptorOwnership();
            own.SetOwner(100, 5); // Key 100, Owner 5
            repo.SetManagedComponent(entity, own);

            // First Execute - detects everything as "new" relative to empty cache
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var events = ((ISimulationView)repo).ConsumeEvents<OwnershipUpdate>();
            Assert.Equal(1, events.Length);
            Assert.Equal(100, events[0].PackedKey);
            Assert.Equal(5, events[0].NewOwnerNodeId);

            // Execute again - No change
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();
            Assert.Equal(0, ((ISimulationView)repo).ConsumeEvents<OwnershipUpdate>().Length);

            // Update ownership and verify change is published
            own.SetOwner(100, 6);
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            events = ((ISimulationView)repo).ConsumeEvents<OwnershipUpdate>();
            Assert.Equal(1, events.Length);
            Assert.Equal(6, events[0].NewOwnerNodeId);
        }
    }
}
