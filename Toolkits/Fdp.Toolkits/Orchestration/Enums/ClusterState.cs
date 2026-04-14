namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Pure FDP domain mirror of <c>Hrot.NED.Descriptors.Orchestration.ClusterState</c>.
    /// Integer values must remain identical to the NED counterpart (verified by unit tests).
    /// Do NOT add a reference to Hrot.NED in FDP.Toolkit.Orchestration to use this enum.
    /// </summary>
    public enum ClusterState : int
    {
        Idle = 0,
        LoadingEdit = 10,
        OperatingEdit = 11,
        UnloadingEdit = 12,
        LoadingPreview = 20,
        OperatingPreview = 21,
        UnloadingPreview = 22,
        LoadingLive = 30,
        OperatingLive = 31,
        UnloadingLive = 32,
        LoadingReplay = 40,
        OperatingReplay = 41,
        UnloadingReplay = 42,
        Degraded = 99,
    }
}
