using System;
using Fdp.Kernel;

namespace FDP.Toolkit.Replay
{
    /// <summary>
    /// ECS tag component marking an entity as belonging to a specific story.
    /// Story recorders use <c>Query().With&lt;StoryTag&gt;().Build()</c> as their
    /// entity-filter predicate so that only story entities enter the story's
    /// <c>AsyncRecorder</c>.
    /// </summary>
    [ComponentId(ReplayComponentIds.StoryTag)]
    public struct StoryTag
    {
        /// <summary>Identifier of the story this entity belongs to.</summary>
        public Guid StoryId;
    }
}
