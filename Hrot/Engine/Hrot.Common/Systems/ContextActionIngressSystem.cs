using System.Globalization;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Events;

namespace Hrot.Common.Systems
{
    // Bridges the managed ContextActionTriggered event (forwarded by the IG over DDS)
    // into the typed, unmanaged GlobalActionRequestedEvent that GlobalActionDispatchSystem
    // consumes.
    //
    // Runs before GlobalActionDispatchSystem in the same Input phase so that the
    // translated events are available for dispatch in the same kernel update.
    [UpdateInPhase(SystemPhase.Input)]
    [UpdateBefore(typeof(GlobalActionDispatchSystem))]
    public sealed class ContextActionIngressSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public ContextActionIngressSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new System.ArgumentNullException(nameof(entityMap));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            var repo = view as EntityRepository
                ?? throw new System.InvalidOperationException(
                    $"{nameof(ContextActionIngressSystem)} requires EntityRepository access.");

            foreach (var evt in view.ReadManagedEvents<ContextActionTriggered>())
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

                repo.Bus.Publish(new GlobalActionRequestedEvent
                {
                    ActionId = actionId,
                    Target   = target,
                });
            }
        }
    }
}
