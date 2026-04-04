using System.Collections.Generic;
using FDP.Toolkit.DER;

namespace Hrot.Editor;

/// <summary>
/// Application-level facade exposed to all HROT Editor UI panels.
/// Panels must only call methods on this interface — no direct access to
/// <c>FdpEventBus</c>, <c>EntityRepository</c>, <c>ScenarioEditorModule</c>,
/// or any DDS type is permitted in panel code.
/// </summary>
public interface IEditorLogic
{
    /// <summary>Clears the world and resets time to zero.</summary>
    void NewScenario();

    /// <summary>Serializes current world state to <paramref name="filePath"/>.</summary>
    void SaveScenario(string filePath);

    /// <summary>
    /// Clears the world, then deserializes entities from <paramref name="filePath"/>.
    /// </summary>
    void LoadScenario(string filePath);

    /// <summary>Activates the specified interactive tool.</summary>
    void ActivateTool(EditorTool tool);

    /// <summary>
    /// Publishes an <c>UpdateEntityCommand</c> for <paramref name="networkId"/>
    /// with the supplied component replacements.
    /// </summary>
    void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents);

    /// <summary>Read-only non-ECS view of the current entity set (for panels).</summary>
    IDerRepo View { get; }
}
