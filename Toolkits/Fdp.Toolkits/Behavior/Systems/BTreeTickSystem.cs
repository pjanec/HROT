using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Steps the <see cref="BrainBTreeState"/> for every entity whose
    /// <see cref="DoctrineState.BrainTier"/> equals <see cref="BehaviorConstants.BrainTierBTree"/>.
    ///
    /// Ordering: must run AFTER <see cref="ChannelArbitrationSystem"/> so that stale
    /// channels are cleared before the BTree writes new actions.
    ///
    /// Zero allocation per tick: <see cref="BTreeContext"/> is a stack-allocated struct.
    ///
    /// Publishes <see cref="DoctrineFinishedEvent"/> exactly once per terminal doctrine
    /// transition (Success or Failure). A secondary tick on an already-terminal doctrine
    /// does not re-publish; the event is suppressed until the doctrine's
    /// <see cref="DoctrineState.InstanceId"/> changes (i.e. a new doctrine is assigned).
    /// </summary>
    [UpdateInPhase(SystemPhase.Simulation)]
    // [UpdateAfter(typeof(ChannelArbitrationSystem))] -- ordering maintained by array position in CognitiveRuntimeModule.
    public class BTreeTickSystem : IEcsModuleSystem
    {
        private readonly DoctrineRegistry _registry;

        /// <summary>
        /// Tracks the <see cref="DoctrineState.InstanceId"/> for which a terminal
        /// <see cref="DoctrineFinishedEvent"/> was last published, keyed by entity index.
        /// Prevents repeated publication when the same doctrine evaluation stays terminal
        /// across consecutive ticks.
        /// </summary>
        private readonly Dictionary<int, uint> _publishedTerminalForInstanceId = new();

        // Reusable collections for dead-entity pruning — pre-allocated to avoid per-frame heap pressure.
        private readonly HashSet<int> _seenThisFrame = new();
        private readonly List<int>    _staleKeys     = new();

        /// <summary>
        /// Number of entity indices currently tracked in the terminal-event deduplication
        /// dictionary. Exposed for test verification only.
        /// </summary>
        internal int TrackedEntityCount => _publishedTerminalForInstanceId.Count;

        public BTreeTickSystem(DoctrineRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BTreeTickSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var q = repo.Query()
                .With<DoctrineState>()
                .With<BrainBTreeState>()
                .With<BrainBlackboard>()
                .Build();

            _seenThisFrame.Clear();

            foreach (var entity in q)
            {
                _seenThisFrame.Add(entity.Index);

                var doctrine = repo.GetComponent<DoctrineState>(entity);

                // Only process BTree-tier entities.
                if (doctrine.BrainTier != BehaviorConstants.BrainTierBTree)
                    continue;

                // If the doctrine is not registered, skip silently.
                if (!_registry.TryGetDefinition(doctrine.ActiveDoctrineHash, out var def)
                    || def.BTreeInterpreter == null)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"[BTreeTickSystem] Doctrine hash {doctrine.ActiveDoctrineHash} not registered; entity {entity.Index} skipped.");
#endif
                    continue;
                }

                ref var btState    = ref repo.GetComponentRW<BrainBTreeState>(entity);
                ref var blackboard = ref repo.GetComponentRW<BrainBlackboard>(entity);

                // Stack-allocate context -- zero heap allocation.
                var context = new BTreeContext
                {
                    Self        = entity,
                    World       = repo,
                    _deltaTime  = deltaTime,
                    _floatParams = Array.Empty<float>(),
                    _intParams   = Array.Empty<int>(),
                };

                var rootResult = def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context);

                // Publish DoctrineFinishedEvent exactly once per terminal transition per
                // doctrine instance. Suppress re-publication when the same InstanceId has
                // already triggered the event (e.g. the BTree stays at Success across ticks).
                if (rootResult == NodeStatus.Success || rootResult == NodeStatus.Failure)
                {
                    if (!_publishedTerminalForInstanceId.TryGetValue(entity.Index, out uint prevInstanceId)
                        || prevInstanceId != doctrine.InstanceId)
                    {
                        repo.Bus.Publish(new DoctrineFinishedEvent
                        {
                            Entity = entity,
                            Result = rootResult
                        });
                        _publishedTerminalForInstanceId[entity.Index] = doctrine.InstanceId;
                    }
                }
            }

            // Prune entries for entities that were not seen in this frame (destroyed or
            // their required components removed). Uses pre-allocated collections to avoid
            // per-frame heap allocations.
            _staleKeys.Clear();
            foreach (var key in _publishedTerminalForInstanceId.Keys)
                if (!_seenThisFrame.Contains(key))
                    _staleKeys.Add(key);
            foreach (var key in _staleKeys)
                _publishedTerminalForInstanceId.Remove(key);
        }
    }
}
