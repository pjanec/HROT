using System.Collections.Generic;
using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Components
{
    /// <summary>
    /// Tracks publication state for smart egress optimization.
    /// Transient component - not persisted in snapshots.
    /// </summary>
    [DataPolicy(DataPolicy.Transient)]
    [ComponentId(GlobalComponentIds.EgressPublicationState)]
    public class EgressPublicationState
    {
        /// <summary>
        /// Map of PackedKey (DescriptorOrdinal + InstanceId) → Last Published Tick.
        /// Used for dirty tracking and refresh logic.
        /// </summary>
        public Dictionary<long, uint> LastPublishedTickMap { get; } = new();
        
        /// <summary>
        /// Dirty flags per descriptor. Set when component changes.
        /// Cleared after publication.
        /// </summary>
        public HashSet<long> DirtyDescriptors { get; } = new();
    }
}
