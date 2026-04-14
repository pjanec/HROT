using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.NetworkSpawning.Tests.Helpers;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Network;
using Xunit;

namespace Fdp.Toolkit.NetworkSpawning.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NetworkSpawningSystem"/> update and destroy paths (NS1.5).
    /// </summary>
    public class UpdateDestroySystemTests
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Shared fixture
        // ─────────────────────────────────────────────────────────────────────

        private const long DefaultTkbType = 42L;
        private const int  LocalNodeId    = 1;

        [ComponentId(243)]
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
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<PendingNetworkAck>();
            repo.RegisterComponent<TestPositionComponent>();
            // ELM commands publish these events — register so command buffer playback works
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<DestructionOrder>();
            return repo;
        }

        private static TkbDatabase CreateTkb()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("T", DefaultTkbType));
            return db;
        }

        private static EntityLifecycleModule CreateElm(ITkbDatabase tkb) =>
            new EntityLifecycleModule(tkb, participatingModuleIds: System.Array.Empty<int>());

        private static NetworkSpawningSystem CreateSystem(
            ITkbDatabase tkb, EntityLifecycleModule elm,
            NetworkEntityMap networkMap, StubIdAllocator idAllocator) =>
            new NetworkSpawningSystem(tkb, elm, networkMap, idAllocator, LocalNodeId);

        /// <summary>Creates an entity, registers it in the map, adds a TestPositionComponent.</summary>
        private static Entity PrepareEntity(EntityRepository repo, NetworkEntityMap map, long netId)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new TestPositionComponent { X = 0f, Y = 0f });
            map.Register(netId, e);
            return e;
        }

        private static void RunExecute(EntityRepository repo, NetworkSpawningSystem system)
        {
            repo.Bus.SwapBuffers();
            system.Execute(repo, 0f);
            var cb = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
            cb.Playback(repo);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Update tests
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Update_WithKnownNetworkId_AppliesComponentUpdate()
        {
            var repo       = CreateWorld();
            var tkb        = CreateTkb();
            var elm        = CreateElm(tkb);
            var networkMap = new NetworkEntityMap();
            var idAlloc    = new StubIdAllocator();
            var system     = CreateSystem(tkb, elm, networkMap, idAlloc);

            var entity = PrepareEntity(repo, networkMap, netId: 10L);

            repo.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId = 10L,
                ComponentsToUpdate = new List<object>
                {
                    new TestPositionComponent { X = 9.0f, Y = 5.5f }
                }
            });
            RunExecute(repo, system);

            var pos = repo.GetComponent<TestPositionComponent>(entity);
            Assert.Equal(9.0f, pos.X);
            Assert.Equal(5.5f, pos.Y);
        }

        [Fact]
        public void Update_WithUnknownNetworkId_DoesNotThrow()
        {
            var repo       = CreateWorld();
            var tkb        = CreateTkb();
            var elm        = CreateElm(tkb);
            var networkMap = new NetworkEntityMap();
            var idAlloc    = new StubIdAllocator();
            var system     = CreateSystem(tkb, elm, networkMap, idAlloc);

            repo.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId = 999L,   // not registered
                ComponentsToUpdate = new List<object> { new TestPositionComponent { X = 1f } }
            });

            var exception = Record.Exception(() => RunExecute(repo, system));
            Assert.Null(exception);
        }

        [Fact]
        public void Update_WithNullComponentsToUpdate_DoesNotThrow()
        {
            var repo       = CreateWorld();
            var tkb        = CreateTkb();
            var elm        = CreateElm(tkb);
            var networkMap = new NetworkEntityMap();
            var idAlloc    = new StubIdAllocator();
            var system     = CreateSystem(tkb, elm, networkMap, idAlloc);

            PrepareEntity(repo, networkMap, netId: 20L);

            repo.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId          = 20L,
                ComponentsToUpdate = null
            });

            var exception = Record.Exception(() => RunExecute(repo, system));
            Assert.Null(exception);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Destroy tests
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void Destroy_WithKnownNetworkId_EntityEntersTearDownState()
        {
            var repo       = CreateWorld();
            var tkb        = CreateTkb();
            var elm        = CreateElm(tkb);
            var networkMap = new NetworkEntityMap();
            var idAlloc    = new StubIdAllocator();
            var system     = CreateSystem(tkb, elm, networkMap, idAlloc);

            var entity = PrepareEntity(repo, networkMap, netId: 30L);

            repo.Bus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = 30L,
                Reason    = "unit test destroy"
            });
            RunExecute(repo, system);

            // ProcessDestroy calls cmdBuffer.SetLifecycleState(entity, TearDown) — verified here
            Assert.Equal(EntityLifecycle.TearDown, repo.GetLifecycleState(entity));
        }

        [Fact]
        public void Destroy_WithUnknownNetworkId_DoesNotThrow()
        {
            var repo       = CreateWorld();
            var tkb        = CreateTkb();
            var elm        = CreateElm(tkb);
            var networkMap = new NetworkEntityMap();
            var idAlloc    = new StubIdAllocator();
            var system     = CreateSystem(tkb, elm, networkMap, idAlloc);

            repo.Bus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = 888L,   // not registered
                Reason    = "no such entity"
            });

            var exception = Record.Exception(() => RunExecute(repo, system));
            Assert.Null(exception);
        }
    }
}
