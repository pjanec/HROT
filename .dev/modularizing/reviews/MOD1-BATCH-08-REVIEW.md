# MOD1-BATCH-08 Review

**Batch:** MOD1-BATCH-08  
**Reviewer:** Development Lead  
**Date:** 2026-03-16  
**Status:** ✅ APPROVED

---

## Summary

BATCH-08 is the cleanest batch in the MOD1 series so far. All corrective tasks were resolved properly, the Phase 8 replay architecture is well-structured and correctly domain-split, and the integration tests are genuinely meaningful — they verify real file I/O and actual module topology changes in a live `ModuleHostKernel`.

---

## What Went Well

### CT-MOD1-N — LosRequestBatchingSystem refactor
The dual-inheritance problem is fully resolved. `LosRequestBatchingSystem` is now a plain `IModuleSystem`-only class, and `AutonomousPerceptionModule.Tick()` drives all four systems uniformly in the correct pipeline order (`LocalGridBuilder → VisionBroadphase → LosRequestBatching → ThreatEvaluation`). The CombatModule leftover registration was also caught and cleaned up — good housekeeping.

### DB-MOD1-16 — GeographicComponentIds
Correctly executed. The three ground-clamping IDs (77–79) now live in `GeographicComponentIds` inside `Fdp.Toolkit.Geographic`, consistent with the per-toolkit registry pattern established in Phase 5.

### Phase 8 — Recording/Replay Architecture
The domain split is exemplary:
- `FDP.Toolkit.Replay` contains only generic, ECS-agnostic replay infrastructure (`RecordingModule`, `StoryRecorderModule`, `ReplayModule`, component IDs). Zero Hrot references.
- `Hrot.SimHost.Modules.Orchestration` contains the `EcsRecordReplayController` which is the sole composition root that bridges DSM commands to the generic toolkit modules.
- The dependency graph is strictly one-way: `Hrot.SimHost → FDP.Toolkit.Replay → Fdp.Kernel`.

### Zero-cost idle path
The "pay for what you use" scheduler topology design is correct and well-articulated (Q2). No `if (isRecording)` guards on the hot path — the recorder simply doesn't exist in the scheduler graph when idle.

### Integration tests (RecordReplayIntegrationTests.cs)
These tests are doing real work:
- They run an actual `ModuleHostKernel` update loop on a background thread.
- They verify that `.fdp` files are physically created on disk after recording.
- They verify that concurrent global + story recording produces two independent files.
- They verify that `IsModuleInstalled` transitions correctly across `PrepareRecordingAsync`/`FinalizeRecordingAsync`.

This is exactly the standard we want — tests that prove the system works end-to-end, not just that methods were called.

### Story filter with `BuildStoryFilter`
Using a closure over `_repo` with `HasComponent<StoryTag>` + `StoryId == storyId` is clean and correct. The filter correctly lives in the Hrot domain (it reads Hrot-owned component layout) rather than polluting `FDP.Toolkit.Replay`.

---

## Issues Found

### Minor: `ReplayModule_SeekToFrameAsync_IsOffMainThread` test is too weak (DB-MOD1-18)

The test asserts `Assert.NotNull(seekTask)` and then `await seekTask`. This does not actually prove the seek ran off the main thread — it only proves it returned without throwing. `Task.Run` does schedule onto the thread pool, but the test doesn't observe that (e.g. by checking `Task.IsCompleted` immediately before awaiting, or by capturing the thread ID). This is a minor documentation/test quality issue, not a functional defect. Logged as low-priority debt.

### Minor: `ClusterSlave` remains a skeleton (DB-MOD1-19)

The `ClusterSlave` class provides `RegisterHandler`, `IsHandlerRegistered<T>`, and `IReadOnlyList<IDsmHandler> RegisteredHandlers`, but the actual 2PC DSM protocol (`NodeOpStatus`, `ACK/NAK` flow) is noted as deferred. This is acceptable for now since the scope of BATCH-08 was explicitly a skeleton, but it must not be forgotten — the application cannot accept or reject DSM drill commands until this is wired.

---

## Verdict

**Status:** ✅ APPROVED

No blocking issues. Both minor finds are logged as debt and must be included in a future batch before the application ships. Phase 8 is complete.

---

**Next Batch:** MOD1-BATCH-09
