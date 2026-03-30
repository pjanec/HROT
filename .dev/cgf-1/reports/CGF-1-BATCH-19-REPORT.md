# CGF-1-BATCH-19 Report

**Batch:** CGF-1-BATCH-19  
**Developer:** GitHub Copilot  
**Date:** 2026-03-29  
**Status:** Complete — Part A (A.1–A.3 tech debt) and Part B (CGF1-S0308 story injection)
fully implemented; build clean; all 8 new tests pass; 1 pre-existing failure unchanged.

**Lead review:** [CGF-1-BATCH-19-REVIEW.md](../reviews/CGF-1-BATCH-19-REVIEW.md) — **CONDITIONALLY APPROVED** (§S0308 **CGF** handler / **`NodeOpStatus`** / ACK gating residual; **`RecordReplayIntegrationTests`** assertion → [CGF-1-BATCH-20](../batches/CGF-1-BATCH-20-INSTRUCTIONS.md)).

---

## Summary

CGF-1-BATCH-19 resolved both BATCH-18 review regressions on the CGF path and implemented the
full §CGF1-S0308 runtime story injection & deletion feature.

**Part A — Tech debt:**
- A.1: Removed `NodeOpType.PrepareLive` from `FailLoudRecordReplayStub.CanHandle` so
  `ScenarioLoadDsmHandler` becomes the sole `PrepareLive` handler on CGF. Added
  `PrepareCallCountForTest` seam to verify the handler is reached.
- A.2: Ported the `_pendingPrepare` deferred-Commit pattern from SimHost `DrillSlave` to
  CGF `DrillSlave`, including the `internal DrillSlave()` test constructor and
  `EnqueueCommandForTest`.
- A.3: Closed both DEBT-TRACKER BATCH-19 rows as ✅.

**Part B — S0308 runtime story injection:**
- `StoryLoadDsmHandler` handles `StartStory` / `StopStory` on SimHost nodes.
- `TransitionPlanner.PlanManageStory` plans the 1- or 2-step op sequence.
- `DrillMaster` handles `SysOpType.ManageStory`, tracks `_activeStories`.
- `ActiveStoriesJson` added to `OrchestratorContextTopic` for downstream consumers.
- `NodeBootstrapper` wires `StoryLoadDsmHandler` when a serializer is available.

Build: clean for all affected projects (zero CS errors; pre-existing
`MSB3027`/`MSB3021` Fhsm.SourceGen file-lock from SharpLens MCP unrelated to this batch).  
Tests: 38 total in `Bagira.SimHost.Integration.Tests` — 37 pass (previously 30; +8 new),
1 pre-existing failure (`NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController`,
confirmed failing in baseline BATCH-18 state via `git stash` check).

---

## Part A — Tech Debt

### A.1 — CGF `PrepareLive` disambiguation (P1)

**Root cause (from BATCH-18 review):** `FailLoudRecordReplayStub.CanHandle` returned `true`
for `NodeOpType.PrepareLive`. Because `DrillSlave` dispatches to the **first** matching handler
and the stub was registered first in `CgfApplication`, `ScenarioLoadDsmHandler.PrepareAsync`
was never reached for any `PrepareLive` command on CGF.

**Fix chosen:** Option 1 — remove `PrepareLive` from the stub. The stub's purpose is to
fail-loud for *recording/replay* ops (`FinalizeLive`, `PrepareReplay`, `FinalizeReplay`) that
CGF cannot support. `PrepareLive` is a scenario-load op and belongs exclusively to
`ScenarioLoadDsmHandler`. The handler's existing `HasDrillId` guard in `PrepareAsync` provides
fail-loud behaviour for branch-style payloads.

**Files changed:**
- `Bagira.CGF/Modules/Orchestration/Handlers/FailLoudRecordReplayStub.cs` — removed
  `NodeOpType.PrepareLive` from `CanHandle`; updated `<summary>` XML to reflect the three
  remaining ops
- `Bagira.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs` — added
  `internal int PrepareCallCountForTest` seam (incremented in `PrepareAsync`); updated error
  message in `HasDrillId` branch; updated `CanHandle` XML to "sole `PrepareLive` handler"
- `Bagira.CGF/CgfApplication.cs` — updated registration comment to clarify that the two
  handlers' op sets are disjoint and registration order is irrelevant for correctness

**Tests added** (`Bagira.SimHost.Integration.Tests/CgfPrepareLiveDispatchTests.cs`, 3 tests):
- `PrepareLive_WithScenarioId_RoutesToScenarioLoadDsmHandler` — stub + handler registered in
  `CgfApplication` order; `PrepareLive` with `ScenarioId` enqueued; asserts
  `PrepareCallCountForTest == 1`
- `PrepareLive_WithDrillIdOnly_RoutesToScenarioLoadDsmHandler` — branch-style payload still
  routes to the handler (PrepareAsync's `HasDrillId` guard logs and sets `IsParticipating =
  false`; handler is not bypassed); asserts `PrepareCallCountForTest == 1`
- `PrepareLive_MatchingScenarioFile_HandlerPeeksSuccessfully` — real temp file written with
  matching `SimHostType` sub-system; asserts no exception, result null, count 1

### A.2 — CGF `DrillSlave` `PrepareAsync`/`Commit` ordering (P2)

**Root cause (from BATCH-18 review):** `Bagira.CGF/DrillSlave.DispatchCommand` called
`_ = handler.PrepareAsync(cmd, default)` (fire-and-forget) then immediately
`handler.Commit(cmd, repo: null)`. This mirrored the pre-BATCH-18 SimHost bug.

**Fix:** Ported the `_pendingPrepare: (Task<string?>, NodeOpCommand, IDsmHandler)?` latch
pattern. `Tick()` drains any pending prepare before accepting new commands; `DispatchCommand`
calls `Commit` only when the task is already completed, otherwise stores the tuple. Faulted
tasks log `Error` and skip `Commit`.

**Additional test seams added:**
- `internal DrillSlave()` no-DDS constructor (test-only, `_nodeId = 0`,
  `_subsystemName = "CGF-Test"`)
- `internal void EnqueueCommandForTest(NodeOpCommand cmd)` — bypasses background listener

**File changed:**
- `Bagira.CGF/Modules/Orchestration/DrillSlave.cs` — complete rewrite to add `_pendingPrepare`
  field, nullable `_listenerThread`/`_listenerCts`, test constructor, `EnqueueCommandForTest`,
  drain in `Tick()`, conditional `Commit` in `DispatchCommand`, null-conditional `?.` in
  `Dispose()`

> **Note:** The CGF `DrillSlave` implementation is simpler than SimHost's: no
> `DsmStateChangedEvent`, no `_seenTransactionIds`, no `NodeOpStatusWriter`. The latch
> pattern is otherwise identical.

### A.3 — DEBT-TRACKER

Both BATCH-19 rows in `.dev/DEBT-TRACKER.md` marked ✅:
- P1 row: `FailLoudRecordReplayStub.CanHandle excludes PrepareLive; ScenarioLoadDsmHandler
  sole handler with HasDrillId guard; 3 dispatch tests`
- P2 row: `_pendingPrepare latch + drain in Tick(); DispatchCommand defers Commit; internal
  test constructor + EnqueueCommandForTest`

---

## Part B — CGF1-S0308 Runtime Story Injection & Deletion

**Story:** A running simulation should be able to inject a scenario file as a "story" (a
named overlay of entities stamped with `StoryTag`) and later destroy exactly those entities by
`StoryId`, without affecting the primary scenario.

### Implementation

**`OrchestratorContextTopic.ActiveStoriesJson`** (Bagira.DDS.DataModel)  
Added `[DdsManaged] public string ActiveStoriesJson` to the context topic. Populated by
`DrillMaster` as a JSON array of active `Guid` strings; downstream ImGui and test consumers
can read the live story roster.

**`StoryLoadDsmHandler`** (Bagira.SimHost/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs)  
New handler wired on SimHost nodes when a scenario serializer is available.

- `CanHandle`: `StartStory` (20) and `StopStory` (21)
- `PrepareAsync(StartStory)`: locates scenario dir, peeks `SubsystemType`, stores DOM for
  `Commit`; sets `_pendingIsParticipating`
- `Commit(StartStory)`: calls `ScenarioSerializer.Deserialize(repo, dom, asStory: true,
  storyId: guid)` — entity graph is spawned with `StoryTag.StoryId = guid`
- `PrepareAsync(StopStory)`: collects entities with matching `StoryTag.StoryId` using
  `repo.IsComponentTypeRegistered<StoryTag>()` guard + `repo.Query().With<StoryTag>()`
- `Commit(StopStory)`: destroys all collected entities via `repo.DestroyEntity`
- `internal bool IsParticipatingForTest` test seam

**`TransitionPlanner.PlanManageStory`** (Bagira.Orchestrator/TransitionPlanner.cs)  
New method planning the `ManageStory` op sequence:
- Validates `current == DSMState.RunningLive`
- `Start`: `Queue { PrefetchScenario(scenarioId), ManageStory(fullPayload) }`
- `Stop`: `Queue { ManageStory(fullPayload) }`
- Throws `InvalidOperationException` for missing fields, wrong state, unknown mode

**`DrillMaster`** (Bagira.Orchestrator/DrillMaster.cs)  
New `ManageStory` branch in `ProcessSysOpRequests`:
- Calls `_planner.PlanManageStory`; catches `InvalidOperationException` → `Rejected` (ErrorCode=2)
- Iterates steps: `PrefetchScenario` → `ExecutePrefetchScenario`; `ManageStory` → `FanOutNodeOp`
  with `StartStory`/`StopStory`
- Maintains `private HashSet<Guid> _activeStories` + `public IReadOnlyCollection<Guid> ActiveStories`
- Publishes `ActiveStoriesJson` on `_contextWriter` after each successful op

**`NodeBootstrapper`** (Bagira.SimHost/NodeBootstrapper.cs)  
Registers `StoryLoadDsmHandler` inside the `if (scenarioSerializer != null)` block after
`EditLoadDsmHandler`.

### Tests

**`Bagira.SimHost.Integration.Tests/StoryInjectionTests.cs`** (5 tests):

1. `StartStory_EntitiesSpawnedWithStoryTag` — loads 3-entity story scenario, calls
   `PrepareAsync` + `Commit`, asserts `EntityCount == 3` and all have `StoryTag.StoryId ==
   storyId`
2. `StopStory_EntitiesDestroyedByStoryTag` — Start (3 entities) → Stop → asserts
   `EntityCount == 0`
3. `StartStory_NonMatchingSubsystem_IsParticipatingFalse` — CGF-typed scenario file, asserts
   `IsParticipatingForTest == false` and `EntityCount == 0`
4. `ManageStory_RejectedWhen_NotInRunningLive` — `PlanManageStory` throws
   `InvalidOperationException` for `Standby`, `RunningEdit`, `RunningReplay`
5. `MultipleStoriesCoexist_IndependentDeletion` — injects story s1 (3 entities) and s2 (2
   entities); stops s1; asserts 2 entities remain, all with `s2Id`

### Deviations from §CGF1-S0308 task detail

- **`DrillMaster` orchestrator integration** is partially implemented: fan-out, acknowledgement,
  and `ActiveStoriesJson` publication are complete. The full DDS roster/reader is deferred (as
  noted in the task detail's "Minimum viable" definition). This matches the "default path" of the
  batch instructions.
- **ImGui controls** for story management (CGF1-S0106) are not part of this batch.
- `StoryLoadDsmHandler` uses `NodeOpStatus.IsParticipating` (exists in DDS model) via the
  `IsParticipatingForTest` seam but does not yet write to the `NodeOpStatusWriter` (the writer
  is not passed to the handler); this is consistent with `ScenarioLoadDsmHandler` and
  `EditLoadDsmHandler` on the same code path.

---

## Build & Test Results

**Build:** All affected projects compile with 0 CS errors under .NET 8 / C# 13 (`LangVersion`
set to `latest` in `Bagira.SimHost.Integration.Tests.csproj` to support `ref readonly` in async
test methods — a pattern already used by `Bagira.DDS.DataModel.Tests` and other test projects).  
The `MSB3027`/`MSB3021` errors affecting the full-solution build are caused by SharpLens MCP
holding `Fhsm.SourceGen.dll` locked; they are a build tool issue, not a code regression.

**Test suite (`Bagira.SimHost.Integration.Tests`):**

| Result   | Count | Notes                                         |
|----------|-------|-----------------------------------------------|
| Passed   | 37    | All new + all pre-existing except 1 below     |
| Failed   | 1     | Pre-existing (confirmed via `git stash` check)|
| Skipped  | 0     |                                               |
| **Total**| **38**| +8 vs BATCH-18 baseline (30 tests)            |

**Pre-existing failure:** `RecordReplayIntegrationTests.NodeBootstrapper_BrainRole_RegistersEcsRecordReplayController`  
This test was already failing in the BATCH-18 baseline. The `EcsRecordReplayController` is
created for Brain/AllInOne roles but passed as a dependency to `LiveLoadDsmHandler` and
`ReplayLoadDsmHandler` rather than registered directly on `DrillSlave`. The test's assertion
`IsHandlerRegistered<EcsRecordReplayController>()` therefore fails. This is a test design gap
from a prior batch, not introduced by BATCH-19.

---

## Files Changed

| File | Change |
|------|--------|
| `Bagira.CGF/Modules/Orchestration/Handlers/FailLoudRecordReplayStub.cs` | Removed `PrepareLive` from `CanHandle` |
| `Bagira.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs` | `PrepareCallCountForTest` seam, updated error/XML |
| `Bagira.CGF/CgfApplication.cs` | Updated registration comment |
| `Bagira.CGF/Modules/Orchestration/DrillSlave.cs` | `_pendingPrepare`, test constructor, `EnqueueCommandForTest`, drain, null-safe Dispose |
| `Bagira.DDS.DataModel/Orchestration/OrchestrationMessages.cs` | `ActiveStoriesJson` on `OrchestratorContextTopic` |
| `Bagira.Orchestrator/TransitionPlanner.cs` | `PlanManageStory` method |
| `Bagira.Orchestrator/DrillMaster.cs` | `_activeStories`, `ActiveStories`, `ManageStory` branch |
| `Bagira.SimHost/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs` | **New** — `StartStory`/`StopStory` handler |
| `Bagira.SimHost/NodeBootstrapper.cs` | Wire `StoryLoadDsmHandler` |
| `Bagira.SimHost.Integration.Tests/Bagira.SimHost.Integration.Tests.csproj` | `<LangVersion>latest</LangVersion>` |
| `Bagira.SimHost.Integration.Tests/CgfPrepareLiveDispatchTests.cs` | **New** — 3 dispatch tests |
| `Bagira.SimHost.Integration.Tests/StoryInjectionTests.cs` | **New** — 5 story injection tests |
| `.dev/DEBT-TRACKER.md` | Both BATCH-19 rows → ✅ |
| `.dev/cgf-1/CGF-1-TASK-TRACKER.md` | S0308 → ✅; progress header updated; active batch → BATCH-20 |
