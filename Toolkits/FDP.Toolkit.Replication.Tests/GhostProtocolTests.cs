using System;
using System.Collections.Generic;
using Xunit;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Systems;
using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;
using FDP.Toolkit.Lifecycle.Events;

namespace FDP.Toolkit.Replication.Tests
{
    // Mocks
    class MockTkbDatabase : ITkbDatabase
    {
        public TkbTemplate TemplateToReturn;
        
        public IEnumerable<TkbTemplate> GetAll() => throw new NotImplementedException();
        public TkbTemplate GetByName(string name) => throw new NotImplementedException();
        public TkbTemplate GetByType(long tkbType) => TemplateToReturn;
        public TkbTemplate GetTemplateByEntityType(Fdp.Kernel.DISEntityType entityType) => null;
        public TkbTemplate GetTemplateByName(string templateName) => null;
        public void Register(TkbTemplate template) {}
        public bool TryGetByName(string name, out TkbTemplate template) => throw new NotImplementedException();
        public bool TryGetByType(long tkbType, out TkbTemplate template)
        {
            template = TemplateToReturn;
            return template != null;
        }
    }

    class SlowMockTkbDatabase : ITkbDatabase
    {
        public TkbTemplate TemplateToReturn;
        public int CallCount = 0;

        public IEnumerable<TkbTemplate> GetAll() => throw new NotImplementedException();
        public TkbTemplate GetByName(string name) => throw new NotImplementedException();
        public TkbTemplate GetByType(long tkbType)
        {
            CallCount++;
            System.Threading.Thread.Sleep(5); // 5ms per entity, exceeds 2ms budget
            return TemplateToReturn;
        }
        public TkbTemplate GetTemplateByEntityType(Fdp.Kernel.DISEntityType entityType) => null;
        public TkbTemplate GetTemplateByName(string templateName) => null;
        public void Register(TkbTemplate template) {}
        public bool TryGetByName(string name, out TkbTemplate template) => throw new NotImplementedException();
        public bool TryGetByType(long tkbType, out TkbTemplate template)
        {
            template = GetByType(tkbType);
            return template != null;
        }
    }

    public class GhostProtocolTests
    {
        [Fact]
        public void PromotionSystem_Promotes_WhenRequirementsMet()
        {
            using var repo = new EntityRepository();

            var template = new TkbTemplate("Test", 123);
            var mockTkb = new MockTkbDatabase { TemplateToReturn = template };
            var sys = new GhostPromotionSystem(mockTkb);

            repo.RegisterComponent<NetworkSpawnRequest>();
            repo.RegisterEvent<ConstructionOrder>();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkSpawnRequest { TkbType = 123 });
            repo.SetLifecycleState(entity, EntityLifecycle.Ghost);

            sys.Execute(repo, 0f);

            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(entity));
            Assert.False(repo.HasComponent<NetworkSpawnRequest>(entity));
        }

        [Fact]
        public void Execute_RespectsTimeBudget()
        {
            using var repo = new EntityRepository();

            var template = new TkbTemplate("Test", 123);
            var slowTkb = new SlowMockTkbDatabase { TemplateToReturn = template };
            var sys = new GhostPromotionSystem(slowTkb);

            repo.RegisterComponent<NetworkSpawnRequest>();
            repo.RegisterEvent<ConstructionOrder>();

            // Create 10 ghost entities; each GetByType call sleeps 5ms, well over the 2ms budget
            for (int i = 0; i < 10; i++)
            {
                var e = repo.CreateEntity();
                repo.AddComponent(e, new NetworkSpawnRequest { TkbType = 123 });
                repo.SetLifecycleState(e, EntityLifecycle.Ghost);
            }

            sys.Execute(repo, 0f);

            Assert.True(slowTkb.CallCount < 10,
                $"Processed too many ghosts: {slowTkb.CallCount}. Should be limited by 2ms time budget.");
            Assert.True(slowTkb.CallCount > 0, "Should have processed at least one ghost.");
        }
        
        [Fact]
        public void Execute_DoesNotPromote_EntityNotInGhostLifecycle()
        {
            // Entity has NetworkSpawnRequest but is NOT in Ghost lifecycle;
            // the promotion query filters by Ghost lifecycle, so it must be skipped.
            using var repo = new EntityRepository();

            var template = new TkbTemplate("Test", 123);
            var mockTkb = new MockTkbDatabase { TemplateToReturn = template };
            var sys = new GhostPromotionSystem(mockTkb);

            repo.RegisterComponent<NetworkSpawnRequest>();
            repo.RegisterEvent<ConstructionOrder>();

            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkSpawnRequest { TkbType = 123 });
            // Deliberately do NOT set lifecycle to Ghost (stays at default Active)

            sys.Execute(repo, 0f);

            // Entity should remain unpromoted — still has NetworkSpawnRequest, not Constructing
            Assert.True(repo.HasComponent<NetworkSpawnRequest>(entity));
            Assert.NotEqual(EntityLifecycle.Constructing, repo.GetLifecycleState(entity));
        }
    }
}
