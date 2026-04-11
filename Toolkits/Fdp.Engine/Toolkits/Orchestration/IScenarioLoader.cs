namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Abstracts locating and loading a scenario payload for a given scenario id.
    /// </summary>
    public interface IScenarioLoader
    {
        /// <summary>
        /// Attempts to locate and return raw scenario JSON for <paramref name="scenarioId"/>.
        /// Returns <c>null</c> when no suitable scenario payload is available.
        /// </summary>
        string? TryLoadScenarioJson(string scenarioId);
    }
}
