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
    /// Full spawn → update → destroy lifecycle flow (NS1.6).
    /// Uses only in-memory stubs — no DDS or networking required.
    /// </summary>
    public class NetworkSpawningLifecycleTests
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Component under test
        // ─────────────────────────────────────────────────────────────────────

        private struct TestPositionComponent
        {
            public float X;
            public float Y;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  World factory
        // ─────────────────────────────────────────────────────────────────────

        private const long TkbType    = 200L;
        private const int  LocalNode  = 5;
        private const long NetworkId  = 111L;

        private EntityRepository   _repo       = null!;
        private TkbDatabase        _tkbDb      = null!;
        private EntityLifecycleModule _elm     = null!;
        private NetworkEntityMap   _networkMap = null!;
        private StubIdAllocator    _idAlloc    = null!;
        private NetworkSpawningSystem _system  = null!;

        private void BuildWorld()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterComponent<NetworkOwnership>();
            _repo.RegisterComponent<NetworkAuthority>();
            _repo.RegisterComponent<PendingNetworkAck>();
            _repo.RegisterComponent<TestPositionComponent>();
            // ELM commands publish these events — register so command buffer playback works
            _repo.RegisterEvent<ConstructionOrder>();
            _repo.RegisterEvent<DestructionOrder>();

            _tkbDb = new TkbDatabase();
            // Template has a default TestPositionComponent so EntityComponentReflector
            // is exercised as an override (not first touch).
            var template = new TkbTemplate("IntegrationTemplate", TkbType);
            template.AddComponent(new TestPositionComponent { X = 0f, Y = 0f });
            _tkbDb.Register(template);

            _elm        = new EntityLifecycleModule(_tkbDb, participatingModuleIds: System.Array.Empty<int>());
            _networkMap = new NetworkEntityMap();
            _idAlloc    = new StubIdAllocator(startId: 1);
            _system     = new NetworkSpawningSystem(_tkbDb, _elm, _networkMap, _idAlloc, LocalNode);
        }

        private void RunExecute()
        {
            _repo.Bus.SwapBuffers();
            _system.Execute(_repo, 0f);
            var cb = (EntityCommandBuffer)((ISimulationView)_repo).GetCommandBuffer();
            cb.Playback(_repo);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Integration test
        // ─────────────────────────────────────────────────────────────────────

        [Fact]
        public void FullLifecycle_SpawnUpdateDestroy_StateTransitionsCorrectly()
        {
            BuildWorld();

            // ── 1. SPAWN ────────────────────────────────────────────────────
            _repo.Bus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId   = NetworkId,
                TkbType     = TkbType,
                OwnerNodeId = 3,
                InitType    = ReliableInitType.None,
                InitialComponents = new List<object>
                {
                    new TestPositionComponent { X = 10.0f, Y = 20.0f }
                }
            });
            RunExecute();

            // Entity must be registered in the map
            Assert.True(_networkMap.TryGetEntity(NetworkId, out var entity),
                "Entity should be in the network map after spawn");

            // NetworkIdentity must carry the provided ID
            var identity = _repo.GetComponent<NetworkIdentity>(entity);
            Assert.Equal(NetworkId, identity.Value);

            // InitialComponents override must be applied
            var posAfterSpawn = _repo.GetComponent<TestPositionComponent>(entity);
            Assert.Equal(10.0f, posAfterSpawn.X);
            Assert.Equal(20.0f, posAfterSpawn.Y);

            // ── 2. UPDATE ───────────────────────────────────────────────────
            _repo.Bus.PublishManaged(new UpdateEntityCommand
            {
                NetworkId = NetworkId,
                ComponentsToUpdate = new List<object>
                {
                    new TestPositionComponent { X = 99.0f, Y = 88.0f }
                }
            });
            RunExecute();

            var posAfterUpdate = _repo.GetComponent<TestPositionComponent>(entity);
            Assert.Equal(99.0f, posAfterUpdate.X);
            Assert.Equal(88.0f, posAfterUpdate.Y);

            // ── 3. DESTROY ──────────────────────────────────────────────────
            _repo.Bus.PublishManaged(new DestroyEntityCommand
            {
                NetworkId = NetworkId,
                Reason    = "integration test teardown"
            });
            RunExecute();

            // ProcessDestroy sets TearDown via command buffer before BeginDestruction
            Assert.Equal(EntityLifecycle.TearDown, _repo.GetLifecycleState(entity));
        }
    }
}
