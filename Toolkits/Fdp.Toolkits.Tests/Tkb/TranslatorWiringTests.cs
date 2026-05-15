using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Lifecycle.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.NetworkSpawning.Systems;
using Fdp.Toolkit.NetworkSpawning.Tests.Helpers;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Toolkit.Tkb;
using Xunit;

namespace Fdp.Toolkit.Tkb.Tests
{
    public class TranslatorWiringTests
    {
        // ─── Translator stub ──────────────────────────────────────────────────

        private sealed class RecordingTranslator : ITkbEntityTranslator
        {
            public int InjectCount { get; private set; }
            public Entity LastEntity { get; private set; }

            public IEnumerable<Type> GetConsumedDescriptors()
                => Array.Empty<Type>();

            public void Inject(EntityRepository repo, Entity entity, TkbTemplate template)
            {
                InjectCount++;
                LastEntity = entity;
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private const long TestTkbType = 42L;
        private const int LocalNodeId = 1;

        private static TkbDatabase CreateTkb()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("TestVehicle", TestTkbType));
            return db;
        }

        private static EntityRepository CreateWorldForSpawn()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkOwnership>();
            repo.RegisterComponent<NetworkAuthority>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterComponent<PendingNetworkAck>();
            repo.RegisterEvent<ConstructionOrder>();
            repo.RegisterEvent<DestructionOrder>();
            return repo;
        }

        // ─── Tests ────────────────────────────────────────────────────────────

        [Fact]
        public void BlueprintApplicationSystem_WithTranslator_CallsInjectOnKnownTkbType()
        {
            var repo = new EntityRepository();
            repo.RegisterEvent<ConstructionOrder>();

            var db = CreateTkb();
            var translator = new RecordingTranslator();
            var system = new BlueprintApplicationSystem(db, new[] { translator });

            var entity = repo.CreateEntity();
            repo.Bus.Publish(new ConstructionOrder { Entity = entity, BlueprintId = TestTkbType });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0f);

            Assert.Equal(1, translator.InjectCount);
            Assert.Equal(entity, translator.LastEntity);
        }

        [Fact]
        public void NetworkSpawningSystem_WithTranslator_CallsInjectOnSpawn()
        {
            var repo = CreateWorldForSpawn();
            var db = CreateTkb();
            var elm = new EntityLifecycleModule(db, Array.Empty<int>());
            var networkMap = new NetworkEntityMap();
            var idAllocator = new StubIdAllocator(startId: 100);
            var translator = new RecordingTranslator();

            var system = new NetworkSpawningSystem(
                db, elm, networkMap, idAllocator, LocalNodeId,
                translators: new[] { translator });

            repo.Bus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId = 0,
                TkbType = TestTkbType,
                OwnerNodeId = 2
            });
            repo.Bus.SwapBuffers();

            system.Execute(repo, 0f);

            Assert.Equal(1, translator.InjectCount);
        }

        [Fact]
        public void GhostPromotionSystem_WithEmptyTranslators_PromotesWithoutException()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<ConstructionOrder>();

            var db = CreateTkb();
            var elm = new EntityLifecycleModule(db, Array.Empty<int>());
            var system = new GhostPromotionSystem(db, elm, Array.Empty<ITkbEntityTranslator>());

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new TkbIdentity { TkbType = TestTkbType });
            repo.AddComponent(entity, new GhostStateTracker { FirstSeenFrame = 0 });
            repo.SetLifecycleState(entity, EntityLifecycle.Ghost);

            // Must not throw even with empty translator list
            system.Execute(repo, 0f);

            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(entity));
        }
    }
}
