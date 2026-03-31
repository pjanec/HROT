using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Hrot.NED.Common;

namespace Hrot.NED.Descriptors
{
    // ==============================================================================
    // 1. ENUMS
    // ==============================================================================

    public enum eTaskState
    {
        TASK_PLANNED,   // Waiting for triggers or sequence
        TASK_ACTIVE,    // Currently executing
        TASK_DONE,      // Completed successfully
        TASK_FAILED,    // Failed execution
        TASK_SKIPPED    // Skipped because a later task was forced active
    }

    // ==============================================================================
    // 2. STRUCTS (DATA BUILDING BLOCKS)
    // ==============================================================================

    [DdsStruct]
    [DdsIdlFile("hrot-missions-desc")]
    [DdsManaged]
    public partial struct MissionTrigger
    {
        public string Type;          // e.g., "LineCrossed", "TimeElapsed"
        public string Params;        // JSON string (Schema validated)
    }

    [DdsStruct]
    [DdsIdlFile("hrot-missions-desc")]
    [DdsManaged]
    public partial struct MissionTask
    {
        public Guid TaskId;            // Unique stringified GUID

        public string ExecutingEngine;      // who is going to execute the behavior "CGFX" etc.

        public string BehaviorId;           // e.g., "MoveToLocation", could be also bkbId od the doctrine (for CGFX)

        public string BehaviorParams;       // JSON string (Schema validated) for the doctrine

        [DdsManaged]
        public List<MissionTrigger> Triggers;

        public eTaskState State;     // Current status of this specific task
    }

    // Reusable structure for the "Content" of a mission.
    // Used in both the EntityMission state and the REPLACE_MISSION command.
    [DdsStruct]
    [DdsIdlFile("hrot-missions-desc")]
    public partial struct MissionPlan
    {
        // ID of the task currently running. 
        // Must match one of the TaskIds in the Tasks sequence.
        public Guid ActiveTaskId;            // Unique stringified GUID

        // Ordered list of all tasks
        [DdsManaged]
        public List<MissionTask> Tasks;
    }

    // ==============================================================================
    // 4. TOPIC: ENTITY MISSION (STATE)
    // ==============================================================================

    [DdsTopic("EntityMission")]
    [DdsIdlFile("hrot-missions-desc")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.TransientLocal, HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
    public partial struct EntityMission
    {
        [DdsKey]
        public long EntityId; //@key

        // The current state of the mission
        public MissionPlan Plan;
    }
}
