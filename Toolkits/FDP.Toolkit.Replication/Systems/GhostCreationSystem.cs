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
        ///
        /// Sets <see cref="EntityLifecycle.Ghost"/> so <c>GhostPromotionSystem</c> can query
        /// by lifecycle state.  Also attaches <see cref="GhostStateTracker"/> stamped with the
        /// current simulation tick so that promotion and timeout systems can measure age.
        /// </summary>
        /// <param name="repo">The live entity repository.</param>
        /// <param name="networkId">The network (DIS) entity ID.</param>
        /// <param name="tick">
        ///   Current simulation tick (frame number).  Pass <c>view.Tick</c> from the
        ///   calling translator.  Defaults to <c>0</c> for backward compatibility.
        /// </param>
        public Entity CreateGhost(EntityRepository repo, long networkId, uint tick = 0)
        {
            var entity = repo.CreateEntity();
            repo.AddComponent(entity, new NetworkIdentity(networkId));
            repo.AddComponent(entity, new GhostStateTracker { FirstSeenFrame = tick });

            repo.SetLifecycleState(entity, EntityLifecycle.Ghost);

            _entityMap.Register(networkId, entity);

            return entity;
        }
    }
}

