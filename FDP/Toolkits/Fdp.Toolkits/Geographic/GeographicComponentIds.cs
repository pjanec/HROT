namespace Fdp.Toolkit.Geographic
{
    /// <summary>
    /// Toolkit-local component ID registry for <c>Fdp.Toolkit.Geographic</c>.
    /// Follows the per-toolkit registry pattern established in Phase 5.
    /// IDs 77–79 are reserved for ground clamping and terrain query components.
    /// </summary>
    public static class GeographicComponentIds
    {
        /// <summary>Component ID for <c>GroundClampingConfig</c>.</summary>
        public const byte GroundClampingConfig  = 77;

        /// <summary>Component ID for <c>TerrainClampBaseline</c> (formerly <c>GroundClampingState</c>; same id 78 for wire/save stability).</summary>
        public const byte TerrainClampBaseline   = 78;

        /// <summary>Component ID for <c>TerrainQueryBatchData</c>.</summary>
        public const byte TerrainQueryBatchData = 79;
    }
}
