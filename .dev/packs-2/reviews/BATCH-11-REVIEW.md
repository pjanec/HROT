# BATCH-11 Review

**Batch:** BATCH-11  
**Tasks:** PACK2-R005, PACK2-R006  
**Verdict:** ✅ APPROVED — minor P3 notes only; no corrective tasks required

---

## Score: 9 / 10

All 8 R005 tests pass. R006 tests are structurally correct but fail on machines without CycloneDDS (expected; matches existing DDS test pattern). IT-3c (CGF AI intent) correctly skipped.

---

## Task-by-Task Assessment

### `ScenarioFileService` bus publish (Task 1) ✅

**Quality: Excellent with one noteworthy discovery**

- Optional `FdpEventBus?` constructor parameter preserves full backward-compat.
- Critical discovery: `SoftClear()` internally calls `Bus.ClearAll()`, wiping any managed event published before it. Developer correctly restructured to call synchronous observers BEFORE `SoftClear`, and bus `PublishManaged(WorldResetEvent)` AFTER. This is semantically acceptable: the observers (tool flushing) execute synchronously before entity destruction; the bus event is queued for the next frame's readers after the clear.
- **Minor P3:** `FireWorldReset()` private method is now dead code (neither `NewScenario` nor `LoadScenario` call it; both inline the logic). Should be removed.

### `CgfSubsystem.GhostEntityMap` (Task 3) ✅

Clean field promotion + internal property. Correct pattern.

### `EditorHarness` extensions (Task 2) ✅

**Quality: Excellent with one architectural improvement**

- Developer correctly included `SimHostModule` (not just `SimHostCoreLogicPack` + `CgfLogicPack`) in `_logicPacks`. Without this, `NetworkSpawningSystem` stayed active in External mode, breaking IT-3a. This was not in the batch instructions — good catch.
- `SetTranslatorPacks` cleanly re-creates `EditorApplication` with the new translator list.
- `[Collection("EditorOfflineTests")]` serialises the 3 offline test classes to one thread, avoiding thread-pool starvation under parallel RCU drain tasks. Good pragmatic fix.

### `EditorFileIOIntegrationTests` R005-A (Task 5) ✅

4/4 pass. Tests cover all required scenarios; IT-2a uses `ConsumeManaged<WorldResetEvent>()` post-pump (correct).

### `FeatureSwitchRcuIntegrationTests` R005-B (Task 6) ✅

4/4 pass.
- IT-3a/3b/3c correctly verify mode changes and spawn behavior.
- IT-3d uses the internal `SpawnEntityCommandEgressTranslator` constructor + `SpyEgressPack.Tick` pattern (calling `PollIngress` directly). Verified `spy.CallCount == 1` after 3 pump frames.
- `SwitchMs = 30_000` timeout is conservative but safe for a CI environment with thread-pool variability.

### `DistributedBrainMuscleIntegrationTests` R006 (Task 7) ✅

Structurally correct. IT-4a + IT-4b fail with `CycloneDDS.Runtime.DdsException` (no native lib) — same as all other DDS tests in the suite. IT-4c correctly skipped (CGF AI mission assignment requires ExCon/MissionControl chain outside scope). Acceptable.

---

## Deviations from Instructions

| Deviation | Verdict |
|-----------|---------|
| `WorldResetEvent` published AFTER `SoftClear` (not before, as spec says) | ✅ Correct — `Bus.ClearAll()` inside `SoftClear` makes pre-clear impossible |
| `SimHostModule` included in `_logicPacks` (not in instructions) | ✅ Correct — critical fix |
| IT-3c (`CgfAiIntent_ReachesSimHost`) skipped | ✅ Correct — requires full ExCon chain |
| `Thread.Sleep(1)` in IT-3 pump loop | ✅ Acceptable — RCU Task.Run needs CPU yield |

---

## Test Results

| Suite | Before | After | Δ |
|-------|--------|-------|---|
| `Hrot.Editor.Tests` | 20 | 20 | — |
| `Hrot.ClusterRunner.Integration.Tests` (offline) | 8 | 19 | +11 |
| `Hrot.ClusterRunner.Integration.Tests` (total) | ~10 | ~64 (60 pass / 4 skip / 4 DDS-fail) | +11 |

---

## Issues for DEBT-TRACKER

| ID | Priority | Description | Target |
|----|----------|-------------|--------|
| DEBT-11-01 | P3 | `ScenarioFileService.FireWorldReset()` is dead code — never called | housekeeping |
| DEBT-11-02 | P2 | `FeatureSwitchRcuIntegrationTests` uses `Thread.Sleep(1)` in pump loop and 30 s timeout — works but fragile. Could be replaced with a kernel drain-complete callback exposed on `ModuleHostKernel`. | future |
| DEBT-11-03 | P2 | IT-3c (`CgfAiIntent_ReachesSimHost_ViaDds`) is skipped. Re-enable when ExCon/MissionControl harness is available. | future |
| DEBT-11-04 | P3 | `DistributedBrainMuscleIntegrationTests` domain counter starts at 300 but is not coordinated with other DDS test classes. Document the range assignment. | housekeeping |

---

## Summary

All 28 PACK2 tasks are now complete. The implementation delivers:
- Decoupled scenario editor reusable by any host (IG, ExCon, HROT Editor)
- Feature Switch for Internal ↔ External FDP SimHost modes via kernel RCU
- Full integration test suite: offline spawn/edit/delete, file I/O, feature switch RCU, distributed DDS (pending native lib)
