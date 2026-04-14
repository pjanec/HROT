using Fdp.ModuleHost_Core.Abstractions;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Kernel;

namespace Fdp.Examples.NetworkDemo.Modules
{
    public class GameLogicModule : IEcsModule
    {
        public string Name => "GameLogic";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly int _localNodeId;
        private readonly FdpEventBus _bus; 

        public GameLogicModule(int localNodeId, FdpEventBus bus)
        {
            _localNodeId = localNodeId;
            _bus = bus;
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new PhysicsSystem());
            registry.RegisterSystem(new RefactoredPlayerInputSystem());
            registry.RegisterSystem(new DamageControlModule()); 
            registry.RegisterSystem(new CombatFeedbackSystem(_localNodeId, _bus));
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
