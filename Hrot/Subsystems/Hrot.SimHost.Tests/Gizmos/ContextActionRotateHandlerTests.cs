using System.Linq;
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
            _entityMap.Register(42, entity);

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

        /// <summary>⭐ CE-3179 — the world-following form the ReplayBrowser uses: its active world (and so its entity map) changes when
        /// a recording is rebound. A map-menu click resolves its anchor against the map current THAT frame; with no map the action
        /// still runs, untargeted (a main-menu item has no target).</summary>
        [Fact]
        public void CE3179_AMenuClick_ResolvesAgainstTheCurrentWorldsMap_AndStillRunsWithoutOne()
        {
            var a = _repo.CreateEntity();
            var b = _repo.CreateEntity();
            var first = new NetworkEntityMap();
            first.Register(7, a);
            var second = new NetworkEntityMap();
            second.Register(7, b);
            NetworkEntityMap? current = first;
            var bus = new FdpEventBus();
            bus.Register<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent>();
            bus.Register<GlobalActionRequestedEvent>();
            var sys = new ContextActionIngressSystem(() => current, bus);

            Entity Click()
            {
                bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent { AnchorId = 7, ActionId = GlobalActionIds.CenterOnEntity });
                bus.SwapBuffers();
                sys.Execute(_repo, 0f);
                bus.SwapBuffers();
                var events = bus.Read<GlobalActionRequestedEvent>();
                Assert.Equal(1, events.Length);
                Assert.Equal(GlobalActionIds.CenterOnEntity, events[0].ActionId);
                return events[0].Target;
            }

            Assert.Equal(a, Click());
            current = second;                 // a recording was rebound
            Assert.Equal(b, Click());
            current = null;                   // no world yet
            Assert.Equal(Entity.Null, Click());
        }

        /// <summary>⭐ A4 (R-4162) — a menu click on a SELECTED entity runs a PerEntity action (Delete) for every selected one; a Selection action
        /// (Center View) and a click on an UNSELECTED entity run once, for the clicked one. 🔴 Red-proof (compiles): <c>RunsPerEntity</c> answering false ⇒
        /// one Delete ⇒ red.</summary>
        [Fact]
        public void A4_AClickOnASelectedEntity_RunsAPerEntityActionForEverySelected_OtherwiseOnce()
        {
            _repo.RegisterComponent<Hrot.Map.Common.Components.SelectionState>();
            Entity Unit(int net, bool selected)
            {
                var e = _repo.CreateEntity();
                _repo.AddComponent(e, new Hrot.Map.Common.Components.SelectionState { IsSelected = selected });
                _entityMap.Register(net, e);
                return e;
            }
            var a = Unit(7, true); var b = Unit(8, true); var c = Unit(9, false);
            var bus = new FdpEventBus();
            bus.Register<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent>();
            bus.Register<GlobalActionRequestedEvent>();
            var sys = new ContextActionIngressSystem(_entityMap, bus);
            Entity[] Click(int anchor, int action)
            {
                bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent { AnchorId = anchor, ActionId = action });
                bus.SwapBuffers();
                sys.Execute(_repo, 0f);
                bus.SwapBuffers();
                return bus.Read<GlobalActionRequestedEvent>().ToArray().Select(e => e.Target).OrderBy(e => e.Index).ToArray();
            }

            Assert.Equal(new[] { a, b }, Click(8, GlobalActionIds.Delete));            // in the selection ⇒ every selected one
            Assert.Equal(new[] { c }, Click(9, GlobalActionIds.Delete));               // outside it ⇒ that one
            Assert.Equal(new[] { b }, Click(8, GlobalActionIds.CenterOnEntity));       // a Selection action ⇒ once, the clicked one
        }

        /// <summary>⭐ CE-3178 — a click relayed from another node (the gizmo topic's ContextActionTriggered) keeps that mark into
        /// the request the dispatcher reads; this node's own menu click (GizmoMenuActionEvent) is unmarked.</summary>
        [Fact]
        public void CE3178_ARelayedClick_StaysMarkedRelayed_ALocalOneDoesNot()
        {
            var bus = new FdpEventBus();
            bus.Register<Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent>();
            bus.Register<GlobalActionRequestedEvent>();
            var sys = new ContextActionIngressSystem(_entityMap, bus);
            bus.PublishManaged(new ContextActionTriggered { ActionName = GlobalActionIds.MoveHere.ToString(), Relayed = true });
            bus.Publish(new Fdp.Toolkit.Diagnostics.Gizmos.Events.GizmoMenuActionEvent { ActionId = GlobalActionIds.Stop });
            bus.SwapBuffers();
            sys.Execute(_repo, 0f);
            bus.SwapBuffers();
            var events = bus.Read<GlobalActionRequestedEvent>().ToArray();
            Assert.Equal(2, events.Length);
            Assert.Contains(events, e => e.ActionId == GlobalActionIds.MoveHere && e.Relayed == 1);
            Assert.Contains(events, e => e.ActionId == GlobalActionIds.Stop && e.Relayed == 0);
        }
    }
}
