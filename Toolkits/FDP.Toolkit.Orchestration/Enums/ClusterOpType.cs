namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Pure FDP domain mirror of <c>Hrot.NED.Descriptors.Orchestration.ClusterOpType</c>.
    /// Integer values must remain identical to the NED counterpart (verified by unit tests).
    /// Do NOT add a reference to Hrot.NED in FDP.Toolkit.Orchestration to use this enum.
    /// </summary>
    public enum ClusterOpType : int
    {
        TransitionState = 1,
        SaveScenario = 2,
        LoadZone = 3,
        TakeCheckpoint = 4,
        CollectCheckpoint = 5,
        ExportArchive = 6,
        ImportArchive = 7,
        ManageEpisode = 8,
        ReplaySeek = 9,
        PauseTime = 10,
        ResumeTime = 11,
        PrefetchScenario = 12,
        CancelOperation = 13,
        StepTime = 14,
        SetTimeScale = 15,
    }
}
