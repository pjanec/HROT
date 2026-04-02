# BATCH-07: Tech Debt — ClusterSlave Multi-Intent and Dedup Fix

**Batch Number:** BATCH-07  
**Tasks:** DEBT-007 (P2), DEBT-004 (P2-followup), DEBT-008 (P3)  
**Phase:** Tech Debt Cleanup  
**Estimated Effort:** 6–10 hours  
**Priority:** HIGH — resolves 2 pre-existing test failures in AllSubsystems  
**Dependencies:** BATCH-06 complete

---

## 📋 Context

After BATCH-06, all CMC tasks are complete. This batch resolves the highest-priority technical debt. The main goal is to fix **DEBT-007**, which causes `AllSubsystemsClusterTransitionTests` to fail. Understanding the root cause is critical before touching any code.

### Key Architecture Facts (Must Know)

1. **`FdpEventBus` double-buffer model:**
   - `PublishManaged(event)` → writes to **write buffer**  
   - `SwapBuffers()` → **write buffer becomes read buffer; OLD READ BUFFER IS CLEARED**  
   - `ConsumeManaged<T>()` → returns ALL items from read buffer (NON-DRAINING — same items returned on every call until next SwapBuffers)

2. **ClusterMaster fan-out pattern (bus mode):**
   - In `PlanTransitionState`, ClusterMaster calls `FanOutNodeOp(PrepareXxx, tx.TransactionId, ...)` followed immediately by `FanOutNodeOp(CommitState, tx.TransactionId, ...)` **in the SAME tick**
   - For multi-step trajectories (e.g., Idle→LoadingLive→OperatingLive), ALL steps' PrepareXxx AND CommitState are published in ONE ClusterMaster.Tick() call
   - This means the read buffer can contain: `[PrepareLive, CommitState(LoadingLive), FinalizeLive, CommitState(OperatingLive)]`

3. **`ClusterSlave.Tick()` current behavior (buggy):**
   - Iterates `ConsumeManaged<ExecuteNodeOpIntent>()` and breaks as soon as `_pendingPrepare.HasValue`
   - For async handlers: PrepareXxx sets `_pendingPrepare` → break → CommitState(s) remain in read buffer but NOT processed
   - On next `SwapBuffers()`: old read buffer (containing remaining CommitState intents) is CLEARED → **intents permanently lost**

4. **Dedup key (buggy for multi-step):**
   - Current: `HashSet<(Guid TransactionId, NodeOpType Operation)>`
   - For a 2-step trajectory: `(tx1, CommitState=LoadingLive)` and `(tx1, CommitState=OperatingLive)` have the SAME dedup key `(tx1, CommitState)` → **second CommitState is dropped as duplicate**

### Root Cause of Pre-Existing Test Failures

Both `AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate` and `AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle` fail for BOTH reasons above in combination:
1. ClusterSlave breaks after PrepareXxx → CommitState intents lost to SwapBuffers
2. Even if fix (1) is applied, a second CommitState in a multi-step trajectory is dropped by dedup

The `ClusterOpE2eScriptTests` failures may have separate root causes; do not assume this batch will fix them.

---

## ✅ Task 1: DEBT-007 — Fix ClusterSlave Multi-Intent Per Frame

### 1.1 Add Internal Pending Intents Queue

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`

Add a new field for buffering intents that could not be processed due to an active async prepare:

```csharp
// After _seenTransactionIds field:
private readonly System.Collections.Generic.Queue<ExecuteNodeOpIntent> _pendingIntents = new();
```

### 1.2 Fix Dedup Key to Include State Discriminant

Current:
```csharp
private readonly System.Collections.Generic.HashSet<(Guid, NodeOpType)> _seenTransactionIds = new();
```

Change to (3-tuple: adds an int discriminant for CommitState with different target states):
```csharp
private readonly System.Collections.Generic.HashSet<(Guid, NodeOpType, int)> _seenTransactionIds = new();
```

In `DispatchIntent()`, change the dedup key computation:
```csharp
// BEFORE:
var dedupKey = (intent.TransactionId, intent.Operation);

// AFTER:
// CommitState intents for different target states within the same transaction must each be accepted.
// Use DomainPayload (target state int) as a discriminant.  All other intents use -1.
int stateDiscriminant = intent.Operation == NodeOpType.CommitState && intent.DomainPayload is int sd
    ? sd : -1;
var dedupKey = (intent.TransactionId, intent.Operation, stateDiscriminant);
```

### 1.3 Rewrite Tick() Intent Dispatch Section

In `Tick()`, replace the current fragile `foreach`+`break` pattern with a loop that buffers unprocessed intents:

**Current (buggy):**
```csharp
if (_eventBus != null)
{
    foreach (var intent in _eventBus.ConsumeManaged<ExecuteNodeOpIntent>())
    {
        DispatchIntent(intent);
        if (_pendingPrepare.HasValue) break;
    }
}
```

**New (correct):**
```csharp
// Drain deferred intents queued in a previous tick (when async prepare was active).
while (_pendingIntents.Count > 0 && !_pendingPrepare.HasValue)
{
    DispatchIntent(_pendingIntents.Dequeue());
}

// Read new intents from bus.  When async prepare is running, unseen intents
// are queued internally so they survive the next SwapBuffers().
if (_eventBus != null)
{
    foreach (var intent in _eventBus.ConsumeManaged<ExecuteNodeOpIntent>())
    {
        if (_pendingPrepare.HasValue)
        {
            // Async prepare in progress — buffer unseen intents for next tick.
            int sd = intent.Operation == NodeOpType.CommitState && intent.DomainPayload is int v ? v : -1;
            if (!_seenTransactionIds.Contains((intent.TransactionId, intent.Operation, sd)))
                _pendingIntents.Enqueue(intent);
        }
        else
        {
            DispatchIntent(intent);
        }
    }
}
```

**Placement:** This replaces the existing `if (_eventBus != null) { foreach ... }` block at the BOTTOM of `Tick()`. The existing placement of the `_pendingPrepare.HasValue` resolution block ABOVE it is unchanged.

### 1.4 Clear Pending Queue on Faulted Prepare

In the faulted path inside `Tick()` (where `PrepareTask.IsFaulted`), add:
```csharp
_pendingIntents.Clear();  // Discard deferred intents for the failed transaction
```

Place this BEFORE the `return` in the faulted branch.

### 1.5 Update Unit Tests for New Behavior

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/` (create or update existing test)

Add a new test class or extend `ClusterSlaveTests.cs`. Add tests:

```
Test: Queue_Survives_SwapBuffers_When_AsyncPrepareIsActive
  - Create ClusterSlave with one async handler (Task.Delay-based)
  - Bus.PublishManaged(PrepareXxx intent)
  - Bus.PublishManaged(CommitState intent)  ← same tx, same write-then-swap cycle
  - SwapBuffers, Slave.Tick()  ← slave should NOT lose CommitState
  - Resolve async task
  - SwapBuffers, Slave.Tick()  ← CommitState should now be dispatched
  - Assert: LocalStateIdForTest == expected target state

Test: MultiStep_Trajectory_BothCommitStatesApplied
  - Two-step transition: PrepareXxx(LoadingLive) + CommitState(LoadingLive) + FinalizeXxx + CommitState(OperatingLive)
  - All 4 intents in the same write buffer
  - After 3 ticks (with appropriate async task completions): LocalStateIdForTest == OperatingLive

Test: FaultedPrepare_ClearsPendingQueue
  - PrepareXxx intent queued, async prepare faults
  - CommitState is also queued
  - Verify CommitState is NOT dispatched after the faulted prepare
```

**Use `EnqueueIntentForTest` for direct injection in unit tests** (bypasses bus) wherever the test harness doesn't provide a full bus.

---

## ✅ Task 2: DEBT-004 — Document ResultPayload Allowed Types

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`

Add XML documentation clarifying the allowed runtime types for `NodeOpCompletedEvent.ResultPayload`. No code change required — this is a documentation-only clarification.

```csharp
/// <summary>
/// Operation-specific result data.  Known runtime types by operation:
/// <list type="bullet">
///   <item><term><see cref="NodeOpType.SerializeLocal"/></term><description><c>FileManifestResult[]</c> — file paths written by the node</description></item>
///   <item><term>All other operations</term><description><c>null</c></description></item>
/// </list>
/// Translators in the Hrot layer are responsible for casting and serializing this payload.
/// </summary>
public object? ResultPayload;
```

---

## ✅ Task 3: DEBT-008 (P3, optional) — Document DdsIdAllocatorServer Hosting Requirement

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterMaster.cs`

In the bus-mode constructor, add a comment:
```csharp
// NOTE: In bus-mode, DdsIdAllocatorServer is NOT created here. The hosting process
// (e.g. OrchestratorSubsystem) is responsible for creating and ticking the server.
_idAllocatorServer = null!;
```

This is documentation only — no behaviour change.

---

## ✅ Task 4: Update DEBT-TRACKER for Resolved Items

In `DEBT-TRACKER.md`, mark the following as resolved (they were completed in prior batches):
- **DEBT-002** → ✅ fixed in BATCH-04 (`ExConSubsystem` `SubsystemName` constant)
- **DEBT-003** → ✅ fixed in BATCH-04 (`ClusterSlave` test constructor explicit params)
- **DEBT-005** → ✅ fixed in BATCH-05 (`ReferencePrefetchHandler.Commit` stale state cleared)

Verify in the source files that these are actually resolved before marking them done.

---

## 🧪 Success Criteria

1. `AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate` → **PASSES**
2. `AllSubsystems_FullCycleTwice_LoadOperateUnloadIdle` → **PASSES**  
3. All existing passing tests remain passing — no regressions (37→39+ pass, or better)
4. New unit tests for multi-intent behavior → all pass
5. `dotnet build IOS-IG-SimHost.sln -v q` → 0 errors

---

## 🔄 Test-Driven Task Progression

**This section is mandatory. Do not proceed to the next task until the current one passes.**

1. **Red phase:** Run `AllSubsystemsClusterTransitionTests` before any code change. Confirm 2 failures.  
2. **Implement** ClusterSlave changes (1.2, 1.3, 1.4).  
3. **Green phase:** Run `AllSubsystemsClusterTransitionTests`. Both should now pass.  
4. **Add unit tests** (Task 1.5). Run unit tests. All should pass.  
5. **Full regression:** Run all test suites. Confirm no regressions.  
6. **FDP submodule commit:** Commit FDP changes to `main` branch with a clear message.  
7. **Top-level commit:** Commit `.dev/` file updates.

---

## Developer Insights Required in Report

Answer these in `BATCH-07-REPORT.md`:

1. Did the fix to `ClusterSlave` resolve the `AllSubsystems` tests? Were there any additional failures?
2. Did `ClusterOpE2eScriptTests` failures change? If so, what's the new failure count and pattern?
3. After resolving DEBT-002/003/005, were they actually done? Any surprises?
4. What was the most subtle aspect of the `_pendingIntents` queue design?
5. Any weak points discovered in ClusterMaster's fan-out pattern?

---

## Report Location

`.dev/cluster-master-cqrs-1/reports/BATCH-07-REPORT.md`
