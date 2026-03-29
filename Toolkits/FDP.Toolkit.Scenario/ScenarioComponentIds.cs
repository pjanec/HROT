namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// ECS component ID catalog for <c>FDP.Toolkit.Scenario</c>.
    /// IDs 200–201 are reserved for scenario-toolkit internal tag components.
    /// </summary>
    public static class ScenarioComponentIds
    {
        /// <summary>
        /// Component ID for <see cref="ScenarioIgnoreTag"/> — entity-level exclusion tag.
        /// Entities carrying this component are skipped by the serializer entirely.
        /// </summary>
        public const int ScenarioIgnoreTag = 200;

        /// <summary>
        /// Component ID for <see cref="StoryTag"/> — marks entities spawned during a story load.
        /// </summary>
        public const int StoryTag = 201;
    }
}
