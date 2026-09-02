using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.Replication.Components;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Replication.Systems
{
    /// <summary>
    /// Promotes ghost entities to <see cref="EntityLifecycle.Constructing"/> once all their
    /// mandatory ECS components (as defined by the <see cref="TkbTemplate"/>) have physically
    /// arrived in memory.
    ///
    /// <para>The system operates as a pure ECS state machine: the promotion query naturally
    /// filters on <c>EntityLifecycle.Ghost + TkbIdentity</c>, so once an entity's lifecycle
    /// advances to <c>Constructing</c> it falls out of the query on the next frame without
    /// any explicit "trigger removal" step.</para>
    ///
    /// <para>Checks are O(1) bitmask operations against the entity's
    /// <see cref="EntityHeader.ComponentMask"/> — no network concepts involved.</para>
    /// </summary>
    [UpdateInPhase(SystemPhase.BeforeSync)]
    public class GhostPromotionSystem : IEcsModuleSystem
    {
        private readonly ITkbDatabase _tkbDatabase;
        private readonly EntityLifecycleModule _lifecycleModule;
        private readonly IReadOnlyList<ITkbEntityTranslator>? _explicitTranslators;

        /// <summary>
        /// ⭐ The node's TKB→ECS projection list. An explicit list wins; otherwise the ONE list the
        /// node's <see cref="EntityLifecycleModule"/> already holds is used — §6.3's
        /// <i>"identical for all three systems within the same node"</i>, satisfied by SHARING the
        /// instance rather than by a second argument nobody passes.
        ///
        /// <para>📌 <c>CE-155</c>. ⚠ <b>Corrected scope, <c>2026-09-01</c>:</b> an earlier version of this
        /// comment said the list was <c>Array.Empty</c> on <i>every</i> node. 📐 It is empty on the
        /// <b>FACTORY path</b> only — <c>NedNetworkFactory.CreateReplicationModule()</c> omits
        /// <c>tkbEntityTranslators</c>, which is how <b>CGF</b> builds its module. Hosts on the
        /// <b>BUILDER path</b> do pass one: <c>HrotNodeBuilderReplicationExtensions.Build():117</c>
        /// forwards <c>.WithTranslators(...)</c>, which SimHost and IG both call. ⇒ the real beneficiary
        /// of this fallback is the factory path. Resolved lazily because composition roots call
        /// <see cref="EntityLifecycleModule.SetTranslators"/> after the module is constructed.</para>
        /// </summary>
        private IReadOnlyList<ITkbEntityTranslator> Translators
            => _explicitTranslators ?? _lifecycleModule.Translators;

        private readonly Queue<Entity> _promotionQueue = new();
        private readonly HashSet<Entity> _inQueue = new();
        private readonly Stopwatch _stopwatch = new();
        private static readonly long PROMOTION_BUDGET_TICKS =
            (long)(0.002 * Stopwatch.Frequency);

        private EntityRepository? _world;
        private EntityQuery? _readyGhostQuery;

        public GhostPromotionSystem(
            ITkbDatabase tkbDatabase,
            EntityLifecycleModule lifecycleModule,
            IReadOnlyList<ITkbEntityTranslator>? translators = null)
        {
            _tkbDatabase = tkbDatabase ?? throw new ArgumentNullException(nameof(tkbDatabase));
            _lifecycleModule = lifecycleModule ?? throw new ArgumentNullException(nameof(lifecycleModule));
            _explicitTranslators = translators;
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
                if (!_world.HasComponent<TkbIdentity>(entity)) continue;

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
            var tkbIdentity = _world!.GetComponent<TkbIdentity>(entity);

            // Fetch component mask for O(1) bitmask checks.
            ref var compGP = ref _world.GetComponentMask(entity.Index);

            // Read ghost age for soft-timeout evaluation.
            var tracker = _world.GetComponent<GhostStateTracker>(entity);

            // Evaluate mandatory components defined by the template.
            if (_tkbDatabase.TryGetByType(tkbIdentity.TkbType, out var template))
            {
                foreach (var req in template.MandatoryComponents)
                {
                    bool hasComponent = compGP.IsSet(req.ComponentTypeId);

                    if (!hasComponent)
                    {
                        if (req.IsHard)
                            return; // Abort — hard requirement not yet satisfied.

                        // Soft requirement: wait until timeout expires.
                        if (tick - tracker.FirstSeenFrame <= req.SoftTimeoutFrames)
                            return;
                        // Timeout elapsed — proceed without this optional component.
                    }
                }

                // All requirements satisfied: apply blueprint defaults.
                foreach (var t in Translators)
                    t.Inject(_world!, entity, template);
            }

            // Promote: Ghost → Constructing.
            // The entity naturally falls out of _readyGhostQuery next frame because
            // the query requires EntityLifecycle.Ghost.
            _world!.SetLifecycleState(entity, EntityLifecycle.Constructing);

            // Remove the transient tracker now that the ghost has been promoted.
            _world!.RemoveComponent<GhostStateTracker>(entity);

            _lifecycleModule.BeginConstruction(entity, tkbIdentity.TkbType, tick, cmdBuffer);
        }

        private void EnsureQueriesInitialized(EntityRepository repo)
        {
            if (_readyGhostQuery != null) return;

            _readyGhostQuery = repo.Query()
                .With<TkbIdentity>()
                .WithLifecycle(EntityLifecycle.Ghost)
                .Build();
        }
    }
}

