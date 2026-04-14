using Fdp.ModuleHost.Abstractions;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Kernel;

namespace Fdp.Examples.NetworkDemo.Modules
{
    public class BridgeModule : IEcsModule
    {
        public string Name => "Bridge";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly FdpEventBus _bus;
        private readonly ReplayBridgeSystem? _replaySystem;
        private readonly bool _isMaster;
        private readonly int _localNodeId;

        public BridgeModule(FdpEventBus bus, ReplayBridgeSystem? replaySystem, int localNodeId, bool isMaster)
        {
            _bus = bus;
            _replaySystem = replaySystem;
            _localNodeId = localNodeId;
            _isMaster = isMaster;
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new PacketBridgeSystem(_bus, _isMaster, _localNodeId));
            registry.RegisterSystem(new TimeSyncSystem(_bus, _isMaster));
            if (_replaySystem != null)
            {
                registry.RegisterSystem(_replaySystem);
            }
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
