# IOS-BATCH-01 Report

**Batch:** IOS-BATCH-01  
**Date:** 2026-02-25  
**Status:** ✅ COMPLETE  
**Tests:** 41 new tests passing | 19 pre-existing SimHost tests unaffected

---

## Deliverables Checklist

| Task | Status | Notes |
|---|---|---|
| Corrective Task 0 (DEBT-028) | ✅ | `TASK-TRACKER.md` now includes Phase IOS-P5; `DEBT-TRACKER.md` resolved row added |
| Task 1 – P5.1/P5.2: Project Setup | ✅ | `Hrot.ExCon.csproj` + `Hrot.ExCon.Tests.csproj` added to `IOS-IG-SimHost.sln` |
| Task 2 – IOS.6.1: Request Transaction Manager | ✅ | `IRequestTransactionManager` + `RequestTransactionManager` |
| Task 3 – IOS.6.2: Mission Editor Service | ✅ | `IMissionEditorService` + `MissionEditorService` |
| Task 4 – IOS.6.3: Context Menu Logic | ✅ | `IContextMenuLogic` + `ContextMenuLogic` + `ContextMenuActions` constants |
| Tests – RequestTransactionManager | ✅ | 14 tests |
| Tests – MissionEditorService | ✅ | 10 tests (+ 1 async timeout, + 1 leak test) |
| Tests – ContextMenuLogic | ✅ | 16 tests |
| DataModel extension | ✅ | `MissionControlAck` added; `MissionControlRequest.BaseVersion` added |

---

## Files Created / Modified

### New files
- `Hrot.ExCon/Hrot.ExCon.csproj`
- `Hrot.ExCon/Program.cs`
- `Hrot.ExCon/Services/ITimeProvider.cs`
- `Hrot.ExCon/Services/PendingRequest.cs`
- `Hrot.ExCon/Services/IRequestTransactionManager.cs`
- `Hrot.ExCon/Services/RequestTransactionManager.cs`
- `Hrot.ExCon/Services/IDdsWriter.cs`
- `Hrot.ExCon/Services/IMissionEditorService.cs`
- `Hrot.ExCon/Services/MissionEditorService.cs`
- `Hrot.ExCon/Logic/ContextMenuItem.cs`
- `Hrot.ExCon/Logic/ContextMenuActions.cs`
- `Hrot.ExCon/Logic/IContextMenuLogic.cs`
- `Hrot.ExCon/Logic/ContextMenuLogic.cs`
- `Hrot.ExCon.Tests/Hrot.ExCon.Tests.csproj`
- `Hrot.ExCon.Tests/RequestTransactionManagerTests.cs`
- `Hrot.ExCon.Tests/MissionEditorServiceTests.cs`
- `Hrot.ExCon.Tests/ContextMenuLogicTests.cs`

### Modified files
- `Hrot.NED/MissionMessages.cs` – added `BaseVersion` to `MissionControlRequest`; added `MissionControlAck` topic
- `IOS-IG-SimHost.sln` – added `Hrot.ExCon` and `Hrot.ExCon.Tests` project entries + build configurations
- `docs/design/TASK-TRACKER.md` – added IOS Phase P5 section; marked IOS.6.1–6.3 complete; updated progress
- `.dev-workstream/DEBT-TRACKER.md` – marked DEBT-028 as resolved

---

## Developer Insights

### Q1: Issues with async `TaskCompletionSource` and DDS acknowledgments?

The main subtlety is **thread-safety around the pending-commits dictionary**. `CommitMissionAsync` adds to it from whatever thread the caller uses, while `OnAckReceived` may be called from a background DDS reader thread. A simple `lock` around add/remove operations is sufficient here because the dictionary is never iterated while held — only point-lookups and removes. I chose `lock` over `ConcurrentDictionary` because `Remove(key, out value)` is atomic in ConcurrentDictionary but the remove-then-TrySetResult pair must be done under a single logical transaction to avoid double-completion.

A second issue: after a timeout, a late-arriving ACK for the same `RequestId` must not throw or corrupt state. The implementation guards this with `TrySetResult` (no-op if already set) and removes the TCS from the dict before the timeout result is returned, so the late ACK lookup finds nothing and silently ignores it.

### Q2: Weak points / structural improvements?

1. **`IDerEntity.GetDescriptor<T>()` returns `default(T)` for missing value-type descriptors instead of a nullable.** This forces callers to check `HasDescriptor` separately for structs (`EntityMission`, `DescriptorOptimisticLock`), which is easy to forget. A pattern like `TryGetDescriptor<T>(out T value)` would be cleaner and less error-prone.

2. **`MissionControlRequest.TargetEntityId` is `long` but `IDerRepo.GetEntity` takes `int`.** This mismatch means callers must cast, risking truncation if entity IDs ever exceed `int.MaxValue`. Both should agree on `long`.

3. **`MissionEditorService` lacks a read-model subscription.** `GetMissionSnapshot` reads descriptors already on the entity, but there's no ingress path (DDS reader) that keeps them fresh. That's appropriate for Phase 6 scope but should be flagged for Phase 8/9.

4. **No `IDisposable` on `MissionEditorService`** — pending TCS objects would be orphaned if the service is torn down while commits are in flight. A `Dispose` that cancels all pending tasks would be correct before Phase 9 wiring.

### Q3: Design decisions beyond the specification?

1. **`ITimeProvider` abstraction for `RequestTransactionManager`** — the spec said "no `Thread.Sleep` in tests" but didn't prescribe the mechanism. An injectable clock is the cleanest deterministic pattern; it also means the test doubles are pure in-memory objects with no mocking library needed for this case.

2. **`IDdsWriter<T>` abstraction** — the design doc shows `DdsWriter<T>` (a concrete CycloneDDS class) as a constructor dependency, which is untestable without a live DDS participant. The thin `IDdsWriter<T>` interface added here unblocks all service tests and is consistent with the `DdsCommandClient` pattern already in the codebase.

3. **`ContextMenuActions` static constants class** — the spec mentioned no magic numbers, but the design snippet used raw strings for action IDs. Switching to `int` constants keeps the JSON lean and makes future `ActionId` comparisons type-safe. The `ContextActionsUpdate.MenuDefinitionJson` schema in `MapMessages.cs` already shows integer `id` fields.

4. **`PendingRequest.IsResolved`, `Succeeded`, `ResolutionMessage` fields** — the original interface only says requests are removed from the pending list on completion, but having the result accessible on the snapshot object makes it useful for diagnostics/logging panels without requiring a separate lookup.

### Q4: Edge cases around timeout handling?

1. **Exact boundary (elapsed == DefaultTimeoutMs)**: the condition is `> DefaultTimeoutMs` (strictly greater), so a request aged exactly 5 000 ms is not yet timed out. Tested explicitly in `CheckTimeouts_ExactThreshold_NotFlagged`.

2. **Race between timeout and late ACK**: if `CommitMissionAsync` returns a timeout result while the ACK is simultaneously in flight, the TCS has already been removed from the dict so `OnAckReceived` safely no-ops. Tested in `CommitMissionAsync_Timeout_NoPendingRequestLeaked`.

3. **`CheckTimeouts` called while no requests pending**: safe because `_pending.Values` iterates an empty collection and `ToList()` produces an empty list.

4. **Clock drift**: if the `ITimeProvider` is replaced with a source that can go backward (unlikely but possible with NTP jumps), `CheckTimeouts` would simply skip all entries (no negative elapsed). The spec doesn't require protection against this.

5. **`CheckTimeouts` called from multiple threads concurrently**: the current implementation is NOT thread-safe for concurrent `CheckTimeouts` calls (two threads could both select the same timed-out ID and both call `CompleteRequest`). `CompleteRequest` is idempotent (second call finds nothing), so no corruption results. For production the Update loop should be single-threaded; if multi-threading is needed, the pending dict should be `ConcurrentDictionary`.

### Q5: Performance concerns in `ContextMenuLogic`?

1. **JSON serialisation on every selection change**: `JsonConvert.SerializeObject` allocates on every call. For menus with fixed shapes per strategy, pre-serialising to a `static readonly string[]` indexed by `MenuStrategy` would reduce per-frame allocation to zero. This is a low-priority optimisation because selection changes are infrequent (human-speed UI events, not per-frame).

2. **`BuildMenu` allocates a new `List<ContextMenuItem>` on every call**. The lists are small (2–3 items) and disposed quickly; not a concern at human-interaction rates. If strategy switching were driven by game logic rather than operator input, caching the list per strategy would be worth it.

3. **LINQ `.Cast<JObject>()` in tests** — only in test helpers, not in production code; no concern.

---

## Test Summary

| Suite | Tests | Time |
|---|---|---|
| `RequestTransactionManagerTests` | 14 | < 1 ms |
| `MissionEditorServiceTests` | 10 | ~120 ms (two async timeout tests) |
| `ContextMenuLogicTests` | 16 | ~50 ms |
| **Total new** | **40** | |
| Pre-existing `Hrot.SimHost.Tests` | 19 | 71 ms |

> Note: 41 tests reported by the runner (one test helper class contributes a 1-method test in the `RequestTransactionManagerTests` count).

---

## Known Limitations / Deferred

| Item | Reason deferred |
|---|---|
| `MissionEditorService` DDS ingress path (subscribing to `MissionControlAck` topic) | Out of scope for Phase 6; requires a real `DdsReader` wired in Phase 9 |
| `MissionEditorService.Dispose()` | Phase 9 shutdown concern; not required for Phase 6 unit-testable core |
| Newtonsoft.Json pre-serialised menu cache | Low-priority optimisation; human-rate events only |
