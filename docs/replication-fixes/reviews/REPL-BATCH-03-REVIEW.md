# BATCH-03 Review

**Batch:** REPL-BATCH-03
**Reviewer:** Development Lead
**Date:** 2026-03-02
**Status:** ❌ REJECTED

---

## Summary

You successfully completed the mechanical relocation and renaming required for the Phase 5 (Part B) Translator Unification. The `Bagira.Map.Common` library successfully encapsulates `DescriptorMapper` and the disparate Ingress/Egress pipelines. You also brought up the Integration Tests for Phase 4.

However, the batch **FAILED** its architectural and integration validation because you did not verify the results natively, and you bypassed testing the full `dotnet test` suite. The solution crashes at runtime during standard interactions. 

---

## Technical Findings

1. **System Crashes (TkbType 0):** Running the global test suite throws fatal `SystemScheduler` exceptions in `GhostPromotionSystem`. Wait... `GhostPromotionSystem.PromoteGhost` looks up the TKB template via `_tkbDatabase.GetTemplate(spawnReq.TkbType)`. `spawnReq.TkbType` defaults to `0` for Ghost entities that haven't received an `EntityMaster` packet yet. The system attempts a direct lookup and blows up the scheduler with a `KeyNotFoundException`.
2. **ELM Zero-Participant Bug (REPL-C03 realization):** You correctly identified `GhostPromotionSystem` as the missing link and successfully wired it to call `_lifecycleModule.BeginConstruction`. However, because IG has `0` global ELM participants, `BeginConstruction` tracks it in `pendingConstruction` but it permanently stalls because nobody ever sends an ACK. (This is a bug in `EntityLifecycleModule.cs`, which you must fix in BATCH-04).
3. **ID Pool Exhaustion:** `CreateEntityRequestSystem` logs `ID pool exhausted and no response from server` natively because the backend DDS responses fail to connect or the entities are black-holed in staging.
4. **False "Ghost-Only" Assumptions (REPL-C04):** Reviewing the Ingress Translators migrated in BATCH-02 reveals several comments assuming `IG is a ghost-only node`. This is architecturally incorrect! IG is a full FDP node and can create entities. The reason Ingress translators have empty `ScanAndPublish` methods is strictly because they are *Ingress* translators, NOT because the application is a "ghost-only node".

---

## Verdict

**Status:** REJECTED.

The pull request will be closed. I have compiled all these fixes into **REPL-BATCH-04**. You will resolve the `TkbType 0` crash, fix the stalled Ghost promotion inside the ELM codebase, purge the "Ghost-Only" comments (REPL-C04), and convert the Defs to Unmanaged Components (REPL-C05).

Please ensure you actually run `dotnet test` (across the entire solution, not just the Runner) before submitting next time!
