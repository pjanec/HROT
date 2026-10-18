using System;
using Fdp.Core;
using System.Linq;
using Fdp.Toolkit.Behavior.Components;
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

        /// <summary>⭐ CE-3167 — the wire's Kind and Flags come back as the event's Kind and ClearsPriorIntent.</summary>
        [Fact]
        public void CE3167_KindAndFlags_BecomeTheEventsKindAndClearsPriorIntent()
        {
            var translator = new TacticalIntentIngressTranslator(null, _entityMap);
            var entity = _world.CreateEntity();
            _entityMap.Register(42L, entity);

            var sample = new TacticalIntentRequest
            {
                TargetEntityId = 42L, IntentId = "Idle", JsonParams = "{}",
                Kind = (byte)TacticalIntentKind.Clear, Flags = TacticalIntentFlags.ClearsPriorIntent,
            };
            translator.ProcessSample(in sample, _world);
            _world.Bus.SwapBuffers();

            var evt = Assert.Single(_world.Bus.ReadManaged<AssignTacticalIntentEvent>());
            Assert.Equal(TacticalIntentKind.Clear, evt.Kind);
            Assert.True(evt.ClearsPriorIntent);
        }

        /// <summary>⭐ CE-3251 (BR-13) — a forwarded ROE is raised again as the local <see cref="SetRoeEvent"/> with its origin, for
        /// the owner's RoeSystem gate; it is never read as an order.</summary>
        [Fact]
        public void CE3251_AForwardedRoe_IsRaisedAgain_NotAsAnOrder()
        {
            var translator = new TacticalIntentIngressTranslator(null, _entityMap);
            _world.Bus.Register<SetRoeEvent>();
            var entity = _world.CreateEntity();
            _entityMap.Register(42L, entity);
            var sent = new SetRoeEvent { Fire = RoeFire.HoldFire, Reactions = RoeReactions.StayOnTask,
                                         ReturnFireWindowSeconds = 4f, ForceLevel = RoeForceLevel.Warn };

            var sample = new TacticalIntentRequest
            {
                TargetEntityId = 42L, IntentId = string.Empty, Kind = TacticalIntentWireKind.SetRoe,
                Origin = (byte)BehaviorOrigin.Superior, JsonParams = RoeDirectiveJson.Encode(in sent),
            };
            translator.ProcessSample(in sample, _world);
            _world.Bus.SwapBuffers();

            Assert.Empty(_world.Bus.ReadManaged<AssignTacticalIntentEvent>());
            var roe = Assert.Single(_world.Bus.Read<SetRoeEvent>().ToArray());
            Assert.Equal(entity, roe.Entity);
            Assert.Equal(BehaviorOrigin.Superior, roe.Origin);
            Assert.Equal(RoeFire.HoldFire, roe.Fire);
            Assert.Equal(RoeReactions.StayOnTask, roe.Reactions);
            Assert.Equal(4f, roe.ReturnFireWindowSeconds);
            Assert.Equal(RoeForceLevel.Warn, roe.ForceLevel);
        }

        /// <summary>⭐ CE-3251 — a directive kind this node does not know (a newer sender: <c>SetReporting</c> before CC-2 lands) is
        /// counted and dropped — ⛔ before BR-13 any Kind was cast to an order kind.</summary>
        [Fact]
        public void CE3251_AnUnknownDirective_IsCounted_NeverAnOrder()
        {
            var translator = new TacticalIntentIngressTranslator(null, _entityMap);
            var entity = _world.CreateEntity();
            _entityMap.Register(42L, entity);

            var sample = new TacticalIntentRequest { TargetEntityId = 42L, IntentId = "Idle", JsonParams = "{}",
                                                     Kind = TacticalIntentWireKind.SetReporting };
            translator.ProcessSample(in sample, _world);
            _world.Bus.SwapBuffers();

            Assert.Empty(_world.Bus.ReadManaged<AssignTacticalIntentEvent>());
            Assert.Equal(1, translator.UnhandledDirectiveCount);
        }
    }
}
