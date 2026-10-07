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
using Fdp.Toolkit.Replication;
using Xunit;

namespace Fdp.Toolkit.NetworkSpawning.Tests
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
        [ComponentId(242)]
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
        public void Spawn_SetsNetworkAuthority_LocalNodeIdMatchesSystemConfig()
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

            var authority = repo.GetComponent<NetworkAuthority>(entity);
            Assert.Equal(LocalNodeId, authority.LocalNodeId);
            Assert.Equal(3, authority.PrimaryOwnerId);
            Assert.False(repo.HasAuthority<NetworkIdentity>(entity));
        }

        [Fact]
        public void Spawn_LocalOwner_InitializesAuthorityMaskForPresentComponents()
        {
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator();
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 11L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = LocalNodeId,
                InitialComponents = new List<object>
                {
                    new TestPositionComponent { X = 1f, Y = 2f }
                }
            });

            Assert.True(networkMap.TryGetEntity(11L, out var entity));
            Assert.True(repo.HasAuthority<NetworkIdentity>(entity));
            Assert.True(repo.HasAuthority<NetworkAuthority>(entity));
            Assert.True(repo.HasAuthority<TkbIdentity>(entity));
            Assert.True(repo.HasAuthority<TestPositionComponent>(entity));
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

        [Fact]
        public void Spawn_SetsTkbIdentity_WithCorrectTkbTypeAndDisType()
        {
            // Arrange
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator();
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            // Act: spawn entity with OwnerNodeId = 5
            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 90L,
                TkbType     = DefaultTkbType,
                DisType     = 123UL,
                OwnerNodeId = 5
            });

            // Assert: TkbIdentity is permanently attached
            Assert.True(networkMap.TryGetEntity(90L, out var entity));
            Assert.True(repo.HasComponent<TkbIdentity>(entity),
                "TkbIdentity must be present on every spawned entity.");

            var tkbId = repo.GetComponent<TkbIdentity>(entity);
            Assert.Equal(DefaultTkbType, tkbId.TkbType);

            // DisType is now stored natively in EntityHeader — verify via GetHeader.
            var disType = repo.GetMetadata(entity.Index).DisType.Value;
            Assert.Equal(123UL, disType);
        }

        [Fact]
        public void Spawn_EntityHasConstructingLifecycle()
        {
            // Arrange
            var repo        = CreateWorld();
            var tkb         = CreateTkb();
            var elm         = CreateElm(tkb);
            var networkMap  = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator();
            var system      = CreateSystem(repo, networkMap, idAllocator, tkb, elm);

            // Act: spawn entity
            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId   = 95L,
                TkbType     = DefaultTkbType,
                OwnerNodeId = 1
            });

            // Assert: immediately after spawn the entity is in Constructing state
            Assert.True(networkMap.TryGetEntity(95L, out var entity));
            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(entity));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ⭐ CE-1017 S2 — the birth-height request (docs/DESIGN_Add_Entity_Picker.md §2e)
        // ─────────────────────────────────────────────────────────────────────

        // A 12 m building at (40..90, 40..90) on flat ground at Z 0.
        private const string LevelWorld = """
        {
          "type": "FeatureCollection",
          "hrot": { "schemaVersion": 1, "bounds": [0, 0, 200, 200], "groundZ": 0 },
          "features": [
            { "type": "Feature", "properties": { "kind": "building", "height": 12, "floors": 3 },
              "geometry": { "type": "Polygon", "coordinates": [[[40,40],[90,40],[90,90],[40,90],[40,40]]] } }
          ]
        }
        """;

        /// <summary>Spawns one entity at (x, y, sentZ) with <paramref name="height"/> and returns its born Z.</summary>
        private static float BornZ(float x, float y, float sentZ, SpawnHeight? height, bool withTerrain = true)
        {
            var repo = CreateWorld();
            repo.RegisterComponent<SimTransform>();
            if (withTerrain) repo.SetSingletonManaged(Fdp.Toolkit.Terrain.TerrainWorldParser.Parse(LevelWorld));
            var tkb = CreateTkb();
            var map = new NetworkEntityMap();
            var system = CreateSystem(repo, map, new StubIdAllocator(), tkb, CreateElm(tkb));
            RunSpawn(repo, system, new SpawnEntityCommand
            {
                NetworkId        = 7001L,
                TkbType          = DefaultTkbType,
                OwnerNodeId      = LocalNodeId,
                InitialTransform = new SimTransform { Position = new System.Numerics.Vector3(x, y, sentZ), Rotation = System.Numerics.Quaternion.Identity },
                SpawnHeight      = height,
            });
            Assert.True(map.TryGetEntity(7001L, out var e));
            return repo.GetComponent<SimTransform>(e).Position.Z;
        }

        [Fact]
        public void CE1017_SpawnHeight_OnLevel_ReplacesTheSentZ_Level0IsTheGroundEvenInsideABuilding()
        {
            Assert.Equal(0f,  BornZ(60, 60, 50f, new SpawnHeight(SpawnHeightMode.OnLevel, 0)));   // 🔒 rev 5: ground, not the roof
            Assert.Equal(12f, BornZ(60, 60, 50f, new SpawnHeight(SpawnHeightMode.OnLevel, 1)));   // the roof is an explicit +1
            Assert.Equal(12f, BornZ(60, 60, 50f, new SpawnHeight(SpawnHeightMode.OnLevel, 9)));   // past the top ⇒ the highest
            Assert.Equal(0f,  BornZ(10, 10, 50f, new SpawnHeight(SpawnHeightMode.OnLevel, -1)));  // past the bottom ⇒ the lowest
        }

        [Fact]
        public void CE1017_SpawnHeight_AboveLevel_AddsTheSentZ_ParachutistAndSubmarine()
        {
            Assert.Equal(800f, BornZ(10, 10, 800f, new SpawnHeight(SpawnHeightMode.AboveLevel, 0)));
            Assert.Equal(-50f, BornZ(10, 10, -50f, new SpawnHeight(SpawnHeightMode.AboveLevel, 0)));
            Assert.Equal(15f,  BornZ(60, 60, 3f,   new SpawnHeight(SpawnHeightMode.AboveLevel, 1)));   // 3 m above the roof
        }

        [Fact]
        public void CE1017_SpawnHeight_AbsentAbsoluteOrNoTerrain_KeepsTheSentZ()
        {
            Assert.Equal(50f, BornZ(60, 60, 50f, null));                                                   // every caller before CE-1017
            Assert.Equal(50f, BornZ(60, 60, 50f, new SpawnHeight(SpawnHeightMode.Absolute, 1)));
            Assert.Equal(50f, BornZ(60, 60, 50f, SpawnHeight.OnGround, withTerrain: false));               // no terrain resident
        }
    }
}
