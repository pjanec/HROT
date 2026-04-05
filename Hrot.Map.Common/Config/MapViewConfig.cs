namespace Hrot.Map.Common.Config
{
    /// <summary>
    /// Plain data object that holds the current map layer visibility configuration.
    /// Used by <c>EditorMapConfigAdapter</c> as an in-process alternative to the
    /// IG-only <c>MapUserConfig</c>, decoupling the Editor from the <c>Hrot.IG</c>
    /// project reference.
    /// </summary>
    public sealed class MapViewConfig
    {
        /// <summary>Whether the satellite/imagery base layer is visible.</summary>
        public bool ShowSatelliteLayer { get; set; } = true;

        /// <summary>Whether the ground unit symbology layer is visible.</summary>
        public bool ShowGroundUnits { get; set; } = true;

        /// <summary>Whether the air unit symbology layer is visible.</summary>
        public bool ShowAirUnits { get; set; } = true;

        /// <summary>Whether the coordinate grid overlay is visible.</summary>
        public bool ShowGrid { get; set; } = false;
    }
}
