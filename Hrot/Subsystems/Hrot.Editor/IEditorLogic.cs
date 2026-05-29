using System.Collections.Generic;
using System.Threading.Tasks;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.DER;

namespace Hrot.Editor;

/// <summary>
/// Application-level facade exposed to all HROT Editor UI panels.
/// Panels must only call methods on this interface — no direct access to
/// <c>FdpEventBus</c>, <c>EntityRepository</c>, <c>ScenarioEditorModule</c>,
/// or any DDS type is permitted in panel code.
/// </summary>
public interface IEditorLogic
{
    /// <summary>Updates the editor logic state machine.</summary>
    void Update();

    /// <summary>Clears the world and resets time to zero.</summary>
    void NewScenario();

    /// <summary>Serializes current world state to <paramref name="filePath"/>.</summary>
    void SaveScenario(string filePath);

    /// <summary>
    /// Clears the world, then deserializes entities from <paramref name="filePath"/>.
    /// </summary>
    void LoadScenario(string filePath);

    /// <summary>
    /// Loads a scenario by name from the scenarios root directory.
    /// Remembers the name for subsequent <see cref="SaveCurrentScenario"/> calls.
    /// </summary>
    void LoadScenarioByName(string scenarioName);

    /// <summary>
    /// Saves the current world state to the scenario most recently loaded via
    /// <see cref="LoadScenarioByName"/>. When no scenario is loaded, does nothing.
    /// </summary>
    void SaveCurrentScenario();

    /// <summary>
    /// Saves the current world state to a new scenario with the given name.
    /// Remembers the name for subsequent <see cref="SaveCurrentScenario"/> calls.
    /// </summary>
    void SaveScenarioAs(string scenarioName);

    /// <summary>
    /// The name of the currently loaded scenario, or <c>null</c> / empty when no
    /// scenario has been loaded (or after <see cref="NewScenario"/> was called).
    /// </summary>
    string? LoadedScenarioName { get; }

    /// <summary>
    /// List of scenario names available in the local scenarios root directory.
    /// Updated asynchronously by the offline orchestrator; may be empty initially.
    /// </summary>
    IReadOnlyList<string> AvailableScenarios { get; }

    /// <summary>Activates the specified interactive tool.</summary>
    void ActivateTool(EditorTool tool);

    /// <summary>
    /// Publishes an <c>UpdateEntityCommand</c> for <paramref name="networkId"/>
    /// with the supplied component replacements.
    /// </summary>
    void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents);

    /// <summary>Read-only non-ECS view of the current entity set (for panels).</summary>
    IDerRepo View { get; }

    /// <summary>
    /// Ejects the local FDP SimHost logic packs and (if translator packs are configured)
    /// installs the ACL translator packs. No-op when kernel is not configured.
    /// </summary>
    Task SwitchToExternalAsync();

    /// <summary>
    /// Uninstals translator packs (if any) and reinstalls the local FDP SimHost logic packs.
    /// No-op when kernel is not configured or already in Internal mode.
    /// </summary>
    Task SwitchToInternalAsync();

    /// <summary>Current operational mode of the editor.</summary>
    SimHostMode CurrentMode { get; }

    /// <summary>
    /// Pans and zooms the map canvas to centre on the entity identified by
    /// <paramref name="entityId"/>.
    /// </summary>
    void CenterOnEntity(long entityId);

    /// <summary>
    /// Programmatically selects the entity identified by <paramref name="entityId"/>
    /// as the primary selection, switching to the Select tool if required.
    /// </summary>
    void SelectEntity(long entityId);

    /// <summary>
    /// Opens the in-map rename dialog for the entity identified by
    /// <paramref name="entityId"/>.
    /// </summary>
    void OpenRenameDialog(long entityId);

    /// <summary>
    /// Invokes the MSBuild compiler on the AI Behaviors assembly in the background.
    /// Once the build completes and overwrites the DLL, the FileSystemWatcher in
    /// <c>FbtAssemblyHotReloader</c> automatically detects the change and swaps
    /// the active BTree interpreters without stalling the editor loop.
    /// </summary>
    void RebuildAndReloadAI();

    /// <summary>
    /// True when the currently loaded scenario was opened in degraded mode
    /// (snapshot fallback; the file was too new for the current migration chain).
    /// </summary>
    bool IsScenarioDegraded { get; }

    /// <summary>
    /// Returns the sidecar files (snapshots and journals) stored alongside the
    /// currently loaded scenario file. Returns an empty list when no scenario
    /// has been loaded or when no migration services are configured.
    /// </summary>
    IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario();
}
