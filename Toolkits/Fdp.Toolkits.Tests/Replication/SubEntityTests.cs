using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Systems;
using Fdp.ModuleHost_Core.Abstractions;
using System;
using System.Collections.Generic;
using Xunit;

namespace FDP.Toolkit.Replication.Tests
{
    public class SubEntityTests
    {
        class TestTkbDatabase : ITkbDatabase
        {
            public Dictionary<long, TkbTemplate> Templates = new Dictionary<long, TkbTemplate>();

            public IEnumerable<TkbTemplate> GetAll() => Templates.Values;
            public TkbTemplate GetByName(string name) => throw new NotImplementedException();
            public TkbTemplate GetByType(long tkbType) => Templates[tkbType];
            public void Register(TkbTemplate template) { Templates[template.TkbType] = template; }
            public bool TryGetByName(string name, out TkbTemplate template) => throw new NotImplementedException();
            public bool TryGetByType(long tkbType, out TkbTemplate template)
            {
                return Templates.TryGetValue(tkbType, out template);
            }
        }

        [Fact]
        public void PromoteGhost_WithRegisteredTemplate_PromotesToConstructingAndPublishesOrder()
        {
            // GhostPromotionSystem queries entities in Ghost lifecycle with TkbIdentity,
            // looks up their template, applies it, advances lifecycle to Constructing,
            // removes GhostStateTracker (TkbIdentity is permanent), and fires ConstructionOrder.
            using var repo = new EntityRepository();

            var tkb = new TestTkbDatabase();
            var parentTemplate = new TkbTemplate("Parent", 100);
            tkb.Register(parentTemplate);

            var elm = new FDP.Toolkit.Lifecycle.EntityLifecycleModule(tkb, Array.Empty<int>());
            var sys = new GhostPromotionSystem(tkb, elm);

            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<ConstructionOrder>();

            // Create ghost entity
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new TkbIdentity { TkbType = 100 });
            repo.AddComponent(entity, new GhostStateTracker { FirstSeenFrame = 0 });
            repo.SetLifecycleState(entity, EntityLifecycle.Ghost);

            sys.Execute(repo, 0f);
            var cmdBuffer = (EntityCommandBuffer)((ISimulationView)repo).GetCommandBuffer();
            cmdBuffer.Playback(repo);

            // Ghost should be promoted
            Assert.Equal(EntityLifecycle.Constructing, repo.GetLifecycleState(entity));
            // TkbIdentity is permanent — NOT removed after promotion
            Assert.True(repo.HasComponent<TkbIdentity>(entity));
            // GhostStateTracker is transient — removed after promotion
            Assert.False(repo.HasComponent<GhostStateTracker>(entity));

            // ConstructionOrder event should have been published
            repo.Bus.SwapBuffers();
            var orders = ((ISimulationView)repo).ConsumeEvents<ConstructionOrder>();
            Assert.Equal(1, orders.Length);
            Assert.Equal(entity, orders[0].Entity);
        }
    }
}
