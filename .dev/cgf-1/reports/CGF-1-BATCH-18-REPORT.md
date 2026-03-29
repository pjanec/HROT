# CGF-1-BATCH-18 Report

**Batch:** CGF-1-BATCH-18  
**Developer:** GitHub Copilot  
**Date:** 2026-03-29  
**Status:** Complete — Part A (A.1–A.3 tech debt, A.4 DEBT-TRACKER closure) and Part B
(`FullBranchPipelineTests`) fully implemented; build clean; all 387 `Bagira.SimHost.Tests` pass.

**Lead review:** [CGF-1-BATCH-18-REVIEW.md](../reviews/CGF-1-BATCH-18-REVIEW.md) — **CONDITIONALLY APPROVED** (CGF **`PrepareLive`** blocks **`ScenarioLoadDsmHandler`**; CGF **`DrillSlave`** still fire-and-forget **`PrepareAsync`** → [CGF-1-BATCH-19](../batches/CGF-1-BATCH-19-INSTRUCTIONS.md)).

---

## Summary

CGF-1-BATCH-18 resolved the three BATCH-17 review P1/P2 critical gaps and implemented the §CGF1-S0305 integration test deferred from BATCH-17.

**Part A:** Fixed SimHost `PrepareLive` dispatch routing (`ReplayLoadDsmHandler` now receives
Live-from-Replay commands when replay is active), CGF branch `PrepareLive` visibility
(`FailLoudRecordReplayStub` now runs before `ScenarioLoadDsmHandler` in `CgfApplication`), and
`DrillSlave.DispatchCommand` `PrepareAsync`/`Commit` ordering (deferred `Commit` until the
async prepare task completes).

**Part B:** `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`
runs 80+ ticks of original recording, seeks replay to frame 50, executes the Live-from-Replay
branch, records 50 branched ticks, and asserts the branched `.fdp` keyframe at frame 0 matches
the historical entity snapshot.

Build: clean (zero new CS errors; pre-existing third-party CS1591 warnings only).  
Tests: 387/387 `Bagira.SimHost.Tests` (was 385; +2 new tests).

---

## Part A — Tech Debt

### A.1 — `PrepareLive` must reach `ReplayLoadDsmHandler` on SimHost (P1)

**Root cause:** `NodeBootstrapper.BuildOrchestration` registered `LiveLoadDsmHandler` before
`ReplayLoadDsmHandler`. Since `DrillSlave.DispatchCommand` dispatches to the first matching
handler, `ReplayLoadDsmHandler.CanHandle(PrepareLive)` was vacuously true but never reached.

**Fix (both guards applied):**
1. **`ReplayLoadDsmHandler.CanHandle`** now returns `true` for `PrepareLive` only when
   `_controller.ActiveReplayModule != null` (replay session is active). Cold `PrepareLive`
   commands fall through to `LiveLoadDsmHandler` as before.
2. **`NodeBootstrapper.BuildOrchestration`** now registers `ReplayLoadDsmHandler` **before**
   `LiveLoadDsmHandler` (belt-and-suspenders; registration order now matches the conditional
   priority intent).

**Files changed:**
- `Bagira.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs` — `CanHandle` narrowed
- `Bagira.SimHost/NodeBootstrapper.cs` — handler registration order swapped + comment updated

**Test added** (`Bagira.SimHost.Tests/NodeBootstrapperReplayTests.cs`):
- `DrillSlaveDispatch_PrepareLiveWithActiveReplay_RoutesToReplayBranch` — uses real
  `EcsRecordReplayController` (creates recording, opens replay so `ActiveReplayModule != null`),
  registers both handlers on a `DrillSlave` in the fixed order, dispatches `PrepareLive` via
  `EnqueueCommandForTest` + multi-tick loop, asserts `ActiveReplayModule == null` and
  `ActiveRecordingModule != null` (proving `ReplayLoadDsmHandler` handled the command, not
  `LiveLoadDsmHandler`).

### A.2 — CGF `PrepareLive` branch must not be swallowed by `ScenarioLoadDsmHandler` (P2)

**Root cause:** In `CgfApplication`, `ScenarioLoadDsmHandler` was registered before
`FailLoudRecordReplayStub`. `ScenarioLoadDsmHandler.CanHandle(PrepareLive)` is always true, so
branch payloads (DrillId only, no ScenarioId) were silently acknowledged; the stub never ran.

**Fix (registration order + guard log):**
1. **`CgfApplication`** now registers `FailLoudRecordReplayStub` **before**
   `ScenarioLoadDsmHandler`. Branch `PrepareLive` reaches the stub first and logs `Error`.
2. **`ScenarioLoadDsmHandler.PrepareAsync`** (CGF variant) adds an early guard: if
   `scenarioId` is empty AND `DrillId` is present in the payload, it logs `Error` as a
   belt-and-suspenders diagnostic that surfaces if handler registration order is changed.

**Files changed:**
- `Bagira.CGF/CgfApplication.cs` — registration order: stub first, scenario handler second
- `Bagira.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs` — branch guard +
  `HasDrillId` helper

### A.3 — `DrillSlave.DispatchCommand` `PrepareAsync`/`Commit` ordering (P2)

**Root cause:** `DrillSlave.DispatchCommand` used `_ = handler.PrepareAsync(cmd, default)` then
immediately called `handler.Commit(cmd, repo: null)`. Handlers with truly async `PrepareAsync`
(e.g. `ReplayLoadDsmHandler` calling `TeardownReplayAsync` → `UninstallModuleAsync`) could see
`Commit` run before the async teardown/install completed.

**Fix (`_pendingPrepare` deferred dispatch):**
Added a `_pendingPrepare: (Task<string?>, NodeOpCommand, IDsmHandler)?` field.  
- When `PrepareAsync` returns a completed task (synchronous handlers: `DryRunDsmHandler`,
  `CheckpointDsmHandler`, `PrefetchFilesDsmHandler`): `Commit` is called immediately — no
  behaviour change.
- When `PrepareAsync` returns a pending task: the tuple is stored and `Tick()` defers `Commit`
  until the task completes on the next tick(s). New commands are held until the pending prepare
  drains.
- Faulted `PrepareAsync` tasks log `Error` and skip `Commit`.

**Files changed:**
- `Bagira.SimHost/Modules/Orchestration/DrillSlave.cs` — `_pendingPrepare` field, `Tick`
  drain logic, `DispatchCommand` conditional `Commit`

### A.4 — DEBT-TRACKER closure

Marked rows 168–171 (CGF-1-BATCH-17 review items: handler dispatch, CGF branch, await ordering,
FullBranchPipelineTests) as ✅ in `.dev/DEBT-TRACKER.md`.

---

## Part B — `FullBranchPipelineTests` (§CGF1-S0305)

**File created:** `Bagira.SimHost.Tests/FullBranchPipelineTests.cs`

**Test:** `BranchedRecording_CapturesHistoricalStateAsKeyframe`

**Flow:**
1. Creates 5 entities with predictable `SimTransform` positions.
2. Starts a background kernel loop; prepares recording for `originalDrillId`; drives ~100
   kernel ticks via `Task.Delay(20)` × 100 (accumulates ~125 kernel frames).
3. Finalizes original recording; opens replay; seeks to frame 50 (blasts historical ECS state
   into `_world`).
4. Calls `ReadSimTransformPositions(_world)` (sync helper, avoids `ref readonly` in async
   method, C# 12 constraint) to snapshot entity positions at frame 50.
5. Constructs `ReplayLoadDsmHandler` and dispatches `PrepareAsync(PrepareLive, branchedDrillId)`
   + `Commit` directly — mirrors the fixed production dispatch (BATCH-18 A.1/A.3).
6. Runs 50 more branched recording ticks; finalizes branched recording.
7. Opens `{tempDir}/{branchedDrillId}/node_1.fdp` with `RecordingReader`; reads frame 0 into a
   fresh `EntityRepository`.
8. Asserts entity count and `SimTransform` positions match the frame-50 snapshot.

**Design notes:**
- Frame 0 of any recording is always a keyframe (`RecorderTickSystem` initialises
  `_framesSinceKeyframe = KeyframeInterval - 1 = 59`, so the very first `Execute` issues a
  keyframe). This confirms "keyframe at frame 0" without needing to inspect frame-type bytes.
- `ReadSimTransformPositions` is a private sync helper that captures component values into a
  `Dictionary<int, Vector3>` to work around C# 12.0's CS9202 restriction on `ref readonly` in
  async methods.
- `Assert.Equal(precision: 4)` used for floating-point comparisons.

---

## Task-Tracker Updates

- `CGF-1-TASK-TRACKER.md` progress line updated: Phase 3 now **9 / 10** done; S0305 residual
  note cleared; "fully closed in CGF-1-BATCH-18 ✅" appended to the S0305 checklist entry.

---

## Test Results

| Suite | Passed | Failed |
|---|---|---|
| `Bagira.SimHost.Tests` | **387** | 0 |

New tests (2 net additions):
- `NodeBootstrapperReplayTests.DrillSlaveDispatch_PrepareLiveWithActiveReplay_RoutesToReplayBranch`
- `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`

---

## Deviations

None from the batch instructions. All three fix options were applied in combination (A.1:
conditional `CanHandle` + registration order swap; A.2: registration order + guard log;
A.3: `_pendingPrepare` deferred dispatch) per the "pick one, document in XML" guidance,
since they are complementary and the belt-and-suspenders approach is consistent with the
codebase's defensive style.

---

## Known Issues / Follow-ups

- `FailLoudRecordReplayStub` still has no `NodeOpStatus` NAK writer on CGF — orchestrator may
  count CGF as ACK-success until a `NodeOpStatus` DDS writer is wired. Tracked as Opportunistic
  in DEBT-TRACKER.
- `FullBranchPipelineTests` relies on wall-clock delays for frame accumulation; it can be
  accelerated in future by switching to a blocking recording configuration
  (`RecordingConfiguration.Blocking = true`) to allow deterministic frame-count control
  without background kernel loops.
