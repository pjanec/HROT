# BATCH-02 Review

**Batch:** BATCH-02  
**Reviewer:** Dev Lead  
**Date:** 2025-07-17  
**Decision:** APPROVED

---

## Summary

BATCH-02 implemented TASK-S002 (StorageProcessManager corrective tests) and TASK-S003
(EpisodeConsensusAggregator + EpisodeProcessManager + EpisodeStateChangedEvent). The developer's
code was structurally correct; all build errors were zero. The batch required two dev-lead-level
corrections before tests passed.

---

## Test Results

**Before corrections:** 7 failures (4 episode + 3 pre-existing)  
**After corrections:** 3 failures (3 pre-existing only)

Pre-existing failures confirmed unchanged:
- `ClusterMasterArchiveTests.CancelOperation_CancelsActiveCts`
- `ClusterMasterFanOutTests.PayloadJson_PopulatedFromClusterOpRequest`
- `ClusterMasterPrefetchTests.PrefetchScenario_WhenGatewaySucceeds_PrefetchFilesIsFanOutAfterCompletion`

---

## Corrections Applied by Dev Lead

### Correction 1 (P1 Bug Fix): Wrong JSON key in episode test PayloadJson

**Files:** `ClusterMasterEpisodeTests.cs`, `EpisodeProcessManagerTests.cs`

Test payloads used `"Mode":"Start"` but `ManageEpisodePayloadDto` only has the property
`IsStart` (bool). `OrchestrationJsonOptions.Default` has `PropertyNameCaseInsensitive=true` but
`"Mode"` is not a field on the DTO at all, so `JsonSerializer` ignores it and leaves
`dto.IsStart=false` (C# default). This caused `ProcessManageEpisodeIntent` to plan a
`StopEpisode` fan-out instead of `StartEpisode`, failing all Start-episode test assertions.

**Fix applied:**
- `"Mode":"Start"` -> `"IsStart":true` in all Start-episode payloads (4 in
  `ClusterMasterEpisodeTests.cs`, 2 in `EpisodeProcessManagerTests.cs`)
- `"Mode":"Stop"` -> `"IsStart":false` in StopEpisode payload (1 in
  `EpisodeProcessManagerTests.cs`)
- The `ManageEpisode_BadPayload_Rejected_NoStartEpisodeFanOut` test payload left unchanged;
  it intentionally omits `EpisodeId` to test rejection -- the `Mode` key irrelevance does not
  affect that test path.

### Correction 2 (P1 Test Logic Bug): ClusterOpCompletedEvent read after buffer cleared

**Files:** `ClusterMasterEpisodeTests.cs`

Two tests (`StartEpisode_NakFromNode_AbortsPendingTask_ActiveEpisodesUnchanged` and
`StartEpisode_AllAcks_EmitsSysOpStatusSuccess`) called `bus.ReadManaged<ClusterOpCompletedEvent>()`
from the READ buffer AFTER a `bus.SwapBuffers()` that had already moved that buffer to WRITE and
cleared it. `ManagedEventStream<T>.Swap()` clears the old front buffer (old READ becomes new
WRITE, wiped). The `ClusterOpCompletedEvent` published by `ClusterMaster.Tick()` was present in
the READ buffer for one frame, then cleared when the test called one more swap to bring
`EpisodeStateChangedEvent` into READ.

**Fix applied:** Reordered assertions so `ClusterOpCompletedEvent` is read immediately after the
swap that brings it into the READ buffer, before `episodeMgr.Tick()` / final swap consume that
buffer slot.

---

## Task Outcomes

### TASK-S002 (corrective): StorageProcessManager unit tests

- `StorageProcessManagerTests.cs`: 3 tests written (SC1 real-file gateway, SC2/SC3 no-NAS-dir).
- Tests pass. DEBT-01 resolved.

### TASK-S003: EpisodeConsensusAggregator and EpisodeProcessManager

- `EpisodeConsensusAggregator.cs`: implemented, registered for both `StartEpisode` and
  `StopEpisode`.
- `EpisodeProcessManager.cs`: implemented, wired in `OrchestratorSubsystem.Update()`.
- `EpisodeStateChangedEvent` added with `[EventId(9018)]`.
- `ClusterOpIntent` renumbered from 9018 to 9019 to accommodate the new event ID -- APPROVED.
- `ClusterMaster._activeEpisodes` and `ActiveEpisodes` property removed -- APPROVED.
- `ClusterScenarioPanel` updated to use `_uiCache.ActiveEpisodes` directly -- APPROVED.
- All 4 ClusterMasterEpisodeTests and 3 EpisodeProcessManagerTests pass after corrections.

---

## Open Items Carried Forward

| ID | Priority | Description |
|---|---|---|
| DEBT-02 | P2 | `_pendingSerializeTasks`, `SerializeLocalTask`, `HandleSerializeLocalCompletion` still in `ClusterMaster` (ExportArchive path). Addressed in TASK-P001. |
| DEBT-03 | P2 | 3 pre-existing test failures (Archive, FanOut, Prefetch). Addressed in TASK-P002 / TASK-T001. |

DEBT-01 is **resolved** by BATCH-02.

---

## Decision

**APPROVED.** BATCH-02 code ships. Proceed to BATCH-03 (TASK-T000).
