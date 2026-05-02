namespace Hrot.UI.Common.Facades;

/// <summary>
/// Port interface for ORBAT interaction commands.
/// Panels use this interface to select, create, expand, embark, and disembark entities
/// without coupling to the underlying ECS command dispatch.
/// </summary>
public interface IOrbatController
{
    /// <summary>Selects the entity with the given ID in the scenario, centering the map view on it.</summary>
    /// <param name="entityId">The network entity ID to select.</param>
    void SelectEntity(int entityId);

    /// <summary>Creates a new unplaced unit of the specified TKB type.</summary>
    /// <param name="tkbType">The TKB entity type constant.</param>
    void CreateUnit(long tkbType);

    /// <summary>Toggles the expanded/collapsed state of the entity node in the ORBAT tree.</summary>
    /// <param name="entityId">The network entity ID whose expansion state is toggled.</param>
    void ToggleExpanded(int entityId);

    /// <summary>
    /// Requests that the passenger entity embark on the vehicle entity.
    /// The execution system validates capacity constraints and fires the embark command.
    /// </summary>
    /// <param name="passengerEntityId">The network entity ID of the passenger.</param>
    /// <param name="vehicleEntityId">The network entity ID of the vehicle.</param>
    void RequestEmbark(int passengerEntityId, int vehicleEntityId);

    /// <summary>
    /// Requests that the passenger entity disembark from its current vehicle.
    /// </summary>
    /// <param name="passengerEntityId">The network entity ID of the passenger to disembark.</param>
    void RequestDisembark(int passengerEntityId);

    /// <summary>
    /// Requests that the subordinate entity be assigned to the specified commander entity.
    /// </summary>
    /// <param name="subordinateEntityId">The network entity ID of the entity to assign as subordinate.</param>
    /// <param name="commanderEntityId">The network entity ID of the commanding entity.</param>
    void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId);

    /// <summary>
    /// Requests that the entity be removed from its current command hierarchy.
    /// </summary>
    /// <param name="subordinateEntityId">The network entity ID of the subordinate to remove.</param>
    void RequestRemoveSubordinate(int subordinateEntityId);
}
