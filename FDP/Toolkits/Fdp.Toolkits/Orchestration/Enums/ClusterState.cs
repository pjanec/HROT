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
        // ⭐ CE-3329 P3 (R5) — During Action Review: a replay of the LIVE run in place, whose only exit is back to live.
        //   ⛔ PERMANENT WIRE VALUES (R-42), allocated 2026-10-26 in BOTH enums. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §3a.
        LoadingReview = 50,
        OperatingReview = 51,
        UnloadingReview = 52,
        Degraded = 99,
    }
}
