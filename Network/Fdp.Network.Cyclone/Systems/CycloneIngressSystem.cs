using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using Fdp.Interfaces;
using Fdp.ModuleHost.Core.Abstractions;
using Fdp.ModuleHost.Network.Cyclone.Services;

namespace Fdp.ModuleHost.Network.Cyclone.Systems
{
    /// <summary>
    /// System responsible for polling all registered translators for incoming network data.
    /// Iterates translators, which now own their data readers.
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class CycloneIngressSystem : IEcsModuleSystem
    {
        private readonly DdsParticipant _participant;
        private readonly IDescriptorTranslator[] _translators;
        private readonly NetworkEntityMap _entityMap;
        
        public CycloneIngressSystem(
            DdsParticipant participant, 
            IEnumerable<IDescriptorTranslator> translators,
            NetworkEntityMap entityMap)
        {
            _participant = participant;
            _translators = new List<IDescriptorTranslator>(translators).ToArray();
            _entityMap = entityMap;
        }
        
        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            
            // Iterate all translators (Polymorphism handles Unsafe vs Managed vs Replay)
            foreach (var translator in _translators)
            {
                // In Owner Model, we just call PollIngress.
                // The translator holds the Reader (or Replay source).
                translator.PollIngress(cmd, view);
            }
        }
    }
}
