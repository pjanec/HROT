# BATCH-01: Storage Aggregator and Process Manager

**Batch Number:** BATCH-01  
**Tasks:** TASK-S001 (StorageConsensusAggregator), TASK-S002 (StorageProcessManager)  
**Phase:** Phase 1 -- Storage and Episode Extractions  
**Estimated Effort:** 6-8 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch extracts the first two domain concerns from `ClusterMaster`: the manifest aggregation
logic (TASK-S001) and the NAS pull side-effects (TASK-S002). Both are tightly sequenced -- S001
builds the aggregator that feeds the event, S002 builds the process manager that reacts to it.
Do not skip ahead; finish S001 with passing tests before starting S002.

### Required Reading (IN ORDER)

1. **Onboarding:** `.dev/cluster-master-refact/ONBOARDING.md` -- key files, build commands, pattern template
2. **Design Document:** `.dev/cluster-master-refact/DESIGN.md` -- read the Background, Architecture Pattern, Cross-Cutting section, Phase 1.1 and Phase 1.2
3. **Task Definitions:** `.dev/cluster-master-refact/TASK-DETAIL.md` -- read TASK-S001 and TASK-S002 in full (including all success conditions)
4. **Developer Skill:** `.github/skills/developer/SKILL.md` -- how the batch workflow operates
5. **Code Standards:** `.github/skills/CODE-STANDARDS.md` -- must comply

### Source Code Location

- **Primary Work Area:** `Hrot/Subsystems/Hrot.Orchestrator/`
- **Unit Test Project:** `Hrot/Subsystems/Hrot.Orchestrator.Tests/`
- **Integration Test Project:** `Hrot/Subsystems/Hrot.Orchestrator.Integration.Tests/`
- **Existing Aggregator Reference:** `Hrot/Subsystems/Hrot.Orchestrator/ReplayConsensusAggregator.cs`
- **Existing Process Manager Reference:** `Hrot/Subsystems/Hrot.Orchestrator/ReplayProcessManager.cs`
- **Aggregator Interface:** `Hrot/Subsystems/Hrot.Orchestrator/INodeResponseAggregator.cs`
- **Wiring Point:** `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`
- **Target God Class:** `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs`

### Report Submission

**When done, write your report to:**
`.dev/cluster-master-refact/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/cluster-master-refact/questions/BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **TASK-S001:** Implement → Write tests → **ALL tests pass** ✅
2. **TASK-S002:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous task tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context

`ClusterMaster` is a Two-Phase Commit coordinator. Its sole job is fan-out → collect ACKs →
reduce payloads → publish `ClusterOpCompletedEvent`. The `SerializeLocal` operation currently
violates this by also running NAS I/O inside the ACK loop (`HandleSerializeLocalCompletion`
calls `_gateway.PullToNasAsync`).

This batch surgically removes that violation in two steps: first teach `ClusterMaster` to reduce
`SerializeLocal` payloads through the aggregator pipeline (TASK-S001), then move the NAS pull
to a dedicated `StorageProcessManager` that reacts to the published event (TASK-S002).

---

## 🎯 Batch Objectives

1. `StorageConsensusAggregator` exists, is registered, and reduces per-node manifest JSON into
   a flat `List<FileManifestEntry>` on `ClusterOpCompletedEvent`.
2. `StorageProcessManager` exists, reads that event, and calls `PullToNasAsync` + `WriteScenarioManifestAsync`.
3. `ClusterMaster` no longer calls `_gateway.PullToNasAsync` in the `SerializeLocal` completion path.
4. All unit and integration tests pass.

---

## ✅ Tasks

### Task 1: StorageConsensusAggregator (TASK-S001)

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/StorageConsensusAggregator.cs` (NEW FILE)
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` (MODIFY -- SerializeLocal completion path)
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` (MODIFY -- register aggregator)
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/StorageConsensusAggregatorTests.cs` (NEW FILE)

**Task Definition:** See `.dev/cluster-master-refact/TASK-DETAIL.md` § TASK-S001 for full scope, all constraints, and all success conditions.

**Key implementation points:**

1. `StorageConsensusAggregator` implements `INodeResponseAggregator`. `TargetOp` returns
   `NodeOpType.SerializeLocal`. `Aggregate()` receives `IReadOnlyDictionary<int, Dictionary<NodeOpType, string>> nodeResponses`, iterates values, deserializes each inner string as `List<FileManifestEntry>`, and flattens to a single list. Skip (do not throw) malformed JSON entries.

2. In `ClusterMaster`, find `HandleSerializeLocalCompletion` (or the equivalent ACK loop for
   `_pendingSerializeTasks`). At the end of this method (all ACKs collected), collect the raw per-node payloads, call the registered aggregator via the same `TryAggregate()` path (or a direct call if `TryAggregate()` is `TransitionState`-only), and publish `ClusterOpCompletedEvent` with the aggregated list as `ResultPayload`.
   
   **Important:** The existing `_gateway.PullToNasAsync` call in `HandleSerializeLocalCompletion`
   must **remain** for this task (it is removed in TASK-S002). Just ensure the event is also
   published alongside the existing logic.

3. Register `new StorageConsensusAggregator()` in `OrchestratorSubsystem.Initialize` via
   `_clusterMaster.RegisterAggregator(new StorageConsensusAggregator())`.

**Tests Required:**
- All 4 success conditions from TASK-S001 must have corresponding unit tests.
- Tests must be in `Hrot.Orchestrator.Tests` project.
- Use the same test patterns as `ClusterMasterSeekTests` or `ClusterMasterPrefetchTests` as reference.

---

### Task 2: StorageProcessManager (TASK-S002)

**Files:**
- `Hrot/Subsystems/Hrot.Orchestrator/StorageProcessManager.cs` (NEW FILE)
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` (MODIFY -- remove `HandleSerializeLocalCompletion`, `_pendingSerializeTasks`, `SerializeLocalTask`)
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` (MODIFY -- wire `StorageProcessManager`)
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/StorageProcessManagerTests.cs` (NEW FILE)

**Task Definition:** See `.dev/cluster-master-refact/TASK-DETAIL.md` § TASK-S002 for full scope, all constraints, and all success conditions.

**Key implementation points:**

1. `StorageProcessManager` constructor: `(FdpEventBus bus, StorageGatewayModule gateway, string nasBasePath, GlobalContextClusterOpHandler contextHandler)`. The `contextHandler` is a **transitional shim** -- it is read only to get `CommitManifestEntry` and prepend it to the manifest before calling `PullToNasAsync`. Mark the constructor parameter and its usage site with:
   ```csharp
   // TODO(TASK-P001): remove contextHandler shim when GlobalContextProcessManager publishes manifest entry via bus
   ```

2. `Tick()` reads `ClusterOpCompletedEvent` from the bus. On `StatusCode == Success` and
   `ResultPayload is List<FileManifestEntry> manifest` (and manifest is non-empty): prepend the
   `contextHandler.CommitManifestEntry` (if non-null) to `manifest`, then call
   `_gateway.PullToNasAsync(fullManifest, nasBasePath)`. On completion, call
   `_gateway.WriteScenarioManifestAsync(...)`.

3. Wire in `OrchestratorSubsystem.Initialize`: create `_storageProcessManager = new StorageProcessManager(...)` and tick it in `Update()` after `_clusterMaster.Tick()`.

4. Remove from `ClusterMaster`: `HandleSerializeLocalCompletion` method, `_pendingSerializeTasks` dictionary, `SerializeLocalTask` inner class, and the `_gateway.PullToNasAsync` + `_gateway.WriteScenarioManifestAsync` calls in the SerializeLocal path.
   `SetStorageGateway()` and the `StorageGateway` property stay.

**Tests Required:**
- All 5 success conditions from TASK-S002 must have corresponding unit tests.
- Success condition 1 is critical: verify that `CommitManifestEntry` from the shim IS included in the manifest passed to `PullToNasAsync`.
- Tests must be in `Hrot.Orchestrator.Tests` project.

---

## 🧪 Testing Requirements

- Minimum: 9 unit tests total (4 for S001, 5 for S002).
- Every test must verify actual behavior -- not just "no exception thrown".
- Tests asserting on `PullToNasAsync` arguments must check the actual list contents (count, specific entries), not just that it was called.
- Integration test `ScenarioSaveLoadTests.OrchestratorContextRestored_AfterLoad` must pass after TASK-S002 (run it after S002 to confirm).

---

## ⚠️ Quality Standards

**❗ TEST QUALITY**
- **NOT ACCEPTABLE:** `Assert.NotNull(aggregator)` or "no exception thrown" tests
- **REQUIRED:** Assert the exact `List<FileManifestEntry>` contents returned by `Aggregate()`
- **REQUIRED:** Assert that `PullToNasAsync` is called with the expected manifest list (verify both count and presence of the orchestrator's own entry from the shim)

**❗ CODE STANDARDS**
- No magic numbers per `.github/skills/CODE-STANDARDS.md`
- All new classes in `Hrot.Orchestrator` namespace
- Transitional shim reference must have the `// TODO(TASK-P001):` comment

**❗ DO NOT STOP**
- Do not ask for permission to run tests. Run them.
- Do not ask if you should fix compilation errors. Fix them.
- Do not ask if you should keep going after one task passes. Continue to the next task.
- Write your report only after all success conditions are met and all tests pass.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `StorageConsensusAggregator` implemented and registered (TASK-S001)
- [ ] `ClusterMaster.SerializeLocal` completion path publishes `ClusterOpCompletedEvent` with manifest payload (TASK-S001)
- [ ] `StorageProcessManager` implemented with transitional `GlobalContextClusterOpHandler` shim (TASK-S002)
- [ ] `ClusterMaster` no longer contains `_pendingSerializeTasks`, `SerializeLocalTask`, or `HandleSerializeLocalCompletion` (TASK-S002)
- [ ] `ClusterMaster` contains zero calls to `PullToNasAsync` in SerializeLocal path (TASK-S002)
- [ ] All unit tests pass (min 9 new tests)
- [ ] Integration test `ScenarioSaveLoadTests.OrchestratorContextRestored_AfterLoad` passes
- [ ] `dotnet build Hrot/Subsystems/Hrot.Orchestrator/` compiles clean
- [ ] Report submitted to `.dev/cluster-master-refact/reports/BATCH-01-REPORT.md`

---

## 📊 Report Requirements

Include in your report:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did the `TryAggregate()` method in `ClusterMaster` cover `SerializeLocal` ops, or did you need a different approach? What did you find and what did you do?

**Q3:** Are there any weak points in `ClusterMaster`'s SerializeLocal path you noticed beyond what the tasks covered? What would you fix?

**Q4:** What design decisions did you make beyond the task spec? What alternatives did you consider?

**Q5:** Any edge cases discovered during implementation not mentioned in the spec?

**Suggested commit message:** What did you achieve in this batch?

---

## 📚 Reference Materials

- **Task Defs:** `.dev/cluster-master-refact/TASK-DETAIL.md` -- TASK-S001, TASK-S002
- **Design:** `.dev/cluster-master-refact/DESIGN.md` -- Background, Architecture Pattern, Cross-Cutting section, § Phase 1.1, § Phase 1.2
- **Onboarding:** `.dev/cluster-master-refact/ONBOARDING.md`
- **Aggregator example:** `Hrot/Subsystems/Hrot.Orchestrator/ReplayConsensusAggregator.cs`
- **Process Manager example:** `Hrot/Subsystems/Hrot.Orchestrator/ReplayProcessManager.cs`
- **Aggregator interface:** `Hrot/Subsystems/Hrot.Orchestrator/INodeResponseAggregator.cs`
- **Wiring example:** `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs`
