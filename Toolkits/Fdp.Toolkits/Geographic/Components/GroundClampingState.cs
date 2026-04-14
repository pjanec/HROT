using Fdp.Core;
using Fdp.Toolkit.Geographic;

namespace Fdp.Modules.Geographic.Components
{
    /// <summary>
    /// Per-entity runtime state written by <c>TerrainQueryResolutionSystem</c>.
    /// Consumed by <c>TransformSyncSystem</c> to apply a smooth Z-axis correction
    /// that reconciles simulation altitude (DIS/HLA) with IG terrain height.
    /// </summary>
    [ComponentId(GeographicComponentIds.GroundClampingState)]
    public struct GroundClampingState
    {
        /// <summary>
        /// Desired visual Z correction computed from the latest terrain query hit.
        /// <c>TargetZOffset = terrainHitZ − simulationZ</c>.
        /// </summary>
        public float TargetZOffset;

        /// <summary>
        /// Smoothed Z correction actually applied this frame (lerped toward
        /// <see cref="TargetZOffset"/> by <c>TransformSyncSystem</c>).
        /// </summary>
        public float CurrentZOffset;

        /// <summary>
        /// IG-space altitude of the last <em>accepted</em> terrain hit.
        /// Used by <c>TerrainQueryResolutionSystem</c> for jump-rejection:
        /// hits that differ from this value by more than the configured
        /// threshold are discarded (geometry seams / tunnels).
        /// </summary>
        public float LastValidIgAltitude;

        /// <summary>
        /// Set to 1 after the first terrain hit is accepted for this entity.
        /// Jump-rejection is suppressed while this is 0 so the pipeline can seed state;
        /// sea-level worlds use <see cref="LastValidIgAltitude"/> = 0 as a <em>valid</em>
        /// altitude, so "baseline unset" must not be inferred from that field alone.
        /// </summary>
        public byte IgAltitudeBaselineEstablished;
    }
}
