using Fdp.Interfaces;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Systems;

namespace Fdp.Examples.NetworkDemo.Modules
{
    /// <summary>
    /// Thin module wrapper hosting <see cref="CycloneNetworkCleanupSystem"/>.
    /// The cleanup system watches locally-owned entities and calls
    /// <see cref="Fdp.Interfaces.IDescriptorTranslator.Dispose"/> when they are
    /// destroyed, so that DDS readers on remote nodes receive the instance-disposed
    /// notification.
    /// </summary>
    public class NetworkCleanupModule : IEcsModule
    {
        public string Name => "NetworkCleanup";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly CycloneNetworkCleanupSystem _system;

        public NetworkCleanupModule(IDescriptorTranslator masterEgressTranslator)
        {
            _system = new CycloneNetworkCleanupSystem(masterEgressTranslator);
        }

        public void RegisterSystems(ISystemRegistry registry)
            => registry.RegisterSystem(_system);

        public void Tick(ISimulationView view, float dt) { }
    }
}
