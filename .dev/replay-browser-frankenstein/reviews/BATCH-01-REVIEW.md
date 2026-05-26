# BATCH-01 Review

**Batch:** BATCH-01
**Reviewer:** Development Lead
**Date:** 2026-05-26
**Status:** APPROVED

---

## Summary

RBF-P1T1 through RBF-P1T4 and RBF-P2T1/P2T2 are all implemented correctly. 21 new tests pass. Build is clean. Two P3 debt items noted below; neither blocks approval.

---

## Issues Found

### Issue 1: SeekAll test does not verify per-node offset displacement

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Federation/FederatedReplayManagerTests.cs`
**Tests:** `RBF_P2T1_SeekAll_SeeksEachContext`, `RBF_P2T1_SetNodeOffset_FiresOnTimeChanged`

`RBF_P2T1_SeekAll_SeeksEachContext` only asserts both contexts land on frame 0 after seeking to tick 0 — it does not verify that applying a non-zero `NodeOffset` actually causes a node to land on a *different* frame than base. `RBF_P2T1_SetNodeOffset_FiresOnTimeChanged` only checks the event fires (correct) but never asserts the resulting seek target. No test exercises: "node 1 with offset=+1_000_000 seeks to frame 1 while node 2 with offset=0 stays at frame 0." This is a P3 gap — the base seek behavior is verified through the default-offset test, and the offset propagation path is simple, so this is not a blocker.

### Issue 2: `SetNodeOffset` for unknown NodeId is silently permissive

**File:** `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/FederatedReplayManager.cs`

`SetNodeOffset(nodeId, ...)` accepts any `nodeId` even if it is not in `Contexts`; the entry is stored but ignored during `SeekAll`. `SetLocalEntitiesProvider` by contrast throws `ArgumentOutOfRangeException` for unknown node IDs. The inconsistency is benign today but will become confusing once the UI connects offset controls to the manager. P3 — record in debt tracker, tighten in a later batch.

---

## Test Quality Assessment

Tests verify actual values (ExerciseId, NodeId, event fire count, CurrentFrame). No shallow "object exists" tests. Disposal verification via `FileShare.None` open is correct given `PlaybackController` holds `FileShare.Read` (exclusive open from test would fail if controller is still alive). P1T1 legacy-JSON test uses a hard-coded JSON string without the new fields — good forward-compat test.

---

## Verdict

**Status: APPROVED**

---

## Commit Message

```
feat: RBF Phase P1+P2 foundation — metadata extension + federated loader (BATCH-01)

Completes RBF-P1T1, RBF-P1T2, RBF-P1T3, RBF-P1T4, RBF-P2T1, RBF-P2T2

Extends recording metadata with ExerciseId/NodeId fields (additive, default-safe
for legacy .fdp files) and wires RecordingModule to stamp them at record time.
Introduces FederatedReplayManager with validated LoadGroup, wall-tick time state,
coordinated SeekAll, and IDisposable lifetime management.

RecordingMetadata (Fdp.Core):
- Added Guid ExerciseId and int NodeId (Guid.Empty/0 defaults for legacy compat)

RecordingConfiguration (Fdp.Toolkits):
- Added required int NodeId; all call sites updated (EcsRecordReplayController x2,
  breakpoints integration test)

RecordingModule (Fdp.Toolkits):
- RegisterSystems now builds RecordingMetadata{ExerciseId, NodeId} and passes it
  to AsyncRecorder ctor (no AsyncRecorder surface change)

FederatedReplayManager (Fdp.Toolkits, new):
- LoadGroup: validates ExerciseId/NodeId uniqueness before creating contexts
- LoadGroupException: human-readable rejection reasons
- BaseWallTicks, NodeOffsets, LocalEntitiesProviderNodeId time state
- SeekAll: per-context seek to BaseWallTicks + offset; fires OnTimeChanged
- IDisposable: disposes all contexts; double-dispose is no-op

Tests: 21 new tests (6 in Fdp.Core.Tests, 15 in Fdp.Toolkits.Tests)
```

---

**Next Batch:** BATCH-02 (RBF-P2T3 subsystem wiring + Phase P3 synthesis engine start)
