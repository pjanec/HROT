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
    public class BinaryGhostStore
    {
        public Dictionary<long, byte[]> StashedData = new Dictionary<long, byte[]>();
        public uint FirstSeenFrame;
        public uint IdentifiedAtFrame;
    }
}
