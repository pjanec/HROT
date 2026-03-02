using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Kernel;
using Fdp.Interfaces;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Lifecycle.Events;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Replication.Systems
{
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class GhostPromotionSystem : IModuleSystem
    {
        private readonly ITkbDatabase _tkbDatabase;

        private readonly Queue<Entity> _promotionQueue = new();
        private readonly HashSet<Entity> _inQueue = new();
        private readonly Stopwatch _stopwatch = new();
        private static readonly long PROMOTION_BUDGET_TICKS =
            (long)(0.002 * Stopwatch.Frequency);

        private EntityRepository? _world;
        private EntityQuery? _readyGhostQuery;

        public GhostPromotionSystem(ITkbDatabase tkbDatabase)
        {
            _tkbDatabase = tkbDatabase ?? throw new ArgumentNullException(nameof(tkbDatabase));
        }

        public void Execute(ISimulationView view, float dt)
        {
            _world = view as EntityRepository;
            if (_world == null) return;

            EnsureQueriesInitialized(_world);

            EnqueueReadyGhosts();

            if (_promotionQueue.Count == 0) return;

            _stopwatch.Restart();
            while (_promotionQueue.Count > 0)
            {
                if (_stopwatch.ElapsedTicks > PROMOTION_BUDGET_TICKS) break;

                var entity = _promotionQueue.Dequeue();
                _inQueue.Remove(entity);

                if (!_world.IsAlive(entity)) continue;
                if (!_world.HasComponent<NetworkSpawnRequest>(entity)) continue;

                PromoteGhost(entity);
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

        private void PromoteGhost(Entity entity)
        {
            var spawnReq = _world!.GetComponent<NetworkSpawnRequest>(entity);
            var template = _tkbDatabase.GetTemplate(spawnReq.TkbType);
            if (template == null) return;

            template.ApplyTo(_world!, entity, preserveExisting: true);

            _world!.SetLifecycleState(entity, EntityLifecycle.Constructing);
            _world!.RemoveComponent<NetworkSpawnRequest>(entity);

            _world!.Bus.PublishManaged(new ConstructionOrder { Entity = entity });
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
