namespace CarKinem.Core
{
    /// <summary>
    /// Kinematics navigation mode enumeration.
    /// Determines how the vehicle physics controller calculates its steering target.
    /// </summary>
    /// <remarks>
    /// Renamed from <c>NavigationMode</c> to <c>KinematicsMode</c> (MOD1-BATCH-02 DB-MOD1-01)
    /// to eliminate the name collision with <c>FDP.Toolkit.Navigation.NavigationMode</c>
    /// (the CQRS contract enum).  All references to the old name inside the CarKinem
    /// toolkit and in the Navigation executor files have been updated.
    /// </remarks>
    public enum KinematicsMode : byte
    {
        None = 0,           // No active navigation (stationary or manual control)
        RoadGraph = 1,      // Follow road network (approach → follow → leave)
        CustomTrajectory = 2, // Follow custom trajectory from trajectory pool
        Formation = 3,      // Follow formation target (overrides other modes)
        Direct = 4          // Drive directly to FinalDestination (used by MoveToExecutor / FleeExecutor)
    }

    /// <summary>
    /// Road graph state machine.
    /// Tracks progress through approach → follow → leave phases.
    /// </summary>
    public enum RoadGraphPhase : byte
    {
        Approaching = 0,    // Moving to closest entry point on road graph
        Following = 1,      // Following road segments
        Leaving = 2,        // Moving from road exit point to final destination
        Arrived = 3         // Reached final destination
    }
}
