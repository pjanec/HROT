namespace Fdp.Toolkit.Navigation.Fake
{
    /// <summary>
    /// Flight constraints used by <see cref="IVolumetricPathProvider"/> queries.
    /// </summary>
    public struct FlyProfile
    {
        /// <summary>Minimum allowed altitude (Y coordinate, metres).</summary>
        public float MinAltitude;
        /// <summary>Maximum allowed altitude (Y coordinate, metres).</summary>
        public float MaxAltitude;
        /// <summary>Obstacle avoidance radius around the flight path (metres).</summary>
        public float ObstacleAvoidanceRadius;
    }
}
