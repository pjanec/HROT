using System;
using System.Collections.Generic;
using Fdp.Core;

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
        public string Params { get; set; } = string.Empty;
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
        public string BehaviorParams  { get; set; } = string.Empty;
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
    /// Managed ECS component holding the current active mission plan for an entity.
    /// Populated by <c>MissionControlExecutionSystem</c> on receipt of a mission intent.
    /// Replaces the DDS-aware <c>EntityMissionHolder</c> and <c>IgMissionHolder</c> components.
    /// </summary>
    [ComponentId(BehaviorApplicationComponentIds.ActiveMissionPlan)]
    public class ActiveMissionPlan
    {
        public DomainMissionPlan Plan { get; set; } = new();

        /// <summary>⭐ <c>R-3039</c> S1 — the plan's task ids in phase order, what <c>CMD_JUMP_TO_TASK</c> resolves against. Was a field of
        /// <c>MissionControlExecutionSystem</c>: a checkpoint restored into a fresh system jumped every request to phase 0.
        /// 📄 docs/DESIGN_System_State_Inventory.md §6 #7.</summary>
        public List<Guid> TaskOrder { get; set; } = new();
    }
}
