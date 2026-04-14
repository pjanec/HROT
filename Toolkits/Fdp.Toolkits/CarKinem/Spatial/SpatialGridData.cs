using CarKinem.Spatial;
using Fdp.Core;

namespace CarKinem.Spatial
{
    /// <summary>
    /// Singleton component containing spatial hash grid.
    /// Produced by SpatialHashSystem, consumed by CarKinematicsSystem.
    /// </summary>
    [ComponentId(GlobalComponentIds.SpatialGridData)]
    public struct SpatialGridData
    {
        public SpatialHashGrid Grid;
    }
}
