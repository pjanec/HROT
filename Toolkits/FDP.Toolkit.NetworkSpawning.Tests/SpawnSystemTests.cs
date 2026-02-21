using System.Collections.Generic;
using Fdp.Kernel;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.NetworkSpawning.Tests.Helpers;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;
using Xunit;

namespace FDP.Toolkit.NetworkSpawning.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NetworkSpawningSystem"/> spawn path (NS1.4).
    /// </summary>
    public class SpawnSystemTests
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Test fixture helpers
        // ─────────────────────────────────────────────────────────────────────

        private const long DefaultTkbType = 42L;
        private const int  LocalNodeId    = 1;

        /// <summary>Unmanaged component used to verify InitialComponents overrides.</summary>
        private struct TestPositionComponent
        {
            public float X;
            public float Y;
        }

        private static EntityRepository CreateWorld()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkOwnership>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<PendingNetworkAck>();
            repo.RegisterComponent<TestPositionComponent>();
            // ELM commands publish these events — register so command buffer playback works
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<DestructionOrder>();
            return repo;
        }

        private static TkbDatabase CreateTkb(long tkbType = DefaultTkbType)
        {
            var db = new TkbDatabase();
            var template = new TkbTemplate("TestTemplate", tkbType);
            db.Register(template);
            return db;
        }

        private static EntityLifecycleModule CreateElm(ITkbDatabase tkb) =>
            new EntityLifecycleModule(tkb, participatingModuleIds: System.Array.Empty<int>());

        /// <summary>
        /// Publishes <paramref name="cmd"/>, swaps the bus so it is visible, runs Execute,
        /// and plays back the command buffer so any lifecycle commands apply immediately.
        /// Returns the entity in <paramref name="networkMap"/> if exactly one was registered.
        /// </summary>
        private static void RunSpawn(
            EntityRepository repo,
            NetworkSpawningSystem system,
            SpawnEntityCommand cmd)
        {
            repo.Bus.PublishManaged(cmd);
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0f);

            var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
            cb.Playback(repo);
        }

        private static NetworkSpawningSystem CreateSystem(
            EntityRepository repo,
            NetworkEntityMap networkMap,
            StubIdAllocator idAllocator,
            ITkbDatabase tkb,
            EntityLifecycleModule elm)
        {
            return new NetworkSpawningSystem(tkb, elm, networkMap, idAllocator, LocalNodeId);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Tests
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Spawn_WithNetworkIdZero_AllocatesNewIdFromAllocator()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator(startId: 100);
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId = 0,
                TkbType   = DefaultTkbType,
                OwnerNodeId = 2
            });

            // Allocator was called → LastAllocatedId is 100
            Assert.Equal(100L, idAllocator.LastAllocatedId);

            // Entity is registered under the allocated ID
            Assert.True(networkMap.TryGetEntity(100L, out var entity));

            // NetworkIdentity carries the allocated ID
            var identity = repo.GetComponent<NetworkIdentity>(entity);
            Assert.Equal(100L, identity.Value);
        }

        [Fact]
        public void Spawn_WithExplicitNetworkId_UsesProvidedIdWithoutAllocating()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator(startId: 100);
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 999L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = 2
            });

            // No ID was allocated (LastAllocatedId stays at default 0)
            Assert.Equal(0L, idAllocator.LastAllocatedId);

            // Entity is registered under the explicitly provided ID
            Assert.True(networkMap.TryGetEntity(999L, out var entity));

            var identity = repo.GetComponent<NetworkIdentity>(entity);
            Assert.Equal(999L, identity.Value);
        }

        [Fact]
        public void Spawn_RegistersEntityInNetworkMap()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator(startId: 1);
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 7L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = 2
            });

            Assert.True(networkMap.TryGetEntity(7L, out _));
        }

        [Fact]
        public void Spawn_SetsNetworkOwnership_LocalNodeIdMatchesSystemConfig()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator();
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 10L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = 3
            });

            Assert.True(networkMap.TryGetEntity(10L, out var entity));

            var ownership = repo.GetComponent<NetworkOwnership>(entity);
            Assert.Equal(LocalNodeId, ownership.LocalNodeId);
            Assert.Equal(3, ownership.PrimaryOwnerId);
        }

        [Fact]
        public void Spawn_WithInitialComponents_AppliesOverridesOnTopOfTemplate()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator();
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 20L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = 1,
                InitialComponents = new List<object>
                {
                    new TestPositionComponent { X = 3.5f, Y = 7.0f }
                }
            });

            Assert.True(networkMap.TryGetEntity(20L, out var entity));

            var pos = repo.GetComponent<TestPositionComponent>(entity);
            Assert.Equal(3.5f, pos.X);
            Assert.Equal(7.0f, pos.Y);
        }

        [Fact]
        public void Spawn_WithDuplicateNetworkId_SecondSpawnIsIgnored()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator();
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            // First spawn
            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 50L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = 1
            });

            Assert.True(networkMap.TryGetEntity(50L, out var firstEntity));

            // Second spawn with same ID — should be silently dropped
            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 50L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = 1
            });

            // Map must still resolve to the SAME original entity
            Assert.True(networkMap.TryGetEntity(50L, out var sameEntity));
            Assert.Equal(firstEntity, sameEntity);
        }

        [Fact]
        public void Spawn_WithUnknownTkbType_DoesNotCreateEntityAndDoesNotRegisterInMap()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb(DefaultTkbType); // only type 42 registered
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator();
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 60L,
                TkbType     = 999L, // unknown type
                OwnerNodeId = 1
            });

            Assert.False(networkMap.TryGetEntity(60L, out _));
        }

        [Fact]
        public void Spawn_WithInitTypeNone_PendingNetworkAckIsNotAdded()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator();
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 70L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = 1,
                InitType    = ReliableInitType.None
            });

            Assert.True(networkMap.TryGetEntity(70L, out var entity));
            Assert.False(repo.HasComponent<PendingNetworkAck>(entity),
                "PendingNetworkAck must NOT be present when InitType is None");
        }

        [Fact]
        public void Spawn_WithInitTypeAllPeers_PendingNetworkAckIsAdded()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator();
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 80L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = 1,
                InitType    = ReliableInitType.AllPeers
            });

            Assert.True(networkMap.TryGetEntity(80L, out var entity));
            Assert.True(repo.HasComponent<PendingNetworkAck>(entity),
                "PendingNetworkAck MUST be present when InitType is AllPeers");
        }
    }
}
