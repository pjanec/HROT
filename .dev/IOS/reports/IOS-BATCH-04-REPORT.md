# IOS-BATCH-04 — Report

**Date:** 2026-02-25  
**Batch:** IOS-BATCH-04  
**Status:** ✅ Complete — 205/205 tests passing, 0 build errors, 0 xUnit warnings

---

## Summary of Completed Work

### Corrective Tasks

| ID | Description | Status |
|---|---|---|
| DEBT-031 | `MissionEditorService` ACK ingress path | ✅ Resolved |
| DEBT-032 | `MissionEditorService` IDisposable | ✅ Resolved |
| DEBT-033 | `OrbatPanel` O(n²) → O(n) traversal | ✅ Resolved |

### Integration & Workflow Tests

| Task | Class | Tests Added |
|---|---|---|
| IOS.9.1 | `StandaloneIosTests` | 9 |
| IOS.9.2 | `IosIgIntegrationTests` | 8 |
| IOS.9.3 | `IosSimHostIntegrationTests` | 6 |
| IOS.9.4 Scenario 4 | `FullStackWorkflowTests` | 4 |
| IOS.9.4 Scenario 5 | `ConflictDetectionWorkflowTests` | 6 |

**Totals:** 33 new tests added. Existing 172 tests remain green. Full suite: **205 passed, 0 failed**.

---

## Implementation Notes

### DEBT-031 — ACK Ingress Path

`MissionEditorService` now accepts an optional `IEventQueue<MissionControlAck>? ackQueue`
parameter in its constructor and implements `IIngressHandler`. The `Poll()` method drains
the queue and forwards each item to the existing `OnAckReceived` handler. When `ackQueue`
is `null` (the original contract), `Poll()` is a no-op, preserving backwards compatibility
with all existing unit tests.

Registering the service in `IosLogic`'s `ingressHandlers` list means the main game loop
drives ACK resolution — no background threads, no timers, no busy-waiting.

### DEBT-032 — IDisposable

`MissionEditorService` now implements `IDisposable`. On teardown it captures all entries
from `_pendingCommits` under the lock, clears the dictionary, then calls
`TrySetResult(new MissionCommitResult { Success=false, ErrorMessage="Service disposed" })`
on each orphaned TCS. `TrySetResult` (not `TrySetCanceled`) was used deliberately: callers
receive a typed failure result rather than an `OperationCanceledException`, which would
force every callsite to add a catch block. The guard `if (_disposed) return;` makes
`Dispose()` idempotent.

`IMissionEditorService` was extended to inherit `IDisposable`. Moq auto-generates the
`Dispose()` stub, so no existing test required changes.

### DEBT-033 — OrbatPanel O(n²) → O(n)

A new private static method `BuildChildrenLookup(IDerRepo repo)` was added. It performs
a single O(n) pass over all entities to build a `Dictionary<int, List<IDerEntity>>`
keyed by `CommanderId`. `GetVisibleNodes` calls this once, then passes the dictionary
into `CollectNodes` which performs O(1) lookups via `TryGetValue` instead of the previous
`FindChildren` full-scan at each tree level.

The public `FindChildren(int parentId, IDerRepo repo)` method was left unchanged for
backwards-compatibility with the existing `OrbatPanelTests` that call it directly.

---

## Developer Insights

### Q1: DDS Domain Isolation for Integration Tests

No live DDS participants were created in any test. All DDS interaction is simulated using:

- **`CapturingWriter<T>`** — an in-memory `IDdsWriter<T>` that appends to a `List<T>`.
  This replaces all outgoing writers (config, create-entity, mission-control, context-menu).
- **`ConcurrentEventQueue<T>`** — the existing `IEventQueue<T>` implementation backed by
  `ConcurrentQueue<T>`. This replaces all incoming DDS queues (clicks, selections, ACKs).

Because no sockets, DDS domains, or OS resources are allocated, there is nothing to leak
between tests. Parallelism isolation is still enforced via:

```csharp
[CollectionDefinition("Integration", DisableParallelization = true)]
```

applied to all new integration and workflow test classes. This prevents shared in-process
state (the `DerRepo`, `InteractionPanel`, etc.) from being exercised concurrently by
different test classes, matching the recommended pattern from the batch instructions.

### Q2: O(n²) Removal Confirmation

**Before (O(n²)):**
`CollectNodes` called `FindChildren(entity.EntityId, repo)` for every node visited.
`FindChildren` performs a full linear scan of `repo.AllEntities`. For a tree with `N`
entities and `D` levels of depth, this is O(N × visited_nodes) ≈ O(N²) in a balanced tree.

**After (O(n)):**
`BuildChildrenLookup` scans `repo.AllEntities` exactly once, building a dictionary.
`CollectNodes` then performs a single `TryGetValue` (O(1)) per visited node. Total cost
for `GetVisibleNodes` is now O(N) regardless of tree shape or depth.

**Verification:** The existing `OrbatPanelTests` (9 tests covering multi-level hierarchy,
filter, collapse, and root detection) all continue to pass, confirming behavioural
equivalence of the refactored traversal.

### Q3: Structural Issues During Full Stack Testing

Two structural discoveries required test corrections:

1. **`TaskCreationOptions.RunContinuationsAsynchronously` timing:**
   `CommitMissionAsync` creates its TCS with `RunContinuationsAsynchronously`, which means
   that after `Poll()` calls `TrySetResult()`, the state-machine continuation is scheduled
   on the thread pool rather than running inline. Consequently `commitTask.IsCompletedSuccessfully`
   was `false` immediately after `Poll()`, even though the result had been set. Tests that
   checked `IsCompletedSuccessfully` synchronously were removed; tests now use `await commitTask`
   directly, which correctly suspends until the continuation has run.

2. **One-frame log drain lag:**
   `IosLogic.Update()` calls `DrainPendingLogs()` at the **start** of the frame (step 2),
   before `ProcessClickEvents()` and `ProcessSelectionEvents()` (steps 3–4). Log entries
   enqueued by event-processing are therefore only visible in `InteractionPanel.Entries`
   after the *next* call to `Update()`. Tests asserting log content after a click or
   selection event now call `Update()` twice: once to process the event and once to drain
   the resulting log entry.

Neither issue required changes to production code — both were test-authoring assumptions
that did not match the actual single-threaded, frame-ordered execution model.

### Q4: Orphaned TCS Complexity During Dispose

No unexpected complexities arose. The key design decisions were:

- **`TrySetResult` over `TrySetCanceled`:** Calling `TrySetCanceled` would cause
  `OperationCanceledException` to propagate to all `await`ers of orphaned commits, forcing
  callsites to add catch blocks. Using `TrySetResult` with `Success=false` keeps the error
  path uniform with timeout and version-conflict failures.
- **Idempotency via `_disposed` guard:** `Dispose()` checks `if (_disposed) return;` on
  entry. The test `Dispose_IsIdempotent_DoesNotThrow` confirms multiple calls are safe.
- **Lock scope:** The pending-commits dictionary is captured into a local `List` under the
  lock, then the lock is released before calling `TrySetResult`. This avoids holding the
  lock while running continuations (which could schedule work on the thread pool and re-enter).
- **Multiple pending commits:** `Dispose_MultiplePendingCommits_AllResolvedWithFailure`
  confirms that starting three concurrent commits and then disposing the service resolves
  all three with `Success=false` and the `"Service disposed"` message.

---

## Test Count Verification

```
dotnet test Hrot.ExCon.Tests → Passed: 205, Failed: 0, Skipped: 0
dotnet test Hrot.SimHost.Tests → Passed: 19, Failed: 0, Skipped: 0
```
