using System;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class DisposalMonitoringSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public DisposalMonitoringSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void Execute(ISimulationView view, float dt)
        {
            // Main-thread PostSimulation: view is the live EntityRepository.
            if (view is EntityRepository repo)
                _entityMap.PruneDeadEntities(repo);
        }
    }
}
