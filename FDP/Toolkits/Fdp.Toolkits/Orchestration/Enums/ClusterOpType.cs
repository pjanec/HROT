namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Pure FDP domain mirror of <c>Hrot.NED.Descriptors.Orchestration.ClusterOpType</c>.
    /// Integer values must remain identical to the NED counterpart (verified by unit tests).
    /// Do NOT add a reference to Hrot.NED in FDP.Toolkit.Orchestration to use this enum.
    /// </summary>
    public enum ClusterOpType : int
    {
        TransitionState = 1,
        // 2 — RESERVED gap: the legacy SaveScenario op (CE-278) was retired; wire value 2 is not reused.
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
        DumpDiagnostics = 16,

        // ⚠⚠ 17 IS NOT FREE, and it is deliberately absent HERE: the authoritative NED enum
        //    (Hrot.Network.Orchestration/Orchestration/OrchestrationMessages.cs) has
        //    `SaveScenario = 17` — the live distributed JSON scenario save (CE-277(c0), renamed by
        //    CE-278 with the wire value unchanged), routed at ClusterMaster and translated both ways.
        //    ⛔ This mirror never gained it, so the two enums are ALREADY DIVERGED despite the header
        //    above claiming they are "verified by unit tests" — measured 2026-09-17: no such test
        //    exists. Do NOT reuse 17 here to close the hole; adding SaveScenario to this mirror changes
        //    the generated CycloneDDS IDL and is a wire-surface decision, not a tidy-up.

        // ⛔ PERMANENT WIRE VALUE (R-42). Ruled 2026-09-17 after 17 was found occupied.
        // 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §6.
        BuildTerrainAsset = 18,
    }
}
