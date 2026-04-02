# BATCH-03 Review

**Batch:** BATCH-03  
**Tasks:** CMC-S006, CMC-S007  
**Reviewer:** Dev Lead Agent  
**Date:** 2025-07-14  
**Decision:** ✅ APPROVED

---

## Scope Verification

### CMC-S006 — ClusterSlave Event Bus Integration
- ✅ `NodeHeartbeatEvent` added to `ClusterCqrsEvents.cs` (EventId 9014, `DataPolicy.NoRecord`, `string SubsystemName` field, correctly uses `PublishManaged`)
- ✅ `OrchestrationStatusCode.Failure = 13` added
- ✅ `ClusterSlave.Tick()` publishes `NodeHeartbeatEvent` at 1 Hz via `_eventBus?.PublishManaged`
- ✅ `ClusterSlave.Tick()` drains `ExecuteNodeOpIntent` via `_eventBus.ConsumeManaged<ExecuteNodeOpIntent>()`
- ✅ `DispatchIntent()` publishes `NodeOpCompletedEvent` with `IsParticipating=true` on both sync-success and sync-faulted paths
- ✅ Deferred async-prepare path (pending task) resolves to `NodeOpCompletedEvent` with `ResultPayload = prepareTask.Result` on success, `Failure` on fault
- ✅ `_transport` field fully removed; old production constructor removed
- ✅ 4 new unit tests added — all check real values (`NodeId`, `StatusCode`, `IsParticipating`, `SubsystemName`), not just compilation

### CMC-S007 — Delete IOrchestrationTransport
- ✅ `IOrchestrationTransport.cs` deleted
- ✅ `DdsOrchestrationTransport.cs` deleted
- ✅ `DdsOrchestrationTransportTests.cs` deleted
- ✅ Zero `IOrchestrationTransport` references in any `.cs` file (grep confirmed)
- ✅ All 6 reference handlers updated — transport/nodeId constructor parameters removed
- ✅ All 4 composition roots updated (NodeBootstrapper, CgfApplication, IgApplication, ExConSubsystem)

---

## Test Quality Assessment

| Suite | Result | Assessment |
|---|---|---|
| FDP.Toolkit.Orchestration.Tests | 29/29 ✅ | **Good.** 4 new tests check actual event field values. |
| Hrot.Orchestrator.Tests | 67/67 ✅ | No regressions. |
| Hrot.SimHost.Tests | 391/393 ⚠️ | 2 pre-existing failures (unchanged from BATCH-02). |
| Hrot.SimHost.Integration.Tests | 36/38 ⚠️ | 1 pre-existing TraceLogging failure; 1 flaky DDS contention (passes in isolation). |
| Hrot.Orchestrator.Integration.Tests | 5/5 ✅ | |

**Test assertion quality: PASS.** New tests assert `Assert.Equal(txId, ...)`, `Assert.Equal(nodeId, ...)`, `Assert.True(IsParticipating)`, `Assert.Equal(Success, StatusCode)` — checking behaviour and values, not just that the code compiles.

`ClusterSlaveHeartbeatTests` correctly rewritten to observe `NodeHeartbeatEvent` on `FdpEventBus` directly. Per design, the DDS forwarding path is deferred to Phase 5.

---

## Design Alignment

✅ **Strict alignment with DESIGN.md §3.4 (ClusterSlave role):**
- ClusterSlave is a pure domain state machine. It consumes typed bus events and publishes typed bus events; no DDS, no JSON.
- `PrepareAsync → ResultPayload` flow implemented correctly: ReferenceArchiveHandler builds manifest in `PrepareAsync` (returns `object?`); ClusterSlave captures it and puts it in `NodeOpCompletedEvent.ResultPayload`.
- Heartbeat uses `Stopwatch` (correct — wall-clock independent).

✅ **No silent error swallowing:**
- Faulted prepare tasks log with `FdpLog.Error` and emit a `Failure` completion event.
- Duplicate intent dedup uses `_seenTransactionIds` set and logs at `Debug` level — no silent drops beyond what the spec requires.

---

## Issues Found

**None blocking approval.**

Tech debt logged in DEBT-TRACKER.md:
- DEBT-002 (P3): magic string `"ExCon"` in ExConSubsystem
- DEBT-003 (P3): ClusterSlave test ctor hard-coded defaults
- DEBT-004 (P2): `object? ResultPayload` bypasses type system — needs closed payload taxonomy post-Phase 5
- DEBT-005 (P3): `ReferencePrefetchHandler` accumulates prepare state but never clears it
- DEBT-006 (P3): `ReferenceArchiveHandler.PrepareAsync` has no timeout on file-scan

---

## Suggested Git Commit Message

```
CMC-S006/S007: ClusterSlave event bus integration; delete IOrchestrationTransport

Phase 3 complete. ClusterSlave is now a pure domain state machine:
- Tick() publishes NodeHeartbeatEvent at 1 Hz via FdpEventBus
- Tick() drains ExecuteNodeOpIntent via ConsumeManaged; dispatches to handlers
- DispatchIntent() publishes NodeOpCompletedEvent (success/failure, sync/deferred)
- OrchestrationStatusCode.Failure = 13 added
- IOrchestrationTransport deleted; zero references in any .cs file
- DdsOrchestrationTransport deleted
- All 6 reference handlers and 4 composition roots updated
- ClusterSlaveHeartbeatTests rewritten for bus-based assertion (DDS forwarding deferred to Phase 5)

Tests: Orchestration 29/29, Orchestrator 67/67, SimHost 391/393 (2 pre-existing),
       Integration 36/38 (1 pre-existing + 1 flaky DDS contention)
```

---

## Next Batch Recommendation

**BATCH-04** should implement **Phase 4 — ClusterMaster Event Bus Integration** (CMC-S008, CMC-S009, CMC-S010):
- Remove DDS readers from ClusterMaster (`ConsumeManaged<ExecuteClusterOpIntent>` + specific intents)
- Remove DDS writers from ClusterMaster (`PublishManaged<ClusterOpCompletedEvent>` + `NodeOpCompletedEvent` fan-out)
- Remove all `JsonDocument.Parse` / `PayloadJson` parsing

**Recommended carry-in tech debt for BATCH-04:** DEBT-002 (P3, magic string "ExCon") and DEBT-003 (P3, test ctor defaults) — both are small and can be fixed alongside the ClusterMaster work.
