using System;
using Fdp.Kernel;
using MessagePack;

namespace FDP.Toolkit.Replication.Messages
{
    [EventId(9020)]
    [MessagePackObject]
    public class IdBlockRequest
    {
        [Key(0)]
        public string ClientId = string.Empty;
        
        [Key(1)]
        public int RequestSize;
    }

    [EventId(9021)]
    [MessagePackObject]
    public class IdBlockResponse
    {
        [Key(0)]
        public string ClientId = string.Empty;
        
        [Key(1)]
        public long StartId;
        
        [Key(2)]
        public int Count;
    }
}
