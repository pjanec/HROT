using System;
using Fdp.Core;
using Hrot.Common.Constants;
using Hrot.Common.Events;
using Hrot.Common.Interactions;
using Hrot.Common.Systems;
using Xunit;

namespace Hrot.SimHost.Tests.Gizmos
{
    // SC-ER001 through SC-ER004: Global action registry and dispatch system tests.
    // Verifies the typed GlobalActionRegistry + GlobalActionDispatchSystem pipeline.

    // =========================================================================
    // SC_ER001 / SC_ER002: GlobalActionRegistry contract
    // =========================================================================

    public sealed class GlobalActionRegistryTests
    {
        // SC_ER001: Registering a handler and calling TryGetHandler returns that handler.
        [Fact]
        public void SC_ER001_Register_ThenTryGetHandler_ReturnsRegisteredHandler()
        {
            var registry = new GlobalActionRegistry();
            GlobalActionHandler? captured = null;
            GlobalActionHandler expected = (view, target) => captured = null /* side-effect placeholder */;

            registry.Register(GlobalActionIds.Rotate, expected);

            bool found = registry.TryGetHandler(GlobalActionIds.Rotate, out var actual);

            Assert.True(found);
            Assert.Equal(expected, actual);
        }

        // SC_ER002: TryGetHandler returns false for an ID that was never registered.
        [Fact]
        public void SC_ER002_TryGetHandler_ReturnsFalse_ForUnregisteredId()
        {
            var registry = new GlobalActionRegistry();

            bool found = registry.TryGetHandler(GlobalActionIds.Rotate, out _);

            Assert.False(found);
        }
    }

    // =========================================================================
    // SC_ER003 / SC_ER004: GlobalActionDispatchSystem
    // =========================================================================

    public sealed class GlobalActionDispatchSystemTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public GlobalActionDispatchSystemTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterEvent<GlobalActionRequestedEvent>();
        }

        public void Dispose() => _repo.Dispose();

        // SC_ER003: Publishing GlobalActionRequestedEvent causes the registered handler to run.
        [Fact]
        public void SC_ER003_PublishedEvent_InvokesRegisteredHandler()
        {
            Entity handlerTarget = Entity.Null;
            var registry = new GlobalActionRegistry();
            registry.Register(GlobalActionIds.Rotate, (view, target) => { handlerTarget = target; });

            var entity = _repo.CreateEntity();
            var interactionBus = new FdpEventBus();
            interactionBus.Publish(new GlobalActionRequestedEvent { ActionId = GlobalActionIds.Rotate, Target = entity });
            interactionBus.SwapBuffers();

            var sys = new GlobalActionDispatchSystem(registry, interactionBus);
            sys.Execute(_repo, 0f);

            Assert.Equal(entity, handlerTarget);
        }

        // SC_ER004: An unregistered action ID in the event produces no exception.
        [Fact]
        public void SC_ER004_UnregisteredActionId_DoesNotThrow()
        {
            var registry = new GlobalActionRegistry();
            var interactionBus = new FdpEventBus();
            var sys = new GlobalActionDispatchSystem(registry, interactionBus);

            interactionBus.Publish(new GlobalActionRequestedEvent { ActionId = 9999, Target = Entity.Null });
            interactionBus.SwapBuffers();

            // Should not throw.
            sys.Execute(_repo, 0f);
        }
    }
}
