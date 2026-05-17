using System;
using Fdp.Core;
using MessagePack;

namespace Fdp.Toolkit.Replication.Messages
{
    [EventId(9020)]
    [MessagePackObject]
    [DataPolicy(DataPolicy.NoRecord)]
    public class IdBlockRequest
    {
        [Key(0)]
        public string ClientId = string.Empty;
        
        [Key(1)]
        public int RequestSize;
    }

    [EventId(9021)]
    [MessagePackObject]
    [DataPolicy(DataPolicy.NoRecord)]
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
