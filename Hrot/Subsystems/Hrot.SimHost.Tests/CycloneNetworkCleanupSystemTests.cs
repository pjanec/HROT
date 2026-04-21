using System;
using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;
using Fdp.Network.Cyclone.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CycloneNetworkCleanupSystem"/> verifying the
    /// fan-out descriptor disposal behaviour (BUG1-N002).
    /// </summary>
    public class CycloneNetworkCleanupSystemTests
    {
        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Minimal world with the components the cleanup system queries.</summary>
        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkOwnership>();
            return repo;
        }

        /// <summary>
        /// Creates an entity that is locally authoritative and adds it to the
        /// cleanup system's tracking set via a first Execute call.
        /// </summary>
        private static Entity CreateAuthoritativeEntity(EntityRepository repo, long netId,
            int primaryOwner = 1, int localNode = 1)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(netId));
            repo.AddComponent(entity, new NetworkOwnership
            {
                PrimaryOwnerId = primaryOwner,
                LocalNodeId    = localNode
            });
            return entity;
        }

        // â”€â”€ Mock translator â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private sealed class MockTranslator : Fdp.Interfaces.IDescriptorTranslator
        {
            public long DescriptorOrdinal => 0;
            public string TopicName => "mock";
            public long ReceivedSampleCount { get; private set; }
            public long SentSampleCount { get; private set; }
            public List<long> DisposedIds { get; } = new();
            public bool ThrowOnDispose { get; set; }

            public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }
            public void ScanAndPublish(ISimulationView view) { }
            public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }

            public void Dispose(long networkEntityId)
            {
                if (ThrowOnDispose) throw new InvalidOperationException("Simulated disposal failure");
                DisposedIds.Add(networkEntityId);
            }
        }

        // â”€â”€ BUG1-N002 Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [Fact]
        public void AllTranslatorsReceiveDispose_WhenEntityDies()
        {
            var repo    = CreateWorld();
            var t1      = new MockTranslator();
            var t2      = new MockTranslator();
            var t3      = new MockTranslator();
            var system  = new CycloneNetworkCleanupSystem(new[] { t1, t2, t3 });

            var entity = CreateAuthoritativeEntity(repo, netId: 100L);

            // First tick: system tracks the entity.
            system.Execute(repo, 0f);

            // Delete the entity so the system detects it as dead.
            repo.DestroyEntity(entity);

            // Second tick: system detects dead entity and calls Dispose on all translators.
            system.Execute(repo, 0f);

            Assert.Contains(100L, t1.DisposedIds);
            Assert.Contains(100L, t2.DisposedIds);
            Assert.Contains(100L, t3.DisposedIds);
        }

        [Fact]
        public void OneFaultyTranslator_DoesNotBlockOthers()
        {
            var repo    = CreateWorld();
            var t1      = new MockTranslator { ThrowOnDispose = true };
            var t2      = new MockTranslator();
            var t3      = new MockTranslator();
            var system  = new CycloneNetworkCleanupSystem(new[] { t1, t2, t3 });

            var entity = CreateAuthoritativeEntity(repo, netId: 200L);

            system.Execute(repo, 0f);
            repo.DestroyEntity(entity);
            system.Execute(repo, 0f);

            // t1 threw, but t2 and t3 must still have been called
            Assert.Contains(200L, t2.DisposedIds);
            Assert.Contains(200L, t3.DisposedIds);
        }

        [Fact]
        public void NonAuthoritativeEntity_IsNotTracked_AndNeverDisposed()
        {
            var repo   = CreateWorld();
            var t1     = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1 });

            // PrimaryOwner=2, Local=1 â†’ not authoritative
            var entity = CreateAuthoritativeEntity(repo, netId: 300L, primaryOwner: 2, localNode: 1);

            system.Execute(repo, 0f);
            repo.DestroyEntity(entity);
            system.Execute(repo, 0f);

            Assert.Empty(t1.DisposedIds);
        }

        [Fact]
        public void LiveAuthoritativeEntity_IsNotDisposed()
        {
            var repo   = CreateWorld();
            var t1     = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1 });

            CreateAuthoritativeEntity(repo, netId: 400L);

            system.Execute(repo, 0f);
            system.Execute(repo, 0f);

            // Entity is still alive â†’ no dispose
            Assert.Empty(t1.DisposedIds);
        }

        [Fact]
        public void MultipleEntities_EachTranslatorDisposedOnce()
        {
            var repo   = CreateWorld();
            var t1     = new MockTranslator();
            var t2     = new MockTranslator();
            var system = new CycloneNetworkCleanupSystem(new[] { t1, t2 });

            var e1 = CreateAuthoritativeEntity(repo, netId: 500L);
            var e2 = CreateAuthoritativeEntity(repo, netId: 501L);

            system.Execute(repo, 0f);

            repo.DestroyEntity(e1);
            repo.DestroyEntity(e2);

            system.Execute(repo, 0f);

            Assert.Contains(500L, t1.DisposedIds);
            Assert.Contains(501L, t1.DisposedIds);
            Assert.Contains(500L, t2.DisposedIds);
            Assert.Contains(501L, t2.DisposedIds);
            Assert.Equal(2, t1.DisposedIds.Count);
            Assert.Equal(2, t2.DisposedIds.Count);
        }
    }
}
