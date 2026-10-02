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

    /// <summary>
    /// ⭐⭐ <c>E5</c> — activates TERRAIN-ZONE authoring: the operator draws a closed polygon which is
    /// born as <c>TkbEntityTypes.TerrainZone</c> (<c>B1</c>) rather than <c>TacGraphic_Area</c>.
    ///
    /// <para>⭐ Same mechanism as <see cref="StartAreaAuthoringMode"/> — one
    /// <c>AreaAuthoringArm</c>, one <c>PointSequenceGizmo</c>, one command shape — differing only in
    /// the TKB type, because 📄 design §2.1 makes <c>TkbType</c> THE discriminator for what a drawn
    /// shape IS.</para>
    ///
    /// <para>⛔⛔ <b>No default implementation, deliberately.</b> A defaulted no-op here is exactly
    /// <c>R-133</c> — *"a capability reported present that silently no-ops is worse than an absent
    /// one"*: every implementer would compile, the menu item would appear, and nothing would be
    /// drawn on the hosts that forgot. ⭐ Each implementer states how it services a zone —
    /// <c>ScenarioSpawnAdapter</c> arms locally, <c>ExConLogic</c> sends
    /// <c>CMD_START_AUTHORING</c> carrying the zone <c>tkbType</c>.</para>
    /// </summary>
    /// <param name="styleOverrideJson">Optional JSON string overriding visual style for the zone.</param>
    void StartZoneAuthoringMode(string styleOverrideJson = "");
}
