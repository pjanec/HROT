using System.Collections.Generic;

namespace Fhsm.Demo.Visual
{
    public class MachineMetadata
    {
        public Dictionary<ushort, string> StateNames { get; set; } = new();
        public Dictionary<ushort, string> EventNames { get; set; } = new();
        public Dictionary<ushort, string> ActionNames { get; set; } = new();
    }
}
