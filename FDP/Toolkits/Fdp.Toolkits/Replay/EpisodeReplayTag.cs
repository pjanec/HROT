using System;
using Fdp.Core;

namespace Fdp.Toolkit.Replay
{
    /// <summary>
    /// ECS tag component marking a ghost entity injected during episode replay.
    /// AI and physics systems should query <c>Without&lt;EpisodeReplayTag&gt;()</c>
    /// to exclude these ghost entities from live simulation logic.
    /// </summary>
    [ComponentId(ReplayComponentIds.EpisodeReplayTag)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct EpisodeReplayTag
    {
        /// <summary>Identifier of the episode being replayed.</summary>
        public Guid EpisodeId;

        /// <summary>Entity ID from the original recorded session.</summary>
        public int OriginalEntityId;
    }
}
