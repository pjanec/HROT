using System;
using Fdp.Kernel;

namespace FDP.Toolkit.Replay
{
    /// <summary>
    /// ECS tag component marking a ghost entity injected during story replay.
    /// AI and physics systems should query <c>Without&lt;StoryReplayTag&gt;()</c>
    /// to exclude these ghost entities from live simulation logic.
    /// </summary>
    [ComponentId(ReplayComponentIds.StoryReplayTag)]
    public struct StoryReplayTag
    {
        /// <summary>Identifier of the story being replayed.</summary>
        public Guid StoryId;

        /// <summary>Entity ID from the original recorded session.</summary>
        public int OriginalEntityId;
    }
}
