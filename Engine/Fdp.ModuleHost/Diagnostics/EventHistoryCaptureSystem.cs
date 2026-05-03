using Fdp.Core;
using Fdp.Core.Diagnostics;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Diagnostics
{
    /// <summary>
    /// ECS system that captures events from a <see cref="FdpEventBus"/> into
    /// <see cref="IDiagnosticEventHistoryService"/> once per simulation tick.
    /// Must be registered as a global system in the <see cref="SystemPhase.PostSimulation"/>
    /// phase so that all domain systems have committed their events before the buffer is updated.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public sealed class EventHistoryCaptureSystem : IEcsModuleSystem
    {
        private readonly IDiagnosticEventHistoryService _historyService;
        private readonly FdpEventBus _eventBus;

        public EventHistoryCaptureSystem(
            IDiagnosticEventHistoryService historyService,
            FdpEventBus eventBus)
        {
            _historyService = historyService;
            _eventBus       = eventBus;
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            _historyService.Capture(_eventBus, view.Tick);
        }
    }
}
