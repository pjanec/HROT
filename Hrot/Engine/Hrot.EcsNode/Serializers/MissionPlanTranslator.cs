using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common;

namespace Hrot.SimHost.Serializers
{
    /// <summary>
    /// Custom scenario translator for <see cref="ActiveMissionPlan"/> (managed) and
    /// <see cref="MissionPlanQueue"/> (unmanaged <c>[InlineArray]</c>).
    ///
    /// <para>
    /// <see cref="ActiveMissionPlan"/> is a managed class skipped entirely by
    /// <see cref="Fdp.Toolkit.Scenario.FdpAutoSerializer"/>.
    /// <see cref="MissionPlanQueue"/> contains a <c>[InlineArray]</c> field
    /// (<see cref="MissionPhaseBuffer"/>) that the auto-serializer cannot iterate
    /// correctly — it sees only the single compiler-generated backing element.
    /// This translator intercepts both components and serialises them atomically.
    /// </para>
    /// </summary>
    public sealed class MissionPlanTranslator : IEntityScenarioTranslator
    {
        private const string Key = "MissionPlan";

        private readonly BehaviorRegistry _registry;

        // MissionPlan components store safe Network IDs, not volatile ECS Entity handles.
        // They must survive extraction so the genesis pipeline can remap and apply them.
        public bool IsExtractionSafe => true;

        public MissionPlanTranslator(BehaviorRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        // ── IEntityScenarioTranslator ─────────────────────────────────────────

        public BitMask512 GetConsumedComponentsMask()
        {
            var mask = new BitMask512();

            int qId = ComponentTypeRegistry.GetId(typeof(MissionPlanQueue));
            if (qId >= 0) mask.SetBit(qId);

            // ActiveMissionPlan is managed; the auto-serializer skips managed types already.
            // Set the bit explicitly so the serializer mask is consistent.
            int aId = ComponentTypeRegistry.GetId(typeof(ActiveMissionPlan));
            if (aId >= 0)
                mask.SetBit(aId);
            else
                mask.SetBit(BehaviorApplicationComponentIds.ActiveMissionPlan);

            return mask;
        }

        public IEnumerable<string> GetOutputDomKeys() { yield return Key; }

        public bool CanTranslate(EntityRepository repo, Entity entity)
            => repo.HasManagedComponent<ActiveMissionPlan>(entity);

        public Dictionary<string, object> Extract(
            EntityRepository repo, Entity entity, IGuidResolver guidResolver)
        {
            var activePlan = ((ISimulationView)repo)
                .GetManagedComponentRO<ActiveMissionPlan>(entity);
            var queue      = repo.GetComponent<MissionPlanQueue>(entity);

            // Serialize the pure-domain plan string so BehaviorParams and BehaviorId survive
            // the round-trip intact (no GUID patching required here — entity refs in params
            // are already stored as network IDs by ScenarioBehaviorRemapper during genesis).
            var planJson = JsonSerializer.Serialize(
                activePlan.Plan, HrotSerializerOptions.HrotJsonOptions);

            var obj = new JsonObject
            {
                ["PlanData"]            = JsonNode.Parse(planJson),
				
                // The following are intentionally commented out so that
                // the scenario doesn't capture transient execution state
				//["CurrentPhase"]        = (int)queue.CurrentPhase,
				//["PhaseElapsedSeconds"] = queue.PhaseElapsedSeconds,
			};

            return new Dictionary<string, object> { [Key] = obj };
        }

        public void Inject(
            EntityRepository repo, Entity entity,
            Dictionary<string, object> scenarioData, IGuidResolver guidResolver)
        {
            if (!scenarioData.TryGetValue(Key, out var raw) || raw is not JsonObject obj)
                return;

            var planNode = obj["PlanData"];
            if (planNode == null) return;

            var domainPlan = planNode.Deserialize<DomainMissionPlan>(
                HrotSerializerOptions.HrotJsonOptions);
            if (domainPlan == null) return;

            // 1. Restore managed component.
            repo.SetManagedComponent(entity, new ActiveMissionPlan { Plan = domainPlan });

            // 2. Rebuild unmanaged execution queue.
            //    Use Get -> Mutate -> SetComponent to avoid [InlineArray] defensive-copy trap.
            var queue = new MissionPlanQueue
            {
                // Force a clean slate. Never restore execution progress from disk.
                //CurrentPhase        = (byte)(obj["CurrentPhase"]?.GetValue<int>() ?? 0),
                //PhaseElapsedSeconds = obj["PhaseElapsedSeconds"]?.GetValue<float>() ?? 0f,
                CurrentTask        = 0, 
                TaskElapsedSeconds = 0f,

                TaskCount          = (byte)Math.Min(domainPlan.Tasks.Count, MissionPlanQueue.MaxTasks),
            };

            Span<MissionTaskSlot> phases = queue.Tasks;
            for (int i = 0; i < queue.TaskCount; i++)
            {
                var task = domainPlan.Tasks[i];
                _registry.TryGetId(task.BehaviorName, out int behaviorId);
                var hrotTriggers = task.Triggers?.ConvertAll(t => new Hrot.Core.Mission.MissionTrigger
                {
                    Type   = t.Type,
                    Params = t.Params,
                });
                // ⭐ CE-4157 — an unknown trigger type / a second trigger / a bad parameter FAILS the load, naming the entity and the task
                (Fdp.Toolkit.Behavior.Components.MissionTrigger trigger, float triggerParam) resolved;
                try { resolved = Hrot.Core.Mission.MissionTriggerHelper.ResolveTrigger(hrotTriggers, $"{task.BehaviorName} ({task.TaskId})"); }
                catch (Hrot.Core.Mission.MissionPlanException ex)
                { throw new Hrot.Core.Mission.MissionPlanException($"scenario entity #{entity.Index}: {ex.Message}"); }
                var (trigger, triggerParam) = resolved;

                phases[i] = new MissionTaskSlot
                {
                    BehaviorId   = behaviorId,
                    Trigger      = trigger,
                    TriggerParam = triggerParam,
                };
            }

            repo.SetComponent(entity, queue);
        }
    }
}

