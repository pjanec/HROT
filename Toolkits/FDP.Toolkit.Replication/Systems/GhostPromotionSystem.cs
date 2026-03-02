using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Replication.Components;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class GhostPromotionSystem : IModuleSystem
    {
        private readonly ITkbDatabase _tkbDatabase;
        private readonly EntityLifecycleModule _lifecycleModule;

        private readonly Queue<Entity> _promotionQueue = new();
        private readonly HashSet<Entity> _inQueue = new();
        private readonly Stopwatch _stopwatch = new();
        private static readonly long PROMOTION_BUDGET_TICKS =
            (long)(0.002 * Stopwatch.Frequency);

        private EntityRepository? _world;
        private EntityQuery? _readyGhostQuery;

        public GhostPromotionSystem(ITkbDatabase tkbDatabase, EntityLifecycleModule lifecycleModule)
        {
            _tkbDatabase = tkbDatabase ?? throw new ArgumentNullException(nameof(tkbDatabase));
            _lifecycleModule = lifecycleModule ?? throw new ArgumentNullException(nameof(lifecycleModule));
        }

        public void Execute(ISimulationView view, float dt)
        {
            _world = view as EntityRepository;
            if (_world == null) return;

            EnsureQueriesInitialized(_world);

            EnqueueReadyGhosts();

            if (_promotionQueue.Count == 0) return;

            _stopwatch.Restart();
            var cmdBuffer = view.GetCommandBuffer();
            var tick = view.Tick;

            while (_promotionQueue.Count > 0)
            {
                if (_stopwatch.ElapsedTicks > PROMOTION_BUDGET_TICKS) break;

                var entity = _promotionQueue.Dequeue();
                _inQueue.Remove(entity);

                if (!_world.IsAlive(entity)) continue;
                if (!_world.HasComponent<NetworkSpawnRequest>(entity)) continue;

                PromoteGhost(entity, cmdBuffer, tick);
            }
            _stopwatch.Stop();
        }

        private void EnqueueReadyGhosts()
        {
            foreach (var entity in _readyGhostQuery!)
            {
                if (_inQueue.Contains(entity)) continue;

                _promotionQueue.Enqueue(entity);
                _inQueue.Add(entity);
            }
        }

        private void PromoteGhost(Entity entity, IEntityCommandBuffer cmdBuffer, uint tick)
        {
            var spawnReq = _world!.GetComponent<NetworkSpawnRequest>(entity);

            // Apply blueprint template if available.  Unknown types still receive lifecycle
            // tracking so they can reach Active state and be discovered by queries.
            if (_tkbDatabase.TryGetByType(spawnReq.TkbType, out var template))
            {
                template.ApplyTo(_world!, entity, preserveExisting: true);
            }

            _world!.SetLifecycleState(entity, EntityLifecycle.Constructing);
            _world!.RemoveComponent<NetworkSpawnRequest>(entity);

            _lifecycleModule.BeginConstruction(entity, spawnReq.TkbType, tick, cmdBuffer);
        }

        private void EnsureQueriesInitialized(EntityRepository repo)
        {
            if (_readyGhostQuery != null) return;

            _readyGhostQuery = repo.Query()
                .With<NetworkSpawnRequest>()
                .WithLifecycle(EntityLifecycle.Ghost)
                .Build();
        }
    }
}
