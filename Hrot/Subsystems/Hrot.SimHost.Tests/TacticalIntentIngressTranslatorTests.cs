using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Xunit;

namespace Hrot.SimHost.Tests
{
    public class TacticalIntentIngressTranslatorTests : IDisposable
    {
        private readonly EntityRepository _world;
        private readonly NetworkEntityMap _entityMap;

        public TacticalIntentIngressTranslatorTests()
        {
            _world = new EntityRepository();
            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _world.Dispose();

        // SC-1: Entity in map -> AssignTacticalIntentEvent published
        [Fact]
        public void ProcessSample_EntityInMap_PublishesAssignTacticalIntentEvent()
        {
            var translator = new TacticalIntentIngressTranslator(null, _entityMap);

            var entity = _world.CreateEntity();
            _entityMap.Register(42L, entity);

            var sample = new TacticalIntentRequest
            {
                TargetEntityId = 42L,
                IntentId       = "DefendArea",
                JsonParams     = "{\"radius\":100}",
            };

            translator.ProcessSample(in sample, _world);
            _world.Bus.SwapBuffers();

            var events = _world.Bus.ReadManaged<AssignTacticalIntentEvent>();
            Assert.Single(events);
            Assert.Equal(entity, events[0].Entity);
            Assert.Equal("DefendArea", events[0].IntentId);
            Assert.Equal("{\"radius\":100}", events[0].JsonParams);
        }

        // SC-2: Entity NOT in map -> no event published, no exception
        [Fact]
        public void ProcessSample_EntityNotInMap_NoEventPublished()
        {
            var translator = new TacticalIntentIngressTranslator(null, _entityMap);
            // No entities registered in _entityMap

            var sample = new TacticalIntentRequest
            {
                TargetEntityId = 99L,
                IntentId       = "DefendArea",
                JsonParams     = "{}",
            };

            var ex = Record.Exception(() =>
            {
                translator.ProcessSample(in sample, _world);
            });
            Assert.Null(ex);

            _world.Bus.SwapBuffers();
            var events = _world.Bus.ReadManaged<AssignTacticalIntentEvent>();
            Assert.Empty(events);
        }
    }
}
