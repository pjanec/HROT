using System;

namespace Fdp.Core
{
    /// <summary>
    /// ECS tag component marking an entity as belonging to a specific episode.
    /// Episode recorders use <c>Query().With&lt;EpisodeTag&gt;().Build()</c> as their
    /// entity-filter predicate so that only episode entities enter the episode's
    /// <c>AsyncRecorder</c>.  Episode load handlers stamp this tag on every entity
    /// created during a episode load (<c>asEpisode: true</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Canonical type:</b> This is the single authoritative <c>EpisodeTag</c> for the
    /// entire FDP platform.  <c>FDP.Toolkit.Replay</c> and <c>FDP.Toolkit.Scenario</c>
    /// both use this definition to guarantee that episode-membership queries are consistent.
    /// Component ID 84 is reserved for this type across all toolkits.
    /// </para>
    /// <para>
    /// <b>Data policy:</b> Marked <see cref="DataPolicy.NoSave"/> — this is a runtime
    /// marker stamped at load time and must never appear in persisted scenario files.
    /// </para>
    /// </remarks>
    [ComponentId(84)]
    [DataPolicy(DataPolicy.NoSave)]
    public struct EpisodeTag
    {
        /// <summary>Identifier of the episode this entity belongs to.</summary>
        public Guid EpisodeId;
    }
}
