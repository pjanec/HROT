using System;

namespace Fdp.Kernel
{
    /// <summary>
    /// ECS tag component marking an entity as belonging to a specific story.
    /// Story recorders use <c>Query().With&lt;StoryTag&gt;().Build()</c> as their
    /// entity-filter predicate so that only story entities enter the story's
    /// <c>AsyncRecorder</c>.  Story load handlers stamp this tag on every entity
    /// created during a story load (<c>asStory: true</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Canonical type:</b> This is the single authoritative <c>StoryTag</c> for the
    /// entire FDP platform.  <c>FDP.Toolkit.Replay</c> and <c>FDP.Toolkit.Scenario</c>
    /// both use this definition to guarantee that story-membership queries are consistent.
    /// Component ID 84 is reserved for this type across all toolkits.
    /// </para>
    /// <para>
    /// <b>Data policy:</b> Marked <see cref="DataPolicy.NoSave"/> — this is a runtime
    /// marker stamped at load time and must never appear in persisted scenario files.
    /// </para>
    /// </remarks>
    [ComponentId(84)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct StoryTag
    {
        /// <summary>Identifier of the story this entity belongs to.</summary>
        public Guid StoryId;
    }
}
