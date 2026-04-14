using System;
using System.Collections.Generic;
using CycloneDDS.Schema;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;

namespace Hrot.NED.Messages
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
    [DdsIdlFile("hrot-missions-msgs")]
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
    [DdsIdlFile("hrot-missions-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    public partial struct MissionControlRequest
    {
        // Unique ID for this specific request
        public Guid RequestId;

        // The entity to control
        public long TargetEntityId;

        // Optimistic locking: the version of the mission the sender believes is current.
        // The server rejects the request with ERR_VERSION_CONFLICT if its version is greater.
        // 0 = no version check required.
        public long BaseVersion;

        // The polymorphic payload
        public MissionCommandUnion Payload;
    }

    // Acknowledgment sent by the CGF/SimHost for a MissionControlRequest.
    [DdsTopic("MissionControlAck")]
    [DdsIdlFile("hrot-missions-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile, HistoryKind = DdsHistoryKind.KeepAll)]
    [DdsManaged]
    public partial struct MissionControlAck
    {
        // Echoes the RequestId from MissionControlRequest for correlation.
        public Guid RequestId;

        // 0 = success; non-zero = error (see error code table in GenericMessages.cs).
        public int ErrorCode;

        // Human-readable error description; null/empty on success.
        public string? ErrorMessage;

        // The new version of the mission descriptor after a successful commit.
        // 0 if the request failed.
        public long NewVersion;
    }}