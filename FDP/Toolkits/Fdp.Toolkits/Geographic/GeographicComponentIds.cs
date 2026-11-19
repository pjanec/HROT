using Fdp.Core;
namespace Fdp.Toolkit.Geographic
{
    /// <summary>
    /// Toolkit-local component ID registry for <c>Fdp.Toolkit.Geographic</c>.
    /// Follows the per-toolkit registry pattern established in Phase 5.
    /// IDs 77–79 are reserved for ground clamping and terrain query components.
    /// </summary>
    // ⛔ ALIASES ONLY (CE-4628 follow-up, user 2026-11-19: "all component ids ... written at one single place"): every id lives in
    //   Fdp.Core GlobalComponentIds (values unchanged); a NEW id is added THERE, never here. Rail: ComponentIdCatalogueTests.
    public static class GeographicComponentIds
    {
        /// <summary>Component ID for <c>GroundClampingConfig</c>.</summary>
        public const byte GroundClampingConfig  = GlobalComponentIds.GroundClampingConfig;

        /// <summary>Component ID for <c>TerrainClampBaseline</c> (formerly <c>GroundClampingState</c>; same id 78 for wire/save stability).</summary>
        public const byte TerrainClampBaseline   = GlobalComponentIds.TerrainClampBaseline;

        /// <summary>Component ID for <c>TerrainQueryBatchData</c>.</summary>
        public const byte TerrainQueryBatchData = GlobalComponentIds.TerrainQueryBatchData;
    }
}
