using System;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class GhostCreationSystem : IModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        public GhostCreationSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        // No-op: system is registered for pipeline consistency.
        public void Execute(ISimulationView view, float dt) { }

        /// <summary>
        /// Creates a ghost shell entity for the given network ID.
        /// Called by ingress translators on the Input phase main thread.
        /// The caller must supply a live <see cref="EntityRepository"/> from their view.
        /// Sets EntityLifecycle.Ghost so GhostPromotionSystem can query by lifecycle state.
        /// </summary>
        public Entity CreateGhost(EntityRepository repo, long networkId)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(networkId));

            repo.SetLifecycleState(entity, EntityLifecycle.Ghost);

            _entityMap.Register(networkId, entity);

            return entity;
        }
    }
}
