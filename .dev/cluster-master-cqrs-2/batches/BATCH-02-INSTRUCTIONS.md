# BATCH-02: Explicit Domain Payload Structs — Replace Boxed Primitives

**Batch Number:** BATCH-02  
**Tasks:** TASK-D01  
**Phase:** 2 — Explicit Payload Structs  
**Estimated Effort:** 4–6 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (approved and complete)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.github/skills/developer/SKILL.md`  
2. **Task Definitions:** `.dev/cluster-master-cqrs-2/TASK-DEFINITIONS.md` — see TASK-D01
3. **Code Standards:** `.github/skills/CODE-STANDARDS.md`
4. **Previous Review:** `.dev/cluster-master-cqrs-2/reviews/BATCH-01-REVIEW.md` — context on completed work

### Source Code Locations
- **New file to create:** `FDP/Toolkits/FDP.Toolkit.Orchestration/NodeOpPayloads.cs`
- **FDP ClusterSlave:** `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`
- **Slave translator (ACL):** `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`
- **Master translator (ACL):** `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`
- **ClusterMaster orchestrator:** `Hrot.Orchestrator/ClusterMaster.cs`
- **TransitionPlanner:** `Hrot.Orchestrator/TransitionPlanner.cs`

### Test Projects
- `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/` — ClusterSlaveTests.cs, ReferenceHandlerTests.cs
- `Hrot.Orchestrator.Tests/` — ClusterMasterReplayTests.cs, TranslatorRoundTripTests.cs, NodeOpMasterTranslatorTests.cs
- `Hrot.Orchestrator.Integration.Tests/` — CqrsOrchestrationIntegrationTests.cs

### Report Destination
`.dev/cluster-master-cqrs-2/reports/BATCH-02-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW

Complete TASK-D01 end-to-end: create the new structs → update all call sites → build → fix any errors → run tests → fix failing tests → repeat until clean.  
Do NOT stop and ask permission for any obvious next step. Work through the full fix-check loop independently.

Build & test commands (run from repo root `d:\Work\IOS-IG-SimHost-FDP-2`):
```powershell
dotnet build IOS-IG-SimHost.sln
dotnet test FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FDP.Toolkit.Orchestration.Tests.csproj --no-build -v n
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj --no-build -v n
dotnet test Hrot.Orchestrator.Integration.Tests/Hrot.Orchestrator.Integration.Tests.csproj --no-build -v n
```

---

## Context

After BATCH-01, the domain events use `OrchestrationStatusCode` enum instead of raw `int`. This batch applies the same principle to `ExecuteNodeOpIntent.DomainPayload`: replace all boxed primitive types (`int`, `long`, `Guid`) with named record structs that carry explicit field names.

Currently:
- `ClusterMaster` fans out `CommitState` with `(int)tStep.TargetState` — a boxed int.
- `ClusterMaster` fans out `NodeReplaySeek` with `intent.TargetWallTicks` — a boxed long.
- `ClusterMaster` fans out `AbortTransaction` with `targetId` — a boxed Guid.
- `ClusterSlave.DispatchIntent()` must use `intent.DomainPayload is int stateId` to extract the target state — brittle.
- `NodeOpSlaveTranslator.DeserializeNodePayload()` returns a boxed `(object)stateId` for CommitState.
- `NodeOpMasterTranslator.SerializeNodePayload()` has a `domainPayload is int stateId` special-case.

After this batch, all three operations will carry typed structs throughout the pipeline.

---

## 🎯 Batch Objectives

1. Define three new `readonly record struct` payload types in `FDP.Toolkit.Orchestration`.  
2. Update `ClusterMaster` to use them when fanning out `CommitState`, `NodeReplaySeek`, `AbortTransaction`.  
3. Update `TransitionPlanner` so `OperationStep` for `ReplaySeek` stores `ReplaySeekPayload` instead of `long`.  
4. Update `ClusterSlave.DispatchIntent()` and its dedup buffer to pattern-match on the new struct.  
5. Update both translators: `NodeOpSlaveTranslator` (deserialize to struct), `NodeOpMasterTranslator` (serialize from struct).  
6. Update the DDS‐path legacy fallback `DomainPayloadToString()` in `ClusterMaster` for the new types.

---

## ✅ Tasks

### TASK-D01: Add CommitStatePayload, ReplaySeekPayload, AbortTransactionPayload

**Task Definition:** See [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md#task-d01--explicit-payload-structs-replace-boxed-primitives)

---

#### Step 1: Create `FDP/Toolkits/FDP.Toolkit.Orchestration/NodeOpPayloads.cs`

Create a new file with these contents (same namespace as `ClusterCqrsEvents.cs`):

```csharp
namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Payload for <see cref="NodeOpType.CommitState"/> intents.
    /// Replaces the previously boxed <c>int</c> state ID.
    /// </summary>
    public readonly record struct CommitStatePayload(int TargetStateId);

    /// <summary>
    /// Payload for <see cref="NodeOpType.NodeReplaySeek"/> intents.
    /// Replaces the previously boxed <c>long</c> wall-clock tick target.
    /// </summary>
    public readonly record struct ReplaySeekPayload(long TargetWallTicks);

    /// <summary>
    /// Payload for <see cref="NodeOpType.AbortTransaction"/> intents.
    /// Replaces the previously boxed <c>Guid</c> target transaction ID.
    /// </summary>
    public readonly record struct AbortTransactionPayload(Guid TargetTransactionId);
}
```

---

#### Step 2: Update `ClusterSlave.cs`

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`

**Change A — `DispatchIntent()` dedup discriminant** (around line 227):
```csharp
// Before:
int stateDiscriminant = intent.Operation == NodeOpType.CommitState && intent.DomainPayload is int sd
    ? sd : -1;
// After:
int stateDiscriminant = intent.Operation == NodeOpType.CommitState &&
                        intent.DomainPayload is CommitStatePayload csp
    ? csp.TargetStateId : -1;
```

**Change B — `DispatchIntent()` CommitState state extraction** (around line 240):
```csharp
// Before:
int nextStateId = intent.DomainPayload is int stateId ? stateId : _localStateId;
// After:
int nextStateId = intent.DomainPayload is CommitStatePayload cp ? cp.TargetStateId : _localStateId;
```

**Change C — `Tick()` buffered intent dedup** (around line 191):
```csharp
// Before:
int sd = intent.Operation == NodeOpType.CommitState && intent.DomainPayload is int v ? v : -1;
// After:
int sd = intent.Operation == NodeOpType.CommitState && intent.DomainPayload is CommitStatePayload csp2
    ? csp2.TargetStateId : -1;
```

---

#### Step 3: Update `ClusterMaster.cs` — Fan-Out Call Sites

**File:** `Hrot.Orchestrator/ClusterMaster.cs`

**Change A — `ProcessTransitionStateIntent()` CommitState fan-out** (around line 862):
```csharp
// Before:
FanOutNodeOp(NodeOpType.CommitState, tx.TransactionId, (int)tStep.TargetState, activeNodeIds);
// After:
FanOutNodeOp(NodeOpType.CommitState, tx.TransactionId,
    new CommitStatePayload((int)tStep.TargetState), activeNodeIds);
```

**Change B — `ProcessSeekReplayIntent()` NodeReplaySeek fan-out** (around line 1073):
```csharp
// Before:
FanOutNodeOp(NodeOpType.NodeReplaySeek, Guid.NewGuid(), intent.TargetWallTicks, seekNodeIds);
// After:
FanOutNodeOp(NodeOpType.NodeReplaySeek, Guid.NewGuid(),
    new ReplaySeekPayload(intent.TargetWallTicks), seekNodeIds);
```

**Change C — `ProcessCancelOperationIntent()` AbortTransaction fan-out** (around line 1093):
```csharp
// Before:
FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(), targetId, cancelNodeIds);
// After:
FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(),
    new AbortTransactionPayload(targetId), cancelNodeIds);
```

**Change D — `DomainPayloadToString()` DDS legacy path** (around line 1181):  
The existing pattern matches `int i`, `long l`, `Guid g`. Replace those raw-primitive branches with the new struct branches, and keep the specific handler payload branches:
```csharp
private static string DomainPayloadToString(object? domainPayload) => domainPayload switch
{
    null                       => string.Empty,
    CommitStatePayload    csp  => csp.TargetStateId.ToString(),
    ReplaySeekPayload     rsp  => rsp.TargetWallTicks.ToString(),
    AbortTransactionPayload atp => atp.TargetTransactionId.ToString(),
    string s                   => s,
    ArchiveHandlerPayload  p   => p.ExerciseId  != null ? $"{{\"ExerciseId\":\"{p.ExerciseId}\"}}"   : string.Empty,
    PrefetchHandlerPayload p   => p.ScenarioId != null  ? $"{{\"ScenarioId\":\"{p.ScenarioId}\"}}" : string.Empty,
    _                          => string.Empty,
};
```

**Note:** Also check the `EjectNode` abort fan-out near line 620:
```csharp
FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(), null, survivingIds);
```
This currently passes `null` as the payload (no specific target ID). After the change, `AbortTransaction` with `null` payload is still valid — the handler should defensively handle either `null` or `AbortTransactionPayload`. Leave this as `null` (no breaking change needed here).

---

#### Step 4: Update `TransitionPlanner.cs`

**File:** `Hrot.Orchestrator/TransitionPlanner.cs`

The `ReplaySeek` OperationStep currently stores a raw `long` as its `DomainPayload`:
```csharp
// Line ~157 (before):
queue.Enqueue(new OperationStep(ClusterOpType.ReplaySeek, intent.TargetWallTicks));
// After:
queue.Enqueue(new OperationStep(ClusterOpType.ReplaySeek,
    new ReplaySeekPayload(intent.TargetWallTicks)));
```

This ensures that when `ClusterMaster` processes the step at line 878:
```csharp
FanOutNodeOp(NodeOpType.NodeReplaySeek, Guid.NewGuid(), opStep.DomainPayload, activeNodeIds);
```
`opStep.DomainPayload` is already a `ReplaySeekPayload`, so it is correctly serialized by `NodeOpMasterTranslator.SerializeNodePayload()`.

You will need to add `using FDP.Toolkit.Orchestration;` to `TransitionPlanner.cs` if it is not already there.

---

#### Step 5: Update `NodeOpSlaveTranslator.cs`

**File:** `Hrot.Common/Orchestration/NodeOpSlaveTranslator.cs`

In `DeserializeNodePayload()`, update and add cases:

```csharp
case NedNodeOpType.CommitState:
{
    // Before: returns (object)stateId (boxed int)
    // After: returns CommitStatePayload
    if (hasPayload && int.TryParse(payloadJson!.Trim(), out var stateId))
        return new CommitStatePayload(stateId);
    return null;
}

case NedNodeOpType.NodeReplaySeek:
{
    // Was: default → null. Now explicitly handled.
    if (hasPayload && long.TryParse(payloadJson!.Trim(), out var ticks))
        return new ReplaySeekPayload(ticks);
    return null;
}

case NedNodeOpType.AbortTransaction:
{
    // Was: default → null. Now explicitly handled.
    if (hasPayload && Guid.TryParse(payloadJson!.Trim(), out var txId))
        return new AbortTransactionPayload(txId);
    return null;
}
```

Add `using FDP.Toolkit.Orchestration;` if needed (it may already be there).

---

#### Step 6: Update `NodeOpMasterTranslator.cs`

**File:** `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`

In `SerializeNodePayload()`, replace the `domainPayload is int stateId` branch with explicit struct handling:

```csharp
private string SerializeNodePayload(FdpNodeOpType operation, object? domainPayload)
{
    if (domainPayload is null) return string.Empty;

    return domainPayload switch
    {
        CommitStatePayload    csp => csp.TargetStateId.ToString(),
        ReplaySeekPayload     rsp => rsp.TargetWallTicks.ToString(),
        AbortTransactionPayload atp => atp.TargetTransactionId.ToString(),

        EditLoadHandlerPayload p => JsonSerializer.Serialize(
            new NodeTransitionPayloadDto(
                TargetState: p.TargetState != 0
                    ? ((Hrot.NED.Descriptors.Orchestration.ClusterState)p.TargetState).ToString()
                    : null,
                ScenarioId: p.ScenarioId,
                ExerciseId: null),
            _jsonOptions),

        EpisodeHandlerPayload p => JsonSerializer.Serialize(
            new NodeEpisodePayloadDto(
                IsStart:    p.IsStart,
                EpisodeId:  p.EpisodeId == Guid.Empty ? null : p.EpisodeId,
                ScenarioId: p.ScenarioId),
            _jsonOptions),

        PrefetchHandlerPayload p => JsonSerializer.Serialize(
            new NodePrefetchPayloadDto(p.ScenarioId), _jsonOptions),

        ArchiveHandlerPayload p => JsonSerializer.Serialize(
            new NodeTransitionPayloadDto(
                TargetState: null,
                ScenarioId:  null,
                ExerciseId:  p.ExerciseId),
            _jsonOptions),

        _ => JsonSerializer.Serialize(domainPayload, domainPayload.GetType(), _jsonOptions),
    };
}
```

The key changes are: removed the `if (domainPayload is int stateId) return stateId.ToString();` special-case and replaced with `CommitStatePayload csp => csp.TargetStateId.ToString()` in the switch, plus added the two new entries for `ReplaySeekPayload` and `AbortTransactionPayload`.

---

## 🧪 Tests Required

### Test 1: ClusterSlave uses CommitStatePayload for dispatch and dedup

Add to `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs`:

```csharp
/// <summary>
/// CommitState intent with CommitStatePayload (not boxed int) correctly updates local state.
/// </summary>
[Fact]
public void ClusterSlave_CommitState_WithCommitStatePayload_UpdatesLocalState()
{
    var bus    = new FdpEventBus();
    var slave  = new ClusterSlave(eventBus: bus, nodeId: 1, subsystemName: "Test");
    var txId   = Guid.NewGuid();

    slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
    {
        TransactionId = txId,
        TargetNodeId  = 1,
        Operation     = NodeOpType.CommitState,
        DomainPayload = new CommitStatePayload(TargetStateId: 5),
    });

    Assert.Equal(5, slave.LocalStateIdForTest);
}

/// <summary>
/// Two CommitState intents with different TargetStateIds should each be processed once
/// (dedup key includes TargetStateId discriminant).
/// </summary>
[Fact]
public void ClusterSlave_CommitState_DeduplicatesOnStateId()
{
    var bus   = new FdpEventBus();
    var slave = new ClusterSlave(eventBus: bus, nodeId: 1, subsystemName: "Test");
    var txId  = Guid.NewGuid();

    // Same transaction but two different target states
    slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
    {
        TransactionId = txId,
        TargetNodeId  = 1,
        Operation     = NodeOpType.CommitState,
        DomainPayload = new CommitStatePayload(2),
    });
    slave.EnqueueIntentForTest(new ExecuteNodeOpIntent
    {
        TransactionId = txId,
        TargetNodeId  = 1,
        Operation     = NodeOpType.CommitState,
        DomainPayload = new CommitStatePayload(5),
    });

    // Last CommitState wins
    Assert.Equal(5, slave.LocalStateIdForTest);
}
```

### Test 2: Update existing ClusterSlave tests that pass raw int

Search `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/ClusterSlaveTests.cs` for any test that uses `DomainPayload = (int)X` or `DomainPayload = X` where X is an integer, for `CommitState` operations. Update them to use `new CommitStatePayload(X)`.

### Test 3: Translator round-trip — CommitStatePayload

In `Hrot.Orchestrator.Integration.Tests/TranslatorRoundTripTests.cs` (or `Hrot.Orchestrator.Tests/NodeOpMasterTranslatorTests.cs`), verify that a `CommitStatePayload` serialized by `NodeOpMasterTranslator` is correctly deserialized by `NodeOpSlaveTranslator` back to a `CommitStatePayload`.

Check if there are existing `CommitState` round-trip tests — if so, update them; if not, add one:
```csharp
[Fact]
public void CommitStatePayload_RoundTrips_ThroughTranslators()
{
    // Setup mock DDS readers/writers and wired bus...
    // (follow the pattern in TranslatorRoundTripTests.cs for the existing round-trip tests)
    
    // Master side: publish CommitState intent with CommitStatePayload
    bus.PublishManaged(new ExecuteNodeOpIntent
    {
        TransactionId = txId,
        TargetNodeId  = 1,
        Operation     = FdpNodeOpType.CommitState,
        DomainPayload = new CommitStatePayload(3),
    });
    masterTranslator.Tick();  // serializes to DDS

    // Slave side: pick up command and deserialize
    slaveTranslator.Tick();   // deserializes from DDS, publishes ExecuteNodeOpIntent
    
    var intents = slaveBus.ConsumeManaged<ExecuteNodeOpIntent>().ToList();
    Assert.Single(intents);
    var payload = Assert.IsType<CommitStatePayload>(intents[0].DomainPayload);
    Assert.Equal(3, payload.TargetStateId);
}
```

---

## ⚠️ Quality Standards

**❗ BEHAVIOUR TESTS ONLY**  
Tests must verify actual state changes or payload types — not just compilation.

**❗ Update existing tests that break**  
Any existing test that uses `DomainPayload = someInt` for `CommitState` must be updated to `DomainPayload = new CommitStatePayload(someInt)`. Do not leave them with the old int pattern.

**❗ Do not break the null-payload AbortTransaction path**  
The `EjectNode` call at line ~620 passes `null` for `AbortTransaction` payload: `FanOutNodeOp(NodeOpType.AbortTransaction, Guid.NewGuid(), null, survivingIds)`. This is intentional (no specific target). Leave it as `null` — ClusterSlave handlers should defensively handle `null` DomainPayload for AbortTransaction.

---

## 📊 Report Requirements

**Q1:** Which existing test files needed updating because they passed raw `int` as `CommitState` DomainPayload? How many tests were affected?

**Q2:** Did you find any other sites that pattern-match on `DomainPayload is int` that weren't listed? What were they?

**Q3:** What was the challenge (if any) with the `TransitionPlanner` ReplaySeek DomainPayload update? Did it require adding a `using` directive or was it already imported?

**Q4:** Did you discover any edge cases in the serialization/deserialization round-trip for `AbortTransactionPayload` or `ReplaySeekPayload`?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `NodeOpPayloads.cs` created with three new `readonly record struct` types.
- [ ] `ClusterSlave.cs` uses `CommitStatePayload` for dispatch and dedup — no more `is int` pattern matching for CommitState.
- [ ] `ClusterMaster.cs` uses struct payloads for CommitState, NodeReplaySeek, AbortTransaction fan-out and in `DomainPayloadToString`.
- [ ] `TransitionPlanner.cs` wraps `TargetWallTicks` in `new ReplaySeekPayload(...)`.
- [ ] `NodeOpSlaveTranslator.cs` returns `CommitStatePayload`, `ReplaySeekPayload`, `AbortTransactionPayload` from `DeserializeNodePayload()`.
- [ ] `NodeOpMasterTranslator.cs` serializes the three new structs correctly.
- [ ] New unit tests for `ClusterSlave` pass.
- [ ] Round-trip test for `CommitStatePayload` passes.
- [ ] Full build: 0 errors.
- [ ] All affected test projects pass.
- [ ] Report submitted to `.dev/cluster-master-cqrs-2/reports/BATCH-02-REPORT.md`.

---

## 📚 Reference Materials
- **Task Definitions:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md#task-d01--explicit-payload-structs-replace-boxed-primitives)
- **BATCH-01 review:** `.dev/cluster-master-cqrs-2/reviews/BATCH-01-REVIEW.md`
- **Existing payload structs:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/ReferenceArchiveHandler.cs` (ArchiveHandlerPayload), `ReferenceEditLoadHandler.cs` (EditLoadHandlerPayload), `ReferenceEpisodeLoadHandler.cs` (EpisodeHandlerPayload), `ReferencePrefetchHandler.cs` (PrefetchHandlerPayload)
