using Fdp.Core;
using Hrot.Common.Events;
using Hrot.Common.Systems;

namespace Hrot.Common.Interactions
{
    // ⭐⭐ CE-1150 (canvas-context-menu-design §12 HA-1; global-action-system-design §5.3 / §5.6) — THE HOST'S actions: one registry,
    //   the interaction bus requests travel on, and the system that runs them. Made ONCE by the host object, at its start — never by a
    //   map or a window. The map pack, the inspector's menu and the Details bundle all register on it and request through it.
    //   🔒 User, 2026-10-16: "action channel must exists without a map, handling menus etc".
    public sealed class HostActions
    {
        public HostActions()
        {
            InteractionBus = new FdpEventBus();
            InteractionEventRegistry.RegisterAll(InteractionBus);
            Dispatch = new GlobalActionDispatchSystem(Actions, InteractionBus);
        }

        // The host's action registry (action id -> handler).
        public GlobalActionRegistry Actions { get; } = new();

        // The bus a request travels on (GlobalActionRequestedEvent, the map's menu clicks, the gizmo events).
        public FdpEventBus InteractionBus { get; }

        // Runs the requests on InteractionBus; the host schedules it (today through the map's system list).
        public GlobalActionDispatchSystem Dispatch { get; }

        // Asks for <paramref name="actionId"/> on <paramref name="target"/>; the dispatch system runs it on its next tick.
        public void Request(int actionId, Entity target)
            => InteractionBus.Publish(new GlobalActionRequestedEvent { ActionId = actionId, Target = target });
    }
}
