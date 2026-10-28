using System;
using CycloneDDS.Schema;

namespace Fdp.Network.Cyclone.Topics
{
    public enum EIdRequestType : int
    {
        Req_Alloc = 0,
        Req_Reset = 1,
        Req_GetStatus = 2
    }

    public enum EIdResponseType : int
    {
        Resp_Alloc = 0,
        Resp_Reset = 1,
        Resp_Status = 2
    }

    [DdsTopic]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepAll
    )]
    public partial struct IdRequest
    {
        [DdsManaged]
        [DdsKey] public string ClientId;
        public long ReqNo;
        public EIdRequestType Type;
        public ulong Start;
        public ulong Count;
    }

    [DdsTopic]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepAll
    )]
    public partial struct IdResponse
    {
        [DdsManaged]
        [DdsKey] public string ClientId;
        public long ReqNo;
        public EIdResponseType Type;
        public ulong Start;
        public ulong Count;
    }

    [DdsTopic]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1
    )]
    public partial struct IdStatus
    {
        public ulong HighestIdAllocated;
    }
}
