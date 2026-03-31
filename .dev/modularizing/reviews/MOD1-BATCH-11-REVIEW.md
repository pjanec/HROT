# MOD1-BATCH-11 Review

**Batch:** MOD1-BATCH-11  
**Reviewer:** Development Lead  
**Date:** 2026-03-16  
**Status:** ⚠️ APPROVED WITH MANDATORY FOLLOW-UP

---

## Summary

DB-MOD1-22 and DB-MOD1-24 are both correctly implemented. The `IgSymbolOverride` ID migration is clean and safe (no replay persistence concern). The translator packs exist and are tested. However, the developer has reported **2 failing integration tests** in `Hrot.ClusterRunner.Integration.Tests` and classified them as "pre-existing failures unrelated to BATCH-11." This classification is **incorrect** — direct inspection of the test and the source code confirms these are unresolved real failures, not pre-existing noise, and one of them (`SimHostDrag_IgReceivesPositionUpdateWithinFewFrames`) exposes a real production bug.

---

## What Went Well

### DB-MOD1-22 — `IgSymbolOverride` ID Migration
- ID correctly moved from `GlobalComponentIds` (119, freed with tombstone comment) to `HrotComponentIds` (167).
- The developer confirmed the component is not persisted to `.fdp` files — it is a transient display-layer managed class re-populated from live DDS. ID change is safe.
- `HrotComponentIds_NoDuplicates` and `HrotComponentIds_AllInApplicationRange` tests confirm the uniqueness contract is maintained.
- All 20 `StyleResolutionSystemTests` pass at the new ID.

### DB-MOD1-24 — `KinematicTranslatorPack` + `CognitiveTranslatorPack`
- Both packs exist, are correctly implemented, and are registered in `NodeBootstrapper.BuildTranslators`.
- The Q2 analysis explaining **why `SimHostApp.OnLoad` was not refactored** to use `AddRange` is architecturally sound: applying both Brain-side and Muscle-side translator packs to the same DDS participant in a standalone AllInOne process causes self-subscription loops (egress publishes → ingress overwrites ECS state). The packs are for distributed multi-node use.
- However, this means the original DB-MOD1-24 success criterion ("SimHostApp.OnLoad uses AddRange calls") was **not fully met**. The spirit of the task is satisfied (`NodeBootstrapper` uses the packs correctly for distributed nodes); the letter is not. This is acceptable given the DDS topology constraint, but it should be documented.

---

## Issues Found

### 🔴 P1 — Two Failing Integration Tests Not Pre-Existing (DB-MOD1-26)

The developer reports:
> "29 / 31 — 2 pre-existing failures (see Outstanding Issues)"

This classification is **incorrect**. Both tests were authored specifically to verify behaviour that requires a production fix. The review initially assumed `SmartEgressUtil.MarkDirty` was missing — this was wrong.

**Corrected root cause analysis:**  
`WorldPosEgressTranslator` does **not** use `SmartEgressUtil`. It compares `SimTransform.Position` against `NetworkTransform.LastPosition` directly (shadow component). Writing to `SimTransform.Position` via `TestHook_SimulateDrag` should trigger an automatic publish on the next `ScanAndPublish` frame — **if** the entity has both a `NetworkTransform` shadow component and `HasAuthority(dtWorldPos)` authority. The actual root cause is therefore likely one of:
- Spawned entities missing the `NetworkTransform` shadow component (excluded from the translator's query).
- `DescriptorOwnership` for the `dtWorldPos` ordinal not set at spawn time (authority check fails).
- For `DragDropIntegrationTests` (full round-trip): `UpdateEntityDescriptorRequestSystem` not correctly applying the new position or the DDS message not being processed within the timeout.

The `DragDropIntegrationTests` already include diagnostic log lines (`[D2c]`/`[D2d]`) that will pinpoint the exact failure. The fix must be data-driven by running the tests with verbose output first.

**Required fix (BATCH-12):** Diagnose and fix both failing tests. Both must pass unconditionally.

---

## Verdict

**Status:** ⚠️ APPROVED WITH MANDATORY FOLLOW-UP

DB-MOD1-22 and DB-MOD1-24 are delivered correctly. The BATCH-11 task scope is fulfilled. **However, the two failing tests must be addressed as the first task in BATCH-12** — they are P1 and expose a real user-facing bug (drag position propagation latency of ~10 seconds instead of ~1 frame).

---

**Next Batch:** MOD1-BATCH-12
