using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Bagira.DDS.DM;
using Bagira.BDC.SSTD;

namespace Bagira.BDC.SSTM
{
    // ==============================================================================
    // 5. COMMAND INTERFACE (GUI -> CGF)
    // ==============================================================================

    public enum eMissionCommandType
    {
        CMD_JUMP_TO_TASK,       // Switch active task to a specific ID
        CMD_APPEND_TASK,        // Add a single task to the end
        CMD_INSERT_TASK,        // Insert a task (specifics handled by logic/index)
        CMD_REPLACE_MISSION,    // Wipe everything and set a new full mission
        CMD_ABORT_ALL           // Stop everything
    }

    [DdsUnion]
    [DdsIdlFile("bdc-sst-missions-msgs")]
    public partial struct MissionCommandUnion
    {
        [DdsDiscriminator]
        public eMissionCommandType _d;

        // CASE: Switch execution to a specific existing task
        [DdsCase(eMissionCommandType.CMD_JUMP_TO_TASK)]
        public Guid TargetTaskId;

        // CASE: Add new single tasks
        [DdsCase(eMissionCommandType.CMD_APPEND_TASK)]
        public MissionTask NewTaskData;

        // CASE: Full Mission Upload
        // Reuses the MissionPlan struct to set list + active index atomically
        [DdsCase(eMissionCommandType.CMD_REPLACE_MISSION)]
        public MissionPlan FullMissionData;

        // CASE: Commands with no parameters
        [DdsCase(eMissionCommandType.CMD_ABORT_ALL)]
        public bool UnusedPlaceholder;
    }

    [DdsTopic("MissionControlRequest")]
    [DdsIdlFile("bdc-sst-missions-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct MissionControlRequest
    {
        // Unique ID for this specific request
        public Guid RequestId;

        // The entity to control
        public long TargetEntityId;

        // The polymorphic payload
        public MissionCommandUnion Payload;
    }
}
