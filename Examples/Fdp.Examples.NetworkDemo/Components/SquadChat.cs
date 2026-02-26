using System;
using FDP.Interfaces.Abstractions;
using CycloneDDS.Schema;
using MessagePack;
using Fdp.Kernel;

namespace Fdp.Examples.NetworkDemo.Components
{
    [Serializable]
    [DdsTopic("SST_SquadChat")] // TRIGGERS SOURCE GENERATOR
    [DdsManaged] // Explicitly allow GC allocations for strings
    [FdpDescriptor(205, "SST_SquadChat")] // Ordinal 205 (Arbitrary free ordinal)
    [MessagePackObject]
    [ComponentId(205)]
    public partial class SquadChat
    {
        [Key(0)]
        public long EntityId { get; set; }
        
        [Key(1)]
        public string SenderName { get; set; } = string.Empty;
        [Key(2)]
        public string Message { get; set; } = string.Empty;
    }
}
