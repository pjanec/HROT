using System;
using System.Collections.Generic;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication
{
    public class ReplicationLogicModule : IEcsModule
    {
        public string Name => "ReplicationLogic";
        // Runs every frame on main thread
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly NetworkEntityMap _entityMap;
        private readonly ITkbDatabase _tkbDatabase;
        private readonly EntityLifecycleModule _lifecycleModule;
        private readonly GhostCreationSystem _ghostCreationSystem;
        private readonly IReadOnlyList<ITkbEntityTranslator> _translators;

        public GhostCreationSystem GhostCreationSystem => _ghostCreationSystem;

        public ReplicationLogicModule(
            NetworkEntityMap entityMap,
            ITkbDatabase tkbDatabase,
            EntityLifecycleModule lifecycleModule,
            IReadOnlyList<ITkbEntityTranslator>? translators = null)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _tkbDatabase = tkbDatabase ?? throw new ArgumentNullException(nameof(tkbDatabase));
            _lifecycleModule = lifecycleModule ?? throw new ArgumentNullException(nameof(lifecycleModule));
            _translators = translators ?? System.Array.Empty<ITkbEntityTranslator>();
            _ghostCreationSystem = new GhostCreationSystem(_entityMap);
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new OwnershipIngressSystem(_entityMap));
            registry.RegisterSystem(_ghostCreationSystem);
            registry.RegisterSystem(new GhostPromotionSystem(_tkbDatabase, _lifecycleModule, _translators));
            registry.RegisterSystem(new SubEntityCleanupSystem());
            registry.RegisterSystem(new DisposalMonitoringSystem(_entityMap));
            registry.RegisterSystem(new OwnershipEgressSystem());
            registry.RegisterSystem(new SmartEgressSystem());
        }

        public void Tick(ISimulationView view, float dt) { }

    }
}
