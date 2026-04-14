using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Network;
using Fdp.Interfaces; // For Interfaces

using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;
// IDataWriter alias removed

namespace Fdp.Network.Cyclone.Systems
{
    /// <summary>
    /// System responsible for publishing owned descriptors to the network.
    /// Handles normal periodic publishing and force-publish requests.
    /// </summary>
    [UpdateInPhase(SystemPhase.Export)]
    public class CycloneEgressSystem : IEcsModuleSystem
    {
        private readonly IDescriptorTranslator[] _translators;
        
        public CycloneEgressSystem(IDescriptorTranslator[] translators)
        {
            _translators = translators ?? throw new ArgumentNullException(nameof(translators));
        }
        
        public void Execute(ISimulationView view, float deltaTime)
        {
            // Process force-publish requests first
            ProcessForcePublish(view);
            
            //FDP.Kernel.Logging.FdpLog<CycloneEgressSystem>.Info("Publishing via {0} translators", _translators.Length);

            // Normal periodic publishing
            for (int i = 0; i < _translators.Length; i++)
            {
               // FDP.Kernel.Logging.FdpLog<CycloneEgressSystem>.Info("Scanning {0}: {1}", i, _translators[i].DescriptorOrdinal);
                _translators[i].ScanAndPublish(view);
            }
        }
        
        private void ProcessForcePublish(ISimulationView view)
        {
            var cmd = view.GetCommandBuffer();
            
            // Query entities with ForceNetworkPublish
            var query = view.Query()
                .With<ForceNetworkPublish>()
                .Build();
            
            foreach (var entity in query)
            {
                // Remove the component - it's one-time
                cmd.RemoveComponent<ForceNetworkPublish>(entity);
                
                // Force publish happens implicitly in next ScanAndPublish
                // The translators will see this entity and publish it
            }
        }
    }
}
