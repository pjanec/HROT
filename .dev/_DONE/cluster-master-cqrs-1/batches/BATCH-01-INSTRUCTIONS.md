# BATCH-01: FDP Domain Enums and CQRS Event Structs

**Batch Number:** BATCH-01  
**Tasks:** CMC-S001, CMC-S002, CMC-S003  
**Phase:** Phase 1 — FDP Domain Vocabulary (Additive, Zero Breaking Changes)  
**Estimated Effort:** 4–6 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch establishes the pure FDP domain vocabulary for the ClusterMaster CQRS refactor. All work is **additive only** — you create new files and add new tests. Nothing existing is modified. No breaking changes.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md` — How to work with batches
2. **Onboarding:** `.dev/cluster-master-cqrs-1/ONBOARDING.md` — Project context
3. **Design Document:** `.dev/cluster-master-cqrs-1/DESIGN.md` — Full architecture, especially §3.1, §3.2, §3.3
4. **Task Details:** `.dev/cluster-master-cqrs-1/TASK-DETAIL.md` — See CMC-S001, CMC-S002, CMC-S003

### Source Code Locations

- **New enum files:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/` (create new directory)
- **New event structs:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/` (create new directory)
- **FDP test project:** `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/` (xunit, .net8)
- **Hrot test project (enum sync tests):** `Hrot.Orchestrator.Tests/` (xunit, .net8)
- **NED enum source of truth:** `Hrot.NED/Orchestration/OrchestrationMessages.cs`
- **DataPolicy attribute:** `FDP/Kernel/Fdp.Kernel/DataPolicyAttribute.cs`
- **EventId attribute:** `FDP/Kernel/Fdp.Kernel/EventIdAttribute.cs`
- **FdpEventBus:** `FDP/Kernel/Fdp.Kernel/FdpEventBus.cs` — use `PublishManaged<T>()` and `ConsumeManaged<T>()`

### Build Commands

```powershell
# Build (run from repo root d:\Work\IOS-IG-SimHost-FDP-2)
dotnet build FDP/FDP.sln -v q
dotnet build IOS-IG-SimHost.sln -v q

# Test the FDP orchestration toolkit
dotnet test FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FDP.Toolkit.Orchestration.Tests.csproj

# Test the Hrot orchestrator (enum sync tests)
dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj
```

### Report Submission

**When done, submit your report to:**  
`.dev/cluster-master-cqrs-1/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev/cluster-master-cqrs-1/questions/BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **CMC-S001:** Implement enums → Write sync tests → **ALL tests pass** ✅
2. **CMC-S002:** Implement core event structs → Write struct tests → **ALL tests pass** ✅
3. **CMC-S003:** Implement operation intent structs → Write struct tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous tasks' tests)

Do NOT stop for confirmation or ask if it's OK to run tests. Run them, fix the root cause until green, then proceed.

---

## Context

This is the foundational batch for the ClusterMaster CQRS Decoupling workstream. The goal is to define the pure FDP domain vocabulary that will replace the current "Stringly Typed" design (raw `int` operation IDs, `string PayloadJson`).

These types live exclusively inside `FDP.Toolkit.Orchestration` and have **zero** references to `Hrot.NED`, `CycloneDDS`, or `System.Text.Json`. They are the domain contract.

---

## ✅ Tasks

### Task 1: Domain Enums — CMC-S001

**File location:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/` (new directory)  
**Task Definition:** See [TASK-DETAIL.md CMC-S001](../TASK-DETAIL.md#cmc-s001--domain-enums-in-fdptoolkitorchestration)  
**Design Reference:** [DESIGN.md §3.1](../DESIGN.md#31-domain-enums-dual-enum-pattern)

**Files to create:**
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/ClusterState.cs`
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/ClusterOpType.cs`
- `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/NodeOpType.cs`

**Critical rule:** The integer values must be identical to the NED counterparts. Read the **actual source of truth** at `Hrot.NED/Orchestration/OrchestrationMessages.cs` — do not guess or copy from DESIGN.md without verifying. The NED file is authoritative.

**Namespace:** `FDP.Toolkit.Orchestration` (same as the rest of the toolkit)

**Constraint:** After this task, `grep -r "Hrot.NED" FDP/Toolkits/FDP.Toolkit.Orchestration/` must return zero results.

**Tests for CMC-S001:**  
Add file `Hrot.Orchestrator.Tests/FdpOrchestrationEnumSyncTests.cs`.  
The `Hrot.Orchestrator.Tests` project already references `Hrot.Orchestrator.csproj` which transitively references both `FDP.Toolkit.Orchestration` and `Hrot.NED` — no new project reference needed.

Write three `[Fact]` tests:

```csharp
// Pattern: for every value in the FDP enum, cast to int, cast to Hrot NED enum, assert .ToString() matches
[Fact]
public void ClusterStateValuesMatchHrot()
{
    foreach (FDP.Toolkit.Orchestration.ClusterState fdpVal in Enum.GetValues<FDP.Toolkit.Orchestration.ClusterState>())
    {
        var nedVal = (Hrot.NED.Descriptors.Orchestration.ClusterState)(int)fdpVal;
        Assert.Equal(fdpVal.ToString(), nedVal.ToString());
    }
}
```

Repeat for `NodeOpType` and `ClusterOpType`.

**Minimum: 3 sync tests covering all three enums.**

---

### Task 2: Core CQRS Event Structs — CMC-S002

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs` (new file, new directory)  
**Task Definition:** See [TASK-DETAIL.md CMC-S002](../TASK-DETAIL.md#cmc-s002--core-cqrs-event-bus-structs)  
**Design Reference:** [DESIGN.md §3.2](../DESIGN.md#32-core-cqrs-event-bus-dtos)

**Three structs to define:**

| Struct | EventId | Notes |
|--------|---------|-------|
| `ClusterOpCompletedEvent` | 9011 | `Guid RequestId`, `int StatusCode`, `object? ResultPayload` |
| `ExecuteNodeOpIntent` | 9012 | `Guid TransactionId`, `int TargetNodeId`, `NodeOpType Operation`, `object? DomainPayload` |
| `NodeOpCompletedEvent` | 9013 | `Guid TransactionId`, `int NodeId`, `int StatusCode`, `bool IsParticipating`, `object? ResultPayload` |

**Required attributes on each struct:**
```csharp
using Fdp.Kernel;

[EventId(9011)]
[DataPolicy(DataPolicy.NoRecord)]
public struct ClusterOpCompletedEvent { ... }
```

**Key constraints:**
- No `string PayloadJson` or `string ResultJson` fields — payload is `object? DomainPayload` / `object? ResultPayload`
- No `ExecuteClusterOpIntent` — it does not exist
- These are managed structs: route via `_eventBus.PublishManaged<T>()` / `_eventBus.ConsumeManaged<T>()`
- EventIds 9011-9013 are confirmed free in this codebase

**Tests for CMC-S002:**  
Add `FdpOrchestrationCqrsStructTests.cs` in `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/`.

Required tests:
1. Each struct has `[DataPolicy(DataPolicy.NoRecord)]` — check via reflection
2. Each struct has unique `[EventId]` — check via reflection  
3. `ExecuteNodeOpIntent` has field `DomainPayload` of type `object?` (no field named `PayloadJson`) — check via `typeof(ExecuteNodeOpIntent).GetFields()`
4. `NodeOpCompletedEvent` and `ClusterOpCompletedEvent` have field `ResultPayload` of type `object?`
5. `FdpEventBus.PublishManaged<ExecuteNodeOpIntent>` and `ConsumeManaged<ExecuteNodeOpIntent>` compile and execute without exception (use a fresh `new FdpEventBus()` in the test)

**Minimum: 8 tests.**

---

### Task 3: Operation Payload Intent Structs — CMC-S003

**File:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterOpIntents.cs` (new file)  
**Task Definition:** See [TASK-DETAIL.md CMC-S003](../TASK-DETAIL.md#cmc-s003--specific-operation-payload-intent-structs)  
**Design Reference:** [DESIGN.md §3.3](../DESIGN.md#33-specific-operation-payload-intents)

**Structs to define (all in `FDP.Toolkit.Orchestration` namespace, all `[DataPolicy(DataPolicy.NoRecord)]`):**

| Type | EventId | Key Fields |
|------|---------|------------|
| `TransitionStateIntent` | 9050 | `Guid TransactionId`, `ClusterState TargetState`, `long TargetWallTicks`, `string? ScenarioId`, `string? ExerciseId`, `string? TimeMode` |
| `ManageEpisodeIntent` | 9051 | `Guid TransactionId`, `bool IsStart`, `Guid EpisodeId`, `string? ScenarioId` |
| `SeekReplayIntent` | 9052 | `Guid RequestId`, `long TargetWallTicks` |
| `CancelOperationIntent` | 9053 | `Guid TargetRequestId` |
| `StorageOpType` enum | — | `Export, Import, SaveScenario` (no EventId — it's an enum) |
| `ExecuteStorageOpIntent` | 9054 | `Guid RequestId`, `StorageOpType Operation`, `string? ExerciseId` |
| `StorageOpCompletedEvent` | 9055 | `Guid RequestId`, `int StatusCode`, `int SuccessCount`, `int FailureCount` |
| `TakeCheckpointIntent` | 9056 | `Guid RequestId` only — no other fields |
| `LoadZoneIntent` | 9057 | `Guid RequestId`, `string? ZoneId` |

**EventIds 9050-9057 confirmed free** (checked: existing IDs in range 9000-9031, 9201, 9999).

**Key constraints:**
- Use `FDP.Toolkit.Orchestration.ClusterState` enum (from CMC-S001), not `Hrot.NED`
- No `Hrot.NED` references
- No `System.Text.Json`

**Tests for CMC-S003:**  
Extend `FdpOrchestrationCqrsStructTests.cs` in `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/`.

Required tests:
1. Reflection check: all 8 struct types (not the enum) have `[DataPolicy(DataPolicy.NoRecord)]`
2. Reflection check: EventIds 9050-9057 are all present and non-overlapping
3. `TransitionStateIntent.TargetState` field is of type `FDP.Toolkit.Orchestration.ClusterState` — NOT `int`, NOT Hrot type
4. `ManageEpisodeIntent` has `bool IsStart`, `Guid EpisodeId`, `string? ScenarioId`
5. `TakeCheckpointIntent` has exactly one field: `Guid RequestId`
6. No type named `ExecuteClusterOpIntent` exists in namespace `FDP.Toolkit.Orchestration`

**Minimum: 6 tests.**

---

## 🧪 Testing Requirements

- Test framework: **xunit** (already referenced in both test projects)
- All tests must **pass** before the report is written
- Run full suite: `dotnet test FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/FDP.Toolkit.Orchestration.Tests.csproj` AND `dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj`
- Tests must validate **behavior and structure**, not just "does it compile"
- Reflection-based tests are appropriate here since we are enforcing attributes

**Total minimum: 17 new tests** (3 + 8 + 6)

---

## ⚠️ Quality Standards

**❗ NO BREAKING CHANGES** — This batch only adds files. No existing file is modified.

**❗ NO Hrot.NED IN FDP** — After this batch, `grep -r "Hrot" FDP/Toolkits/FDP.Toolkit.Orchestration/` must return zero results.

**❗ NO System.Text.Json IN FDP** — `grep -r "System.Text.Json" FDP/Toolkits/FDP.Toolkit.Orchestration/` must return zero results.

**❗ EventId uniqueness** — Use the EventId values specified above. Verify no collision by checking existing IDs in `FDP/Kernel/Fdp.Kernel/` and `FDP/Toolkits/` before finalizing.

**❗ TEST QUALITY** — Not acceptable: tests that only check "does it build". Required: tests that check specific field types, attribute values, and bus round-trips.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] CMC-S001: 3 enum files exist in `FDP/Toolkits/FDP.Toolkit.Orchestration/Enums/`, values match NED exactly
- [ ] CMC-S001: 3 sync tests in `Hrot.Orchestrator.Tests/` pass
- [ ] CMC-S002: `ClusterCqrsEvents.cs` with 3 structs exists in `Events/`
- [ ] CMC-S002: 8 struct tests pass
- [ ] CMC-S003: `ClusterOpIntents.cs` with 9 types exists in `Events/`
- [ ] CMC-S003: 6 struct tests pass
- [ ] `dotnet build FDP/FDP.sln` succeeds with 0 errors
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds with 0 errors
- [ ] All new tests pass
- [ ] Report submitted

---

## 📊 Report Requirements

Submit `.dev/cluster-master-cqrs-1/reports/BATCH-01-REPORT.md` with:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Were there any discrepancies between DESIGN.md and the actual NED enum values in `OrchestrationMessages.cs`? What did you find and what did you do about it?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases or gotchas did you discover that weren't mentioned?

**Q5:** Are there any concerns or observations about the upcoming phases (Phase 2+) based on what you saw in the existing code?

**Q6:** Suggested git commit message for this batch.

---

## 📚 Reference Materials

- **Design:** `.dev/cluster-master-cqrs-1/DESIGN.md` — §3.1, §3.2, §3.3
- **Task Details:** `.dev/cluster-master-cqrs-1/TASK-DETAIL.md` — CMC-S001, CMC-S002, CMC-S003
- **NED Enums (source of truth):** `Hrot.NED/Orchestration/OrchestrationMessages.cs`
- **DataPolicy:** `FDP/Kernel/Fdp.Kernel/DataPolicyAttribute.cs`
- **EventId:** `FDP/Kernel/Fdp.Kernel/EventIdAttribute.cs`
- **FdpEventBus:** `FDP/Kernel/Fdp.Kernel/FdpEventBus.cs` — `PublishManaged<T>`, `ConsumeManaged<T>`
- **Existing tests (pattern reference):** `FDP/Toolkits/FDP.Toolkit.Orchestration.Tests/OrchestrationContractTests.cs`
