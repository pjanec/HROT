using System;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;

namespace FDP.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Steps the <see cref="BrainBTreeState"/> for every entity whose
    /// <see cref="DoctrineState.BrainTier"/> equals <see cref="BehaviorConstants.BrainTierBTree"/>.
    ///
    /// Ordering: must run AFTER <see cref="ChannelArbitrationSystem"/> so that stale
    /// channels are cleared before the BTree writes new actions.
    ///
    /// Zero allocation per tick: <see cref="BTreeContext"/> is a stack-allocated struct.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ChannelArbitrationSystem))]
    public class BTreeTickSystem : ComponentSystem
    {
        private readonly DoctrineRegistry _registry;

        public BTreeTickSystem(DoctrineRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        protected override void OnUpdate()
        {
            var q = World.Query()
                .With<DoctrineState>()
                .With<BrainBTreeState>()
                .With<BrainBlackboard>()
                .Build();

            foreach (var entity in q)
            {
                var doctrine = World.GetComponent<DoctrineState>(entity);

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

                ref var btState    = ref World.GetComponentRW<BrainBTreeState>(entity);
                ref var blackboard = ref World.GetComponentRW<BrainBlackboard>(entity);

                // Stack-allocate context — zero heap allocation.
                var context = new BTreeContext
                {
                    Self        = entity,
                    World       = World,
                    _deltaTime  = DeltaTime,
                    _floatParams = Array.Empty<float>(),
                    _intParams   = Array.Empty<int>(),
                };

                def.BTreeInterpreter!.Tick(ref blackboard, ref btState.State, ref context);
            }
        }
    }
}
