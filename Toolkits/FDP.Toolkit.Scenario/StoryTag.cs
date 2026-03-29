using Fdp.Kernel;

namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// Tag component stamped on every entity created during a story load
    /// (<c>asStory: true</c>).  Allows downstream systems (e.g. story recorders,
    /// story-scope queries) to identify and clean up story entities independently
    /// of the base scenario.
    /// </summary>
    /// <remarks>
    /// This is a managed class component stored via the managed component path.
    /// It is marked <c>NoSave</c> so it is never written to the scenario file —
    /// it exists only as a runtime marker stamped during <c>Deserialize(asStory: true)</c>.
    /// </remarks>
    [ComponentId(ScenarioComponentIds.StoryTag)]
    [DataPolicy(DataPolicy.NoSave)]
    public class StoryTag
    {
        /// <summary>Identifier of the story this entity belongs to.</summary>
        public string? StoryId;
    }
}
