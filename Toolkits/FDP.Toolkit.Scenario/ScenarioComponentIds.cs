namespace FDP.Toolkit.Scenario
{
    /// <summary>
    /// ECS component ID catalog for <c>FDP.Toolkit.Scenario</c>.
    /// ID 200 is reserved for the scenario-toolkit internal exclusion tag.
    /// ID 201 is freed (was previously <c>FDP.Toolkit.Scenario.StoryTag</c> — removed in
    /// CGF-1-BATCH-12 A.7; use <c>Fdp.Kernel.StoryTag</c> with component ID 84 instead).
    /// </summary>
    public static class ScenarioComponentIds
    {
        /// <summary>
        /// Component ID for <see cref="ScenarioIgnoreTag"/> — entity-level exclusion tag.
        /// Entities carrying this component are skipped by the serializer entirely.
        /// </summary>
        public const int ScenarioIgnoreTag = 200;
    }
}
