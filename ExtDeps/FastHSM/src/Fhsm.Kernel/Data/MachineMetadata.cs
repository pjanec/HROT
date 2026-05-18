using System.Collections.Generic;

namespace Fhsm.Kernel.Data
{
    /// <summary>
    /// Metadata for symbolication and debugging.
    /// Maps IDs to human-readable names.
    /// </summary>
    public class MachineMetadata
    {
        public Dictionary<ushort, string> StateNames { get; set; } = new();
        public Dictionary<ushort, string> EventNames { get; set; } = new();
        public Dictionary<ushort, string> ActionNames { get; set; } = new();
        
        public string GetStateName(ushort id) => StateNames.TryGetValue(id, out var name) ? name : $"State_{id}";
        public string GetEventName(ushort id) => EventNames.TryGetValue(id, out var name) ? name : $"Event_{id}";
        public string GetActionName(ushort id) => ActionNames.TryGetValue(id, out var name) ? name : $"Action_{id}";
    }
}
