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
    // ⭐ G-7 / CE-4354 NodeLocal (DESIGN_Review_Perspective §6.4 I9): a spatial hash grid over THIS node's entity indices — never copied into a merged view.
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay | DataPolicy.NodeLocal)]
    [ComponentId(GlobalComponentIds.SpatialGridData)]
    public struct SpatialGridData
    {
        public SpatialHashGrid Grid;

        /// <summary>⭐ <c>CE-3209</c> (HL-2) — the largest collider radius placed in <see cref="Grid"/> this frame: a round's cell walk pads by
        /// it (an entity's grid point is its body's centre, its body reaches this far). 0 = not measured (a grid built by hand).</summary>
        public float MaxRadius;

        /// <summary>
        /// ⭐ CE-10006 (air combat AC-8, R-10029) — the AIRCRAFT the grid could not place (beyond the terrain it follows): a round still
        /// meets them (<c>RaycastSolverSystem</c> tests these few beside the cells it walks). A ground unit off its terrain stays an
        /// authoring error (W9, CE-2193). Entries [0, <see cref="AirOutsideCount"/>).
        /// </summary>
        public Fdp.Core.Collections.NativeArray<Entity> AirOutside;

        /// <summary>How many of <see cref="AirOutside"/> are set this frame.</summary>
        public int AirOutsideCount;
    }
}
