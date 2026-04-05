namespace Hrot.UI.Common.Models;

/// <summary>
/// Represents the current visibility state of the map rendering layers.
/// </summary>
/// <param name="Satellite">Whether the satellite/imagery base layer is visible.</param>
/// <param name="GroundUnits">Whether the ground unit symbology layer is visible.</param>
/// <param name="AirUnits">Whether the air unit symbology layer is visible.</param>
/// <param name="Grid">Whether the coordinate grid overlay is visible.</param>
public record MapLayerState(bool Satellite, bool GroundUnits, bool AirUnits, bool Grid);
