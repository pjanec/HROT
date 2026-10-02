using System;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Messages
{
    [EventId(9020)]
    [DataPolicy(DataPolicy.NoReplay)]
    public class IdBlockRequest
    {
        public string ClientId = string.Empty;
        
        public int RequestSize;
    }

    [EventId(9021)]
    [DataPolicy(DataPolicy.NoReplay)]
    public class IdBlockResponse
    {
        public string ClientId = string.Empty;
        
        public long StartId;
        
        public int Count;
    }
}
