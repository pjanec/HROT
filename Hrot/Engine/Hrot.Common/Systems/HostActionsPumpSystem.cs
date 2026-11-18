using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Interactions;

namespace Hrot.Common.Systems
{
    /// <summary>
    /// ⭐ <c>CE-1246</c> (docs/designs/gizmos-1/canvas-context-menu-design.md §12, HA-4: <i>"a map-less host would schedule
    /// <c>Host.Dispatch</c> itself"</i>) — the host's actions on a host that composes NO map: each frame the host's interaction bus is swapped
    /// (what the map's <c>MapPresentationStep</c> does on a map host) and its <see cref="HostActions.Dispatch"/> runs the requests. ONE system for
    /// every map-less host, so none hand-writes the two calls. ⛔ Never beside a map's own list: that host already swaps and dispatches.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    [TickMode(TickPolicy.EveryFrame)]
    public sealed class HostActionsPumpSystem : IEcsModuleSystem
    {
        private readonly HostActions _host;

        public HostActionsPumpSystem(HostActions host) => _host = host ?? throw new System.ArgumentNullException(nameof(host));

        public void Execute(ISimulationView view, float deltaTime)
        {
            _host.InteractionBus.SwapBuffers();
            _host.Dispatch.Execute(view, deltaTime);
        }
    }
}
