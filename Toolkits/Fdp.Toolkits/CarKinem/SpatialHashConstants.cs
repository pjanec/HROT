namespace CarKinem.Spatial
{
    /// <summary>
    /// Compile-time parameters for <see cref="Systems.SpatialHashSystem"/> and <see cref="SpatialHashGrid"/>.
    /// See CODE-STANDARDS.md §1 (No magic numbers in production code).
    /// </summary>
    public static class SpatialHashConstants
    {
        /// <summary>Grid cell count along X axis. Width × CellSizeMeters = X coverage.</summary>
        public const int GridWidth  = 150;
        /// <summary>Grid cell count along Y axis. Height × CellSizeMeters = Y coverage.</summary>
        public const int GridHeight = 150;
        /// <summary>Cell edge length in meters.</summary>
        public const float CellSizeMeters = 5.0f;
        /// <summary>
        /// World-space X origin (bottom-left corner).
        /// Grid covers [OriginX, OriginX + GridWidth × CellSizeMeters] in X.
        /// Value: −GridWidth/2 × CellSizeMeters = −375 m, centring the grid on world origin.
        /// </summary>
        public const float OriginX = -375f;
        /// <summary>See <see cref="OriginX"/>. Grid covers [OriginY, OriginY + GridHeight × CellSizeMeters] in Y.</summary>
        public const float OriginY = -375f;
        /// <summary>Maximum entity capacity of the spatial hash (linked-list slot count).</summary>
        public const int MaxEntities = 100_000;
    }
}
