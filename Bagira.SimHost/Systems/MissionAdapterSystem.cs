using System;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Legacy adapter that keeps <see cref="DoctrineState"/> aligned with the current
    /// <see cref="MissionPlanQueue"/> phase.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class MissionAdapterSystem : ComponentSystem
    {
        private readonly DoctrineRegistry _doctrineRegistry;
        private readonly NetworkEntityMap _entityMap;

        public MissionAdapterSystem(DoctrineRegistry doctrineRegistry, NetworkEntityMap entityMap)
        {
            _doctrineRegistry = doctrineRegistry ?? throw new ArgumentNullException(nameof(doctrineRegistry));
            _entityMap        = entityMap        ?? throw new ArgumentNullException(nameof(entityMap));
        }

        protected override void OnUpdate()
        {
            var query = World.Query()
                .With<MissionPlanQueue>()
                .With<DoctrineState>()
                .Build();

            foreach (var entity in query)
            {
                ref var queue = ref World.GetComponentRW<MissionPlanQueue>(entity);
                ref var doctrine = ref World.GetComponentRW<DoctrineState>(entity);

                if (queue.CurrentPhase >= queue.PhaseCount)
                    continue;

                // Use Span to safely read the inline Phases buffer without triggering
                // the InlineArray defensive-copy behaviour on the JIT.
                Span<MissionPhase> phases = queue.Phases;
                var phase = phases[queue.CurrentPhase];
                if (doctrine.ActiveDoctrineHash == phase.DoctrineId)
                    continue;

                doctrine.ActiveDoctrineHash = phase.DoctrineId;
                unchecked { doctrine.InstanceId++; }
            }
        }
    }
}
