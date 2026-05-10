using System.Globalization;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Events;

namespace Hrot.Common.Systems
{
    // Bridges the managed ContextActionTriggered event (forwarded by the IG over DDS or
    // published locally by JsonEntityContextMenuHandler) into the typed, unmanaged
    // GlobalActionRequestedEvent that GlobalActionDispatchSystem consumes.
    //
    // ContextActionTriggered is read from the isolated _interactionBus (not the global
    // world bus) so that UI event noise cannot pollute the core simulation bus.
    // GlobalActionRequestedEvent is published back to _interactionBus so that
    // GlobalActionDispatchSystem can read it in the same GizmoInteractionModule.Tick().
    [UpdateInPhase(SystemPhase.Input)]
    [UpdateBefore(typeof(GlobalActionDispatchSystem))]
    public sealed class ContextActionIngressSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;
        private readonly FdpEventBus _interactionBus;

        public ContextActionIngressSystem(NetworkEntityMap entityMap, FdpEventBus interactionBus)
        {
            _entityMap      = entityMap      ?? throw new System.ArgumentNullException(nameof(entityMap));
            _interactionBus = interactionBus ?? throw new System.ArgumentNullException(nameof(interactionBus));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            // Read from the isolated interaction bus, NOT the global world view.
            foreach (var evt in _interactionBus.ReadManaged<ContextActionTriggered>())
            {
                if (!int.TryParse(evt.ActionName,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int actionId))
                {
                    FdpLog<ContextActionIngressSystem>.Warn(
                        "ContextActionIngressSystem: ignoring non-integer ActionName '{0}'.", evt.ActionName);
                    continue;
                }

                Entity target = Entity.Null;
                if (evt.EntityNetworkId != 0)
                    _entityMap.TryGetEntity((long)evt.EntityNetworkId, out target);

                _interactionBus.Publish(new GlobalActionRequestedEvent
                {
                    ActionId = actionId,
                    Target   = target,
                });
            }

            // Offline/editor path: context-menu item clicks can arrive as unmanaged
            // GizmoMenuActionEvent on the same interaction bus (no DDS ingress translator).
            foreach (ref readonly var evt in _interactionBus.Read<GizmoMenuActionEvent>())
            {
                Entity target = Entity.Null;
                if (evt.AnchorId > 0)
                    _entityMap.TryGetEntity(evt.AnchorId, out target);

                _interactionBus.Publish(new GlobalActionRequestedEvent
                {
                    ActionId = evt.ActionId,
                    Target   = target,
                });
            }
        }
    }
}
