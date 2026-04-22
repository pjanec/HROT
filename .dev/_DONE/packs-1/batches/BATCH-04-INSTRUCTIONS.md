# BATCH-04: Phase 5 Orchestration CQRS Cleanup + Corrective (DEBT-006)

**Batch Number:** BATCH-04
**Tasks:** DEBT-006 (Corrective — delete vestigial MissionControlRequestSystem), PACK-C001, PACK-C002
**Phase:** Phase 5 (Orchestration Domain CQRS Cleanup)
**Estimated Effort:** 14–17 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 ✅, BATCH-02 ✅, BATCH-03 ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

**Corrective Task (DEBT-006 — P2) — Delete vestigial MissionControlRequestSystem:**
The original `MissionControlRequestSystem` was refactored in BATCH-03 but the old file was
left in the codebase unwired. It must be deleted before this batch proceeds to avoid confusion
with the new `MissionControlExecutionSystem`.

**Phase 5 — Orchestration Domain CQRS Cleanup:**

**PACK-C001 — Purify ClusterMaster:** `ClusterMaster` currently has two DDS-based constructors
that start a `DdsIdAllocatorServer` background thread and initialize seven DDS readers/writers.
These must be completely removed. Only the bus-based constructor survives. Additionally:
- Define `AssetInventoryUpdateEvent` (`[EventId(9017)]`) and publish it from `PublishAssetInventory()`.
- Update `ClusterOpMasterTranslator` to consume `AssetInventoryUpdateEvent` and write DDS.

**PACK-C002 — Purify ClusterUiCache + Create OrchestrationObserverTranslator:**
`ClusterUiCache` currently holds seven DDS readers. Remove all of them — the class accepts only
`FdpEventBus`. Create `OrchestrationObserverTranslator` in `Hrot.Common/Orchestration/` to hold
the seven readers and bridge them onto the bus. Update ExCon wiring to use the new 3-component
wiring pattern:
```csharp
_orchestrationBus = new FdpEventBus();
_orchestrationObserverTranslator = new OrchestrationObserverTranslator(_participant, _orchestrationBus);
_uiCache = new ClusterUiCache(_orchestrationBus, _slaveSyncController);
```
Also define `SystemStateUpdateEvent` (`[EventId(9016)]`).

### Required Reading (IN ORDER)

1. **Developer Workflow Guide:** `.github/skills/developer/SKILL.md`
2. **Architecture & Design:** `.dev/packs-1/DESIGN.md` — read §Phase 5 (§5.A and §5.B) fully
3. **Task Specifications:** `.dev/packs-1/TASK-DETAIL.md` — sections PACK-C001 and PACK-C002
4. **Previous Reviews:** `.dev/packs-1/reviews/BATCH-03-REVIEW.md`
5. **Debt Tracker:** `.dev/packs-1/DEBT-TRACKER.md` — see DEBT-006

### Source Code Locations

**Corrective:**
- `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` — DELETE this file
- Check for any remaining references to `MissionControlRequestSystem` in tests or modules

**PACK-C001:**
- `Hrot.Orchestrator/ClusterMaster.cs` — major purge target
- `Hrot.Orchestrator/Events/ClusterCqrsEvents.cs` — add `AssetInventoryUpdateEvent [EventId(9017)]`
- `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs` — add inventory event consumption

**PACK-C002:**
- `Hrot.ClusterRunner/Services/ClusterUiCache.cs` — remove 7 DdsReader fields
- `Hrot.Orchestrator/Events/ClusterCqrsEvents.cs` — add `SystemStateUpdateEvent [EventId(9016)]`
- `Hrot.Common/Orchestration/` — create `OrchestrationObserverTranslator.cs` here
- ExCon wiring file — grep for `ClusterUiCache` constructor calls to find the wiring site

### Test Projects

- `Hrot.Orchestrator.Tests/` — unit tests for ClusterMaster
- `Hrot.Orchestrator.Integration.Tests/` — integration tests (must pass after purge)
- `Hrot.ClusterRunner.Tests/` — tests for ClusterScenarioPanel and ClusterUiCache
- `Hrot.ClusterRunner.Integration.Tests/` — integration tests (smoke)

### Report Submission

**When done, submit your report to:**
`.dev/packs-1/reports/BATCH-04-REPORT.md`

**If you have questions, create:**
`.dev/packs-1/questions/BATCH-04-QUESTIONS.md`

---

## 🔄 Mandatory Workflow: Test-Driven Task Progression

```
1. READ the task detail in TASK-DETAIL.md (understand WHY, not just WHAT)
2. READ the relevant source files before touching anything
3. WRITE the test(s) first — watch them FAIL
4. IMPLEMENT the minimum code to make tests PASS
5. VERIFY: dotnet test [relevant project] — ALL tests must pass
6. Only then move to the next task
```

**Never skip tests. Never fake assertions. Tests must check real logic/values/behavior.**

---

## 📌 Tasks

### Order of Execution

```
DEBT-006 corrective  →  PACK-C001  →  PACK-C002
```

---

### DEBT-006 Corrective — Delete MissionControlRequestSystem

**Priority:** P2 — must be done before starting PACK-C001.

**Steps:**
1. Delete `Hrot.SimHost/Systems/MissionControlRequestSystem.cs`.
2. Grep for all remaining references to `MissionControlRequestSystem` (class name).
   Update or remove them (should only be in tests or module wiring, not production logic).
3. Verify `dotnet build` succeeds after deletion.

**No new tests required** — the successor `MissionControlExecutionSystemTests.cs` already covers the replacement.

---

### PACK-C001 — Purify ClusterMaster (Remove DDS Constructors and Fallback Paths)

See: `TASK-DETAIL.md#pack-c001`

**Summary of deletions from ClusterMaster.cs:**
- Delete `ClusterMaster(DdsParticipant)` and `ClusterMaster(DdsParticipant, ClusterConfiguration)` constructors entirely
- Delete all DDS fields: `_systemStateWriter`, `_heartbeatReader`, `_sysOpRequestReader`, `_sysOpStatusWriter`, `_nodeOpStatusReader`, `_nodeOpWriterCache`, `_nodeOpParticipant`, `_inventoryWriter`
- Delete `_idAllocatorServer`, `_idServerCts`, `_idServerThread`
- Delete DDS polling branches in `Tick()`, `IngestHeartbeats()`, `ConsumeNodeOpStatuses()`, `PublishOpStatus()`, `PublishClusterState()`, `FanOutNodeOp()`, `EjectNode()`, `Dispose()`
- Consolidate 2PC ACK logic into the `_eventBus.ConsumeManaged<NodeOpCompletedEvent>()` loop only

**Additions to ClusterMaster.cs:**
- `PublishAssetInventory()` publishes `AssetInventoryUpdateEvent` on the bus instead of writing DDS directly

**New in ClusterCqrsEvents.cs:**
- `AssetInventoryUpdateEvent` with `[EventId(9017)]` and `[DataPolicy(DataPolicy.NoRecord)]`

**New in ClusterOpMasterTranslator.cs:**
- Consume `AssetInventoryUpdateEvent` → call `_inventoryWriter.Write(...)`

**Resulting constructor signature:**
```csharp
public ClusterMaster(FdpEventBus eventBus, ClusterConfiguration? config = null)
```

**Key constraints:**
- `_eventBus` must be `private readonly FdpEventBus _eventBus;` (non-nullable, no `!`)
- Zero references to `CycloneDDS.Runtime` or any `Hrot.NED` namespace in `ClusterMaster.cs` after done
- All non-test callers of deleted constructors must be updated

**Tests to write:**
1. Compile gate: zero `DdsParticipant` references in `ClusterMaster.cs`.
2. `new ClusterMaster(eventBus)` constructs without exception.
3. `PublishAssetInventory(...)` publishes `AssetInventoryUpdateEvent` with correct field values.
4. `ConsumeNodeOpStatuses` processes both ACK types (branch task + episode task) via bus events.
5. Existing orchestrator integration tests pass unchanged.

---

### PACK-C002 — Purify ClusterUiCache + Create OrchestrationObserverTranslator

See: `TASK-DETAIL.md#pack-c002`

**Summary:**

**ClusterUiCache changes:**
- Remove all 7 `DdsReader<T>` fields
- New constructor: `ClusterUiCache(FdpEventBus bus, ITimeController? localTimeController = null)`
- `Update()` loop switches from DDS `Take()` to `_bus.ConsumeManaged<T>()`:
  - `SystemStateTopic` DDS → `SystemStateUpdateEvent`
  - `AssetInventoryTopic` DDS → `AssetInventoryUpdateEvent`
  - `NodeHeartbeat` DDS → `NodeHeartbeatEvent`
  - `SwitchTimeModeWireDto` DDS → `SwitchTimeModeEvent`
  - `ClusterOpStatus` DDS → `ClusterOpCompletedEvent`
  - `NodeOpCommand` DDS → `ExecuteNodeOpIntent`
  - `NodeOpStatus` DDS → `NodeOpCompletedEvent`
- `Process2PcNetworkTraffic()` switches from `JsonDocument.Parse` to typed `DomainPayload`

**New OrchestrationObserverTranslator** in `Hrot.Common/Orchestration/`:
- Holds all 7 `DdsReader<T>` fields removed from `ClusterUiCache`
- `Tick()` polls DDS and publishes events to `FdpEventBus`
- Constructor: `OrchestrationObserverTranslator(DdsParticipant participant, FdpEventBus bus)`
- Forwards `NodeOpCommand` messages promiscuously (all nodes, not just local)

**New `SystemStateUpdateEvent`** in `ClusterCqrsEvents.cs`:
- `[EventId(9016)]`, `[DataPolicy(DataPolicy.NoRecord)]`

**ExCon wiring update:**
- The ExCon/ClusterRunner subsystem currently creates `DdsReader`s in `ClusterUiCache` — update to:
  - Create `FdpEventBus _orchestrationBus`
  - Create `OrchestrationObserverTranslator`
  - Pass `_orchestrationBus` to `ClusterUiCache` constructor

**Key constraints:**
- `ClusterUiCache.cs` must have zero `CycloneDDS.Runtime` references after the change
- `FdpEventBus` in ExCon is standalone — no `ModuleHostKernel` required
- `OrchestrationObserverTranslator` must forward ALL `NodeOpCommand` messages

**Tests to write:**
1. Unit: `ClusterUiCache` with mock bus — publish `SystemStateUpdateEvent`; tick; assert `CurrentState` updated.
2. Unit: inventory update — publish `AssetInventoryUpdateEvent`; tick; assert `AvailableScenarios` updated.
3. Unit: 2PC without JSON parsing — publish `ExecuteNodeOpIntent` with typed payload; tick; assert history entry created.
4. Integration: ExCon UI updates on DDS message (via `OrchestrationObserverTranslator`).
5. Compile gate: `ClusterUiCache.cs` has zero `DdsReader`/`DdsWriter`/`DdsParticipant` references.

---

## ✅ Batch Success Criteria

1. All tasks implemented per TASK-DETAIL.md and corrective spec above.
2. All tests with real behavioral assertions pass.
3. `dotnet build` succeeds for the full solution (0 errors).
4. `dotnet test` succeeds for:
   - `Hrot.Orchestrator.Tests/`
   - `Hrot.Orchestrator.Integration.Tests/`
   - `Hrot.ClusterRunner.Tests/`
   - `Hrot.ClusterRunner.Integration.Tests/` (smoke — no new failures beyond pre-existing flakies)
5. `ClusterMaster.cs` has zero `CycloneDDS.Runtime`/`Hrot.NED` code references.
6. `ClusterUiCache.cs` has zero `DdsReader`/`DdsWriter`/`DdsParticipant` references.
7. `MissionControlRequestSystem.cs` deleted; zero workspace references to it.

---

## 💡 Developer Insights Section

In your report, please explicitly answer:

1. **What issues were encountered?** (compile errors, unexpected dependencies, etc.)
2. **What weak points were spotted in the codebase?** (fragile patterns, missing abstractions)
3. **What design decisions were made beyond the spec?** (choices you made to resolve ambiguities)
4. **Did any test reveal something unexpected about the current behavior?**

---

## 📄 Report Format

Submit to `.dev/packs-1/reports/BATCH-04-REPORT.md`:

```markdown
# BATCH-04 Report

## Status
[COMPLETE / PARTIAL]

## Tasks Completed
- DEBT-006 Corrective: [summary]
- PACK-C001: [summary]
- PACK-C002: [summary]

## Test Results
[Paste dotnet test summary output]

## Developer Insights
### Issues Encountered
### Weak Points Spotted
### Design Decisions Beyond Spec
### Unexpected Findings from Tests

## Files Changed
[List]
```
