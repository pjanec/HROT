# BATCH-03 Report

**Batch:** BATCH-03  
**Developer:** Coder Sub-agent (GitHub Copilot)  
**Date:** 2025-07-14  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| CMC-S006 | ✅ Done | ClusterSlave fully reads/publishes via FdpEventBus |
| CMC-S007 | ✅ Done | IOrchestrationTransport deleted; zero C# references remain |

---

## 🧪 Testing Results

**Unit Tests Passed:** 487 / 489  
**Integration Tests Passed:** 41 / 43

| Test Suite | Result | Notes |
|---|---|---|
| FDP.Toolkit.Orchestration.Tests | 29 / 29 ✅ | Includes 4 new CMC-S006 tests |
| Hrot.Orchestrator.Tests | 67 / 67 ✅ | |
| Hrot.SimHost.Tests | 391 / 393 ⚠️ | 2 pre-existing failures (same as BATCH-02) |
| Hrot.SimHost.Integration.Tests | 36 / 38 ⚠️ | 1 pre-existing TraceLogging failure; 1 flaky DDS contention |
| Hrot.Orchestrator.Integration.Tests | 5 / 5 ✅ | |

**Pre-existing failures (unchanged from BATCH-02):**
- `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose`
- `SimHostTimeSyncTests.SimHost_BroadcastsTimePulse_PerTick`
- `TraceLoggingTests.SpawnVehicle_EmitsTraceSequence` (integration)
- `EntityLifecycleIntegrationTests.DomainIsolation_Domain0Spawn_DoesNotAffectDomain10` (flaky DDS contention — passes in isolation)

**Key Test Scenarios Verified:**
- [x] `ClusterSlave_BusDispatch_CallsHandlerWhenIntentOnBus` — intent on bus → handler invoked
- [x] `ClusterSlave_BusDispatch_PublishesNodeOpCompletedEvent` — NodeOpCompletedEvent appears on bus with `IsParticipating=true`
- [x] `ClusterSlave_PublishesNodeHeartbeatEvent_AfterOneSecond` — NodeHeartbeatEvent emitted at 1 Hz with correct NodeId
- [x] `ClusterSlave_NullBus_DoesNotThrowOnTick` — null bus is safe
- [x] `ClusterSlaves_PublishNodeHeartbeatEvents_ToBus` (integration) — two independent slaves both emit heartbeats
- [x] All ReferenceHandler tests pass with new transport-free constructors
- [x] ReferenceArchiveHandler manifest returned via `PrepareAsync` result, captured in `NodeOpCompletedEvent.ResultPayload`

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

1. **`OrchestrationStatusCode.Failure` did not exist.** The batch instructions used `OrchestrationStatusCode.Failure` in the ClusterSlave deferred-faulted path, but the enum only had `Success = 0` and `Participating = 1`. Added `Failure = 13` to `OrchestrationStatusCode.cs`.

2. **ReferenceArchiveHandler.ResultPayload timing.** The spec required ClusterSlave to include the handler's result in `NodeOpCompletedEvent.ResultPayload` via `prepareTask.Result`. However, the archive handler was building the file manifest inside `Commit`, which runs after `prepareTask` completes. Resolved by moving all file-finding and JSON manifest building into `PrepareAsync`; `Commit` became a no-op. This matches the spec's intent and the existing PrepareAsync-returns-object? contract.

3. **ClusterSlaveHeartbeatTests used DDS indirectly.** The existing test verified heartbeat delivery by looking at `ClusterMaster.NodeRoster` — a DDS-backed path that no longer exists after transport removal. Rewrote the test per batch instructions to directly observe `NodeHeartbeatEvent` on `FdpEventBus`.

4. **Stale XML doc comments referencing IOrchestrationTransport.** After deleting the interface, two doc-comment `<remarks>` blocks in `ClusterSlave.cs` and `ReferenceLiveLoadHandler.cs` still named it. Updated them to reference `FdpEventBus`/`ClusterSlave.DispatchIntent`.

5. **Multiple test files still used old handler constructors.** After removing transport parameters from the 6 handler constructors, 7 test files in `Hrot.SimHost.Tests` and one integration test file still passed `transport: null, nodeId: 1`. Updated all call sites.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

1. **`ClusterSlave` has two constructors with overlapping responsibilities.** The test-only constructor `ClusterSlave(FdpEventBus? eventBus = null)` uses a hard-coded `nodeId = 0` and `subsystemName = "TestNode"`. These fixed defaults are invisible at the call site and have caused test confusion before. A builder pattern or dedicated `ClusterSlaveTestHarness` factory would be cleaner.

2. **`NodeOpCompletedEvent.ResultPayload` is `object?`.** This bypasses the type system. Once Phase 5 translators are in place, a closed union/discriminated-union approach (or at least a documented set of known payload types per `ClusterOp`) would reduce the risk of silent type mismatches between producer and consumer.

3. **`ReferencePrefetchHandler.Commit` still has no functional body** (it just logs). The handler accumulates state in `PrepareAsync` but never clears it. It isn't harmful now because `ClusterSlave` only dispatches one intent at a time before polling again, but if the state machine ever permits concurrent ops this handler will silently replay stale data.

4. **`ReferenceArchiveHandler` returns a JSON manifest but there is no consumer code yet.** The `ResultPayload` travels through `NodeOpCompletedEvent` but nothing on the master side unpacks it. This is by design (Phase 5), but it may cause confusion when the next batch author looks at the event and finds an untyped `object?` with a JSON string in it with no documentation.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

1. **Handler nodeId parameters removed where they were exclusively used for transport.** The spec said "remove transport from handlers." Some handlers stored `nodeId` only to pass to `transport.PublishAck(nodeId, ...)`. Once transport was gone, the field served no purpose. I removed it from those constructors rather than leaving a dead field. Considered leaving it for tracing but the Ids are readily available from the bus event when needed.

2. **`IOrchestrationTransport.cs` and `DdsOrchestrationTransport.cs` deleted entirely.** The spec called for deletion. Some handlers had thin wrappers around transport calls that could have been replaced with no-ops, but since the batch explicitly required zero C# references and file deletion, full removal was correct.

3. **`ReferenceReplayLoadHandler.PrepareAsync` for `PrepareReplay` now returns `(object?)maxNetworkId`.** The handler's replay logic already computed `maxNetworkId` from the archive; previously it was published via transport. The natural place for its return is `PrepareAsync` result captured by ClusterSlave. This made the handler consistent with the other handlers and kept the ResultPayload pattern uniform.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

1. **Heartbeat timer on first tick.** `_lastHeartbeatUtc` was initialized to `DateTime.MinValue`, so the very first `Tick()` always fires a heartbeat (the delta is always > 1 second). This is actually desirable for faster startup status but was not mentioned in the spec.

2. **`ConsumeManaged<T>` returns `IEnumerable<T>`.** The `foreach` loop in `Tick()` will process all intents queued between ticks in a single tick. This is correct behaviour for burst scenarios but means if two intents arrive in one tick the handler is called twice with no heartbeat in between. The spec did not address this case but the existing `_pendingWork` deferred-op mechanism handles it correctly since only one async op can be in-flight at a time.

3. **`ExConSubsystem` did not use `SubsystemName` as a const** — the new `ClusterSlave("ExCon")` string literal is a magic string. Flagged as tech debt.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

1. **`PublishManaged` allocates a managed wrapper per event.** Both `NodeHeartbeatEvent` (1 Hz per node) and `NodeOpCompletedEvent` (one per op) use `PublishManaged` because they carry `string`/`object?` fields. At simulation scale (dozens of nodes × 1 Hz = ~60 managed allocs/sec) this is negligible. If the heartbeat ever needs to run at higher frequency, migrating `SubsystemName` to a pre-encoded byte field and using `Publish<T>` would eliminate the allocation.

2. **`ReferenceArchiveHandler.PrepareAsync` blocks on `Task.Run` file enumeration.** The file-scan is now in PrepareAsync (correct), which runs off the main tick thread. No regression introduced; note that this IO-bound path has no timeout; a slow NFS mount will stall the op indefinitely.

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] **Tech Debt (P3):** `ExConSubsystem` uses a magic string `"ExCon"` for subsystem name — should use a shared constant.
- [ ] **Tech Debt (P3):** `ClusterSlave` test constructor uses hard-coded `nodeId=0`, `subsystemName="TestNode"` — consider a named factory to make defaults explicit.
- [ ] **Phase 5 (next batch):** `NodeOpCompletedEvent.ResultPayload` is produced but not consumed by ClusterMaster — Phase 5 translators need to unpack and act on it.
- [ ] **Phase 5 (next batch):** DDS-side heartbeat/status forwarding path is temporarily disabled. Phase 5 should re-introduce a thin DDS forwarder that subscribes to `NodeHeartbeatEvent` on the bus and re-publishes to DDS for ClusterMaster's roster.
- [ ] **Pre-existing test failures** (tracked since BATCH-02, unchanged):
  - `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose`
  - `SimHostTimeSyncTests.SimHost_BroadcastsTimePulse_PerTick`
  - `TraceLoggingTests.SpawnVehicle_EmitsTraceSequence`
