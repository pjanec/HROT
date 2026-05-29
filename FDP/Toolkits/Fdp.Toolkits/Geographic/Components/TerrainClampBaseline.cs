using Fdp.Core;
using Fdp.Toolkit.Geographic;

namespace Fdp.Modules.Geographic.Components
{
    /// <summary>
    /// Per-entity terrain-clamp baseline written by <c>TerrainQueryResolutionSystem</c>.
    ///
    /// <para>Holds only the jump-rejection baseline state. Since the 3D Cognitive Spatial
    /// Awareness promotion (P3D-101/102), terrain altitude is authoritative on
    /// <c>SimTransform.Position.Z</c> — it is no longer carried here as a visual rendering
    /// offset. The former <c>TargetZOffset</c>/<c>CurrentZOffset</c> visual-correction fields
    /// have been removed.</para>
    /// </summary>
    [ComponentId(GeographicComponentIds.TerrainClampBaseline)]
    public struct TerrainClampBaseline
    {
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

        // Explicit padding to keep the struct a blittable 8-byte unmanaged value type
        // (4-byte float + 1-byte flag + 3 bytes padding) with 4-byte alignment.
        private byte _pad0;
        private ushort _pad1;
    }
}
