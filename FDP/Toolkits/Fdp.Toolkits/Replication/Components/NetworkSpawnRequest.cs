using Fdp.Core;
using System;

namespace Fdp.Toolkit.Replication.Components
{
    /// <summary>
    /// OBSOLETE: Replaced by <see cref="TkbIdentity"/> (permanent identity component)
    /// combined with native <c>EntityHeader.DisType</c> storage.
    /// Will be removed in a future release.
    /// </summary>
    [Obsolete("Use TkbIdentity and EntityHeader.DisType instead. NetworkSpawnRequest is scheduled for removal.")]
    [ComponentId(GlobalComponentIds.NetworkSpawnRequest)]
    public struct NetworkSpawnRequest
    {
        public ulong DisType;
        public ulong OwnerId;
        public long TkbType;
    }
}
