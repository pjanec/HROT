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
        private static Entity PrepareEntity(EntityRepository repo, NetworkEntityMap map, int netId)
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

            var entity = PrepareEntity(repo, networkMap, netId: 10);

            repo.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId = 10,
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
                NetworkId = 999,   // not registered
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

            PrepareEntity(repo, networkMap, netId: 20);

            repo.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId          = 20,
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

            var entity = PrepareEntity(repo, networkMap, netId: 30);

            repo.Bus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = 30,
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
                NetworkId = 888,   // not registered
                Reason    = "no such entity"
            });

            var exception = Record.Exception(() => RunExecute(repo, system));
            Assert.Null(exception);
        }
    
        /// <summary>⭐ CE-4276 (edit framework F2) — a user's delete (<c>Edits.Submit</c>) on a node that does NOT own the entity is asked of the owner with its
        /// request id and the replica is NOT torn down here; on the owner it is torn down at once and reported Applied.
        /// ✅ Red-proof: the spawning system's skip removed ⇒ the replica enters TearDown ⇒ red.</summary>
        [Fact]
        public void F2_AUsersDeleteOnANonOwner_LeavesTheReplica_TheOwnerTearsItDown()
        {
            var repo = CreateWorld();
            var tkb = CreateTkb();
            var map = new NetworkEntityMap();
            var system = CreateSystem(tkb, CreateElm(tkb), map, new StubIdAllocator());
            var replica = PrepareEntity(repo, map, netId: 40);
            repo.AddComponent(replica, new NetworkIdentity(40));
            repo.AddComponent(replica, new NetworkAuthority(primaryOwnerId: 2, localNodeId: LocalNodeId));
            var owned = PrepareEntity(repo, map, netId: 41);
            repo.AddComponent(owned, new NetworkIdentity(41));
            repo.AddComponent(owned, new NetworkAuthority(primaryOwnerId: LocalNodeId, localNodeId: LocalNodeId));

            var asked = Fdp.Toolkit.Replication.Edits.Edits.Submit(repo, Fdp.Toolkit.Replication.Edits.EditOp.Delete(replica, "Delete"));
            var done = Fdp.Toolkit.Replication.Edits.Edits.Submit(repo, Fdp.Toolkit.Replication.Edits.EditOp.Delete(owned, "Delete"));
            Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Requested, asked.Route);
            Assert.Equal(Fdp.Toolkit.Replication.Patching.EntityWriteRoute.Direct, done.Route);
            RunExecute(repo, system);

            Assert.NotEqual(EntityLifecycle.TearDown, repo.GetLifecycleState(replica));
            Assert.Equal(EntityLifecycle.TearDown, repo.GetLifecycleState(owned));
            var commands = ((ISimulationView)repo).ReadManagedEvents<DestroyEntityCommand>();
            Assert.Contains(commands, c => c.NetworkId == 40 && c.RequestId == asked.RequestId);   // the egress carries this id to the owner
            Assert.True(Fdp.Toolkit.Replication.Edits.EditOutcomeLog.Of(repo).TryGet(done.RequestId, out var o) && o.Status == Fdp.Toolkit.Replication.Edits.EditStatus.Applied);
        }
}
}
