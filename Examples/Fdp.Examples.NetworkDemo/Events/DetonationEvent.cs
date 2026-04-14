using System;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Examples.NetworkDemo.Events
{
    [EventId(11001)] // Added unique ID
    public struct DetonationEvent
    {
        public Vector3 Position;
        public float Radius;
        public float Damage;
    }
}
