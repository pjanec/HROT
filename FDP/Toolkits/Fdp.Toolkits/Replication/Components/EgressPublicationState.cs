using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// Tracks publication state for smart egress optimization.
    /// Transient component - not persisted in snapshots.
    /// </summary>
    // ⭐ G-7 / CE-4354 NodeLocal (DESIGN_Review_Perspective §6.4 I9): this node's egress publication bookkeeping — never copied into a merged view.
    [DataPolicy(DataPolicy.Transient | DataPolicy.NodeLocal)]
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
