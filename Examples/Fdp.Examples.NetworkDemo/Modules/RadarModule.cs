using Fdp.ModuleHost.Core.Abstractions;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Kernel;

namespace Fdp.Examples.NetworkDemo.Modules
{
    public class RadarModule : IEcsModule
    {
        public string Name => "Radar";
        // Runs 5 times a second, on a background thread
        public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(5);

        private readonly IEventBus _bus;

        public RadarModule(IEventBus bus)
        {
            _bus = bus;
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new RadarSystem(_bus));
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
