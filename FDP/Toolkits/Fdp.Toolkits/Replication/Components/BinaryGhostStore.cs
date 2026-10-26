using System;
using System.Collections.Generic;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// OBSOLETE: The BinaryGhostStore stashing mechanic has been replaced by
    /// directly applying ECS components to ghost entities via <c>cmd.SetComponent</c>.
    /// Ghost creation is now handled by <c>GhostCreationSystem</c> in each ingress
    /// translator. Will be removed in a future release.
    /// </summary>
    [Obsolete("BinaryGhostStore stashing is replaced by direct component application on ghost entities. See GhostCreationSystem.")]
    [ComponentId(GlobalComponentIds.BinaryGhostStore)]
    [DataPolicy(DataPolicy.NoPreview)]   // ⭐ CE-3299 D7 — declared: world state, a mutable class: recorded and saved, kept out of the background snapshot (the convention it got by default)
    public record BinaryGhostStore
    {
        public IReadOnlyDictionary<long, byte[]> StashedData { get; init; } = new Dictionary<long, byte[]>();
        public uint FirstSeenFrame { get; init; }
        public uint IdentifiedAtFrame { get; init; }
    }
}
