using System;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Events;

namespace Hrot.CGF.Systems
{
    /// <summary>
    /// Bridges high-level, managed mission plans into the cognitive ECS tier by detecting phase transitions 
    /// and publishing <see cref="AssignDoctrineEvent"/>s containing the behavior JSON parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Architecture (DRY Pipeline):</b> This system intentionally does <i>not</i> mutate <see cref="DoctrineState"/> 
    /// or <see cref="BrainBlackboard"/> directly. Instead, it acts purely as a change-detector and dispatcher. 
    /// When a phase change is detected, it extracts the <c>BehaviorParams</c> JSON and publishes an 
    /// <see cref="AssignDoctrineEvent"/>. This delegates all execution to <see cref="DoctrineIngressSystem"/>, 
    /// making it the single source of truth for doctrine transitions and atomic blackboard parsing. 
    /// This eliminates legacy "double-apply" bugs that previously wiped out behavior memory (e.g., <c>RoundsFired</c>).
    /// </para>
    /// <para>
    /// <b>Re-commits & State Caching:</b> The system actively caches the exhaustion of a mission plan 
    /// (<c>queue.CurrentPhase >= queue.PhaseCount</c>). This ensures that if the exact same mission 
    /// is re-committed and restarted from Phase 0, the adapter correctly detects the phase reset, 
    /// re-publishes the event, and forces the BTree parameters to cleanly re-initialize.
    /// </para>
    /// </remarks>
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

        protected override unsafe void OnUpdate()
        {
            var query = World.Query()
                .With<MissionPlanQueue>()
                .With<DoctrineState>()
                .Build();

            foreach (var entity in query)
            {
                ref var queue = ref World.GetComponentRW<MissionPlanQueue>(entity);
                ref var doctrine = ref World.GetComponentRW<DoctrineState>(entity);

                if (!World.HasComponent<Hrot.CGF.Components.MissionAdapterState>(entity))
                    World.AddComponent(entity, new Hrot.CGF.Components.MissionAdapterState { LastPhase = byte.MaxValue });

                ref var adapterState = ref World.GetComponentRW<Hrot.CGF.Components.MissionAdapterState>(entity);
                var activePlan = World.GetComponent<ActiveMissionPlan>(entity);

                // Cache exhaustion so re-committing the same mission from phase 0 is correctly detected
                if (queue.CurrentPhase >= queue.PhaseCount)
                {
                    adapterState.LastPhase = queue.CurrentPhase;
                    continue;
                }

                Span<MissionPhase> phases = queue.Phases;
                var phase = phases[queue.CurrentPhase];

                string jsonParams = "{}";
                if (activePlan?.Plan?.Tasks != null && queue.CurrentPhase < activePlan.Plan.Tasks.Count)
                {
                    var task = activePlan.Plan.Tasks[queue.CurrentPhase];
                    jsonParams = task.BehaviorParams ?? "{}";
                }

                uint currentDefHash = (uint)(jsonParams.GetHashCode() ^ phase.DoctrineId);

                // Skip if nothing changed
                if (adapterState.LastPhase == queue.CurrentPhase && adapterState.LastPlanVersion == currentDefHash)
                    continue;

                adapterState.LastPhase = queue.CurrentPhase;
                adapterState.LastPlanVersion = currentDefHash;

                var defName = "Idle";
                if (_doctrineRegistry.TryGetDefinition(phase.DoctrineId, out var def))
                    defName = def.Name;

                // Embrace DRY! Remove ALL direct ECS mutation blocks.
                // No writing to BrainBlackboard. No updating DoctrineState. 
                // Just publish the managed event and let DoctrineIngressSystem be the single owner!
                World.Bus.PublishManaged(new AssignDoctrineEvent
                {
                    Entity = entity,
                    DoctrineName = defName,
                    JsonParams = jsonParams
                });
            }
        }
    }
}
