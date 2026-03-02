using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;
using Fdp.Kernel;
using Fdp.Interfaces;
using System;

namespace FDP.Toolkit.Replication
{
    public class ReplicationLogicModule : IModule
    {
        public string Name => "ReplicationLogic";
        // Runs every frame on main thread
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly NetworkEntityMap _entityMap;
        private readonly ITkbDatabase _tkbDatabase;
        private readonly GhostCreationSystem _ghostCreationSystem;

        public GhostCreationSystem GhostCreationSystem => _ghostCreationSystem;

        public ReplicationLogicModule(
            NetworkEntityMap entityMap,
            ITkbDatabase tkbDatabase)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _tkbDatabase = tkbDatabase ?? throw new ArgumentNullException(nameof(tkbDatabase));
            _ghostCreationSystem = new GhostCreationSystem(_entityMap);
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new OwnershipIngressSystem(_entityMap));
            registry.RegisterSystem(_ghostCreationSystem);
            registry.RegisterSystem(new GhostPromotionSystem(_tkbDatabase));
            registry.RegisterSystem(new SubEntityCleanupSystem());
            registry.RegisterSystem(new DisposalMonitoringSystem(_entityMap));
            registry.RegisterSystem(new OwnershipEgressSystem());
            registry.RegisterSystem(new SmartEgressSystem());
        }

        public void Tick(ISimulationView view, float dt) { }

    }
}
