using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Behavior.Devices
{
    /// <summary>
    /// Simulated devices SD-S4 (docs/designs/devices/DESIGN_Simulated_Devices.md sec 3.13b D-99) - clears <see cref="GuidedFlight.Live"/> (and ends its state) when the
    /// round the device launched is gone - a hit, the fuel, the lifetime. The MUSCLE's, serial, registered beside the round path in <c>CombatModule</c> AFTER the systems
    /// that end a round; the launch itself sets <c>Live</c> (<see cref="FireControls.ClaimLaunch"/>). One writer of the flight's end: the sight unit reads it to leave
    /// GUIDING and to give the picture back. EVERY frame (a pause leaves a live round alive, so nothing changes in pause); nothing while a replay plays (the recording owns it).
    /// 0 B per steady frame: a component is written only when a flight ends.
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    [TickMode(TickPolicy.EveryFrame)]
    [Fdp.Core.NotState("a system/module, classified on its own")]
    public sealed class GuidedFlightSystem : IEcsModuleSystem
    {
        private EntityQuery? _flights;
        private EntityRepository? _queryFor;

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<GuidedFlight>()) return;
            if (_flights == null || !ReferenceEquals(repo, _queryFor)) { _flights = repo.Query().With<GuidedFlight>().Build(); _queryFor = repo; }
            foreach (var device in _flights)
            {
                ref readonly var now = ref repo.GetComponentRO<GuidedFlight>(device);
                if (now.Live == 0 || repo.IsAlive(now.Round)) continue;
                ref var flight = ref repo.GetComponentRW<GuidedFlight>(device);
                flight.Live = 0;
                flight.State = GuidedFlightState.Ended;
            }
        }
    }
}
