namespace Hrot.UI.Common.Facades;

/// <summary>
/// Port interface for entity spawning and authoring-mode activation.
/// Panels dependent on this interface can initiate placement of entities
/// on the map without knowing anything about the underlying ECS or DDS transport.
/// </summary>
public interface ISpawnController
{
    /// <summary>
    /// Activates single-entity placement mode for the given TKB blueprint type.
    /// </summary>
    /// <param name="tkbType">The TKB entity type constant (see <see cref="Hrot.Map.Common.TkbEntityTypes"/>).</param>
    /// <param name="initialPropertiesJson">Optional JSON payload carrying initial property overrides.</param>
    void StartPlacementMode(long tkbType, string? initialPropertiesJson = null);

    /// <summary>
    /// Activates area authoring mode, allowing the operator to draw a filled area on the map.
    /// </summary>
    /// <param name="styleOverrideJson">Optional JSON string overriding visual style for the area.</param>
    void StartAreaAuthoringMode(string styleOverrideJson = "");

    /// <summary>
    /// Activates route authoring mode, allowing the operator to draw a polyline route on the map.
    /// </summary>
    void StartRouteAuthoringMode();
}
