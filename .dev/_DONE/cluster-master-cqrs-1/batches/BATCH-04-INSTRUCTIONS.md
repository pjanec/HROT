# BATCH-04: ClusterMaster Event Bus Integration

**Batch Number:** BATCH-04  
**Tasks:** CMC-S008, CMC-S009, CMC-S010 (Phase 4), plus DEBT-002 and DEBT-003  
**Phase:** Phase 4 — ClusterMaster Event Bus Integration  
**Estimated Effort:** 18–22 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-03 completed (ClusterSlave is now fully bus-based; IOrchestrationTransport deleted)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch transforms `ClusterMaster` to follow the same clean architecture as `ClusterSlave`. After this batch, `ClusterMaster` will have zero DDS references, zero JSON parsing, and will communicate exclusively via strongly-typed `FdpEventBus` events. This is the most complex batch so far — `ClusterMaster.cs` is ~1490 lines and there are 8 test files that must be rewritten.

**Do not stop for questions unless you encounter a hard design contradiction.** If a small design decision is needed, make the most defensible choice and record it in the report's insights section.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/cluster-master-cqrs-1/DESIGN.md` — §5 (ClusterMaster Refactoring), §3.2 (Event Bus DTOs), §7.3 (Test Harness)
2. **Task Definitions:** `.dev/cluster-master-cqrs-1/TASK-DETAIL.md` — See CMC-S008, CMC-S009, CMC-S010
3. **Previous Review:** `.dev/cluster-master-cqrs-1/reviews/BATCH-03-REVIEW.md` — Learn from feedback
4. **Previous Report:** `.dev/cluster-master-cqrs-1/reports/BATCH-03-REPORT.md` — Developer insights

### Source Code Locations

- **Primary Work Area:** `Hrot.Orchestrator/ClusterMaster.cs`, `Hrot.Orchestrator/TransitionPlanner.cs`
- **Test Project:** `Hrot.Orchestrator.Tests/` (8 files all require update)
- **Handler payloads:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Handlers/*.cs` (see existing `*HandlerPayload` types)
- **Bus event definitions:** `FDP/Toolkits/FDP.Toolkit.Orchestration/Events/ClusterCqrsEvents.cs`
- **Tech debt items:** `Hrot.ClusterRunner/Services/ExConSubsystem.cs`, `FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs`

### Report Submission

**When done, submit your report to:**  
`.dev/cluster-master-cqrs-1/reports/BATCH-04-REPORT.md`

**If you have questions, create:**  
`.dev/cluster-master-cqrs-1/questions/BATCH-04-QUESTIONS.md`

---

## Context

Phase 3 (BATCH-03) completed ClusterSlave's transition to FdpEventBus. ClusterMaster is the mirror operation: it currently uses DDS readers/writers for all I/O and `JsonDocument.Parse` for payload extraction. After this batch, the 2PC loop between ClusterMaster and ClusterSlave works entirely in memory (AllInOne mode) with no DDS, no JSON.

The translators that bridge DDS ↔ FdpEventBus are Phase 5 work (BATCH-05). For now, **ClusterMaster is tested directly via the bus** — tests push typed intents to `FdpEventBus` and assert typed events from `FdpEventBus`.

**Related Tasks:**
- [CMC-S008](../TASK-DETAIL.md#cmc-s008--remove-dds-from-clustermaster-ingress) — ClusterMaster ingress
- [CMC-S009](../TASK-DETAIL.md#cmc-s009--remove-dds-from-clustermaster-egress) — ClusterMaster egress
- [CMC-S010](../TASK-DETAIL.md#cmc-s010--remove-json-parsing-from-clustermaster-and-handlers) — JSON removal

---

## 🎯 Batch Objectives

1. `ClusterMaster` constructor accepts `FdpEventBus` instead of `DdsParticipant`
2. `ClusterMaster.Tick()` drains typed intents from the bus instead of polling DDS readers
3. `ClusterMaster` publishes `ExecuteNodeOpIntent`, `ClusterOpCompletedEvent`, etc. to the bus instead of writing to DDS
4. `TransitionPlanner.PlanTrajectory` accepts `TransitionStateIntent` instead of `ClusterOpRequest`
5. Zero `JsonDocument.Parse` calls in `ClusterMaster.cs` and `TransitionPlanner.cs`
6. All `Hrot.Orchestrator.Tests` pass with the new bus-based API
7. DEBT-002 and DEBT-003 resolved

---

## ✅ Tasks

---

### Task 0: Carry-In Tech Debt (DEBT-002, DEBT-003)

These are small P3 fixes — do them before the main work to keep diffs clean.

**DEBT-002:** `ExConSubsystem.cs` uses magic string `"ExCon"` for ClusterSlave subsystem name.  
`Hrot.ClusterRunner/Services/ExConSubsystem.cs` — find the constructor call `new ClusterSlave(iosNodeId, "ExCon")` and replace `"ExCon"` with a `private const string SubsystemName = "ExCon"` constant defined in the class.

**DEBT-003:** `ClusterSlave` test constructor `ClusterSlave(FdpEventBus? eventBus = null)` uses hard-coded `nodeId = 0`, `subsystemName = "TestNode"` invisibly.  
`FDP/Toolkits/FDP.Toolkit.Orchestration/ClusterSlave.cs` — update the test-only constructor to accept explicit `nodeId` and `subsystemName` params with defaults:
```csharp
// Before
public ClusterSlave(FdpEventBus? eventBus = null)
// After  
public ClusterSlave(FdpEventBus? eventBus = null, int nodeId = 0, string subsystemName = "TestNode")
```
Update the body to use the parameters. Update any call sites if needed (all test call sites that use only `eventBus:` argument remain valid as is).

---

### Task 1: CMC-S008 — ClusterMaster Ingress (Consume from FdpEventBus)

**File:** `Hrot.Orchestrator/ClusterMaster.cs`  
**Task Definition:** See [TASK-DETAIL.md CMC-S008](../TASK-DETAIL.md#cmc-s008--remove-dds-from-clustermaster-ingress)

**What to build:**

1. **Add constructor:**
   ```csharp
   public ClusterMaster(FdpEventBus eventBus, ClusterConfiguration? config = null)
   {
       _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
       _config   = config ?? ClusterConfiguration.Default;
       _history  = new DistributedTransaction[Math.Max(1, _config.TransactionHistoryCapacity)];
       if (_config.Mandatory.Length == 0) { _bootstrapLatch = true; PublishStandby(); }
       _idAllocatorServer = null!; // no DDS Id server without participant
       _idServerCts       = null!;
       _idServerThread    = null!;
   }

   private readonly FdpEventBus? _eventBus;
   ```
   The old `ClusterMaster(DdsParticipant, ...)` constructors can stay for now if the integration tests need them (see `ScenarioSaveLoadTests.cs`). Check if integration tests use it — if they do, keep the old constructor. Focus unit tests on the new bus constructor.

2. **Replace `IngestHeartbeats()`** — use `_eventBus?.ConsumeManaged<NodeHeartbeatEvent>()`:
   ```csharp
   private void IngestHeartbeats()
   {
       if (_eventBus == null) return;
       foreach (var hb in _eventBus.ConsumeManaged<NodeHeartbeatEvent>())
       {
           var profile = new NodeHealthProfile
           {
               NodeId = hb.NodeId,
               SubsystemName = hb.SubsystemName ?? string.Empty,
               LocalClusterState = (ClusterState)hb.LocalStateId,
               LastHeartbeatUtcSeconds = UtcNowSeconds(),
           };
           _roster.Upsert(profile);
       }
   }
   ```

3. **Replace `ProcessClusterOpRequests()`** — consume specific intent types from bus. The monolithic `ProcessSingleClusterOpRequest(ClusterOpRequest)` must be split into type-specific handlers. The existing business logic stays, just the input type changes:
   - `ConsumeManaged<TransitionStateIntent>()` → new private `ProcessTransitionStateIntent(TransitionStateIntent)`
   - `ConsumeManaged<ManageEpisodeIntent>()` → new private `ProcessManageEpisodeIntent(ManageEpisodeIntent)`
   - `ConsumeManaged<SeekReplayIntent>()` → new private `ProcessSeekReplayIntent(SeekReplayIntent)`
   - `ConsumeManaged<CancelOperationIntent>()` → new private `ProcessCancelOperationIntent(CancelOperationIntent)`
   - `ConsumeManaged<ExecuteStorageOpIntent>()` → new private `ProcessStorageOpIntent(ExecuteStorageOpIntent)`
   
   **Key mapping** from `ClusterOpRequest.OperationType` checks to typed methods:
   - `ClusterOpType.TransitionState` → `ProcessTransitionStateIntent`
   - `ClusterOpType.SaveScenario` → `ProcessStorageOpIntent` (StorageOpType.SaveScenario)
   - `ClusterOpType.ManageEpisode` → `ProcessManageEpisodeIntent`
   - `ClusterOpType.ReplaySeek` → `ProcessSeekReplayIntent`
   - `ClusterOpType.ExportArchive` → `ProcessStorageOpIntent` (StorageOpType.Export)
   - `ClusterOpType.ImportArchive` → `ProcessStorageOpIntent` (StorageOpType.Import)
   - `ClusterOpType.PrefetchScenario` → `ProcessStorageOpIntent` (StorageOpType.SaveScenario with prefetch=true — or just inline prefetch logic)
   - Time control ops `PauseTime/ResumeTime/StepTime/SetTimeScale` → **for now keep these via `HandleClusterOpRequest` injection path; do NOT consume via bus (Phase 5 will add time-control intents)**

4. **Replace `ConsumeNodeOpStatuses()`** — use `_eventBus?.ConsumeManaged<NodeOpCompletedEvent>()`. The internal correlation/ACK logic (`_pendingSerializeTasks`, `_pendingBranchTasks`, `_pendingTransactionTasks`) stays exactly as is; only the source changes from `_nodeOpStatusReader.Take()` to `ConsumeManaged<NodeOpCompletedEvent>()`.

5. **Remove `_heartbeatReader`, `_sysOpRequestReader`, `_nodeOpStatusReader` fields** from the bus-based constructor. The DDS-based constructor may still create them.

6. **Update `Tick()`:**
   ```csharp
   public void Tick()
   {
       IngestHeartbeats();           // bus or DDS based on constructor
       CheckBootstrapLatch();
       DetectAndEjectTimedOutNodes();
       DrainPendingPrefetch();
       DrainInjectedRequests();
       if (_eventBus != null) { ConsumeTransitionStateIntents(); ConsumeManageEpisodeIntents(); ... }
       else ProcessClusterOpRequests(); // old DDS path (for integration tests using DdsParticipant)
       ConsumeNodeOpStatuses();
       ...
   }
   ```

**Existing `HandleClusterOpRequest(ClusterOpRequest)` method MUST STAY** — some integration tests and UI panels use it. This is the "injected request" path that does not go through DDS. After the bus constructor is in place, the intent-typed equivalents are the primary path.

**Success Conditions:**
- Unit test (new): `ClusterMaster` constructed with `FdpEventBus`. Publish `NodeHeartbeatEvent` to bus, tick once. Assert node appears in `NodeRoster`.
- Unit test (new): Publish `TransitionStateIntent { TargetState = ClusterState.LoadingLive }`. Tick. Assert `ExecuteNodeOpIntent` with `Operation = NodeOpType.PrepareLive` appears on bus.
- `dotnet build IOS-IG-SimHost.sln` succeeds.

---

### Task 2: CMC-S009 — ClusterMaster Egress (Publish to FdpEventBus)

**File:** `Hrot.Orchestrator/ClusterMaster.cs`  
**Task Definition:** See [TASK-DETAIL.md CMC-S009](../TASK-DETAIL.md#cmc-s009--remove-dds-from-clustermaster-egress)

**What to build:**

1. **Replace `FanOutNodeOp`** — instead of creating per-node `DdsWriter` and calling `.Write(NodeOpCommand)`, publish `ExecuteNodeOpIntent` to the bus for each target node:
   ```csharp
   private void FanOutNodeOp(NodeOpType operation, IEnumerable<int> targetNodeIds,
       Guid transactionId, object? domainPayload = null)
   {
       foreach (var nodeId in targetNodeIds)
       {
           _eventBus?.PublishManaged(new ExecuteNodeOpIntent
           {
               TransactionId = transactionId,
               TargetNodeId  = nodeId,
               Operation     = operation,
               DomainPayload = domainPayload,
           });
       }
   }
   ```
   Update all call sites to pass `NodeOpType` + `domainPayload` instead of `NodeOpCommand`.

2. **Replace `_sysOpStatusWriter.Write(ClusterOpStatus {...})`** with `_eventBus?.PublishManaged(new ClusterOpCompletedEvent { RequestId = ..., StatusCode = ..., ResultPayload = null })`.  
   `ClusterOpCompletedEvent` already has `object? ResultPayload` — use `null` for now (Phase 5 will populate it from the manifest).

3. **Replace `_systemStateWriter.Write(SystemStateTopic {...})`** with `_eventBus?.PublishManaged(new ClusterStateTransitionedEvent { ... })`.  
   If `ClusterStateTransitionedEvent` doesn't yet exist in `ClusterCqrsEvents.cs`, add it:
   ```csharp
   [EventId(9015)]
   [DataPolicy(DataPolicy.NoRecord)]
   public struct ClusterStateTransitionedEvent
   {
       public int    NewStateId;   // ClusterState enum value
       public string SubsystemName; // "Cluster" (global state)
   }
   ```

4. **`_inventoryWriter`** — leave `PublishAssetInventory()` as-is for now (DDS path). This is background telemetry, not part of the 2PC protocol. Note this as tech debt in the report.

5. **Remove `_nodeOpWriterCache`, `_nodeOpParticipant`, `_sysOpStatusWriter`, `_systemStateWriter`** from the bus-based constructor path.

**DomainPayload mapping** for fan-out operations:
   - `CommitState`: `domainPayload = nextStateId` (int)
   - `PrepareLive`: `domainPayload = branchedExerciseId` (Guid)
   - `SerializeLocal`: `domainPayload = exerciseId` (string? from `ArchiveHandlerPayload`) — use `new ArchiveHandlerPayload(exerciseId)` where applicable
   - `PrefetchFiles`: `domainPayload = new PrefetchHandlerPayload(scenarioId)`
   - `StartEpisode` / `StopEpisode`: `domainPayload = new EpisodeHandlerPayload(episodeId, scenarioId, isStart)`
   - All handlers already access `DomainPayload` via type-cast patterns — confirm they match.

**Success Conditions:**
- Unit test (new): After `TransitionStateIntent` with valid roster + bootstrap latch, assert `ClusterOpCompletedEvent` on bus with `StatusCode = OrchestrationStatusCode.Success` after all `NodeOpCompletedEvent`s consumed.
- `FanOutNodeOp` creates zero `DdsWriter<NodeOpCommand>` instances.

---

### Task 3: CMC-S010 — Remove JSON Parsing

**Files:** `Hrot.Orchestrator/ClusterMaster.cs`, `Hrot.Orchestrator/TransitionPlanner.cs`  
**Task Definition:** See [TASK-DETAIL.md CMC-S010](../TASK-DETAIL.md#cmc-s010--remove-json-parsing-from-clustermaster-and-handlers)

**What to build:**

1. **Update `TransitionPlanner.PlanTrajectory`** signature:
   ```csharp
   // Before:
   public Queue<ISysOpStep> PlanTrajectory(ClusterState current, ClusterOpRequest request)
   // After:
   public Queue<ISysOpStep> PlanTrajectory(ClusterState current, TransitionStateIntent intent)
   ```
   The method body currently parses `request.PayloadJson` to extract `TargetState` and `ScenarioId`. After the change:
   - `TargetState` comes directly from `(ClusterState)(int)intent.TargetState` (cast FDP enum to Hrot enum)
   - `ScenarioId` comes from `intent.ScenarioId`
   - Remove all `JsonDocument.Parse(...)`, `int.TryParse(...)`, and `doc.RootElement.TryGetProperty(...)` calls
   - The `OperationStep.PayloadJson` property on the planner's step objects: these are used by ClusterMaster to pass payload when fanning out. After this change, replace `OperationStep.PayloadJson` with `OperationStep.DomainPayload` (of type `object?`). Update the class definition.

2. **Remove JSON from `ClusterMaster.cs`:**
   - In `ProcessTransitionStateIntent(TransitionStateIntent intent)`:
     - `TimeMode` check: use `intent.TimeMode` directly (no JSON parse)
     - `ExerciseId` for branching: use `intent.ExerciseId` directly
     - `ScenarioId` for prefetch: use `intent.ScenarioId` directly
   - In `ProcessStorageOpIntent(ExecuteStorageOpIntent intent)`:
     - `ExerciseId` for archive/save: use `intent.ExerciseId` directly
   - Remove `using System.Text.Json;` from `ClusterMaster.cs` — once no `JsonDocument` calls remain

3. **DO NOT modify handler implementations** — they already use `DomainPayload` type-casts. The handlers were updated in Phase 2/3.

4. **Verify `TransitionPlannerTests.cs`** still passes — the planner tests may inject `ClusterOpRequest`; update them to inject `TransitionStateIntent` if needed.

**Grep-check (must be zero):**
```
grep -r "JsonDocument\|PayloadJson\|TryGetProperty" Hrot.Orchestrator/ClusterMaster.cs
grep -r "JsonDocument\|PayloadJson\|TryGetProperty" Hrot.Orchestrator/TransitionPlanner.cs
```

---

### Task 4: Update All ClusterMaster Unit Tests

**Files:** All 8 files in `Hrot.Orchestrator.Tests/`  
All existing tests use `new ClusterMaster(DdsParticipant, ...)` and `HandleClusterOpRequest(ClusterOpRequest)`.
After this batch they must use `new ClusterMaster(FdpEventBus bus, ...)` and publish typed intents to the bus.

**New test pattern:**
```csharp
var bus = new FdpEventBus();
using var exercise = new ClusterMaster(bus, NoMandatoryConfig());

// Inject heartbeat to register a node
bus.PublishManaged(new NodeHeartbeatEvent
    { NodeId = 1, LocalStateId = (int)ClusterState.Idle, SubsystemName = "SimHost", WallTicksUtc = DateTimeOffset.UtcNow.Ticks });
bus.SwapBuffers();
exercise.Tick(); // ingests heartbeat, bootstrap latch fires (no mandatory nodes)
bus.SwapBuffers();

// Push intent
bus.PublishManaged(new TransitionStateIntent
    { TransactionId = Guid.NewGuid(), TargetState = FDP.Toolkit.Orchestration.ClusterState.LoadingLive });
bus.SwapBuffers();
exercise.Tick();
bus.SwapBuffers();

// Assert fan-out
var intents = bus.ConsumeManaged<ExecuteNodeOpIntent>().ToList();
Assert.Single(intents);
Assert.Equal(NodeOpType.PrepareLive, intents[0].Operation);
Assert.Equal(1, intents[0].TargetNodeId);
```

**When you need to simulate receiving a NodeOpCompletedEvent** (simulate slave ACK):
```csharp
bus.PublishManaged(new NodeOpCompletedEvent
    { TransactionId = txId, NodeId = 1, StatusCode = OrchestrationStatusCode.Success, IsParticipating = true });
bus.SwapBuffers();
exercise.Tick(); // correlates ACK
bus.SwapBuffers();
var completed = bus.ConsumeManaged<ClusterOpCompletedEvent>().ToList();
```

**Files to update:**
- `ClusterMasterArchiveTests.cs` — use bus; verify `ExecuteNodeOpIntent { Operation = NodeOpType.SerializeLocal }` + `NodeOpCompletedEvent` ACK path
- `ClusterMasterBootstrapTests.cs` — inject heartbeats via bus; same logic
- `ClusterMasterContextHandlerTests.cs` — update construction
- `ClusterMasterEpisodeTests.cs` — use `ManageEpisodeIntent`
- `ClusterMasterFanOutTests.cs` — use `TransitionStateIntent`; assert `ExecuteNodeOpIntent` on bus  
- `ClusterMasterPrefetchTests.cs` — use `ExecuteStorageOpIntent { Operation = StorageOpType.SaveScenario }`
- `ClusterMasterReplayTests.cs` — use `SeekReplayIntent`, `TransitionStateIntent`
- `ClusterMasterTimeControlTests.cs` — time-control ops still use `HandleClusterOpRequest` (not converted in this batch); smaller update scope
- `TransitionPlannerTests.cs` — update to pass `TransitionStateIntent` to `PlanTrajectory`

**Note on enum casting:**  
Tests in `Hrot.Orchestrator.Tests` are in the Hrot application layer and may reference both `Hrot.NED.Descriptors.Orchestration.*` and `FDP.Toolkit.Orchestration.*` enums. Use explicit aliases if needed:
```csharp
using FdpClusterState = FDP.Toolkit.Orchestration.ClusterState;
using HrotClusterState = Hrot.NED.Descriptors.Orchestration.ClusterState;
```
When publishing `TransitionStateIntent`, use `FDP.Toolkit.Orchestration.ClusterState`.
When checking `NodeHealthProfile.LocalClusterState`, it remains `Hrot.NED.Descriptors.Orchestration.ClusterState`.

---

## 🧪 Testing Requirements

### Test-Driven Task Progression (MANDATORY)

**You MUST follow this workflow for every sub-task:**

1. **Write a failing test FIRST** that demonstrates the expected new behavior.
2. **Run the test** — confirm it fails with the expected error.
3. **Implement the change** — minimum code needed to pass the test.
4. **Run the test** — confirm it passes.
5. **Run the full suite** — `dotnet test Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj`
6. **Fix any regressions before proceeding.**

### Minimum Test Coverage

- All 8 test files in `Hrot.Orchestrator.Tests` must pass (currently 67 tests — you should maintain or increase this count)
- At least 2 new tests covering the bus-based ingress/egress paths from a clean integration perspective:
  1. `ClusterMaster_ReadsHeartbeat_FromBus_UpdatesRoster`
  2. `ClusterMaster_TransitionState_FullRound_Trip_ViaEventBus` — push `TransitionStateIntent`, simulate slave ACK via `NodeOpCompletedEvent`, assert `ClusterOpCompletedEvent` on bus
- Node fan-out tests must check `ExecuteNodeOpIntent` on bus (not DDS `NodeOpCommand`)

### Test Quality Requirements

- Tests must assert **actual values** (`NodeId`, `Operation`, `StatusCode`, `IsParticipating`), not just that event exists
- Do not test string existence or just compilation
- Do not swallow exceptions silently in tests

---

## 📊 Report Requirements

Submit your report to `.dev/cluster-master-cqrs-1/reports/BATCH-04-REPORT.md`.

**The report MUST include answers to these specific questions:**

**Q1: What issues did you encounter? How did you resolve them?**
Be specific about any coupling discovered in ClusterMaster that wasn't obvious from the spec.

**Q2: What weak points did you spot in the existing codebase?**
Focus especially on `ClusterMaster` internals — what makes it fragile now and what would be cleaner post-refactor?

**Q3: What design decisions did you make beyond these instructions?**
Specifically: how did you handle the DdsParticipant-based constructor (keep or remove?), how did you handle `PublishAssetInventory`, and how did you handle time-control ops?

**Q4: What edge cases did you discover?**
Any race conditions, ordering dependencies between bus events, or gaps in the spec?

**Q5: Are there any performance or correctness concerns about the new bus-based fan-out?**
Specifically: with `FanOutNodeOp` publishing one `ExecuteNodeOpIntent` per node, are there ordering or double-delivery risks?

---

## ⚠️ Known Constraints

1. `Hrot.Orchestrator.Integration.Tests` uses `ClusterMaster(DdsParticipant)` — do not break it. Keep the old DDS-based constructor. The integration tests do NOT need to be updated in this batch.
2. `PublishAssetInventory()` and `_inventoryWriter` can stay as DDS-based code for now — note as tech debt.
3. Time-control ops (`PauseTime`, `ResumeTime`, etc.) stay on the `HandleClusterOpRequest` path for now — bus-based time control is Phase 5.
4. The `_idAllocatorServer` / `_idServerThread` are DDS-based — keep them in the DDS constructor only. The bus-based constructor skips them.
