using System;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using Fdp.ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class GhostCreationSystem : IEcsModuleSystem
    {
        private readonly NetworkEntityMap _entityMap;

        /// <summary>
        /// When <c>true</c>, <see cref="CreateGhost"/> skips all lifecycle state
        /// assignments and network map registration, creating a bare entity shell only.
        ///
        /// <para>
        /// Set to <c>true</c> by <c>ReplayLoadClusterStateHandler</c> during
        /// <c>RunningReplay</c> so that incoming network samples do not spawn ghost
        /// entities that conflict with the recorded entity IDs being replayed
        /// (CGF1-S0304).  Reset to <c>false</c> when returning to
        /// <c>RunningLive</c> (CGF1-S0305).
        /// </para>
        /// </summary>
        public bool BypassLifecycle { get; set; } = false;

        public GhostCreationSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        // No-op: system is registered for pipeline consistency.
        public void Execute(ISimulationView view, float dt) { }
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

