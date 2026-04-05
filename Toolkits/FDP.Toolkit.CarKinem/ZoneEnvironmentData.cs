using Fdp.Kernel;

namespace CarKinem.Road
{
    /// <summary>
    /// ECS singleton that carries the static environment data for the currently loaded zone.
    ///
    /// <para>
    /// Written by the application-layer <c>ZoneManagerService</c> during scenario load.
    /// Read by <see cref="CarKinem.Systems.CarKinematicsSystem"/> to obtain road-graph data
    /// without constructor injection, following the Data-Oriented ECS singleton pattern.
    /// </para>
    ///
    /// <para>
    /// When no zone is loaded this singleton is absent and
    /// <see cref="CarKinem.Systems.CarKinematicsSystem"/> falls back to an empty
    /// <see cref="RoadNetworkBlob"/> so that non-road vehicle physics continue to run normally.
    /// </para>
    /// </summary>
    [ComponentId(GlobalComponentIds.ZoneEnvironmentData)]
    public struct ZoneEnvironmentData
    {
        /// <summary>Road network blob for the active zone. May be default (empty) when no roads are declared.</summary>
        public RoadNetworkBlob RoadNetwork;
    }
}
