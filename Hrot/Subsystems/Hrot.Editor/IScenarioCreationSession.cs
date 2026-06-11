namespace Hrot.Editor;

/// <summary>
/// Narrow testable seam over <see cref="IEditorLogic"/> for scenario creation.
/// Exposes only the three methods needed by <see cref="ScenarioNewAssetService"/>:
/// create a new empty world, save the current world under a new name, and load
/// an existing scenario by name.
/// </summary>
/// <remarks>
/// Design §19: Scenarios become organizable into subfolders. The scenario name
/// is treated as a relative path under ScenariosRoot.
/// </remarks>
public interface IScenarioCreationSession
{
    /// <summary>Clears the world and resets time to zero.</summary>
    void NewScenario();

    /// <summary>
    /// Saves the current world state to a new scenario with the given name.
    /// The name may be a relative path (e.g. <c>"Combat/Patrol"</c>).
    /// </summary>
    void SaveScenarioAs(string scenarioName);

    /// <summary>
    /// Loads a scenario by name from the scenarios root directory.
    /// The name may be a relative path (e.g. <c>"Combat/Patrol"</c>).
    /// </summary>
    void LoadScenarioByName(string scenarioName);
}
