using CarKinem.Spatial;
using Fdp.Core;

namespace CarKinem.Spatial
{
    /// <summary>
    /// Singleton component containing spatial hash grid.
    /// Produced by SpatialHashSystem, consumed by CarKinematicsSystem.
    /// <para>⭐ CE-3132 — <c>NoScenario | NoReplay</c>: the grid is this process's native memory, rebuilt every frame by
    /// SpatialHashSystem; recorded, it carried raw pointers into a replay. Found by <c>NativeMemoryIsNeverRecordedTests</c>.</para>
    /// </summary>
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    [ComponentId(GlobalComponentIds.SpatialGridData)]
    public struct SpatialGridData
    {
        public SpatialHashGrid Grid;

        /// <summary>⭐ <c>CE-3209</c> (HL-2) — the largest collider radius placed in <see cref="Grid"/> this frame: a round's cell walk pads by
        /// it (an entity's grid point is its body's centre, its body reaches this far). 0 = not measured (a grid built by hand).</summary>
        public float MaxRadius;
    }
}
