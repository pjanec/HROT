using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Constants;
using Hrot.Common.Events;
using Hrot.Common.Systems;
using Xunit;

namespace Hrot.SimHost.Tests.Gizmos
{
    // SC_ER007 / SC_ER008: ContextActionIngressSystem tests.
    // Verifies that managed ContextActionTriggered events are translated into
    // unmanaged GlobalActionRequestedEvent events by ContextActionIngressSystem.

    public sealed class ContextActionIngressSystemTests : IDisposable
    {
        private readonly EntityRepository _repo;
        private readonly NetworkEntityMap _entityMap;

        public ContextActionIngressSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterEvent<GlobalActionRequestedEvent>();

            _entityMap = new NetworkEntityMap();
        }

        public void Dispose() => _repo.Dispose();

        // SC_ER007: A ContextActionTriggered with a valid integer ActionName and a known
        // entity network ID is translated into a GlobalActionRequestedEvent with the
        // matching ActionId and resolved Target entity.
        // B (Fixture Gap TH-3): ContextActionIngressSystem reads from its own _interactionBus,
        // not _repo.Bus. Test must publish ContextActionTriggered to interactionBus and swap it
        // before Execute, then swap again before reading GlobalActionRequestedEvent.
        [Fact]
        public void SC_ER007_ValidActionName_KnownEntity_PublishesGlobalActionRequestedEvent()
        {
            var entity = _repo.CreateEntity();
            _entityMap.Register(42L, entity);

            var interactionBus = new FdpEventBus();
            var sys = new ContextActionIngressSystem(_entityMap, interactionBus);

            // Publish to the isolated interactionBus — NOT _repo.Bus.
            interactionBus.PublishManaged(new ContextActionTriggered
            {
                ActionName      = GlobalActionIds.Rotate.ToString(),
                EntityNetworkId = 42,
            });
            interactionBus.SwapBuffers();

            sys.Execute(_repo, 0f);
            interactionBus.SwapBuffers();

            var events = interactionBus.Read<GlobalActionRequestedEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(GlobalActionIds.Rotate, events[0].ActionId);
            Assert.Equal(entity, events[0].Target);
        }

        // SC_ER008: A ContextActionTriggered with a non-integer ActionName is silently
        // ignored; no GlobalActionRequestedEvent is published.
        [Fact]
        public void SC_ER008_NonIntegerActionName_PublishesNoEvent()
        {
            var interactionBus = new FdpEventBus();
            var sys = new ContextActionIngressSystem(_entityMap, interactionBus);

            _repo.Bus.PublishManaged(new ContextActionTriggered
            {
                ActionName      = "not-a-number",
                EntityNetworkId = 0,
            });
            _repo.Bus.SwapBuffers();

            sys.Execute(_repo, 0f);
            interactionBus.SwapBuffers();

            var events = interactionBus.Read<GlobalActionRequestedEvent>();
            Assert.Equal(0, events.Length);
        }
    }
}
