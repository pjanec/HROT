namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// Pure FDP domain mirror of <c>Hrot.NED.Descriptors.Orchestration.NodeOpType</c>.
    /// Integer values must remain identical to the NED counterpart (verified by unit tests).
    /// Do NOT add a reference to Hrot.NED in FDP.Toolkit.Orchestration to use this enum.
    /// </summary>
    public enum NodeOpType : int
    {
        /// <summary>⭐ AQ107 / CycloneDDS.NET 0.4: a default-initialised message carries 0, and Cyclone DDS 11 refuses to serialize an enum value
        /// the enum does not declare (DdsWriter.Write → BadParameter). Never sent on purpose; module-unique name (IDL scopes enum members by module).</summary>
        NodeOpNone = 0,
        PrepareState = 1,
        CommitState = 2,
        AbortTransaction = 3,
        TakeSnapshot = 4,
        RestoreSnapshot = 5,
        PrepareZone = 7,
        CommitZone = 8,
        PrepareLive = 9,
        FinalizeLive = 10,
        PrepareReplay = 11,
        FinalizeReplay = 12,
        NodeReplaySeek = 13,
        UploadChunk = 14,
        SerializeLocal = 15,
        CleanupTempFiles = 16,
        StartEpisode = 20,
        StopEpisode = 21,
        ReplayEpisode = 22,
        ForgetEpisode = 23,
        LoadEpisodeAssets = 24,
        PrefetchFiles = 25,
        PrepareEdit = 26,
        FinalizeEdit = 27,
        CollectDiagnostics = 28,

        // ── Terrain asset build (29–30) ───────────────────────────────────────────────────────
        // ⛔ PERMANENT WIRE VALUES (R-42): they reach replays and saved scenarios. Deprecate, never
        //    recycle, never renumber. Ruled 2026-09-17.
        // ⛔ The gaps at 6 / 17 / 18 / 19 are HISTORICAL HOLES, not reservations — measured absent from
        //    the authoritative NED enum too. Leave them empty so no future reader wonders what they meant.
        // 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §6.
        PrepareTerrainAsset = 29,
        CommitTerrainAsset = 30,

        // ⭐ CE-3299 B3 (R-3044) — export a checkpoint keyframe as a one-frame recording. ⛔ PERMANENT WIRE VALUE (R-42).
        ExportCheckpoint = 31,

        // ⭐ CE-3299 B4 — phase 1 of a checkpoint restore: read keyframe X and stage it (nothing changes). Phase 2 is the dead
        //   `RestoreSnapshot = 5` slot, REUSED for its documented meaning (restore the ECS from a recording keyframe).
        //   ⛔ PERMANENT WIRE VALUE (R-42).
        PrepareRestore = 32,

        // ⭐ CE-3329 P3 (§3a) — enter / leave a review on a node (the full checkpoint of the live point, the replay over the OPEN
        //   recording; the return). ⛔ PERMANENT WIRE VALUES (R-42), allocated 2026-10-26 in BOTH enums.
        PrepareReview = 33,
        FinalizeReview = 34,
    }
}
