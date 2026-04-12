namespace Hrot.Core.Mission;

/// <summary>Force affiliation for spawn and display.</summary>
public enum eForceIdentifier
{
    FORCE_UNKNOWN  = 0,
    FORCE_FRIENDLY = 1,
    FORCE_OPPOSING = 2,
    FORCE_NEUTRAL  = 3,
}

/// <summary>Lifecycle state of a mission task.</summary>
public enum eTaskState
{
    TASK_PLANNED,
    TASK_ACTIVE,
    TASK_DONE,
    TASK_FAILED,
    TASK_SKIPPED,
}

/// <summary>Imperative mission control command discriminator.</summary>
public enum eMissionCommandType
{
    CMD_JUMP_TO_TASK,
    CMD_APPEND_TASK,
    CMD_INSERT_TASK,
    CMD_REPLACE_MISSION,
    CMD_ABORT_ALL,
}

/// <summary>Condition that triggers a task transition.</summary>
public sealed class MissionTrigger
{
    /// <summary>Trigger type name, e.g. "TimerElapsed".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>JSON parameter string (schema-validated by the engine).</summary>
    public string Params { get; set; } = string.Empty;
}

/// <summary>Single step in a mission plan.</summary>
public sealed class MissionTask
{
    public Guid TaskId { get; set; }
    public string ExecutingEngine { get; set; } = string.Empty;
    public string BehaviorId { get; set; } = string.Empty;
    public string BehaviorParams { get; set; } = string.Empty;
    public List<MissionTrigger> Triggers { get; set; } = new();
    public eTaskState State { get; set; }
}

/// <summary>Ordered sequence of mission tasks for a single entity.</summary>
public sealed class MissionPlan
{
    public Guid ActiveTaskId { get; set; }
    public List<MissionTask> Tasks { get; set; } = new();
}
