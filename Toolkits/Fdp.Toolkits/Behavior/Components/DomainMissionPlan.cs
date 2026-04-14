using System;
using System.Collections.Generic;
using Fdp.Kernel;

namespace FDP.Toolkit.Behavior.Components
{
    /// <summary>
    /// Pure-domain representation of a single mission task.
    /// No dependency on Hrot.NED or CycloneDDS.
    /// </summary>
    public class DomainMissionTask
    {
        public Guid   TaskId          { get; set; }
        public string ExecutingEngine { get; set; } = string.Empty;
        public string BehaviorId      { get; set; } = string.Empty;
        public string BehaviorParams  { get; set; } = string.Empty;
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
    }
}
