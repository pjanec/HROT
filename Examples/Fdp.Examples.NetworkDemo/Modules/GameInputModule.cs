using Fdp.ModuleHost.Core.Abstractions;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Kernel;

namespace Fdp.Examples.NetworkDemo.Modules
{
    public class GameInputModule : IEcsModule
    {
        public string Name => "GameInput";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly IInputSource _source;
        private readonly FdpEventBus _bus;
        private readonly int _localNodeId;

        public GameInputModule(IInputSource source, FdpEventBus bus, int localNodeId)
        {
            _source = source;
            _bus = bus;
            _localNodeId = localNodeId;
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new TimeInputSystem(_source, _bus));
            registry.RegisterSystem(new OwnershipInputSystem(_localNodeId, _bus));
            registry.RegisterSystem(new CombatInputSystem(_localNodeId, _bus));
            registry.RegisterSystem(new ChatSystem(_localNodeId));
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
