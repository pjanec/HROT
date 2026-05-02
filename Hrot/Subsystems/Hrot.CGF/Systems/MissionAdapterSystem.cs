using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;

namespace Hrot.CGF.Systems
{
    /// <summary>
    /// Bridges high-level, managed mission plans into the cognitive ECS tier by detecting phase transitions 
    /// and publishing <see cref="AssignTacticalIntentEvent"/>s containing the behavior ID and JSON parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Architecture (DRY Pipeline):</b> This system intentionally does <i>not</i> mutate <see cref="BehaviorState"/> 
    /// or <see cref="BrainBlackboard"/> directly. Instead, it acts purely as a change-detector and dispatcher. 
    /// When a phase change is detected, it extracts the <c>BehaviorId</c> and <c>BehaviorParams</c> JSON and
    /// publishes an <see cref="AssignTacticalIntentEvent"/>. This delegates resolution to
    /// <see cref="TacticalIntentResolutionSystem"/>, which translates the intent into a concrete
    /// <see cref="AssignBehaviorEvent"/> consumed by <see cref="BehaviorIngressSystem"/>.
    /// This eliminates legacy "double-apply" bugs that previously wiped out behavior memory (e.g., <c>RoundsFired</c>).
    /// </para>
    /// <para>
    /// <b>Re-commits & State Caching:</b> The system actively caches the exhaustion of a mission plan 
    /// (<c>queue.CurrentPhase >= queue.PhaseCount</c>). This ensures that if the exact same mission 
    /// is re-committed and restarted from Phase 0, the adapter correctly detects the phase reset, 
    /// re-publishes the event, and forces the BTree parameters to cleanly re-initialize.
    /// </para>
    /// </remarks>
    [UpdateInPhase(SystemPhase.Simulation)]
    public class MissionAdapterSystem : IEcsModuleSystem
    {
        public MissionAdapterSystem() { }

        public unsafe void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(MissionAdapterSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var query = repo.Query()
                .With<MissionPlanQueue>()
                .With<BehaviorState>()
                .Build();

            foreach (var entity in query)
            {
                ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
                ref var behavior = ref repo.GetComponentRW<BehaviorState>(entity);

                if (!repo.HasComponent<Hrot.CGF.Components.MissionAdapterState>(entity))
                    repo.AddComponent(entity, new Hrot.CGF.Components.MissionAdapterState { LastPhase = byte.MaxValue });

                ref var adapterState = ref repo.GetComponentRW<Hrot.CGF.Components.MissionAdapterState>(entity);
                var activePlan = repo.GetComponent<ActiveMissionPlan>(entity);

                // Detect exhaustion: publish ClearBehaviorEvent once, then cache so that
                // re-committing the same mission from phase 0 is correctly detected.
                if (queue.CurrentPhase >= queue.PhaseCount)
                {
                    if (adapterState.LastPhase != queue.CurrentPhase)
                    {
                        repo.Bus.Publish(new ClearBehaviorEvent { Entity = entity });
                        adapterState.LastPhase = queue.CurrentPhase;
                        adapterState.LastPlanVersion = 0;
                    }
                    continue;
                }

                Span<MissionPhase> phases = queue.Phases;
                var phase = phases[queue.CurrentPhase];

                string jsonParams = "{}";
                if (activePlan?.Plan?.Tasks != null && queue.CurrentPhase < activePlan.Plan.Tasks.Count)
                {
                    var task = activePlan.Plan.Tasks[queue.CurrentPhase];
                    jsonParams = task.BehaviorParams ?? "{}";

                    uint currentDefHash = (uint)(jsonParams.GetHashCode() ^ phase.BehaviorId);

                    // Skip if nothing changed
                    if (adapterState.LastPhase == queue.CurrentPhase && adapterState.LastPlanVersion == currentDefHash)
                        continue;

                    adapterState.LastPhase = queue.CurrentPhase;
                    adapterState.LastPlanVersion = currentDefHash;

                    // Publish generic tactical intent; TacticalIntentResolutionSystem resolves it.
                    if (!string.IsNullOrWhiteSpace(task.BehaviorName))
                    {
                        repo.Bus.PublishManaged(new AssignTacticalIntentEvent
                        {
                            Entity     = entity,
                            IntentId   = task.BehaviorName,
                            JsonParams = jsonParams,
                        });
                    }
                }
                else
                {
                    uint currentDefHash = (uint)phase.BehaviorId;

                    // Skip if nothing changed
                    if (adapterState.LastPhase == queue.CurrentPhase && adapterState.LastPlanVersion == currentDefHash)
                        continue;

                    adapterState.LastPhase = queue.CurrentPhase;
                    adapterState.LastPlanVersion = currentDefHash;
                }
            }
        }
    }
}
