using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Examples.NetworkDemo.Events
{
    [EventId(5001)]
    public struct RadarContactEvent
    {
        public long EntityId;
        public Vector3 Position;
        public DateTime Timestamp;
    }
}
