namespace FDP.Toolkit.Vis2D.Components
{
    /// <summary>
    /// Component attached to entities to control map layer visibility.
    /// Uses bitmask for layer membership (entity can appear on multiple layers).
    /// </summary>
    public struct MapDisplayComponent
    {
        /// <summary>
        /// Bitmask representing the layers this entity belongs to.
        /// Example: 0b0001 (Ground) | 0b0100 (Radar) = 0b0101
        /// </summary>
        public uint LayerMask;

        public static MapDisplayComponent Default => new() { LayerMask = 1 };
    }
}
