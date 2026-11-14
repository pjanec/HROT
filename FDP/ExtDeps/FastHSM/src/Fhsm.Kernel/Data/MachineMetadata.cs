using System;
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
        public Dictionary<ushort, Guid> StateStableIds { get; set; } = new();
        public Dictionary<ushort, Guid> TransitionVisualIds { get; set; } = new();
        // Per-state deferred event IDs, keyed by state flat index.
        public Dictionary<ushort, ushort[]> DeferredEventsByState { get; set; } = new();
        
        public string GetStateName(ushort id) => StateNames.TryGetValue(id, out var name) ? name : $"State_{id}";
        public string GetEventName(ushort id) => EventNames.TryGetValue(id, out var name) ? name
            // ⭐ CE-1335 — the kernel's reserved ids read as what they are, not "Event_65533"
            : id == ReservedEventIds.Polled ? "(every tick)" : id == ReservedEventIds.Timer ? "(timer)" : $"Event_{id}";
        public string GetActionName(ushort id) => ActionNames.TryGetValue(id, out var name) ? name : $"Action_{id}";
    }
}
