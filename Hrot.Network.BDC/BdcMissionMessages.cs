using CycloneDDS.Schema;
using System;

namespace Hrot.BDC.Messages
{
    // BDC mission command types
    public enum BdcMissionCommandType : int
    {
        ReplaceMission = 0,
        AbortAll       = 1,
        JumpToTask     = 2,
    }

    // BDC mission control request sent from ExCon/Editor to CGF.
    // Topic name BDC_MissionControlRequest is distinct from NED's MissionControlRequest.
    [DdsTopic("BDC_MissionControlRequest")]
    [DdsIdlFile("bdc-mission-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepAll)]
    [DdsManaged]
    public partial struct BdcMissionControlRequest
    {
        public Guid RequestId;
        public long TargetEntityId;
        public BdcMissionCommandType CommandType;
        // JSON payload carrying command parameters; empty string for parameterless commands
        public string PayloadJson;
    }

    // BDC acknowledgment sent by CGF/SimHost for a BdcMissionControlRequest.
    [DdsTopic("BDC_MissionControlAck")]
    [DdsIdlFile("bdc-mission-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile,
            HistoryKind = DdsHistoryKind.KeepAll)]
    [DdsManaged]
    public partial struct BdcMissionControlAck
    {
        public Guid RequestId;
        public int ErrorCode;
        public string? ErrorMessage;
    }
}
