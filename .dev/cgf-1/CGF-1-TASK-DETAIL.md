# CGF-1 Task Detail Document

**Reference design:** [CGF-1-DESIGN.md](./CGF-1-DESIGN.md)  
**Tracking:** [CGF-1-TASK-TRACKER.md](./CGF-1-TASK-TRACKER.md)

> Every task includes a unique ID, a detailed description of work to be done, and
> explicit **success conditions** — usually a specification of unit/integration tests
> that must pass before the task can be considered done.
>
> **Architectural constraint (repeated for emphasis):**  
> FDP infrastructure (`Fdp.Kernel` and all `FDP.Toolkit.*`) must never reference any
> `Bagira.*` assembly. The Drill State Machine (`DSMState`, handler interfaces,
> `DsmStateChangedEvent`) lives entirely in the Bagira application layer.

---

## Phase 1 — Skeleton: Control-Plane Foundation

See [CGF-1-DESIGN.md §3](./CGF-1-DESIGN.md#3-phase-1--skeleton-control-plane-foundation).

---

### CGF1-S0101 — Orchestration DDS Schema Definition

**Design ref:** [§3.1](./CGF-1-DESIGN.md#31-stage-11--orchestration-dds-schema)

**Work to do:**
1. Create `Bagira.DDS.DataModel/Orchestration/OrchestrationMessages.cs`.
2. Declare `DSMState`, `SysOpType`, `NodeOpType`, `OpStatus` enumerations.
3. Declare all DDS topic structs: `SystemStateTopic`, `SysOpRequest`, `SysOpStatus`,
   `NodeOpCommand`, `NodeOpStatus`, `NodeHeartbeat`, `OrchestratorContextTopic`.
4. Apply correct `[DdsTopic]`, `[DdsIdlFile("bdc-sst-orchestration")]`, and
   `[DdsQos]` attributes per the design spec.
5. Ensure all structs are in namespace `Bagira.BDC.SSTD.Orchestration`.

**Success conditions:**
- `Bagira.DDS.DataModel.Tests` contains a new test class
  `OrchestrationSchemaTests` with the following tests:
  - `AllTopicStructsHaveDdsTopicAttribute` — reflection scan of all types in the
    `Bagira.BDC.SSTD.Orchestration` namespace; assert every `partial struct` has
    `[DdsTopic]` and `[DdsIdlFile("bdc-sst-orchestration")]`.
  - `DsmStateEnumHasExpectedValues` — assert `DSMState.Standby == 0`,
    `DSMState.RunningLive == 31`, `DSMState.Degraded == 99`.
  - `NodeHeartbeatHasDdsKeyOnNodeId` — assert the `NodeId` field of `NodeHeartbeat`
    bears `[DdsKey]`.
  - `SystemStateTopicQosIsDurableTransientLocal` — assert the `DdsQos` attribute on
    `SystemStateTopic` specifies `Durability = TransientLocal` and `HistoryDepth = 1`.
- All existing `Bagira.DDS.DataModel` tests continue to pass.
- Project builds with zero warnings on new code.

---

### CGF1-S0102 — Bagira.Orchestrator Bootstrapping

**Design ref:** [§3.2](./CGF-1-DESIGN.md#32-stage-12--bagiraorchestrator-bootstrapping)

**Work to do:**
1. Create the `Bagira.Orchestrator` C# project (net8.0; references
   `Bagira.DDS.DataModel`, `Fdp.Kernel`, FDP toolkit projects as needed).
2. Implement `DrillMaster` class:
   - Subscribes to `NodeHeartbeat`; maintains `Dictionary<int, NodeHealthProfile>`.
   - Publishes `SystemStateTopic { CurrentState = DSMState.Standby }` on startup.
   - Exposes a `Tick()` method called by the application loop (BeforeSync phase).
3. Implement skeleton `DistributedTransaction` data class (no 2PC execution yet —
   that comes in Stage 2.1).
4. Implement skeleton `NodeRoster` (heartbeat pruning at > 5 s silence).
5. Add `Bagira.Orchestrator` as a subsystem in `Bagira.Runner` (activated by
   `--mode orchestrator` command-line flag or configuration).

> **Runner-only launch:** `Bagira.Orchestrator.Standalone` is removed; the Orchestrator
> is exclusively launched via `Bagira.Runner --mode orchestrator`.

**Success conditions:**
- New integration test `DrillMasterBootstrapTests.OrchestratorPublishesStandbyOnStartup`:
  - Spawn the Bagira.Orchestrator process (or run in-process via test harness).
  - An out-of-process DDS reader subscribes to `SystemStateTopic`.
  - Within 3 s wall-clock, assert exactly one sample is received with
    `CurrentState == DSMState.Standby` and `TransactionEpoch == 0`.
- All pre-existing tests continue to pass.

---

### CGF1-S0103 — Centralized Identity Migration

**Design ref:** [§3.3](./CGF-1-DESIGN.md#33-stage-13--centralized-identity-migration)

**Work to do:**
1. Remove `DdsIdAllocatorServer` registration from `Bagira.SimHost/SimHostApp.cs`.
2. Add `DdsIdAllocatorServer` registration inside `DrillMaster` (in-process, running
   inside `Bagira.Orchestrator`).
3. Verify `Bagira.SimHost` no longer holds a server instance — it only holds a
   `DdsIdAllocator` client.
4. Add a config flag to support running without an Orchestrator (for existing SimHost
   standalone mode during the transition period — falls back to hosting the server
   locally if no Orchestrator heartbeat seen within 5 s).

**Success conditions:**
- New integration test `DdsIdAllocatorMigrationTests.SimHostReceivesIdFromOrchestratorServer`:
  - Launch Bagira.Orchestrator (hosts the server).
  - Launch a SimHost instance (holds only the client).
  - Assert SimHost's `DdsIdAllocator` receives an ID batch and that the first
    allocated ID is `> 0`.
  - Assert `SimHostApp` contains no reference to `DdsIdAllocatorServer`.
- Existing `Bagira.SimHost.Integration.Tests` suite passes unmodified.

---

### CGF1-S0104 — DrillSlave Foundation

**Design ref:** [§3.4](./CGF-1-DESIGN.md#34-stage-14--drillslave-foundation)

**Work to do:**
1. Create `Bagira.CGF` project.
2. Implement `DrillSlave` in each subsystem:
   - `Bagira.SimHost/Modules/Orchestration/DrillSlave.cs`
   - `Bagira.IG/Modules/Orchestration/DrillSlave.cs`
   - `Bagira.IOS/Orchestration/DrillSlave.cs` (no-ECS variant — skips any
     `IDsmHandler` that requires `EntityRepository`)
   - `Bagira.CGF/Modules/Orchestration/DrillSlave.cs`
3. Each `DrillSlave`:
   - Publishes `NodeHeartbeat` at 1 Hz (wall-clock `Stopwatch`).
   - Receives `NodeOpCommand` on DDS network thread; enqueues to
     `ConcurrentQueue<PendingMainThreadAction>`.
   - Dequeues and dispatches during `Tick()` (BeforeSync phase).
4. Declare `IDsmHandler` interface in `Bagira.Runner` (or a shared `Bagira.Common`
   project — **NOT in any FDP project**).
5. Register both CGF and SimHost `DrillSlave` instances in their respective
   `Application.OnLoad()` methods.

**Success conditions:**
- New integration test `DrillSlaveHeartbeatTests.OrchestratorReceivesHeartbeatsFromBothNodes`:
  - Launch Orchestrator, SimHost, CGF (in-process via test harness or headless).
  - Within 2 s wall-clock, assert `DrillMaster.NodeRoster` contains both node IDs
    (SimHost and CGF).
  - Assert heartbeat `LocalDsmState == DSMState.Standby` for both.
- `IDsmHandler` interface is declared in a Bagira layer project. Verify no FDP project
  uses `typeof(IDsmHandler)` or references its namespace (csproj reference audit).
- All pre-existing tests pass.

---

### CGF1-S0105 — Orchestrator Health Monitoring & Bootstrap Recovery

**Design ref:** [§3.5](./CGF-1-DESIGN.md#35-stage-15--orchestrator-health-monitoring--bootstrap-recovery)

**Work to do:**
1. Create `Bagira.Orchestrator/ClusterConfiguration.cs`:
   ```csharp
   public class ClusterConfiguration
   {
       public string[] Mandatory { get; init; } = Array.Empty<string>();
       public string[] Optional  { get; init; } = Array.Empty<string>();
       public float    HeartbeatTimeoutSeconds    { get; init; } = 5f;
       public int      TransactionHistoryCapacity { get; init; } = 50;
   }
   ```
   Loaded at startup from `orchestrator-config.json` via `System.Text.Json`.
2. Add `_bootstrapLatch: bool` to `DrillMaster` (initially `false`):
   - Set to `true` once every subsystem name in `Mandatory` has appeared in the roster
     with `LocalDsmState == Standby`.
   - While `false`, return `OpStatus.Rejected` for all incoming `SysOpRequest` messages.
   - On becoming `true`, publish `SystemStateTopic { CurrentState = Standby }`.
3. Implement heartbeat timeout detection in `DrillMaster.Tick()`:
   - For each node in `NodeRoster.ActiveNodes`, if
     `secondsSinceLastHeartbeat > HeartbeatTimeoutSeconds`, call `EjectNode(nodeId)`.
4. Implement `EjectNode(int nodeId)`:  *(normative correction: wire type is `int`, matching `NodeHeartbeat.NodeId`)*
   - Cancel any active `DistributedTransaction` ACK wait for that node.
   - Remove from `NodeRoster.ActiveNodes`.
   - If the ejected node was mandatory:
     a. Abort the current transaction if in-flight.
     b. Publish `SystemStateTopic { CurrentState = Degraded }`.
     c. Broadcast `NodeOpCommand(AbortTransaction)` to surviving nodes.
     d. Broadcast `NodeOpCommand(PrepareState, Standby)` to surviving nodes only.
     e. Re-engage `_bootstrapLatch = false` until mandatory nodes return.
5. Add a `DistributedTransaction[]` ring buffer of capacity `TransactionHistoryCapacity`.
   Append every completed/aborted transaction to it.
6. Orchestrator ImGui panel changes:
   - Remove `WaitingRoomCoordinator` gate (Orchestrator boots unconditionally).
   - Render a "Waiting for mandatory nodes: X, Y" banner while `!_bootstrapLatch`.
   - Disable simulation control buttons while `!_bootstrapLatch`.
   - Add system-health table: `NodeId | SubsystemName | ms ago | LocalDsmState | CPU% | RAM`.
   - Add 2PC history table listing last N transactions with per-node ACK latency (ms).

**Success conditions (unit/integration tests in `Bagira.Orchestrator.Tests`):**
- `DrillMasterBootstrapTests.RejectsCommands_UntilMandatoryNodesReady`:
  - Configure `mandatory: ["SimHost"]`.
  - Send `SysOpRequest(TransitionState, LoadingLive)` before a SimHost heartbeat arrives.
  - Assert response is `OpStatus.Rejected`.
  - Deliver SimHost `NodeHeartbeat { LocalDsmState = Standby }`.
  - Assert the next `SysOpRequest(TransitionState, LoadingLive)` is accepted (not rejected).
- `DrillMasterBootstrapTests.EjectsMandatoryNode_EntersDegraded`:
  - Bootstrap with SimHost present.
  - Advance the heartbeat timer past `HeartbeatTimeoutSeconds`.
  - Assert `SystemStateTopic.CurrentState == Degraded` is published.
- `DrillMasterBootstrapTests.SurvivingNodes_CommandedToStandby_AfterEjection`:
  - Configure `mandatory: ["SimHost"]`; add CGF as `optional`.
  - Both nodes bootstrap; eject SimHost by timeout.
  - Assert CGF receives `NodeOpCommand(PrepareState, Standby)`.
  - Assert SimHost is **removed from the orchestrator roster** after ejection.
  - **Phase 1 broadcast note:** `BroadcastNodeOp` writes a single DDS sample without
    per-node key filtering; all domain participants receive the broadcast.  A second
    in-process participant representing the ejected node would still receive the sample.
    The normative guarantee that "SimHost does NOT receive any command after ejection"
    requires keyed per-node topics (Phase 2+).  The current test asserts roster eviction
    and command correctness; per-node delivery isolation is deferred to when per-node
    topic keys exist.
- `DrillMasterBootstrapTests.TransactionHistory_RecordsCompletedTransaction`:
  - Execute a successful `SysOpRequest(TransitionState, LoadingLive)` end-to-end.
  - Assert `DrillMaster.TransactionHistory` contains one entry with `IsAborted == false`.

#### ADR: Per-Node Keyed `NodeOpCommand` Topics (deferred to CGF-1-BATCH-09)

**Context:**
The current `BroadcastNodeOp` implementation writes a single `NodeOpCommand` DDS sample
to a non-keyed topic. All domain participants — including ejected nodes that lost their
roster entry — will still receive this sample because DDS reader filtering is keyed only
when a `[DdsKey]` attribute is applied. Phase 1 accepts this limitation: the test
`SurvivingNodes_CommandedToStandby_AfterEjection` asserts roster eviction and correct
command content, not delivery isolation.

**Decision: Defer to CGF-1-BATCH-09.**
Capacity was consumed by A.1 (DDS time-mode wiring) and S0205 (deterministic CI hookup).

**Proposed Phase 2 design:**

1. **Topic key:** Add `[DdsKey] public int TargetNodeId;` to `NodeOpCommand`. The
   orchestrator participant creates one `DdsWriter<NodeOpCommand>` per active roster
   entry, keyed by `TargetNodeId`. Leaf nodes create `DdsReader<NodeOpCommand>` with a
   content-or-instance filter matching their own `nodeId`.

2. **`DrillMaster` fan-out:** Replace `BroadcastNodeOp(NodeOpCommand)` with
   `FanOutNodeOp(NodeOpCommand template, IReadOnlyCollection<int> targetNodeIds)`.
   Arguments: a populated `NodeOpCommand` prototype and the roster subset to address.
   `FanOutNodeOp` iterates the targets, sets `template.TargetNodeId = nodeId`, and writes
   via a per-key writer (cache writers in `Dictionary<int, DdsWriter<NodeOpCommand>>`
   keyed by `nodeId`; create lazily on first fan-out to that node).

3. **Ejected-node isolation:** After `_roster.Remove(nodeId)`, the ejected node's writer
   key is disposed and removed from the cache. From that point forward, even if the
   ejected node's participant is still running, it will not receive any `NodeOpCommand`
   sample because no writer will write to its instance key.

4. **Test strategy:**
   - `SurvivingNodes_CommandedToStandby_AfterEjection` (updated): use two separate
     in-process `DdsParticipant` instances (one for CGF, one for ejected SimHost), each
     with a reader filtered to its own `nodeId`. Eject SimHost; assert CGF's reader
     yields one sample and SimHost's reader yields zero samples.
   - `FanOutNodeOp_WritesToCorrectKeys`: unit test with a mock `DdsWriter` that records
     written samples; assert only the expected `TargetNodeId` values appear.

5. **Migration note:** Adding `[DdsKey]` to `NodeOpCommand` is a breaking IDL change
   (historic samples change partition semantics). A domain restart is required when
   upgrading from the Phase 1 broadcast schema.

---

## Phase 2 — State & Time: DSM and Synchronization

See [CGF-1-DESIGN.md §4](./CGF-1-DESIGN.md#4-phase-2--state--time-dsm-and-synchronization).

---

### CGF1-S0201 — BFS Transition Planner

**Design ref:** [§4.1](./CGF-1-DESIGN.md#41-stage-21--bfs-transition-planner)

**Work to do:**
1. Implement `TransitionPlanner` class in `Bagira.Orchestrator/TransitionPlanner.cs`.
2. Define the complete directed adjacency list for all valid DSM edges (13 states,
   as listed in the design doc §4.1).
3. Implement `CalculateShortestPath(DSMState current, DSMState target)` via BFS.
4. Implement `PlanTrajectory(DSMState current, SysOpRequest request)` that:
   - Runs BFS and wraps each state in a `TransitionStep`.
   - Appends `OperationStep(ReplaySeek, TargetWallTicks)` when `target == RunningReplay`
     and `PayloadJson` contains a `TargetWallTicks` hint.
5. If BFS exhausts the frontier, throw `InvalidOperationException` with a descriptive
   message — **before** any DDS command is issued.
6. Wire `DrillMaster.Tick()` to call `TransitionPlanner.PlanTrajectory()` when a
   `SysOpRequest` arrives and the result feeds the active `DistributedTransaction`.

**Success conditions (pure unit tests — no DDS, no ECS):**
- `TransitionPlannerTests` in `Bagira.Orchestrator.Tests`:
  - `StandbyToLoadingEdit_Produces_SingleStep`: feed `Standby → LoadingEdit`; assert
    the queue has exactly 1 `TransitionStep(LoadingEdit)`.
  - `RunningLiveToRunningReplay_Produces_FourSteps`: assert queue is
    `[UnloadingLive, Standby, LoadingReplay, RunningReplay]`.
  - `RunningLiveToRunningReplayWithSeek_Produces_FiveSteps`: same 4 + 1
    `OperationStep(ReplaySeek)`.  Payload: `{"TargetState":41,"TargetWallTicks":N}`.
  - `RunningEditToRunningLive_Produces_FourSteps`: assert
    `[UnloadingEdit, Standby, LoadingLive, RunningLive]`.
  - `ImpossibleRequest_ThrowsInvalidOperationException`: feed `Degraded → RunningLive`;
    assert `InvalidOperationException` is thrown with message containing both state names.
    **Note:** `DSMState.Degraded` is the canonical impossible source — it has no outgoing
    planning edges.  The previous entry (`RunningDryRun → RunningReplay`) was incorrect:
    BFS proves that path is reachable in 6 steps via
    `UnloadingDryRun → RunningEdit → UnloadingEdit → Standby → LoadingReplay → RunningReplay`.
    That reachability is covered by `RunningDryRunToRunningReplay_Produces_SixSteps`.
  - `SameState_ReturnsEmptyQueue`: feed `Standby → Standby`; assert queue is empty.

---

### CGF1-S0202 — DSM Handler Wiring

**Design ref:** [§4.2](./CGF-1-DESIGN.md#42-stage-22--dsm-handler-wiring)

**Work to do:**
1. Declare `DsmStateChangedEvent { DSMState Previous; DSMState Next; }` in the Bagira
   application layer (e.g. `Bagira.Runner/Events/DsmStateChangedEvent.cs` or
   `Bagira.Common`).
2. Extend `DrillSlave.Tick()` to, after a `CommitState` command is processed, publish
   `DsmStateChangedEvent` to the local `FdpEventBus`.
3. Implement a **stub** `LiveLoadDsmHandler` that:
   - `CanHandle()` returns `true` for `NodeOpType.PrepareLive` and `NodeOpType.FinalizeLive`.
   - `PrepareAsync()` returns `null` (success) immediately — full implementation in Stage 3.4.
   - `Commit()` publishes `DsmStateChangedEvent` via event bus if not already done by slave.
4. Register `LiveLoadDsmHandler` with `DrillSlave` in `SimHostApp.OnLoad()`.
5. Implement idempotency: `DrillSlave` must silently drop duplicate
   `TransactionId` commands (e.g. re-delivered DDS reliable messages).

**Success conditions:**
- `DrillSlaveHandlerTests.CommitState_RaisesEsmStateChangedEvent`:
  - Construct `DrillSlave` with a mock `FdpEventBus` and the stub handler.
  - Inject `NodeOpCommand { Operation = CommitState, PayloadJson = "LoadingLive" }`.
  - Call `Tick()`.
  - Assert `FdpEventBus.GetPendingEvents<DsmStateChangedEvent>()` contains exactly one
    event with `Next == DSMState.LoadingLive`.
- `DrillSlaveHandlerTests.DuplicateTransactionId_IsDropped`:
  - Inject the same `NodeOpCommand` twice (same `TransactionId`).
  - Assert the event bus receives only one `DsmStateChangedEvent` (not two).
- `DsmStateChangedEvent` is **not** defined in any `FDP/` project — verified by
  csproj reference audit or `grep -r "DsmStateChangedEvent" FDP/`.

---

### CGF1-S0203 — Time Strategy Proxying

**Design ref:** [§4.3](./CGF-1-DESIGN.md#43-stage-23--time-strategy-proxying)

> **Implementation note:** `ITimeController`, `SwitchableTimeController`,
> `MasterTimeController`, `SlaveTimeController`, `SteppedMasterController`, and
> `SteppedSlaveController` are **already implemented** in
> `FDP/Toolkits/FDP.Toolkit.Time/`. This task is **verification and targeted
> extension** — confirm each class satisfies the requirements below and add only
> the missing members (`SeedState`, `TotalWallTicks`).

**Work to do:**
1. Verify `FDP/Toolkits/FDP.Toolkit.Time/ITimeController.cs` declares the
   `ITimeController` interface and `TimeMode` enum. If missing, create them — but
   expect them to exist.
2. Verify (and extend if needed) `SwitchableTimeController`:
   - Implements `ITimeController`.
   - `SwitchTo(ITimeController)` calls `newController.SeedState(currentState)` before
     assigning — preserving simulation time continuity across the swap.
   - `SwitchTo()` is a no-op if `newController` is the currently active instance.
3. Extend `MasterTimeController` and `SlaveTimeController` to implement `ITimeController`:
   - `SeedState(GlobalTime seed)` on `MasterTimeController` sets internal epoch and
     immediately publishes `TimePulseDescriptor` with the new state.
   - `SeedState(GlobalTime seed)` on `SlaveTimeController` bypasses `JitterFilter`
     (sets `_virtualClock = seed.TotalTime` directly) and populates `TotalWallTicks`.
4. Add `long TotalWallTicks` to `Fdp.Kernel/GlobalTime.cs`.
5. Ensure `GlobalTime.TotalWallTicks` is populated by both `MasterTimeController` and
   `SlaveTimeController` on every `Update()` call.

**Success conditions (unit tests in `FDP.Toolkit.Time.Tests`):**
- `SwitchableTimeControllerTests.SwitchTo_TransfersCurrentStateToNewController`:
  - Create a `SwitchableTimeController` wrapping a `MasterTimeController` that has
    advanced to `TotalTime = 5.0`.
  - Call `SwitchTo(new SteppedMasterController(...))`.
  - Assert the `SteppedMasterController.GetCurrentState().TotalTime == 5.0`.
- `SwitchableTimeControllerTests.SwitchTo_SameInstance_IsNoOp`:
  - Call `SwitchTo()` with the currently active instance; assert no state mutation.
- `MasterTimeControllerTests.SeedState_PublishesTimePulseImmediately`:
  - Call `SeedState(new GlobalTime { TotalTime = 100.0 })`.
  - Assert the underlying DDS writer was called exactly once with `SimTimeSnapshot ≈ 100.0`.
- `SlaveTimeControllerTests.SeedState_BypassesJitterFilter`:
  - Advance the slave's internal clock to `TotalTime = 1.0`.
  - Call `SeedState(new GlobalTime { TotalTime = 900.0 })`.
  - Assert the very next `Update()` returns `TotalTime ≈ 900.0` (no slew).
- `GlobalTimeTests.TotalWallTicks_IsPopulatedByMasterController`:
  - Call `MasterTimeController.Update()` and assert `TotalWallTicks > 0`.

---

### CGF1-S0204 — Future Barrier Implementation

**Design ref:** [§4.4](./CGF-1-DESIGN.md#44-stage-24--future-barrier-implementation)

> **Architecture note — why `GlobalTime.TotalWallTicks`, not `DateTime.UtcNow.Ticks`:**
> Nodes run asynchronously in real-time mode. `DateTime.UtcNow` is an NTP-based OS clock
> and drifts up to 50–100 ms across hosts, defeating the purpose of a coordinated barrier.
> FDP already maintains a **PLL-synchronized virtual wall clock**: the `MasterTimeController`
> drives a high-resolution `Stopwatch`-based `TotalWallTicks`; the `SlaveTimeController`
> uses a PLL to keep its own `TotalWallTicks` aligned with the master's. This is
> the only globally coherent timestamp in the cluster and is the correct clock for the
> future barrier.

**Work to do:**
1. Add `SwitchTimeModeEvent` struct to
   `FDP/Toolkits/FDP.Toolkit.Time/Messages/TimeMessages.cs`:
   ```csharp
   public struct SwitchTimeModeEvent
   {
       public TimeMode TargetMode;
       public long     BarrierWallTicks;  // GlobalTime.TotalWallTicks as absolute tick count
       public float    FixedDelta;        // Only used when TargetMode == Deterministic
   }
   ```
2. Register `BlitEventTranslator<SwitchTimeModeEvent>` egress+ingress in
   `TimeNetworkModule.RegisterTranslators()` (or equivalent composition root —
   must run on **every** node, both Master and Slaves).
3. Extend `DistributedTimeCoordinator`:
   - On receiving a mode-switch request, call `SetTimeScale(0.0)` on the active
     `ITimeController`.
   - Compute `BarrierWallTicks = _masterTime.GetCurrentState().TotalWallTicks + LookaheadTicks`
     (configurable; default ≈ 200 ms expressed as `Stopwatch` ticks — sufficient for DDS
     delivery across a LAN even under moderate load).
   - Publish `SwitchTimeModeEvent { TargetMode, BarrierWallTicks, FixedDelta }`.
   - Each subsequent tick, check `_kernel.CurrentTime.TotalWallTicks >= BarrierWallTicks`;
     when true, call `_switchableTime.SwitchTo(newStrategy)` then restore saved `TimeScale`.
4. Extend `SlaveTimeModeListener`:
   - On receiving `SwitchTimeModeEvent`, store `(TargetMode, BarrierWallTicks, FixedDelta)`.
   - Each tick, check `_kernel.CurrentTime.TotalWallTicks >= BarrierWallTicks`; when true,
     call `_switchableTime.SwitchTo(new SteppedSlaveController(...))` or
     `_switchableTime.SwitchTo(new SlaveTimeController(...))`.
   - No dependency on any ECS frame counter or OS wall clock.

**Success conditions (unit/integration tests in `FDP.Toolkit.Time.Tests`):**
- `FutureBarrierTests.SlaveCallsSwitchToAfterBarrierWallTicks`:
  - Inject a mock `ITimeController` whose `GetCurrentState().TotalWallTicks` returns a
    controllable `long` value into `SlaveTimeModeListener`.
  - Feed `SwitchTimeModeEvent { TargetMode = Deterministic, BarrierWallTicks = T+200ms_in_ticks }`.
  - Advance mock `TotalWallTicks` to `T+199ms_in_ticks`: assert `SwitchTo()` has **not** been called.
  - Advance mock `TotalWallTicks` to `T+200ms_in_ticks`: assert `SwitchTo()` is called **exactly once**.
- `FutureBarrierTests.MasterCallsSwitchToAfterBarrierWallTicks`:
  - Same pattern on the `DistributedTimeCoordinator` side using the same mock.
- `FutureBarrierTests.SwitchToIsNotCalledBeforeBarrierWallTicks`:
  - Feed the event; advance to `BarrierWallTicks - 1`; assert zero calls to `SwitchTo()`.
- `FutureBarrierTests.SwitchTimeModeEvent_FieldIsBarrierWallTicks_NotFrameCounter`:
  - Verify via reflection that `SwitchTimeModeEvent` has no field named `BarrierFrame`
    and does have a `long` field named `BarrierWallTicks`.
- `FutureBarrierTests.BarrierWallTicks_IsSetToFuture`:
  - Trigger a mode-switch on `DistributedTimeCoordinator`; capture the published
    `SwitchTimeModeEvent`; assert `BarrierWallTicks > currentState.TotalWallTicks` at
    the moment of publication.

---

### CGF1-S0205 — Deterministic CI Hookup

**Design ref:** [§4.5](./CGF-1-DESIGN.md#45-stage-25--deterministic-ci-hookup)

**Work to do:**
1. Implement or wire the `IScenario` interface for a `MinimalCIScenario`:
   - Spawns 2 dummy entities in the CGF subsystem.
   - Advances 600 deterministic ticks (10 s at 60 Hz).
   - Asserts both entities remain alive.
2. Wire `DrillMaster` to parse `"TimeMode": "Deterministic"` from
   `SysOpRequest.PayloadJson` on `LoadingLive`.
3. When `TimeMode == Deterministic`, `DrillMaster` instructs
   `DistributedTimeCoordinator` to switch to `SteppedMasterController` before the
   cluster enters `RunningLive`.
4. Slaves receive the `SwitchTimeModeEvent` via Future Barrier and switch to
   `SteppedSlaveController`.
5. Add CI entry point: `dotnet run --project Bagira.Runner -- --mode ci --scenario MinimalCI_01`
   passes the scenario name to the `ScenarioSubsystem` which drives the deterministic loop.

**Success conditions:**
- `MinimalCIScenarioTests.DeterministicRun_ExitsWithCode0`:
  - The command `dotnet run --project Bagira.Runner -- --mode ci --scenario MinimalCI_01`
    exits with code `0` within 30 s wall-clock.
- `MinimalCIScenarioTests.DeterministicRun_IsReproducible`:
  - Run the scenario twice with the same seed; assert the entity positions at
    tick 600 are identical (bit-exact) between the two runs.
- `MinimalCIScenarioTests.FailingAssertion_ExitsWithCode1`:
  - A deliberately failing assertion in the scenario script causes exit code `1`.

---

## Phase 3 — Persistence: Scenarios, Checkpoints & Replay

See [CGF-1-DESIGN.md §5](./CGF-1-DESIGN.md#5-phase-3--persistence-scenarios-checkpoints--replay).

---

### CGF1-S0301 — Storage Gateway

**Design ref:** [§5.1](./CGF-1-DESIGN.md#51-stage-31--storage-gateway)

**Work to do:**
1. Implement `Bagira.Orchestrator/StorageGatewayModule.cs`:
   - `PullToNasAsync(IReadOnlyList<FileManifestEntry> manifests, string nasBasePath)`:
     opens one outbound SMB connection to `nasBasePath`, then performs
     `Parallel.ForEach(manifests, MaxDegreeOfParallelism=8, entry => CopyFile(...))`.
   - `PushToNodesAsync(string nasSourcePath, IReadOnlyList<NodeDistributionTarget> targets)`:
     reads from NAS, writes to each node's `C:\FDP_Temp\` via parallel outbound SMB.
   - Both methods return a `Task<GatewayResult>` reporting success/failure counts.
2. Integrate with `DrillMaster`: after all node ACKs for `SerializeLocal`, collect
   manifests from `NodeOpStatus.ResultJson` and invoke `PullToNasAsync`.
3. Define `FileManifestEntry { string SourceUnc; string RelativeDest; }` in
   `Bagira.Orchestrator`.

**Success conditions:**
- `StorageGatewayTests.PullToNas_CopiesAllFiles`:
  - Provide 5 mock manifest entries pointing to local temp files on disk.
  - Call `PullToNasAsync(manifests, localTempNasPath)`.
  - Assert all 5 files exist in `localTempNasPath` after the call.
  - Assert the operation used `MaxDegreeOfParallelism ≤ 8` (verify via mock or
    `Parallel.ForEach` options inspection).
- `StorageGatewayTests.PullToNas_FailingFile_ReturnsPartialFailureResult`:
  - Include one non-existent source file in the manifest.
  - Assert `GatewayResult.FailureCount == 1` and `SuccessCount == 4`.

---

### CGF1-S0302 — Portable Scenario Loading

**Design ref:** [§5.2](./CGF-1-DESIGN.md#52-stage-32--portable-scenario-loading)

**Work to do:**
1. Implement `EditLoadDsmHandler` in `Bagira.SimHost`:
   - `PrepareAsync()`: if `IsNewScenario = true`, skips file I/O and returns success.
     If `ScenarioId != null`, verifies the pre-fetched JSON exists in `C:\FDP_Temp\`.
   - `Commit()`: bootstraps blank world (`BaseTerrain`) OR deserializes the JSON and
     spawns entities via `EntityCommandBuffer`. Does not block the main thread beyond
     baseline spawning cost.
2. Define a minimal scenario JSON schema:
   ```json
   { "SchemaVersion": 1, "Entities": [ { "Type": "Dummy", "Position": [0,0,0] } ] }
   ```
3. Extend `TransitionPlanner` to inject a pre-fetch `StorageGateway` step before
   `LoadingEdit` when `ScenarioId != null` in the payload.

**Success conditions:**
- `EditLoadDsmHandlerTests.NewScenario_SpawnsNoEntities`:
  - Invoke `Commit(cmd, repo)` with `IsNewScenario = true`; assert `repo.EntityCount == 0`.
- `EditLoadDsmHandlerTests.LoadExistingScenario_SpawnsCorrectEntityCount`:
  - Write a local JSON file with 3 entity entries; invoke `Commit` with that path in payload.
  - Assert `repo.EntityCount == 3` and their `Position` components match the JSON.
- `EditLoadDsmHandlerTests.Commit_DoesNotBlockLongerThan50ms`:
  - Measure wall-clock time of `Commit()` with a 100-entity JSON.
  - Assert elapsed < 50 ms.
- `TransitionPlannerTests.PlanWithScenarioId_InjectsStorageGatewayStep`:
  - Feed `SysOpRequest{TargetState=LoadingEdit, PayloadJson={ScenarioId="Alpha"}}`.
  - Assert the planned queue begins with a storage gateway pre-fetch step before
    the `TransitionStep(LoadingEdit)`.

---

### CGF1-S0303 — 3-Step Binary Checkpointing

**Design ref:** [§5.3](./CGF-1-DESIGN.md#53-stage-33--3-step-binary-checkpointing)

**Work to do:**
1. Implement `CheckpointIOWorker` in `Fdp.Kernel/Orchestration/CheckpointIOWorker.cs`:
   - `ConcurrentQueue<(EntityRepository snapshot, Guid requestId)>` drain loop
     on a dedicated background `Thread` (not a `Task` — prevents thread-pool starvation).
   - Each item: LZ4-compress snapshot, write to `{storageDir}/{requestId}_node_{nodeId}.fdp`.
   - On completion, sets `CompletionResults[requestId] = Success/Failure`.
   - Exposes `Enqueue(EntityRepository, Guid)` and `Task DrainAsync()`.
   - `DrainAsync()` returns only when the queue is empty **and** the background thread
     is idle.
2. Implement `CheckpointDsmHandler` in `Bagira.SimHost`:
   - On `TakeSnapshot` command: immediately publish `NodeOpStatus(InProgress)`;
     on main thread at BeforeSync, call `snap.SyncFrom(liveRepo)` (~2 ms);
     enqueue `(snap, requestId)` to `CheckpointIOWorker`.
   - `DrillSlave.Tick()` monitor: check `CompletionResults` each frame; when a matching
     `requestId` is found, publish `NodeOpStatus(Success/Failure)` to DDS (deferred ACK).
3. `LiveLoadDsmHandler.PrepareAsync()` must call `await CheckpointIOWorker.DrainAsync()`
   before triggering `FinalizeRecordingAsync()`.

**Success conditions:**
- `CheckpointDsmHandlerTests.TwoOverlappingCheckpoints_ACKsAreBothDeferred`:
  - Inject two rapid `TakeSnapshot` commands (Req_A then Req_B, 100 ms apart sim time).
  - Assert `NodeOpStatus(InProgress)` messages are published immediately for both.
  - Assert `NodeOpStatus(Success)` messages arrive only **after** disk writes complete
    (verified by mocking `CheckpointIOWorker.OnItemWritten` callback).
- `CheckpointDsmHandlerTests.SecondSnapshotCaptures_DifferentState_thanFirst`:
  - Between Req_A and Req_B, mutate a component value in `liveRepo`.
  - Assert that the deserialized snapshot from Req_A has the old value and
    Req_B has the new value.
- `CheckpointIOWorkerTests.DrainAsync_WaitsForQueueEmpty`:
  - Enqueue 3 items; call `DrainAsync()`; assert it does not return until all 3 files
    exist on disk.
- `CheckpointDsmHandlerTests.LiveUnloading_WaitsForCheckpointDrain`:
  - Trigger `FinalizeLive` while a checkpoint write is still in-flight.
  - Assert the `LiveLoadDsmHandler.PrepareAsync()` call does not complete until the
    in-flight checkpoint finishes writing.

---

### CGF1-S0304 — Dynamic Recording Modules

**Design ref:** [§5.4](./CGF-1-DESIGN.md#54-stage-34--dynamic-recording-modules)

**Work to do:**
1. Create `FDP/Kernel/Fdp.Kernel/Orchestration/IRecordReplayController.cs`
   (pure FDP interface — no Bagira references).
2. Create `FDP/Kernel/Fdp.Kernel/Orchestration/RecordingConfiguration.cs`.
3. Add `long TotalWallTicks` to `GlobalTime` if not yet done (§2.3 dependency).
4. Add `WallClockTicks` field to `FrameMetadata`/`FrameOuterHeader` in
   `Fdp.Kernel/FlightRecorder/`.
5. Extend `RecorderSystem`:
   - Inject and apply `EntityFilter` predicate (null = record all).
   - Stamp `WallClockTicks = DateTime.UtcNow.Ticks` on every captured frame.
6. Extend `PlaybackController`:
   - Add `SeekToWallClockTicks(EntityRepository repo, long wallTicks)` using binary
     search over `_frameIndex` (replace or supplement the existing linear scan in
     `SeekToTick`).
7. Implement `RecordingModule : IModule, IDisposable` in `Bagira.SimHost`:
   - `Initialize()` constructs `AsyncRecorder`, registers `RecorderTickSystem` with
     `ModuleHostKernel`.
   - `Dispose()` calls `AsyncRecorder.Dispose()` (blocking: flush LZ4 + write
     `.meta.json`).
8. Implement `ReplayModule : IModule, IDisposable` in `Bagira.SimHost`.
9. Implement `EcsRecordReplayController : IDsmHandler` in `Bagira.SimHost`:
   - `PrepareRecordingAsync`: factory-constructs `RecordingModule`, calls
     `kernel.InstallModuleAsync()`.
   - `FinalizeRecordingAsync`: calls `kernel.UninstallModuleAsync()` → triggers
     `RecordingModule.Dispose()`.
   - `PrepareReplayAsync`: factory-constructs `ReplayModule`, installs it.
   - `TeardownReplayAsync`: uninstalls `ReplayModule`, leaves `EntityRepository` intact.
10. Implement full `LiveLoadDsmHandler` (replacing the Stage 2.2 stub):
    - Calls `EcsRecordReplayController.PrepareRecordingAsync()` during `PrepareAsync`.
    - Calls `EcsRecordReplayController.FinalizeRecordingAsync()` during `FinalizeLive`.
11. Implement `ReplayLoadDsmHandler`:
    - Calls `PrepareReplayAsync`; extracts `MaxNetworkId` from `.meta.json` and
      includes it in `NodeOpStatus.ResultJson` for the Master to use for ID reset.
    - Disables `SimulationSystemGroup`, `NetworkLifecycleSystemGroup`;
      sets `GhostCreationSystem.BypassLifecycle = true`.
12. Add `bool BypassLifecycle` property to `GhostCreationSystem`.
13. Implement `NetworkLifecycleSystemGroup` in `ModuleHost.Core/Scheduling/`:
    - Wraps `LifecycleSystem`, `GhostPromotionSystem`, `NetworkGatewaySystem`.
    - `bool Enabled` (default `true`); when `false`, `SystemScheduler.ExecuteGroup`
      iterates zero systems.
14. Expose `MaxNetworkId` from `AsyncRecorder.Dispose()` path; write it to
    `RecordingMetadata.MaxNetworkId` in `.meta.json`.

**Success conditions:**
- `RecordingModuleTests.AfterInstall_RecorderTickSystemIsRegistered`:
  - Call `kernel.InstallModuleAsync(new RecordingModule(config))`.
  - Assert `kernel.GetRegisteredModuleTypeNames()` contains `"RecorderTickSystem"`.
- `RecordingModuleTests.AfterUninstall_RecorderTickSystemIsAbsent`:
  - Call `kernel.UninstallModuleAsync(module)`.
  - Assert `"RecorderTickSystem"` is **absent** from the scheduler's system list.
- `EcsRecordReplayControllerTests.FinalizeRecording_WritesMetaJson`:
  - Run a 10-tick recording; call `FinalizeRecordingAsync()`.
  - Assert a `.meta.json` file exists at the expected path and contains `MaxNetworkId > 0`.
- `PlaybackControllerTests.SeekToWallClockTicks_UsesBinarySearch`:
  - Record a 1000-frame sequence; seek to the midpoint.
  - Assert the seek completes in < 5 ms (binary search is O(log n)).
  - Assert the entity state after seek matches the state recorded at that wall-clock tick.
- `NetworkLifecycleSystemGroupTests.Enabled_False_SkipsAllInnerSystems`:
  - Set `group.Enabled = false`; call `ExecuteGroup()`.
  - Assert none of the three inner systems' `Execute()` were called.
- Integration test `ReplayLoadDsmHandlerTests.FullReplayTransition_DisablesSimGroups`:
  - Trigger `PrepareReplay` → `CommitState(RunningReplay)`.
  - Assert `SimulationSystemGroup.Enabled == false`.
  - Assert `GhostCreationSystem.BypassLifecycle == true`.

---

### CGF1-S0305 — Live-from-Replay Temporal Interlock

**Design ref:** [§5.5](./CGF-1-DESIGN.md#55-stage-35--live-from-replay-temporal-interlock)

**Work to do:**
1. Handle `PrepareLive` command when current DSM state is `RunningReplay` (the
   Live-from-Replay path):
   - **Before** issuing the `NodeOpCommand`, `DrillMaster` calls
     `replayMasterModule.SetTimeScale(0.0)` (hard freeze).
   - Generate new branched `DrillId`.
   - Issue `NodeOpCommand(PrepareState, LoadingLive, newDrillId)`.
2. In `ReplayLoadDsmHandler.PrepareAsync()` for the `PrepareLive` command:
   - Call `EcsRecordReplayController.TeardownReplayAsync()`.
   - **Do not** mutate `EntityRepository` — merely uninstall `ReplayModule` (disposes
     `PlaybackController`, closes read handles).
   - Call `EcsRecordReplayController.PrepareRecordingAsync(newDrillId)`.
   - Re-enable `SimulationSystemGroup`, `NetworkLifecycleSystemGroup`.
   - Set `GhostCreationSystem.BypassLifecycle = false`.
3. After all nodes report `NodeOpStatus(Success)`, `DrillMaster` commits
   `SystemStateTopic(RunningLive, newDrillId)` then calls `SetTimeScale(savedScale)`.
4. Add `ReplayMasterModule` to `Bagira.Orchestrator` that wraps `MasterTimeController`
   for replay playhead control.

**Success conditions:**
- `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState`:
  - Open a real `.fdp` file with 5 entities; seek to a mid-point.
  - Call `EcsRecordReplayController.TeardownReplayAsync()`.
  - Assert `repo.EntityCount == 5` (historical state preserved in-place, zero memcpy).
- `LiveFromReplayTests.AfterBranch_RecordingModuleIsInstalled`:
  - After the full `PrepareRecordingAsync(branchedDrillId)` call, assert
    `"RecorderTickSystem"` is present in the kernel's scheduler.
- `LiveFromReplayTests.AfterBranch_SimGroupsReEnabled`:
  - Assert `SimulationSystemGroup.Enabled == true`.
  - Assert `GhostCreationSystem.BypassLifecycle == false`.
- `LiveFromReplayTests.TimeFrozenDuringBranchTransition`:
  - Assert that from the moment `DrillMaster` issues the `PrepareLive` command until
    all nodes ACK success, `MasterTimeController.GetTimeScale() == 0.0`.
- Integration test `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`:
  - Run 100 ticks of live simulation; seek in replay to tick 50; execute the
    Live-from-Replay branch; run 50 more ticks of branched live simulation.
  - Assert the `.fdp` file for the branched DrillId contains a keyframe at frame 0
    that matches the ECS snapshot at tick 50 of the original recording.

---

### CGF1-S0306 — Scenario/Story Serialization Toolkit

**Design ref:** [§5.6](./CGF-1-DESIGN.md#56-stage-36--scenariostory-serialization-toolkit)

**New project:** `FDP/Toolkits/FDP.Toolkit.Scenario` — no Bagira references.

**Work to do:**
1. Create project `FDP.Toolkit.Scenario.csproj` referencing only `Fdp.Kernel` and
   `System.Text.Json`.
2. Declare non-generic `IEntityScenarioTranslator` with consumption mask:
   ```csharp
   public interface IEntityScenarioTranslator
   {
       BitMask256 GetConsumedComponentsMask();
       bool CanTranslate(EntityRepository repo, Entity entity);
       Dictionary<string, object> Extract(
           EntityRepository repo, Entity entity, IGuidResolver guidResolver);
       void Inject(
           EntityRepository repo, Entity entity,
           Dictionary<string, object> scenarioData, IGuidResolver guidResolver);
   }
   ```
3. Declare `IGuidResolver`:
   ```csharp
   public interface IGuidResolver
   {
       string Resolve(Entity entity);    // save: Entity → stable Guid string
       Entity Resolve(string guidStr);   // load: Guid string → live Entity
   }
   ```
   `ScenarioSerializer` builds two concrete implementations — one for save (backed by
   `Dictionary<Entity, Guid>`) and one for load (backed by `Dictionary<Guid, Entity>`).
   Both are populated during the first entity-enumeration pass.
4. Implement `FdpAutoSerializer` (DOM-aware, not the already existing binary one!):
   - `Build(ComponentTypeRegistry registry)` iterates all registered component types.
   - For each type, use `Expression.Property` to compile:
     - `Func<object, JsonObject, IGuidResolver, JsonObject>` (extract: reads struct fields,
       skips `[ScenarioIgnore]` fields, patches `Entity`-typed fields via `guidResolver.Resolve(entity)`).
     - `Action<object, JsonObject, IGuidResolver>` (inject: writes struct fields, patches
       guid strings back to `Entity` via `guidResolver.Resolve(guidStr)`).
   - No `Type.GetProperties()` or `PropertyInfo.GetValue` on the hot path.
   - Components flagged `DataPolicy.NoSave` in the `ComponentTypeRegistry` are excluded
     from delegate compilation entirely (they will never appear in the consumption mask).
5. Implement `ScenarioSerializerBuilder`:
   - `RegisterTranslator(IEntityScenarioTranslator translator)` stores the translator.
   - `Build()` compiles `FdpAutoSerializer`, freezes the translator list, returns a
     `ScenarioSerializer` instance.
6. Implement `ScenarioSerializer`:
   - `JsonObject Serialize(EntityRepository repo, ScenarioHeader header)`:
     - Pass 1: enumerate all entities **not** carrying `ScenarioIgnoreTag`; build
       `IGuidResolver` (save variant).
     - Pass 2: for each entity, run the consumption-mask pipeline:
       1. `remainingMask = repo.GetSaveableMask(entity)` (already excludes `DataPolicy.NoSave`).
       2. For each registered translator: if `CanTranslate` → `Extract` → add named
          entries to entity DOM → `remainingMask.BitwiseAndNot(consumedMask)`.
       3. `FdpAutoSerializer` processes remaining set bits (with `[ScenarioIgnore]` skips
          and `IGuidResolver` patches).
   - `void Deserialize(EntityRepository repo, JsonObject dom, bool asStory = false, Guid? storyId = null)`:
     - Peek `Header.SubsystemType`: if mismatched, return immediately (no entity creation).
     - Pass 1: create all ECS entities; build `IGuidResolver` (load variant).
     - Pass 2: for each entity, route named scenario components to matching translator
       `Inject` calls; remaining components to `FdpAutoSerializer` inject delegates.
     - If `asStory`, stamp **`FDP.Toolkit.Replay.StoryTag { StoryId = storyId.Value }`** on every created entity (`storyId` must be non-null and not `Guid.Empty`, or throw). **Single canonical type:** do not define a second `StoryTag` in `FDP.Toolkit.Scenario` — reuse the existing **`struct`** in **`FDP.Toolkit.Replay`** (`Guid StoryId`, `ReplayComponentIds.StoryTag`). Application-layer JSON may still carry story id as a string; **parse to `Guid` once** before calling `Deserialize`.
7. Define `ScenarioHeader`:
   ```csharp
   public record ScenarioHeader(string SubsystemType, int SchemaVersion = 1);
   ```
8. Add `[ScenarioIgnore]` attribute for field-level exclusion.
   Add `ScenarioIgnoreTag` empty component (`[DataPolicy(DataPolicy.NoSave)]`) for
   entity-level exclusion; queries chain `.Without<ScenarioIgnoreTag>()` to skip them.

**Note on `[DataPolicy(DataPolicy.NoSave)]`:** This is an existing FDP mechanism;
`EntityRepository.GetSaveableMask()` already filters excluded components automatically.
Do not reinvent it — just call `GetSaveableMask()` as the starting point.

**Schema contract to assert:**
```json
{
  "Header": { "SubsystemType": "...", "SchemaVersion": 1 },
  "Entities": {
    "<guid>": { "ComponentName": { "field": "..." } }
  }
}
```

**Success conditions (unit tests in `FDP.Toolkit.Scenario.Tests`):**
- `ScenarioSerializerTests.RoundTrip_1to1_PreservesAllFields`:
  - Register no custom translators. Create 3 entities each with a `DummyPosition`
    component (not `NoSave`). Serialize; deserialize into fresh repo.
  - Assert each entity's `DummyPosition` matches original via `FdpAutoSerializer`.
- `ScenarioSerializerTests.NtoM_CustomTranslator_CompressesComponents`:
  - Register `MissileOrdnanceTranslator` (consumes `BallisticProjectile` + `PhysicsCollider`;
    outputs single `"OrdnanceDef"` entry).
  - Serialize one entity with all three components; assert DOM has `"OrdnanceDef"` key,
    no `"BallisticProjectile"` key, no `"PhysicsCollider"` key.
  - Deserialize; assert entity has both `BallisticProjectile` and `PhysicsCollider`
    with reconstructed values.
- `ScenarioSerializerTests.ConsumptionMask_PreventsDuplication`:
  - Verify that after `MissileOrdnanceTranslator.Extract()` runs, the consumed bits
    are cleared from `remainingMask` and `FdpAutoSerializer` does not emit entries for
    `BallisticProjectile` or `PhysicsCollider`.
- `ScenarioSerializerTests.EntityCrossReference_ResolvedViaIGuidResolver`:
  - Entity A has `GuidedTarget { TargetId: Entity }` pointing to Entity B.
  - Serialize; assert DOM field for `TargetId` is a GUID string, not an integer index.
  - Deserialize into fresh repo; assert resolved handle is valid and refers to an
    entity whose component data matches original Entity B.
- `ScenarioSerializerTests.DataPolicyNoSave_ComponentExcluded`:
  - Create an entity with `SimVelocity` (`[DataPolicy(DataPolicy.NoSave)]`).
  - Serialize; assert DOM has no `"SimVelocity"` key for that entity.
- `ScenarioSerializerTests.ScenarioIgnore_FieldExcluded`:
  - Component `CachedSpeedComponent` has `float MaxSpeed` (saved) and
    `[ScenarioIgnore] float CachedWheelAngle` (excluded).
  - Serialize; assert DOM object for `CachedSpeedComponent` has `"MaxSpeed"` but
    no `"CachedWheelAngle"` key.
- `ScenarioSerializerTests.ScenarioIgnoreTag_EntitySkipped`:
  - Create one entity with `ScenarioIgnoreTag`. Serialize; assert it does not appear
    in `dom["Entities"]`.
- `ScenarioSerializerTests.StoryLoad_StampsStoryTag`:
  - Deserialize with `asStory: true` and a non-empty **`Guid`** (e.g. a fixed test `Guid`).
  - Assert every created entity has **`FDP.Toolkit.Replay.StoryTag`** with matching **`StoryId`**.
- `ScenarioSerializerTests.SubsystemType_MismatchSkipsDeserialize`:
  - Deserialize a DOM with `SubsystemType = "Bagira.SimHost"` using a serializer
    configured for `"Bagira.CGF"`.
  - Assert `EntityRepository.EntityCount` does not increase.
- `ScenarioSerializerTests.FdpAutoSerializer_NoReflectionOnHotPath`:
  - After `Build()`, invoke `Serialize`; assert no `PropertyInfo.GetValue` calls
    occur (use a profiling stub or delegate inspection).

---

### CGF1-S0307 — Application-Layer Scenario Save/Load Wiring

**Design ref:** [§5.7](./CGF-1-DESIGN.md#57-stage-37--application-layer-scenario-saveload-wiring)

**Work to do:**
1. Create `Bagira.Orchestrator/GlobalContextDsmHandler.cs`:
   - **Save:** on `SerializeLocal` command, build a `GlobalContextDto` (start wall
     ticks, weather descriptor, scene identifier) and serialize to
     `C:\FDP_Temp\<DrillId>\Orchestrator.json`. Return UNC path in `NodeOpStatus.ResultJson`.
   - **Load:** on `CommitState(LoadingLive|LoadingEdit)`, parse `Orchestrator.json`,
     call `MasterTimeController.SeedState(new GlobalTime { TotalWallTicks = dto.StartWallTicks })`,
     then publish `OrchestratorContextMessage` to the DDS `OrchestratorContextTopic`.
2. Register `GlobalContextDsmHandler` in the Orchestrator's own `DrillSlave` at startup.
3. In `Bagira.SimHost` (and `Bagira.CGF`), create `ScenarioLoadDsmHandler`:
   - `PrepareAsync`: peek at `Header.SubsystemType`; if mismatch, return `Success` immediately.
   - If match: parse full DOM, call `ScenarioSerializer.Deserialize()`, return `Success`.
4. Wire `ScenarioLoadDsmHandler` registration in `SimHostApp.OnLoad()` and `CgfApp.OnLoad()`.
5. Extend `TransitionPlanner.PlanTrajectory()`:
   - When `SysOpRequest` contains `ScenarioId`, insert a
     `OperationStep(PrefetchScenario, scenarioId)` before the first `TransitionStep`.
6. Extend `StorageGatewayModule`:
   - `PrefetchScenarioAsync(string scenarioId)`: copies all files from
     `\\NAS\Scenarios\<scenarioId>\` to each node's `C:\FDP_Temp\<scenarioId>\` via
     `NodeOpCommand(PrefetchFiles, manifest)`.
7. Extend `StorageGatewayModule` save path:
   - After all nodes return UNC manifests, collect paths and copy to NAS under
     `\\NAS\Scenarios\<scenarioId>\`.
   - Write `scenario_manifest.json` listing all participating file names.

**Success conditions (integration tests in `Bagira.Orchestrator.Integration.Tests`):**
- `ScenarioSaveLoadTests.RoundTrip_SimHost_EntitiesMatchAfterLoad`:
  - Run live simulation with 3 entities in SimHost.
  - Trigger `SysOpRequest(SaveScenario, "test_01")`.
  - Verify `scenario_manifest.json` is written to NAS.
  - Clear ECS; trigger `SysOpRequest(LoadScenario, "test_01")`.
  - Assert 3 entities present in SimHost `EntityRepository` with matching component data.
- `ScenarioSaveLoadTests.OrchestratorContextRestored_AfterLoad`:
  - Save scenario; clear Orchestrator context; load scenario.
  - Assert `OrchestratorContextTopic.SceneId` matches the saved value.
- `ScenarioSaveLoadTests.SubsystemTypeFilter_CGFFileNotLoadedBySimHost`:
  - Create a scenario file with `SubsystemType = "Bagira.CGF"`.
  - Let SimHost's `ScenarioLoadDsmHandler` process it.
  - Assert `EntityRepository.EntityCount` in SimHost does not increase.

---

### CGF1-S0308 — Runtime Story Injection & Deletion

**Design ref:** [§5.8](./CGF-1-DESIGN.md#58-stage-38--runtime-story-injection--deletion)

**Work to do:**
1. Add `ManageStory` operation to `TransitionPlanner`:
   - Validate `CurrentState == RunningLive`; if not, return `OpStatus.InvalidState`.
   - For `Mode:Start`: append `OperationStep(PrefetchStory, storyId)` then
     `OperationStep(StartStory, { StoryId, ScenarioId })`.
   - For `Mode:Stop`: append `OperationStep(StopStory, { StoryId })`.
2. Implement `StoryLoadDsmHandler` in `Bagira.SimHost` (and `Bagira.CGF`):
   - On `StartStory(storyId, scenarioId)` (**`storyId`: `Guid`** — parse from payload JSON if the wire format uses strings):
     - Load DOM from `C:\FDP_Temp\<scenarioId>\{SubsystemType}.json`.
     - Peek `Header.SubsystemType`; if mismatch, reply
       `NodeOpStatus(Success, IsParticipating: false)`.
     - If match, call `ScenarioSerializer.Deserialize(repo, dom, asStory: true, storyId)`.
     - Reply `NodeOpStatus(Success, IsParticipating: true)`.
   - On `StopStory(storyId)` (**`Guid`**):
     - Query all entities with **`FDP.Toolkit.Replay.StoryTag`** where **`StoryId == storyId`**.
     - Destroy each entity.
     - Reply `NodeOpStatus(Success)`.
3. Extend `DrillMaster` to track active stories in `OrchestratorContextTopic.ActiveStories`
   (**`List<Guid>`** of active story IDs, or a wire-serializable equivalent; published after each Start/Stop operation).
4. Extend `NodeOpStatus` with `bool IsParticipating` field (default `true`).  
   `DrillMaster` waits only for ACKs from nodes that replied `IsParticipating: true`
   during the `PrepareStory` phase.

**Success conditions (integration tests in `Bagira.SimHost.Integration.Tests`):**
- `StoryInjectionTests.StartStory_EntitiesSpawnedWithStoryTag`:
  - System is in `RunningLive`. Inject a story with a known **`Guid`** `storyId` targeting `Bagira.SimHost`.
  - Assert 3 new entities appear in `EntityRepository`.
  - Assert each has **`FDP.Toolkit.Replay.StoryTag`** with **`StoryId == storyId`**.
- `StoryInjectionTests.StopStory_EntitiesDestroyedByStoryTag`:
  - Inject `storyId` (3 entities). Stop the same **`Guid`**.
  - Assert those 3 entities are no longer in `EntityRepository`.
- `StoryInjectionTests.StartStory_NonMatchingSubsystem_ReturnsIsParticipatingFalse`:
  - Create a story file with `SubsystemType = "Bagira.CGF"`.
  - Process it through `SimHost.StoryLoadDsmHandler`.
  - Assert `NodeOpStatus.IsParticipating == false` and `EntityRepository.EntityCount`
    unchanged.
- `StoryInjectionTests.ManageStory_RejectedWhen_NotInRunningLive`:
  - Issue `SysOpRequest(ManageStory, { Mode:Start })` while cluster is in `Standby`.
  - Assert response is `OpStatus.InvalidState`.
- `StoryInjectionTests.MultipleStoriesCoexist_IndependentDeletion`:
  - Inject two distinct story **`Guid`**s `s1` / `s2` (3 and 2 entities).
  - Stop `s1`.
  - Assert `s1` entities gone; assert `s2` entities still present.
