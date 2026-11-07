using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.Serialization.Converters;

namespace Fdp.Toolkit.Behavior.Components
{
    /// <summary>
    /// Pure-domain representation of a single mission trigger.
    /// Carries the trigger type name and optional parameter string through the
    /// scenario serialization round-trip without depending on Hrot.Core.
    /// </summary>
    public class DomainMissionTrigger
    {
        public string Type   { get; set; } = string.Empty;
        [JsonText] public string Params { get; set; } = string.Empty;   // ⭐ CE-4295 — written as the object it holds
    }

    /// <summary>
    /// Pure-domain representation of a single mission task.
    /// No dependency on Hrot.NED or CycloneDDS.
    /// </summary>
    public class DomainMissionTask
    {
        public Guid   TaskId          { get; set; }
        public string ExecutingEngine { get; set; } = string.Empty;
        public string BehaviorName      { get; set; } = string.Empty;
        [JsonText] public string BehaviorParams  { get; set; } = string.Empty;   // ⭐ CE-4295 — written as the object it holds
        public List<DomainMissionTrigger> Triggers { get; set; } = new();
    }

    /// <summary>
    /// Pure-domain representation of a mission plan (ordered task list + active task pointer).
    /// </summary>
    public class DomainMissionPlan
    {
        public Guid                    ActiveTaskId { get; set; }
        public List<DomainMissionTask> Tasks        { get; set; } = new();
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4298</c> (mission plan D2, R-4160) — WHO starts a task: ONE answer, read by the director and the adapter alike, so the two
    /// can never both start one (the double start: the director by stored id, the adapter by name — an order's stored id is 0, so an
    /// <c>Order.*</c> task began with an Idle frame). A task of a NAMED plan (an <see cref="ActiveMissionPlan"/> holds it) is started by the
    /// adapter, by name, through the tactical intent; a RAW queue (no plan beside it — an example, a hand-built queue) has only its stored
    /// id, so the director starts it. 📄 <c>docs/UX/DESIGN_Mission_Plan_Editor.md</c> §5.2 D2.
    /// </summary>
    public static class MissionTaskStart
    {
        /// <summary>Is task <paramref name="index"/> of <paramref name="entity"/>'s plan started by NAME (the adapter), not by its stored id?</summary>
        public static bool ByName(Fdp.Core.EntityRepository repo, Fdp.Core.Entity entity, int index)
            => repo.HasManagedComponent<ActiveMissionPlan>(entity)
               && ((Fdp.ModuleHost.Abstractions.ISimulationView)repo).GetManagedComponentRO<ActiveMissionPlan>(entity) is { Plan.Tasks: { } tasks } && index < tasks.Count;
    }

    /// <summary>
    /// Managed ECS component holding the current active mission plan for an entity.
    /// Populated by <c>MissionControlExecutionSystem</c> on receipt of a mission intent.
    /// Replaces the DDS-aware <c>EntityMissionHolder</c> and <c>IgMissionHolder</c> components.
    /// </summary>
    [ComponentId(BehaviorApplicationComponentIds.ActiveMissionPlan)]
    [DataPolicy(DataPolicy.NoPreview)]   // ⭐ CE-3299 D7 — declared: world state, a mutable class: recorded and saved, kept out of the background snapshot (the convention it got by default)
    public sealed record ActiveMissionPlan   // ⭐ CE-4046 — a record: immutable, changed only by a new value + Set
    {
        public DomainMissionPlan Plan { get; init; } = new();

        /// <summary>⭐ <c>R-3039</c> S1 — the plan's task ids in phase order, what <c>CMD_JUMP_TO_TASK</c> resolves against. Was a field of
        /// <c>MissionControlExecutionSystem</c>: a checkpoint restored into a fresh system jumped every request to phase 0.
        /// 📄 docs/DESIGN_System_State_Inventory.md §6 #7.</summary>
        public IReadOnlyList<Guid> TaskOrder { get; init; } = System.Array.Empty<Guid>();

        /// <summary>⭐ A5 (📄 docs/UX/DESIGN_Entity_Copy_Paste.md §2.4) — the same plan as ANOTHER unit's own: a NEW record (CE-4046, never mutated in place)
        /// whose every task has a new id, the active task and <see cref="TaskOrder"/> mapped to them. Triggers name no task id, so they copy as they are.</summary>
        public ActiveMissionPlan WithNewTaskIds()
        {
            var map = new System.Collections.Generic.Dictionary<Guid, Guid>(Plan.Tasks.Count);
            var plan = new DomainMissionPlan();
            foreach (var t in Plan.Tasks)
            {
                var id = Guid.NewGuid();
                if (t.TaskId != Guid.Empty) map[t.TaskId] = id;
                var nt = new DomainMissionTask { TaskId = id, ExecutingEngine = t.ExecutingEngine, BehaviorName = t.BehaviorName, BehaviorParams = t.BehaviorParams };
                foreach (var trig in t.Triggers) nt.Triggers.Add(new DomainMissionTrigger { Type = trig.Type, Params = trig.Params });
                plan.Tasks.Add(nt);
            }
            plan.ActiveTaskId = map.TryGetValue(Plan.ActiveTaskId, out var active) ? active : Guid.Empty;
            var order = new Guid[TaskOrder.Count];
            for (int i = 0; i < order.Length; i++) order[i] = map.TryGetValue(TaskOrder[i], out var o) ? o : TaskOrder[i];
            return new ActiveMissionPlan { Plan = plan, TaskOrder = order };
        }
    }
}
