using System;
using CycloneDDS.Schema;

namespace ModuleHost.Network.Cyclone.Topics
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

    [DdsTopic("IdAlloc_Request")]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepAll
    )]
    public partial struct IdRequest
    {
        [DdsManaged]
        [DdsKey, DdsId(0)] public string ClientId;
        [DdsId(1)] public long ReqNo;
        [DdsId(2)] public EIdRequestType Type;
        [DdsId(3)] public ulong Start;
        [DdsId(4)] public ulong Count;
    }

    [DdsTopic("IdAlloc_Response")]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepAll
    )]
    public partial struct IdResponse
    {
        [DdsManaged]
        [DdsKey, DdsId(0)] public string ClientId;
        [DdsId(1)] public long ReqNo;
        [DdsId(2)] public EIdResponseType Type;
        [DdsId(3)] public ulong Start;
        [DdsId(4)] public ulong Count;
    }

    [DdsTopic("IdAlloc_Status")]
    [DdsQos(
        Reliability = DdsReliability.Reliable,
        Durability = DdsDurability.TransientLocal,
        HistoryKind = DdsHistoryKind.KeepLast,
        HistoryDepth = 1
    )]
    public partial struct IdStatus
    {
        [DdsId(0)] public ulong HighestIdAllocated;
    }
}
