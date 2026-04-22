# CGF-1 Task Detail Document

**Reference design:** [CGF-1-DESIGN.md](./CGF-1-DESIGN.md)  
**Tracking:** [CGF-1-TASK-TRACKER.md](./CGF-1-TASK-TRACKER.md)

> Every task includes a unique ID, a detailed description of work to be done, and
> explicit **success conditions** — usually a specification of unit/integration tests
> that must pass before the task can be considered done.
>
> **Architectural constraint (repeated for emphasis):**  
> FDP infrastructure (`Fdp.Kernel` and all `FDP.Toolkit.*`) must never reference any
> `Hrot.*` assembly. The Drill State Machine (`ClusterState`, handler interfaces,
> `ClusterStateChangedEvent`) lives entirely in the Hrot application layer.

---

## Phase 1 — Skeleton: Control-Plane Foundation

See [CGF-1-DESIGN.md §3](./CGF-1-DESIGN.md#3-phase-1--skeleton-control-plane-foundation).

---

### CGF1-S0101 — Orchestration DDS Schema Definition

**Design ref:** [§3.1](./CGF-1-DESIGN.md#31-stage-11--orchestration-dds-schema)

**Work to do:**
1. Create `Hrot.NED/Orchestration/OrchestrationMessages.cs`.
2. Declare `ClusterState`, `ClusterOpType`, `NodeOpType`, `OpStatus` enumerations.
3. Declare all DDS topic structs: `SystemStateTopic`, `ClusterOpRequest`, `ClusterOpStatus`,
   `NodeOpCommand`, `NodeOpStatus`, `NodeHeartbeat`, `OrchestratorContextTopic`.
4. Apply correct `[DdsTopic]`, `[DdsIdlFile("bdc-sst-orchestration")]`, and
   `[DdsQos]` attributes per the design spec.
5. Ensure all structs are in namespace `Hrot.NED.Descriptors.Orchestration`.

**Success conditions:**
- `Hrot.NED.Tests` contains a new test class
  `OrchestrationSchemaTests` with the following tests:
  - `AllTopicStructsHaveDdsTopicAttribute` — reflection scan of all types in the
    `Hrot.NED.Descriptors.Orchestration` namespace; assert every `partial struct` has
    `[DdsTopic]` and `[DdsIdlFile("bdc-sst-orchestration")]`.
  - `ClusterStateEnumHasExpectedValues` — assert `ClusterState.Standby == 0`,
    `ClusterState.RunningLive == 31`, `ClusterState.Degraded == 99`.
  - `NodeHeartbeatHasDdsKeyOnNodeId` — assert the `NodeId` field of `NodeHeartbeat`
    bears `[DdsKey]`.
  - `SystemStateTopicQosIsDurableTransientLocal` — assert the `DdsQos` attribute on
    `SystemStateTopic` specifies `Durability = TransientLocal` and `HistoryDepth = 1`.
- All existing `Hrot.NED` tests continue to pass.
- Project builds with zero warnings on new code.

---

### CGF1-S0102 — Hrot.Orchestrator Bootstrapping

**Design ref:** [§3.2](./CGF-1-DESIGN.md#32-stage-12--hrotorchestrator-bootstrapping)

**Work to do:**
1. Create the `Hrot.Orchestrator` C# project (net8.0; references
   `Hrot.NED`, `Fdp.Kernel`, FDP toolkit projects as needed).
2. Implement `ClusterMaster` class:
   - Subscribes to `NodeHeartbeat`; maintains `Dictionary<int, NodeHealthProfile>`.
   - Publishes `SystemStateTopic { CurrentState = ClusterState.Standby }` on startup.
   - Exposes a `Tick()` method called by the application loop (BeforeSync phase).
3. Implement skeleton `DistributedTransaction` data class (no 2PC execution yet —
   that comes in Stage 2.1).
4. Implement skeleton `NodeRoster` (heartbeat pruning at > 5 s silence).
5. Add `Hrot.Orchestrator` as a subsystem in `Hrot.ClusterRunner` (activated by
   `--mode orchestrator` command-line flag or configuration).

> **Runner-only launch:** `Hrot.Orchestrator.Standalone` is removed; the Orchestrator
> is exclusively launched via `Hrot.ClusterRunner --mode orchestrator`.

**Success conditions:**
- New integration test `ClusterMasterBootstrapTests.OrchestratorPublishesStandbyOnStartup`:
  - Spawn the Hrot.Orchestrator process (or run in-process via test harness).
  - An out-of-process DDS reader subscribes to `SystemStateTopic`.
  - Within 3 s wall-clock, assert exactly one sample is received with
    `CurrentState == ClusterState.Standby` and `TransactionEpoch == 0`.
- All pre-existing tests continue to pass.

---

### CGF1-S0103 — Centralized Identity Migration

**Design ref:** [§3.3](./CGF-1-DESIGN.md#33-stage-13--centralized-identity-migration)

**Work to do:**
1. Remove `DdsIdAllocatorServer` registration from `Hrot.SimHost/SimHostApp.cs`.
2. Add `DdsIdAllocatorServer` registration inside `ClusterMaster` (in-process, running
   inside `Hrot.Orchestrator`).
3. Verify `Hrot.SimHost` no longer holds a server instance — it only holds a
   `DdsIdAllocator` client.
4. Add a config flag to support running without an Orchestrator (for existing SimHost
   standalone mode during the transition period — falls back to hosting the server
   locally if no Orchestrator heartbeat seen within 5 s).

**Success conditions:**
- New integration test `DdsIdAllocatorMigrationTests.SimHostReceivesIdFromOrchestratorServer`:
  - Launch Hrot.Orchestrator (hosts the server).
  - Launch a SimHost instance (holds only the client).
  - Assert SimHost's `DdsIdAllocator` receives an ID batch and that the first
    allocated ID is `> 0`.
  - Assert `SimHostApp` contains no reference to `DdsIdAllocatorServer`.
- Existing `Hrot.SimHost.Integration.Tests` suite passes unmodified.

---

### CGF1-S0104 — ClusterSlave Foundation

**Design ref:** [§3.4](./CGF-1-DESIGN.md#34-stage-14--clustslave-foundation)

**Work to do:**
1. Create `Hrot.CGF` project.
2. Implement `ClusterSlave` in each subsystem:
   - `Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs`
   - `Hrot.IG/Modules/Orchestration/ClusterSlave.cs`
   - `Hrot.ExCon/Orchestration/ClusterSlave.cs` (no-ECS variant — skips any
     `IDsmHandler` that requires `EntityRepository`)
   - `Hrot.CGF/Modules/Orchestration/ClusterSlave.cs`
3. Each `ClusterSlave`:
   - Publishes `NodeHeartbeat` at 1 Hz (wall-clock `Stopwatch`).
   - Receives `NodeOpCommand` on DDS network thread; enqueues to
     `ConcurrentQueue<PendingMainThreadAction>`.
   - Dequeues and dispatches during `Tick()` (BeforeSync phase).
4. Declare `IDsmHandler` interface in `Hrot.ClusterRunner` (or a shared `Hrot.Common`
   project — **NOT in any FDP project**).
5. Register both CGF and SimHost `ClusterSlave` instances in their respective
   `Application.OnLoad()` methods.

**Success conditions:**
- New integration test `ClusterSlaveHeartbeatTests.OrchestratorReceivesHeartbeatsFromBothNodes`:
  - Launch Orchestrator, SimHost, CGF (in-process via test harness or headless).
  - Within 2 s wall-clock, assert `ClusterMaster.NodeRoster` contains both node IDs
    (SimHost and CGF).
  - Assert heartbeat `LocalClusterState == ClusterState.Standby` for both.
- `IDsmHandler` interface is declared in a Hrot layer project. Verify no FDP project
  uses `typeof(IDsmHandler)` or references its namespace (csproj reference audit).
- All pre-existing tests pass.

---

### CGF1-S0105 — Orchestrator Health Monitoring & Bootstrap Recovery

**Design ref:** [§3.5](./CGF-1-DESIGN.md#35-stage-15--orchestrator-health-monitoring--bootstrap-recovery)

**Work to do:**
1. Create `Hrot.Orchestrator/ClusterConfiguration.cs`:
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
2. Add `_bootstrapLatch: bool` to `ClusterMaster` (initially `false`):
   - Set to `true` once every subsystem name in `Mandatory` has appeared in the roster
     with `LocalClusterState == Standby`.
   - While `false`, return `OpStatus.Rejected` for all incoming `ClusterOpRequest` messages.
   - On becoming `true`, publish `SystemStateTopic { CurrentState = Standby }`.
3. Implement heartbeat timeout detection in `ClusterMaster.Tick()`:
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
   - Add system-health table: `NodeId | SubsystemName | ms ago | LocalClusterState | CPU% | RAM`.
   - Add 2PC history table listing last N transactions with per-node ACK latency (ms).

**Success conditions (unit/integration tests in `Hrot.Orchestrator.Tests`):**
- `ClusterMasterBootstrapTests.RejectsCommands_UntilMandatoryNodesReady`:
  - Configure `mandatory: ["SimHost"]`.
  - Send `ClusterOpRequest(TransitionState, LoadingLive)` before a SimHost heartbeat arrives.
  - Assert response is `OpStatus.Rejected`.
  - Deliver SimHost `NodeHeartbeat { LocalClusterState = Standby }`.
  - Assert the next `ClusterOpRequest(TransitionState, LoadingLive)` is accepted (not rejected).
- `ClusterMasterBootstrapTests.EjectsMandatoryNode_EntersDegraded`:
  - Bootstrap with SimHost present.
  - Advance the heartbeat timer past `HeartbeatTimeoutSeconds`.
  - Assert `SystemStateTopic.CurrentState == Degraded` is published.
- `ClusterMasterBootstrapTests.SurvivingNodes_CommandedToStandby_AfterEjection`:
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
- `ClusterMasterBootstrapTests.TransactionHistory_RecordsCompletedTransaction`:
  - Execute a successful `ClusterOpRequest(TransitionState, LoadingLive)` end-to-end.
  - Assert `ClusterMaster.TransactionHistory` contains one entry with `IsAborted == false`.

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

2. **`ClusterMaster` fan-out:** Replace `BroadcastNodeOp(NodeOpCommand)` with
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

### CGF1-S0106 — Orchestrator ImGui Scenario & Story Controls

**Design ref:** [§3.6](./CGF-1-DESIGN.md#36-stage-16--orchestrator-imgui-scenario--story-controls)

**Depends on:** CGF1-S0105 (health panel already implemented), CGF1-S0303 (checkpoint
backend), CGF1-S0307 (scenario save/load wiring), CGF1-S0308 (story injection backend).

**Context:** S0105 added the system-health table, 2PC history display, and time controls
to the Orchestrator ImGui panel. The scenario and story management buttons referenced in
the design §3.5 bullet "_Scenario controls: Initialize Live / Load Scenario / Save
Scenario / Init Replay / Story list + inject/unload_" have no corresponding
implementation task. This task fills that gap.

**Work to do:**
1. Create `Hrot.Orchestrator/UI/OrchestratorScenarioPanel.cs`:
   - Constructor receives `ClusterMaster drillMaster`, `NodeRoster roster`, and
     `ILogger logger`.
   - `Render()` method (called from `OrchestratorSubsystem.DrawUI()`) draws **six
     ImGui child windows** (Status Banner, Drill Control, Checkpoint, Scenario, Replay,
     Stories). All controls use `ImGui.BeginDisabled` when `!drillMaster.BootstrapComplete`
     or `drillMaster.HasInFlightTransaction`.
   - **Status Banner** (always enabled): `ImGui.Text` showing `CurrentState`,
     short `ExerciseId` (first 8 hex chars), and elapsed ms of in-flight transaction
     (or "idle"). Read directly from `drillMaster.CurrentSystemState` and
     `drillMaster.ActiveTransaction`.
   - **Drill Control** section: one `ImGui.Button` per reachable DSM target from the
     current state. Buttons are generated dynamically from
     `TransitionPlanner.GetReachableTargets(currentState)`. Clicking emits:
     `drillMaster.HandleClusterOpRequestAsync(new ClusterOpRequest { OperationType =
     ClusterOpType.TransitionState, PayloadJson = ... })`.
   - **Checkpoint** section: single [Take Checkpoint] button; button is additionally
     disabled when `CurrentState != RunningLive`.
   - **Scenario** section:
     - `_saveScenarioId` string input field + [Save Scenario] button →
       `ClusterOpType.SaveScenario` with `_saveScenarioId` in payload.
     - `_loadScenarioId` string input field (or dropdown populated via
       `drillMaster.StorageGateway?.ListScenariosAsync()`) + two buttons
       [Load into Edit] / [Load into Live] → `ClusterOpType.TransitionState` with
       `TargetState = LoadingEdit|LoadingLive` and `ScenarioId` in payload.
   - **Replay** section:
     - `_replayExerciseId` string input (dropdown when NAS available) + [Load Replay] →
       `ClusterOpType.TransitionState, TargetState = RunningReplay` with ExerciseId.
     - Seek slider (float, 0 … replay length in seconds) shown only when
       `CurrentState == RunningReplay`; dragging emits `ClusterOpType.ReplaySeek` with
       `TargetWallTicks` converted from seconds.
   - **Stories** section:
     - Scrollable list read from `drillMaster.ActiveStories` (list of `Guid`). Per row:
       short GUID string label + [Unload] button → `ClusterOpType.ManageEpisode,
       Mode:Stop, StoryId`.
     - `_injectScenarioId` + `_injectStoryId` text inputs + [Inject Story] button →
       `ClusterOpType.ManageEpisode, Mode:Start, ScenarioId, StoryId`.
2. Wire `OrchestratorScenarioPanel` into `OrchestratorSubsystem.DrawUI()` after the
   existing health table and 2PC history table calls.
3. Expose `bool BootstrapComplete`, `bool HasInFlightTransaction`,
   `ClusterState CurrentSystemState`, `DistributedTransaction? ActiveTransaction`, and
   `IReadOnlyList<Guid> ActiveStories` as read-only properties on `ClusterMaster`
   (or via a thin read-only `ClusterMasterState` snapshot record if `ClusterMaster` is
   not currently accessible from the `OrchestratorSubsystem`).

**Success conditions:**

- `OrchestratorScenarioPanelTests.AllButtons_DisabledBeforeBootstrap`:
  - Instantiate `OrchestratorScenarioPanel` with a mock `ClusterMaster` where
    `BootstrapComplete = false`.
  - Call `Render()` against a headless ImGui context.
  - Assert `ClusterMaster.HandleClusterOpRequestAsync` was never called.

- `OrchestratorScenarioPanelTests.DrillControlButtons_EmitCorrectClusterOpRequests`:
  - Mock `ClusterMaster` with `BootstrapComplete = true`, `CurrentSystemState = Standby`.
  - Simulate click on the button that corresponds to `LoadingLive`.
  - Assert `HandleClusterOpRequestAsync` was called with
    `OperationType == ClusterOpType.TransitionState` and payload containing
    `"TargetState": "RunningLive"` (or the equivalent integer).

- `OrchestratorScenarioPanelTests.TakeCheckpoint_Button_DisabledOutsideRunningLive`:
  - Set `CurrentSystemState = RunningEdit`.
  - Assert the [Take Checkpoint] button is disabled (no call to
    `HandleClusterOpRequestAsync` on simulated click).

- `OrchestratorScenarioPanelTests.SaveScenario_EmitsCorrectPayload`:
  - Set `_saveScenarioId = "Alpha"`.
  - Simulate click on [Save Scenario].
  - Assert payload JSON contains `"ScenarioId": "Alpha"` and
    `OperationType == ClusterOpType.SaveScenario`.

- `OrchestratorScenarioPanelTests.UnloadStory_EmitsManageEpisodeStop`:
  - Mock `ActiveStories` with one Guid `s1`.
  - Simulate click on [Unload] for `s1`.
  - Assert payload JSON contains `"Mode": "Stop"` and `"StoryId": "<s1 guid>"`.

---

## Phase 2 — State & Time: DSM and Synchronization

See [CGF-1-DESIGN.md §4](./CGF-1-DESIGN.md#4-phase-2--state--time-dsm-and-synchronization).

---

### CGF1-S0201 — BFS Transition Planner

**Design ref:** [§4.1](./CGF-1-DESIGN.md#41-stage-21--bfs-transition-planner)

**Work to do:**
1. Implement `TransitionPlanner` class in `Hrot.Orchestrator/TransitionPlanner.cs`.
2. Define the complete directed adjacency list for all valid DSM edges (13 states,
   as listed in the design doc §4.1).
3. Implement `CalculateShortestPath(ClusterState current, ClusterState target)` via BFS.
4. Implement `PlanTrajectory(ClusterState current, ClusterOpRequest request)` that:
   - Runs BFS and wraps each state in a `TransitionStep`.
   - Appends `OperationStep(ReplaySeek, TargetWallTicks)` when `target == RunningReplay`
     and `PayloadJson` contains a `TargetWallTicks` hint.
5. If BFS exhausts the frontier, throw `InvalidOperationException` with a descriptive
   message — **before** any DDS command is issued.
6. Wire `ClusterMaster.Tick()` to call `TransitionPlanner.PlanTrajectory()` when a
   `ClusterOpRequest` arrives and the result feeds the active `DistributedTransaction`.

**Success conditions (pure unit tests — no DDS, no ECS):**
- `TransitionPlannerTests` in `Hrot.Orchestrator.Tests`:
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
    **Note:** `ClusterState.Degraded` is the canonical impossible source — it has no outgoing
    planning edges.  The previous entry (`RunningDryRun → RunningReplay`) was incorrect:
    BFS proves that path is reachable in 6 steps via
    `UnloadingDryRun → RunningEdit → UnloadingEdit → Standby → LoadingReplay → RunningReplay`.
    That reachability is covered by `RunningDryRunToRunningReplay_Produces_SixSteps`.
  - `SameState_ReturnsEmptyQueue`: feed `Standby → Standby`; assert queue is empty.

---

### CGF1-S0202 — DSM Handler Wiring

**Design ref:** [§4.2](./CGF-1-DESIGN.md#42-stage-22--dsm-handler-wiring)

**Work to do:**
1. Declare `ClusterStateChangedEvent { ClusterState Previous; ClusterState Next; }` in the Hrot
   application layer (e.g. `Hrot.ClusterRunner/Events/ClusterStateChangedEvent.cs` or
   `Hrot.Common`).
2. Extend `ClusterSlave.Tick()` to, after a `CommitState` command is processed, publish
   `ClusterStateChangedEvent` to the local `FdpEventBus`.
3. Implement a **stub** `LiveLoadDsmHandler` that:
   - `CanHandle()` returns `true` for `NodeOpType.PrepareLive` and `NodeOpType.FinalizeLive`.
   - `PrepareAsync()` returns `null` (success) immediately — full implementation in Stage 3.4.
   - `Commit()` publishes `ClusterStateChangedEvent` via event bus if not already done by slave.
4. Register `LiveLoadDsmHandler` with `ClusterSlave` in `SimHostApp.OnLoad()`.
5. Implement idempotency: `ClusterSlave` must silently drop duplicate
   `TransactionId` commands (e.g. re-delivered DDS reliable messages).

**Success conditions:**
- `ClusterSlaveHandlerTests.CommitState_RaisesEsmStateChangedEvent`:
  - Construct `ClusterSlave` with a mock `FdpEventBus` and the stub handler.
  - Inject `NodeOpCommand { Operation = CommitState, PayloadJson = "LoadingLive" }`.
  - Call `Tick()`.
  - Assert `FdpEventBus.GetPendingEvents<ClusterStateChangedEvent>()` contains exactly one
    event with `Next == ClusterState.LoadingLive`.
- `ClusterSlaveHandlerTests.DuplicateTransactionId_IsDropped`:
  - Inject the same `NodeOpCommand` twice (same `TransactionId`).
  - Assert the event bus receives only one `ClusterStateChangedEvent` (not two).
- `ClusterStateChangedEvent` is **not** defined in any `FDP/` project — verified by
  csproj reference audit or `grep -r "ClusterStateChangedEvent" FDP/`.

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
2. Wire `ClusterMaster` to parse `"TimeMode": "Deterministic"` from
   `ClusterOpRequest.PayloadJson` on `LoadingLive`.
3. When `TimeMode == Deterministic`, `ClusterMaster` instructs
   `DistributedTimeCoordinator` to switch to `SteppedMasterController` before the
   cluster enters `RunningLive`.
4. Slaves receive the `SwitchTimeModeEvent` via Future Barrier and switch to
   `SteppedSlaveController`.
5. Add CI entry point: `dotnet run --project Hrot.ClusterRunner -- --mode ci --scenario MinimalCI_01`
   passes the scenario name to the `ScenarioSubsystem` which drives the deterministic loop.

**Success conditions:**
- `MinimalCIScenarioTests.DeterministicRun_ExitsWithCode0`:
  - The command `dotnet run --project Hrot.ClusterRunner -- --mode ci --scenario MinimalCI_01`
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
1. Implement `Hrot.Orchestrator/StorageGatewayModule.cs`:
   - `PullToNasAsync(IReadOnlyList<FileManifestEntry> manifests, string nasBasePath)`:
     opens one outbound SMB connection to `nasBasePath`, then performs
     `Parallel.ForEach(manifests, MaxDegreeOfParallelism=8, entry => CopyFile(...))`.
   - `PushToNodesAsync(string nasSourcePath, IReadOnlyList<NodeDistributionTarget> targets)`:
     reads from NAS, writes to each node's `C:\FDP_Temp\` via parallel outbound SMB.
   - Both methods return a `Task<GatewayResult>` reporting success/failure counts.
2. Integrate with `ClusterMaster`: after all node ACKs for `SerializeLocal`, collect
   manifests from `NodeOpStatus.ResultJson` and invoke `PullToNasAsync`.
3. Define `FileManifestEntry { string SourceUnc; string RelativeDest; }` in
   `Hrot.Orchestrator`.

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
1. Implement `EditLoadDsmHandler` in `Hrot.SimHost`:
   - `PrepareAsync()`: if `IsNewScenario = true`, skips file I/O and returns success.
     If `ScenarioId != null`, verifies the pre-fetched JSON exists in `C:\FDP_Temp\`.
   - `Commit()`: leaves world blank for new scenarios OR deserializes the cached
     `ScenarioSerializer` DOM and spawns entities via `ScenarioSerializer.Deserialize`.
     Does not block the main thread beyond baseline spawning cost.
     **Throws `InvalidOperationException`** when deserialization is required but both
     the `repo` parameter and the injected `_world` are null (mis-wired production path).
2. **Canonical scenario format:** `ScenarioSerializer` DOM (same as `ScenarioLoadDsmHandler`
   for `PrepareLive`). Files follow the naming convention `<subsystemType>.json` within
   the scenario directory. _The minimal `{ "SchemaVersion": 1, "Entities": [...] }` schema
   described in earlier batch instructions is superseded by the ScenarioSerializer DOM._
3. Extend `TransitionPlanner` to inject a pre-fetch `StorageGateway` step before
   `LoadingEdit` when `ScenarioId != null` in the payload.

**Success conditions:**
- `EditLoadDsmHandlerTests.NewScenario_SpawnsNoEntities`:
  - Invoke `Commit(cmd, repo)` with `IsNewScenario = true`; assert `repo.EntityCount == 0`.
- `EditLoadDsmHandlerTests.LoadExistingScenario_SpawnsCorrectEntityCount`:
  - Write a local `ScenarioSerializer` DOM file with 3 entities carrying `EditLoadTestPos`
    components; invoke `PrepareAsync` + `Commit` with that scenario in the payload.
  - Assert `repo.EntityCount == 3`.
  - Assert each expected `(X, Y, Z)` position is present in the deserialized entities
    (component values survive the serialize/deserialize round-trip).
- `EditLoadDsmHandlerTests.Commit_DoesNotBlockLongerThan50ms`:
  - Measure wall-clock time of `Commit()` with a 100-entity JSON.
  - Assert elapsed < 50 ms.
- `TransitionPlannerTests.PlanWithScenarioId_InjectsStorageGatewayStep`:
  - Feed `ClusterOpRequest{TargetState=LoadingEdit, PayloadJson={ScenarioId="Alpha"}}`.
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
2. Implement `CheckpointDsmHandler` in `Hrot.SimHost`:
   - On `TakeSnapshot` command: immediately publish `NodeOpStatus(InProgress)`;
     on main thread at BeforeSync, call `snap.SyncFrom(liveRepo)` (~2 ms);
     enqueue `(snap, requestId)` to `CheckpointIOWorker`.
   - `ClusterSlave.Tick()` monitor: check `CompletionResults` each frame; when a matching
     `requestId` is found, publish `NodeOpStatus(Success/Failure)` to DDS (deferred ACK).
3. `LiveLoadDsmHandler.PrepareAsync()` must call `await CheckpointIOWorker.DrainAsync()`
   before triggering `FinalizeRecordingAsync()`.

**Success conditions:**
- `CheckpointDsmHandlerTests.TwoOverlappingCheckpoints_ACKsAreBothDeferred`:
  - Inject two rapid `TakeSnapshot` commands (Req_A then Req_B, 100 ms apart sim time).
  - Assert `NodeOpStatus(InProgress)` messages are published immediately for both.
  - Assert `NodeOpStatus(Success)` messages arrive only **after** disk writes complete
    (verified by polling `CheckpointIOWorker.TakeCompletedResults()` to confirm the
    deferred ACK path — i.e. the frame-monitor loop in `DrainDeferredAcks` publishes
    Success only once `TakeCompletedResults` returns the matching `requestId`).
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


** notes on similarity to the fligth recorder module **

Reuse the building blocks of the existing async recorder for checkpointing as much as possible - keep the "DRY" principle. 

Duplicating complex memory serialization logic would be a massive architectural anti-pattern. Fortunately, the sources confirm that the true low-level implementation building blocks are already heavily modularized and strictly shared between both operations.

The architecture avoids code duplication by pushing the shared mechanics down into the core ECS memory layer, specifically through the `NativeChunkTable<T>` and the `IUnmanagedComponentTable` interface. Both the async flight recorder and the checkpointing system rely on the exact same zero-allocation primitives for memory extraction, such as `CopyChunkToBuffer`, `SyncDirtyChunks`, and `SanitizeChunk` (which explicitly zeros out dead entity slots in unmanaged memory to maximize LZ4 compression efficiency). 

Furthermore, both pipelines respect the exact same declarative attribute system to filter out transient data. Whether you are recording or saving a checkpoint, the engine generates an optimized `BitMask256` from `[DataPolicy(DataPolicy.NoSave)]` or `[DataPolicy(DataPolicy.NoRecord)]` and applies it using the exact same bitwise filtering mechanisms to ensure temporary runtime states never hit the disk.

Where the two mechanisms diverge is strictly in how they compose these shared blocks to satisfy their different performance profiles. The checkpointing mechanism needs to capture a single, perfect frame instantly, so it uses `EntityRepository.SyncFrom()` to perform a ~2 ms synchronous `memcpy` of the live chunk tables into an isolated, secondary `EntityRepository` living in RAM. It then passes this entire cloned repository to the background `CheckpointIOWorker` for compression, freeing the main thread immediately. 

Conversely, the `AsyncRecorder` streams data continuously, so it iterates over the live chunks, copies them into a pre-allocated scratch buffer, sanitizes them in-place, and feeds them directly into the compression worker chunk-by-chunk without ever allocating a full secondary repository. 

By keeping the low-level chunk manipulation, sanitization, and policy masking completely unified inside `Fdp.Kernel`, the architecture perfectly satisfies the DRY principle while allowing the application layer to compose those blocks optimally for either continuous delta-streaming or isolated full-RAM cloning.
---

### CGF1-S0304 — Dynamic Recording Modules

**Design ref:** [§5.4](./CGF-1-DESIGN.md#54-stage-34--dynamic-recording-modules)

**Work to do:**
1. Create `FDP/Kernel/Fdp.Kernel/Orchestration/IRecordReplayController.cs`
   (pure FDP interface — no Hrot references).
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
7. Implement `RecordingModule : IModule, IDisposable` in `Hrot.SimHost`:
   - `Initialize()` constructs `AsyncRecorder`, registers `RecorderTickSystem` with
     `ModuleHostKernel`.
   - `Dispose()` calls `AsyncRecorder.Dispose()` (blocking: flush LZ4 + write
     `.meta.json`).
8. Implement `ReplayModule : IModule, IDisposable` in `Hrot.SimHost`.
9. Implement `EcsRecordReplayController : IDsmHandler` in `Hrot.SimHost`:
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
   - **Before** issuing the `NodeOpCommand`, `ClusterMaster` calls
     `replayMasterModule.SetTimeScale(0.0)` (hard freeze).
   - Generate new branched `ExerciseId`.
   - Issue `NodeOpCommand(PrepareState, LoadingLive, newExerciseId)`.
2. In `ReplayLoadDsmHandler.PrepareAsync()` for the `PrepareLive` command:
   - Call `EcsRecordReplayController.TeardownReplayAsync()`.
   - **Do not** mutate `EntityRepository` — merely uninstall `ReplayModule` (disposes
     `PlaybackController`, closes read handles).
   - Call `EcsRecordReplayController.PrepareRecordingAsync(newExerciseId)`.
   - Re-enable `SimulationSystemGroup`, `NetworkLifecycleSystemGroup`.
   - Set `GhostCreationSystem.BypassLifecycle = false`.
3. After all nodes report `NodeOpStatus(Success)`, `ClusterMaster` commits
   `SystemStateTopic(RunningLive, newExerciseId)` then calls `SetTimeScale(savedScale)`.
4. Add `ReplayMasterModule` to `Hrot.Orchestrator` that wraps `MasterTimeController`
   for replay playhead control.

**Success conditions:**
- `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState`:
  - Open a real `.fdp` file with 5 entities; seek to a mid-point.
  - Call `EcsRecordReplayController.TeardownReplayAsync()`.
  - Assert `repo.EntityCount == 5` (historical state preserved in-place, zero memcpy).
- `LiveFromReplayTests.AfterBranch_RecordingModuleIsInstalled`:
  - After the full `PrepareRecordingAsync(branchedExerciseId)` call, assert
    `"RecorderTickSystem"` is present in the kernel's scheduler.
- `LiveFromReplayTests.AfterBranch_SimGroupsReEnabled`:
  - Assert `SimulationSystemGroup.Enabled == true`.
  - Assert `GhostCreationSystem.BypassLifecycle == false`.
- `LiveFromReplayTests.TimeFrozenDuringBranchTransition`:
  - Assert that from the moment `ClusterMaster` issues the `PrepareLive` command until
    all nodes ACK success, `MasterTimeController.GetTimeScale() == 0.0`.
- Integration test `FullBranchPipelineTests.BranchedRecording_CapturesHistoricalStateAsKeyframe`:
  - Run 100 ticks of live simulation; seek in replay to tick 50; execute the
    Live-from-Replay branch; run 50 more ticks of branched live simulation.
  - Assert the `.fdp` file for the branched ExerciseId contains a keyframe at frame 0
    that matches the ECS snapshot at tick 50 of the original recording.

---

### CGF1-S0306 — Scenario/Story Serialization Toolkit

**Design ref:** [§5.6](./CGF-1-DESIGN.md#56-stage-36--scenariostory-serialization-toolkit)

**New project:** `FDP/Toolkits/FDP.Toolkit.Scenario` — no Hrot references.

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
  - Deserialize a DOM with `SubsystemType = "Hrot.SimHost"` using a serializer
    configured for `"Hrot.CGF"`.
  - Assert `EntityRepository.EntityCount` does not increase.
- `ScenarioSerializerTests.FdpAutoSerializer_NoReflectionOnHotPath`:
  - After `Build()`, invoke `Serialize`; assert no `PropertyInfo.GetValue` calls
    occur (use a profiling stub or delegate inspection).

---

### CGF1-S0307 — Application-Layer Scenario Save/Load Wiring

**Design ref:** [§5.7](./CGF-1-DESIGN.md#57-stage-37--application-layer-scenario-saveload-wiring)

**Work to do:**
1. Create `Hrot.Orchestrator/GlobalContextDsmHandler.cs`:
   - **Save:** on `SerializeLocal` command, build a `GlobalContextDto` (start wall
     ticks, weather descriptor, scene identifier) and serialize to
     `C:\FDP_Temp\<ExerciseId>\Orchestrator.json`. Return UNC path in `NodeOpStatus.ResultJson`.
   - **Load:** on `CommitState(LoadingLive|LoadingEdit)`, parse `Orchestrator.json`,
     call `MasterTimeController.SeedState(new GlobalTime { TotalWallTicks = dto.StartWallTicks })`,
     then publish `OrchestratorContextMessage` to the DDS `OrchestratorContextTopic`.
2. Register `GlobalContextDsmHandler` in the Orchestrator's own `ClusterSlave` at startup.
3. In `Hrot.SimHost` (and `Hrot.CGF`), create `ScenarioLoadDsmHandler`:
   - `PrepareAsync`: peek at `Header.SubsystemType`; if mismatch, return `Success` immediately.
   - If match: parse full DOM, call `ScenarioSerializer.Deserialize()`, return `Success`.
4. Wire `ScenarioLoadDsmHandler` registration in `SimHostApp.OnLoad()` and `CgfApp.OnLoad()`.
5. Extend `TransitionPlanner.PlanTrajectory()`:
   - When `ClusterOpRequest` contains `ScenarioId`, insert a
     `OperationStep(PrefetchScenario, scenarioId)` before the first `TransitionStep`.
6. Extend `StorageGatewayModule`:
   - `PrefetchScenarioAsync(string scenarioId)`: copies all files from
     `\\NAS\Scenarios\<scenarioId>\` to each node's `C:\FDP_Temp\<scenarioId>\` via
     `NodeOpCommand(PrefetchFiles, manifest)`.
7. Extend `StorageGatewayModule` save path:
   - After all nodes return UNC manifests, collect paths and copy to NAS under
     `\\NAS\Scenarios\<scenarioId>\`.
   - Write `scenario_manifest.json` listing all participating file names.

**Success conditions (integration tests in `Hrot.Orchestrator.Integration.Tests`):**
- `ScenarioSaveLoadTests.RoundTrip_SimHost_EntitiesMatchAfterLoad`:
  - Run live simulation with 3 entities in SimHost.
  - Trigger `ClusterOpRequest(SaveScenario, "test_01")`.
  - Verify `scenario_manifest.json` is written to NAS.
  - Clear ECS; trigger `ClusterOpRequest(LoadScenario, "test_01")`.
  - Assert 3 entities present in SimHost `EntityRepository` with matching component data.
- `ScenarioSaveLoadTests.OrchestratorContextRestored_AfterLoad`:
  - Save scenario; clear Orchestrator context; load scenario.
  - Assert `OrchestratorContextTopic.SceneId` matches the saved value.
- `ScenarioSaveLoadTests.SubsystemTypeFilter_CGFFileNotLoadedBySimHost`:
  - Create a scenario file with `SubsystemType = "Hrot.CGF"`.
  - Let SimHost's `ScenarioLoadDsmHandler` process it.
  - Assert `EntityRepository.EntityCount` in SimHost does not increase.

---

### CGF1-S0308 — Runtime Story Injection & Deletion

**Design ref:** [§5.8](./CGF-1-DESIGN.md#58-stage-38--runtime-story-injection--deletion)

> **Implementation status (BATCH-20):**
> Items 1–3 and `NodeOpStatus.IsParticipating` wire-field (item 4) are complete.
> The `ClusterMaster` **ACK gating** (item 4 — "waits only for participating nodes")
> is an **intentional MVP delta**: `ManageEpisode` currently fans out and immediately
> resolves `ClusterOpStatus.InProgress` with `CompletedSteps == totalSteps` without a
> `NodeOpStatus` round-trip.  Full 2PC for story ops (subscribe on orchestrator side,
> per-transaction participation tracking, timeout) is deferred to a future batch when
> multi-node story coordination becomes a hard product requirement.  The CGF
> `ClusterSlave` now has a `NodeOpStatusWriter` so nodes *can* publish the ACK; the
> orchestrator-side consumption is the missing piece.

**Work to do:**
1. Add `ManageEpisode` operation to `TransitionPlanner`:
   - Validate `CurrentState == RunningLive`; if not, return `OpStatus.InvalidState`.
   - For `Mode:Start`: append `OperationStep(PrefetchStory, storyId)` then
     `OperationStep(StartEpisode, { StoryId, ScenarioId })`.
   - For `Mode:Stop`: append `OperationStep(StopEpisode, { StoryId })`.
2. Implement `StoryLoadDsmHandler` in `Hrot.SimHost` (and `Hrot.CGF`):
   - On `StartEpisode(storyId, scenarioId)` (**`storyId`: `Guid`** — parse from payload JSON if the wire format uses strings):
     - Load DOM from `C:\FDP_Temp\<scenarioId>\{SubsystemType}.json`.
     - Peek `Header.SubsystemType`; if mismatch, reply
       `NodeOpStatus(Success, IsParticipating: false)`.
     - If match, call `ScenarioSerializer.Deserialize(repo, dom, asStory: true, storyId)`.
     - Reply `NodeOpStatus(Success, IsParticipating: true)`.
   - On `StopEpisode(storyId)` (**`Guid`**):
     - Query all entities with **`FDP.Toolkit.Replay.StoryTag`** where **`StoryId == storyId`**.
     - Destroy each entity.
     - Reply `NodeOpStatus(Success)`.
3. Extend `ClusterMaster` to track active stories in `OrchestratorContextTopic.ActiveStories`
   (**`List<Guid>`** of active story IDs, or a wire-serializable equivalent; published after each Start/Stop operation).
4. Extend `NodeOpStatus` with `bool IsParticipating` field (default `true`).  
   `ClusterMaster` waits only for ACKs from nodes that replied `IsParticipating: true`
   during the `PrepareStory` phase.
   > **MVP delta (BATCH-20):** The `IsParticipating` field exists in the DDS schema and
   > nodes publish it.  `ClusterMaster`-side ACK gating (subscribe + per-transaction
   > participation filter + timeout) is deferred — see implementation status note above.

**Success conditions (integration tests in `Hrot.SimHost.Integration.Tests`):**
- `StoryInjectionTests.StartEpisode_EntitiesSpawnedWithStoryTag`:
  - System is in `RunningLive`. Inject a story with a known **`Guid`** `storyId` targeting `Hrot.SimHost`.
  - Assert 3 new entities appear in `EntityRepository`.
  - Assert each has **`FDP.Toolkit.Replay.StoryTag`** with **`StoryId == storyId`**.
- `StoryInjectionTests.StopEpisode_EntitiesDestroyedByStoryTag`:
  - Inject `storyId` (3 entities). Stop the same **`Guid`**.
  - Assert those 3 entities are no longer in `EntityRepository`.
- `StoryInjectionTests.StartEpisode_NonMatchingSubsystem_ReturnsIsParticipatingFalse`:
  - Create a story file with `SubsystemType = "Hrot.CGF"`.
  - Process it through `SimHost.StoryLoadDsmHandler`.
  - Assert `NodeOpStatus.IsParticipating == false` and `EntityRepository.EntityCount`
    unchanged.
- `StoryInjectionTests.ManageEpisode_RejectedWhen_NotInRunningLive`:
  - Issue `ClusterOpRequest(ManageEpisode, { Mode:Start })` while cluster is in `Standby`.
  - Assert response is `OpStatus.InvalidState`.
- `StoryInjectionTests.MultipleStoriesCoexist_IndependentDeletion`:
  - Inject two distinct story **`Guid`**s `s1` / `s2` (3 and 2 entities).
  - Stop `s1`.
  - Assert `s1` entities gone; assert `s2` entities still present.

---

### CGF1-S0309 — Dry Run DSM Handler

**Design ref:** [§5.9](./CGF-1-DESIGN.md#59-stage-39--dry-run-dsm-handler)

**Context:** The checkpointing primitive (`EntityRepository.SyncFrom()`) introduced in
CGF1-S0303 is fully implemented. This task wires the RAM-only variant of that
primitive into the DSM lifecycle to power the edit-preview-rewind loop.

**Work to do:**

1. Implement `DryRunDsmHandler` in
   `Hrot.Common/Orchestration/Handlers/DryRunDsmHandler.cs`:
   - Constructor accepts `EntityRepository? liveRepo` (nullable for subsystems
     without ECS, e.g. `Hrot.ExCon`).
   - `CanHandle(NodeOpType op)` returns `true` for `NodeOpType.PrepareState`.
   - `PrepareAsync` returns `Task.FromResult<string?>(null)` — no async work needed
     for either act.
   - `Commit` implements both acts via target-state inspection of `cmd.PayloadJson`
     (same `ParseTargetState` helper pattern as `EditLoadDsmHandler`):
     - **`LoadingDryRun`:** if `liveRepo != null`, allocate `_snap = new EntityRepository()`,
       call `_snap.SyncFrom(liveRepo)` on the main thread (BeforeSync, ~2 ms).
       If `liveRepo == null`, log a warning and set `_snap = null` — handler
       treats a null repo as a participating no-op (ECS-less subsystems pass through).
     - **`UnloadingDryRun`:** if `_snap != null`, call `liveRepo!.SyncFrom(_snap)`,
       then `_snap.Dispose()`, then `_snap = null`.
       If `_snap == null` (snapshot was never taken or already released), log a
       warning and return — do **not** throw; the state machine must complete.
     - All other `PrepareState` targets: no-op (return without touching `_snap`).
   - `Abort`: dispose and null `_snap` if it was allocated (handles aborted
     `LoadingDryRun` transactions).
   - **Do not** implement `ITickableDsmHandler` — there are no deferred ACKs.
   - **Do not** touch `CheckpointIOWorker` — this is a RAM-only path.

2. Register `DryRunDsmHandler` in `NodeBootstrapper.BuildOrchestration` (already done via
   `Hrot.Common`), and in `CgfApplication` alongside the existing DSM handlers.
   Pass the live `EntityRepository` reference (or `null` for ECS-less subsystems).

3. `Hrot.ExCon` and `Hrot.IG` register the handler with `liveRepo = null` so they
   participate in the 2PC round-trip without error.

**Success conditions:**

- `DryRunDsmHandlerTests.LoadingDryRun_SnapshotCapturesLiveState`:
  - Build a `liveRepo` registered with `DryRunTestPos` (ComponentId 210).
    Create 4 entities, each with a known `DryRunTestPos` value.
  - Create `DryRunDsmHandler(liveRepo)`.
  - Call `Commit(PrepareState → LoadingDryRun, liveRepo)`.
  - Assert `_snap` (exposed via `TestHook_Snap` internal accessor) is non-null.
  - Assert `_snap.EntityCount == 4`.
  - Assert the `DryRunTestPos` values in `_snap` match those in `liveRepo`.

- `DryRunDsmHandlerTests.UnloadingDryRun_RewindsLiveRepo`:
  - Build a `liveRepo` with 4 entities, each with known `DryRunTestPos` values.
  - Call `Commit(PrepareState → LoadingDryRun)` — snapshot captures 4 entities.
  - Tick `liveRepo` once (required so `SyncDirtyChunks` detects mutation).
  - Mutate one entity's `DryRunTestPos` in `liveRepo`.
  - **Spawn an extra (5th) entity** in `liveRepo`.
  - Call `Commit(PrepareState → UnloadingDryRun)`.
  - Assert `liveRepo.EntityCount == 4` (5th entity removed by rewind).
  - Assert the mutated `DryRunTestPos` has been **reverted** to its original value.

- `DryRunDsmHandlerTests.UnloadingDryRun_DisposesSnapshot`:
  - After a full `LoadingDryRun` + `UnloadingDryRun` cycle, assert `_snap == null`
    (internal accessor returns null after restore).

- `DryRunDsmHandlerTests.Abort_DuringLoadingDryRun_DiscardsSnap`:
  - Call `Commit(PrepareState → LoadingDryRun, liveRepo)` to allocate the snap.
  - Call `Abort(...)`.
  - Assert `_snap == null`.

- `DryRunDsmHandlerTests.OtherPrepareStateTargets_AreNoOps`:
  - Call `Commit(PrepareState → LoadingLive, liveRepo)` and
    `Commit(PrepareState → LoadingEdit, liveRepo)` in sequence.
  - Assert `_snap == null` after both calls (handler ignored both).

- `DryRunDsmHandlerTests.UnloadingDryRun_WithNullSnap_LogsWarningAndReturns`:
  - Construct `DryRunDsmHandler(liveRepo)`.
  - Call `Commit(PrepareState → UnloadingDryRun, liveRepo)` **without** a prior
    `LoadingDryRun` commit (simulates an aborted prepare where snap was never set).
  - Assert no exception is thrown; assert `liveRepo` is unchanged.

---

### CGF1-S0310 — E2E DSM Test Script Suite

**Design ref:** [§5.10](./CGF-1-DESIGN.md#510-stage-310--e2e-dsm-test-script-suite)

**Depends on:** CGF1-S0303, CGF1-S0304, CGF1-S0305, CGF1-S0309, and the existing
`HeadlessTestExecutor` + `HrotActionHandlers` infrastructure.

**Context:** The platform has `HeadlessTestExecutor` (in `FDP.Framework.Runner`), and
three generic action handlers (`spawn`, `move`, `assert_position`) in
`Hrot.ClusterRunner/Testing/HrotActionHandlers.cs`. There is no DSM-aware action handler
and no concrete E2E scripts that exercise the distributed orchestration paths. This task
adds both.

**Work to do:**

1. Create `Hrot.ClusterRunner/Testing/OrchestratorActionHandlers.cs` with two handler classes:
   a. **`SysopActionHandler`** (action name `"sysop"`):
      - Constructor: `SysopActionHandler(ClusterMaster drillMaster, DdsReader<ClusterOpStatus>
        statusReader, double timeoutSeconds = 10.0)`.
      - `ExecuteAsync(args)`: reads `args["TargetState"]` (string or integer).
        - If value is a `ClusterState` name → calls
          `drillMaster.HandleClusterOpRequestAsync(new ClusterOpRequest { OperationType =
          ClusterOpType.TransitionState, PayloadJson = ... })` including optional
          `ExerciseId`, `ScenarioId`, `TargetWallTicks` from `args`.
        - `"TakeCheckpoint"` special value → `ClusterOpType.TakeSnapshot`.
        - `"ReplaySeek"` special value + mandatory `args["TargetWallTicks"]` →
          `ClusterOpType.ReplaySeek`.
      - Polls `statusReader` (up to `timeoutSeconds`) for a `ClusterOpStatus` whose
        `RequestId` matches. Returns `{"status": "success"}` or throws
        `TestAssertionException` with the failure message on timeout or
        `ClusterOpStatus.Failure`.
   b. **`AssertEntityCountActionHandler`** (action name `"assert_entity_count"`):
      - `ExecuteAsync(args)`: reads `args["expected"]` (int); asserts
        `_world.EntityCount == expected`; throws `TestAssertionException` on mismatch.

2. Create `Hrot.ClusterRunner.Integration.Tests/Systems/MovingEntitySystem.cs`:
   - Registered only by the E2E test fixture (not in production boots).
   - Each tick: for every entity with `MovingTestTag`, advances
     `SimTransform.Position.X += MovingTestTag.VelocityX * GlobalTime.DeltaTime`.
   - `MovingTestTag` is an unmanaged ECS component `{ float VelocityX; }` declared in
     the same file.

3. Create four JSON test scripts in
   `Hrot.ClusterRunner.Integration.Tests/TestScripts/`:

   **`e2e_record_and_replay_seek.json`**
   ```json
   {
     "TestName": "E2E_Record_And_Replay_Seek",
     "TimeMode": "Deterministic",
     "FixedDeltaSeconds": 0.1,
     "Duration": 20.0,
     "Steps": [
       { "Time": 0.5,  "Action": "sysop",
         "Args": { "TargetState": "RunningLive", "ExerciseId": "e2e-rec-01" } },
       { "Time": 1.0,  "Action": "spawn",
         "Args": { "x": 0.0, "y": 0.0, "z": 0.0 },
         "SaveResult": "entity_a" },
       { "Time": 1.1,  "Action": "add_moving_tag",
         "Args": { "entity_ref": "entity_a", "velocity_x": 10.0 } },
       { "Time": 7.0,  "Action": "sysop",
         "Args": { "TargetState": "RunningReplay", "ExerciseId": "e2e-rec-01" } },
       { "Time": 9.0,  "Action": "sysop",
         "Args": { "TargetState": "ReplaySeek",
                   "TargetWallTicks": 30000000 } },
       { "Time": 10.0, "Action": "assert_position",
         "Args": { "entity_ref": "entity_a" },
         "Assert": { "x": { "ApproxEquals": 30.0, "Tolerance": 0.001 } } }
     ]
   }
   ```
   **Success condition:** Entity at `x ≈ 30.0 m` (3 s × 10 m/s) at the seeked tick, within 0.001 m.

   **`e2e_dryrun_state_restore.json`**
   ```json
   {
     "TestName": "E2E_DryRun_State_Restore",
     "TimeMode": "Deterministic",
     "FixedDeltaSeconds": 0.1,
     "Duration": 15.0,
     "Steps": [
       { "Time": 0.5, "Action": "sysop",
         "Args": { "TargetState": "RunningEdit" } },
       { "Time": 1.0, "Action": "spawn",
         "Args": { "x": 100.0, "y": 0.0, "z": 0.0 },
         "SaveResult": "entity_b" },
       { "Time": 2.0, "Action": "sysop",
         "Args": { "TargetState": "RunningDryRun" } },
       { "Time": 3.0, "Action": "move",
         "Args": { "entity_ref": "entity_b", "x": 999.0, "y": 0.0, "z": 0.0 } },
       { "Time": 4.0, "Action": "spawn",
         "Args": { "x": 50.0, "y": 0.0, "z": 0.0 } },
       { "Time": 5.0, "Action": "assert_entity_count",
         "Args": { "expected": 2 } },
       { "Time": 7.0, "Action": "sysop",
         "Args": { "TargetState": "RunningEdit" } },
       { "Time": 8.0, "Action": "assert_position",
         "Args": { "entity_ref": "entity_b" },
         "Assert": { "x": { "Equals": 100.0 } } },
       { "Time": 8.1, "Action": "assert_entity_count",
         "Args": { "expected": 1 } }
     ]
   }
   ```
   **Success condition:** After returning to RunningEdit, entity_b is at `x = 100.0` and the 5th entity is gone.

   **`e2e_live_from_replay_branch.json`**
   ```json
   {
     "TestName": "E2E_Live_From_Replay_Branch",
     "TimeMode": "Deterministic",
     "FixedDeltaSeconds": 0.1,
     "Duration": 25.0,
     "Steps": [
       { "Time": 0.5,  "Action": "sysop",
         "Args": { "TargetState": "RunningLive", "ExerciseId": "e2e-branch-src" } },
       { "Time": 1.0,  "Action": "spawn",
         "Args": { "x": 0.0, "y": 0.0, "z": 0.0 },
         "SaveResult": "entity_c" },
       { "Time": 1.1,  "Action": "add_moving_tag",
         "Args": { "entity_ref": "entity_c", "velocity_x": 5.0 } },
       { "Time": 8.0,  "Action": "sysop",
         "Args": { "TargetState": "RunningReplay", "ExerciseId": "e2e-branch-src" } },
       { "Time": 12.0, "Action": "sysop",
         "Args": { "TargetState": "RunningLive", "ExerciseId": "e2e-branch-dst" } },
       { "Time": 14.0, "Action": "spawn",
         "Args": { "x": 50.0, "y": 50.0, "z": 0.0 },
         "SaveResult": "entity_d" },
       { "Time": 15.0, "Action": "assert_position",
         "Args": { "entity_ref": "entity_d" },
         "Assert": { "x": { "Equals": 50.0 } } }
     ]
   }
   ```
   **Success condition:** A new entity spawned post-branch has correct position; no ID-allocator collision exception is thrown (proves `MaxNetworkId` high-water reset succeeded).

   **`e2e_overlapping_checkpoints.json`**
   ```json
   {
     "TestName": "E2E_Overlapping_Checkpoints",
     "TimeMode": "Deterministic",
     "FixedDeltaSeconds": 0.1,
     "Duration": 15.0,
     "Steps": [
       { "Time": 0.5, "Action": "sysop",
         "Args": { "TargetState": "RunningLive" } },
       { "Time": 1.0, "Action": "spawn",
         "Args": { "x": 0.0, "y": 0.0, "z": 0.0 },
         "SaveResult": "entity_e" },
       { "Time": 2.0, "Action": "move",
         "Args": { "entity_ref": "entity_e", "x": 10.0, "y": 0.0, "z": 0.0 } },
       { "Time": 3.0, "Action": "sysop",
         "Args": { "TargetState": "TakeCheckpoint" } },
       { "Time": 3.1, "Action": "sysop",
         "Args": { "TargetState": "TakeCheckpoint" } },
       { "Time": 9.0, "Action": "assert_position",
         "Args": { "entity_ref": "entity_e" },
         "Assert": { "x": { "Equals": 10.0 } } }
     ]
   }
   ```
   **Success condition:** Both checkpoints succeed (both deferred `ClusterOpStatus(Success)` ACKs arrive within timeout); simulation continues and entity position is intact.

4. Create `Hrot.ClusterRunner.Integration.Tests/DsmE2eScriptTests.cs` with four xUnit
   `[Fact]` methods, one per script:
   - Each fact boots an in-process all-in-one stack:
     ```
     SubsystemOrchestrator(Headless=true, Stepping=true) + [OrchestratorSubsystem,
     SimHostSubsystem(ECS=true, NAS=MockNas)]
     ```
   - Registers `SysopActionHandler`, `AssertEntityCountActionHandler`, `SpawnActionHandler`,
     `MoveActionHandler`, `AssertPositionActionHandler`, `AddMovingTagActionHandler`.
   - Loads the JSON script from `TestScripts/` (embedded resource or relative path).
   - Calls `HeadlessTestExecutor.RunAsync()` and asserts return value is `TestResult.Pass`.
   - On failure, the test message includes the script name and the `TestAssertionException`
     message from the failing step.
   - **Wall-clock timeout:** each test must complete within 60 s (xUnit `[Fact(Timeout
     = 60000)]`) to prevent CI hang.

5. Create `AddMovingTagActionHandler` (action name `"add_moving_tag"`) in
   `OrchestratorActionHandlers.cs`:
   - Reads `entity_ref` (looked up in `HeadlessTestExecutor.SavedResults`) and
     `velocity_x` (float).
   - Adds `MovingTestTag { VelocityX = velocityX }` to the entity via `EntityRepository`.

**Success conditions:**

- `DsmE2eScriptTests.RecordAndReplaySeek_Passes`:
  - Run `e2e_record_and_replay_seek.json` in-process.
  - Assert `TestResult.Pass` (entity within 0.001 m of expected position at seeked tick).

- `DsmE2eScriptTests.DryRunStateRestore_Passes`:
  - Run `e2e_dryrun_state_restore.json` in-process.
  - Assert `TestResult.Pass` (entity reverts, extra entity gone).

- `DsmE2eScriptTests.LiveFromReplayBranch_Passes`:
  - Run `e2e_live_from_replay_branch.json` in-process.
  - Assert `TestResult.Pass` (post-branch entity spawns without crash or ID collision).

- `DsmE2eScriptTests.OverlappingCheckpoints_Passes`:
  - Run `e2e_overlapping_checkpoints.json` in-process.
  - Assert `TestResult.Pass` (both checkpoints acknowledged; entity state intact).

---

## Phase 4 — Generalization: FDP Toolkit Orchestration

**Goal:** Lift the reusable orchestration engine — `IDsmHandler`, the `ClusterSlave`
dispatch loop, the BFS `TransitionPlanner`, and all reference handler implementations
— out of the Hrot application layer into `FDP.Toolkit.Orchestration`, so any FDP
application can participate in a 2PC state machine without copying Hrot infrastructure.

**Design authority:** [CGF-1-GENERALIZATION.md](./CGF-1-GENERALIZATION.md)

---

### CGF1-G0401 — FDP.Toolkit.Orchestration Core Contracts

**Design ref:** [§4 — New Project: FDP.Toolkit.Orchestration](./CGF-1-GENERALIZATION.md#4-new-project-fdptoolkitorchestration)

**Context:** `IDsmHandler` and `ITickableDsmHandler` currently live in
`Hrot.Common.Orchestration`, which violates the architectural goal of a reusable
FDP toolkit. This task creates the new project, moves the core interfaces, and adds
the new abstraction contracts (`IOrchestrationTransport`, `ITransitionGraph`,
`IScenarioStorageProvider`) alongside the toolkit-level plain value types
(`OrchestrationCommand`, `OrchestrationStatus`, `TkClusterStateChangedEvent`).

**Work to do:**

1. Create `FDP/Toolkits/FDP.Toolkit.Orchestration/FDP.Toolkit.Orchestration.csproj`
   referencing `Fdp.Kernel`, `FDP.Toolkit.Scenario`, `FDP.Toolkit.Replay`, and
   `ModuleHost.Core`. No `Hrot.*` or `CycloneDDS.*` references are permitted.

2. Move `IDsmHandler` (verbatim) from
   `Hrot.Common/Orchestration/IDsmHandler.cs` → new project under
   `FDP/Toolkits/FDP.Toolkit.Orchestration/IDsmHandler.cs`, changing the namespace to
   `FDP.Toolkit.Orchestration`. Rename the `CanHandle` parameter from `NodeOpType op`
   to `int operationId`. Update the XML doc to note that callers may cast `operationId`
   back to their specific enum type.

3. Move `ITickableDsmHandler` similarly to `FDP.Toolkit.Orchestration`.

4. Add to the new project:
   - `OrchestrationCommand` — `readonly record struct` with `Guid TransactionId`,
     `int TargetNodeId`, `int OperationId`, `string PayloadJson`.
   - `OrchestrationStatus` — `readonly record struct` with `Guid TransactionId`,
     `int NodeId`, `int StatusCode` (unified — see §4.2.1 of the generalization addendum),
     `bool IsParticipating`, `string ResultJson`. **No separate `ErrorCode` field.**
   - `OrchestrationStatusCode` — `static class` with named constants:
     `Success=0`, `InProgress=1`, `Pending=2`, `Rejected=10`, `Timeout=11`,
     `Cancelled=12`, `InvalidZone=101`, `ExerciseMismatch=102`,
     `OutOfMemory=1000`, `AssetNotFound=1001`; helper `static bool IsError(int)`.
     **Rationale:** `0` is the C# default for uninitialized `int` fields, so a
     zero-initialised wire struct naturally means «OK» — consistent with
     `SstStatusCode` already in use across Hrot DDS messages.
   - Update `Hrot.NED/Orchestration/OrchestrationMessages.cs`:
     remove `OpStatus` enum, replace `OpStatus Status` + `int ErrorCode` fields in
     `NodeOpStatus` and `ClusterOpStatus` with a single `int StatusCode`.
   - `IOrchestrationTransport` — interface with `PublishHeartbeat(int, string, int,
     long)`, `PublishStatus(OrchestrationStatus)`, `bool TryDequeueCommand(out
     OrchestrationCommand)`, inherits `IDisposable`.
   - `ITransitionGraph` — interface with `IReadOnlyList<int> GetNeighbors(int)` and
     `IReadOnlyList<int> AllStates`.
   - `TransitionGraphBuilder` — fluent builder implementing `AddState(int, string)`,
     `AddTransition(int, int)`, `Build() → ITransitionGraph`.
   - `IScenarioStorageProvider` — interface with `Stream? OpenScenarioFile(string,
     string)`, `string EnsureStagingDirectory(string)`,
     `IEnumerable<string> EnumerateScenarioFiles(string)`.
   - `TkClusterStateChangedEvent` — unmanaged `[EventId(7002)] struct` with
     `int PreviousStateId`, `int NextStateId`.

5. Update all Hrot `using` directives: replace
   `using Hrot.Common.Orchestration;` with
   `using FDP.Toolkit.Orchestration;` in every file that references `IDsmHandler`
   or `ITickableDsmHandler`. Leave `Hrot.Common.Orchestration` namespace intact
   (do not rename the `.cs` files) — only the `IDsmHandler.cs` stub, which already
   contains only a comment, is left in place.

6. Leave `Hrot.Common.Orchestration.ClusterStateChangedEvent` unchanged; add a
   comment referencing the toolkit's `TkClusterStateChangedEvent` as the preferred
   new type.

**Success conditions:**

- `Fact: FDP.Toolkit.Orchestration project builds` — solution builds with zero
  errors after the move; no `Hrot.*` type appears in the new project's source.

- `Fact: All Hrot handler files still compile` — every existing `IDsmHandler`
  implementation compiles after the using-directive update with no changes to
  logic.

- `Fact: OrchestrationCommand round-trips through JSON` — a simple unit test in
  `FDP.Toolkit.Orchestration.Tests` constructs an `OrchestrationCommand`, serialises
  to JSON, deserialises, and asserts field equality.

- `Fact: TransitionGraphBuilder builds valid graph` — a unit test calls
  `AddTransition(0, 1).AddTransition(1, 2).Build()`, then asserts
  `GetNeighbors(0)` returns `[1]` and `GetNeighbors(1)` returns `[2]`.

- `Fact: Unified status code scheme` — unit test asserts
  `OrchestrationStatusCode.IsError(OrchestrationStatusCode.Success) == false`,
  `OrchestrationStatusCode.IsError(OrchestrationStatusCode.Rejected) == true`,
  `OrchestrationStatusCode.IsError(1001) == true`.

- `Fact: DDS NodeOpStatus has single StatusCode field` — `NodeOpStatus` IDL struct
  compiles with a `StatusCode int` field; any reference to the removed `OpStatus`
  enum or `ErrorCode` field produces a build error (verified by grepping for
  `OpStatus` in `OrchestrationMessages.cs` returning zero matches).

---

### CGF1-G0402 — Generic ClusterSlave + DdsOrchestrationTransport

**Design ref:** [§4.3](./CGF-1-GENERALIZATION.md#43-generic-clustslave) and
[§5.1](./CGF-1-GENERALIZATION.md#51-ddsorchestrationtransport)

**Depends on:** CGF1-G0401

**Context:** There are four near-identical `ClusterSlave` classes across
`Hrot.SimHost`, `Hrot.CGF`, `Hrot.IG`, and `Hrot.ExCon`. The SimHost version
is the most complete (async-prepare deferral, `ITickableDsmHandler` poll, EventBus,
deduplication). This task consolidates them into a single generic implementation in
`FDP.Toolkit.Orchestration`, implements `DdsOrchestrationTransport` in
`Hrot.Common`, and replaces all four application-layer copies.

**Work to do:**

1. Implement `FDP.Toolkit.Orchestration.ClusterSlave` following the contract in
   §4.3 of the generalization addendum:
   - Two constructors: production (takes `IOrchestrationTransport`, `int nodeId`,
     `string subsystemName`, `FdpEventBus?`) and test-only `internal ClusterSlave(FdpEventBus?)`.
   - `RegisterHandler(IDsmHandler)`, `IsHandlerRegistered<T>()`,
     `IReadOnlyList<IDsmHandler> RegisteredHandlers`.
   - `Tick()`: publishes heartbeat via transport every 1 s; polls
     `ITickableDsmHandler.DrainDeferredAcks()`; processes `_pendingPrepare` if
     active; dequeues and dispatches via `TryDequeueCommand`.
   - `DispatchCommand()`: on `CommitState` updates `_localStateId` and publishes
     `TkClusterStateChangedEvent` via eventBus; on other ops delegates
     async-prepare + commit/defer using the BATCH-18 pattern.
   - Duplicate `TransactionId` deduplication (same `HashSet<Guid>` pattern).
   - `internal void EnqueueCommandForTest(OrchestrationCommand cmd)` for unit tests.
   - `internal int LocalStateIdForTest { get; }`.

2. Implement `Hrot.Common.Orchestration.DdsOrchestrationTransport : IOrchestrationTransport`
   following §5.1 of the addendum:
   - Constructor `(DdsParticipant participant, int nodeId)`.
   - `PublishHeartbeat` → writes `NodeHeartbeat`.
   - `PublishStatus` → writes `NodeOpStatus` (cast `OrchestrationStatus` fields to
     Hrot DDS types).
   - `TryDequeueCommand` → dequeues from an internal `ConcurrentQueue<OrchestrationCommand>`.
   - Background `Thread` listener reads `NodeOpCommand`, maps to
     `OrchestrationCommand`, enqueues.
   - `Dispose` cancels listener and joins thread.

3. Add wiring helper in `Hrot.Common` (or in each subsystem app):
   Register a forwarder so that `TkClusterStateChangedEvent` published by the toolkit
   ClusterSlave gets picked up and re-published as `ClusterStateChangedEvent { ClusterState }`:
   ```csharp
   eventBus.Register<TkClusterStateChangedEvent>();
   // Forwarder registered on the event bus subscription path
   ```
   The exact subscription API is at the implementer's discretion; the invariant is
   that `ClusterStateChangedEvent` consumers in Hrot still work without change.

4. Replace the four application-layer ClusterSlave copies with wiring to the toolkit:
   - `Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs` → delete (replace with a
     `using alias` or remove class entirely; `NodeBootstrapper` now uses toolkit type).
   - `Hrot.CGF/Modules/Orchestration/ClusterSlave.cs` → delete; `CgfApplication`
     switches to toolkit `ClusterSlave`.
   - `Hrot.IG/Modules/Orchestration/ClusterSlave.cs` → delete; `IgApplication`
     switches to toolkit.
   - `Hrot.ExCon/Orchestration/ClusterSlave.cs` → delete; `IosSubsystem` switches to
     toolkit.
   - All existing unit and integration tests that use
     `new ClusterSlave()` (test constructor) must be updated to use
     `new FDP.Toolkit.Orchestration.ClusterSlave()`.

**Success conditions:**

- `Fact: Toolkit ClusterSlave dispatches PrepareAsync + Commit` — unit test with a stub
  `IDsmHandler` verifies `Commit` is called after `PrepareAsync` completes; a second
  test confirms Commit is deferred one tick when `PrepareAsync` returns an incomplete
  task.

- `Fact: Toolkit ClusterSlave deduplicates transactions` — two commands with the same
  `TransactionId` are enqueued; handler's `PrepareAsync` call count is 1 (second
  dropped).

- `Fact: TkClusterStateChangedEvent published on CommitState` — inject event bus; send
  CommitState(nextState=5); assert `TkClusterStateChangedEvent { PreviousStateId=0,
  NextStateId=5 }` was published.

- `Fact: DdsOrchestrationTransport delivers commands to ClusterSlave` — integration
  test sends a `NodeOpCommand` DDS sample addressed to nodeId; asserts the toolkit
  ClusterSlave receives it within 2 s (`Timeout=5000`).

- `Fact: All existing ClusterSlave-based integration tests still pass` — run the full
  `Hrot.SimHost.Integration.Tests` suite and confirm no regressions.

---

### CGF1-G0403 — Generalize TransitionPlanner with ITransitionGraph

**Design ref:** [§4.4](./CGF-1-GENERALIZATION.md#44-itransitiongraph-and-transitiongraphbuilder)
and [§5.3](./CGF-1-GENERALIZATION.md#53-hrotstrategraph)

**Depends on:** CGF1-G0401

**Context:** `TransitionPlanner` in `Hrot.Orchestrator` contains a hardcoded
`static readonly Dictionary<ClusterState, ClusterState[]>` adjacency dictionary. The BFS
algorithm itself has no Hrot dependencies — it operates purely on integers. This task
extracts the planner into the FDP toolkit, adds `TransitionGraphBuilder`, and introduces
`HrotStateGraph` as the Hrot-specific state graph definition.

**Work to do:**

1. Move `TransitionPlanner` from `Hrot.Orchestrator/TransitionPlanner.cs` to
   `FDP/Toolkits/FDP.Toolkit.Orchestration/TransitionPlanner.cs`:
   - Change constructor to `TransitionPlanner(ITransitionGraph graph)`.
   - Change `CalculateShortestPath(ClusterState from, ClusterState to)` to
     `CalculateShortestPath(int fromStateId, int toStateId)` returning
     `IReadOnlyList<int>`.
   - The BFS algorithm itself needs no changes; only the type signatures and the
     adjacency source change.
   - Remove the hardcoded `_adjacency` dictionary entirely.

2. Create `Hrot.Orchestrator/HrotStateGraph.cs`:
   - Static class `HrotStateGraph` with `static ITransitionGraph Build()`.
   - Calls `TransitionGraphBuilder` with all valid `ClusterState` edges that were
     previously hardcoded in `TransitionPlanner._adjacency`.

3. Update `Hrot.Orchestrator/ClusterMaster.cs`:
   - Replace the direct `new TransitionPlanner()` call with
     `new TransitionPlanner(HrotStateGraph.Build())`.
   - Update call sites that passed `ClusterState` enum values to cast to `int`;
     update call sites that received `IReadOnlyList<ClusterState>` paths to
     `IReadOnlyList<int>`.

4. Add `FDP.Toolkit.Orchestration` project reference to `Hrot.Orchestrator.csproj`.

**Success conditions:**

- `Fact: TransitionPlanner lives only in FDP toolkit` — `grep` / Roslyn analysis
  shows `TransitionPlanner` in `FDP.Toolkit.Orchestration` namespace only; no copy
  in `Hrot.Orchestrator`.

- `Fact: BFS path preserved` — unit test constructs a `HrotStateGraph.Build()`
  derived graph and calls `CalculateShortestPath((int)ClusterState.Standby,
  (int)ClusterState.RunningLive)`, asserts the returned path passes through the same
  intermediate states as the prior `TransitionPlannerTests` verified.

- `Fact: All existing TransitionPlanner unit tests pass` — migrate
  `Hrot.Orchestrator.Tests/TransitionPlannerTests.cs` to use the toolkit type;
  all previously-passing tests remain green.

- `Fact: ClusterMaster uses HrotStateGraph` — `ClusterMaster` constructs
  `TransitionPlanner` from `HrotStateGraph.Build()`; no direct reference to
  `ClusterState` inside `TransitionPlanner.cs`.

---

### CGF1-G0404 — Reference Scenario, Story, and Prefetch Handlers

**Design ref:** [§4.7](./CGF-1-GENERALIZATION.md#47-reference-handler-catalogue),
[§4.5](./CGF-1-GENERALIZATION.md#45-iscenariосtorageprovider),
[§5.2](./CGF-1-GENERALIZATION.md#52-localdiskstorageprovider)

**Depends on:** CGF1-G0401, CGF1-G0402

**Context:** `PrefetchFilesDsmHandler`, `ScenarioLoadDsmHandler`, `EditLoadDsmHandler`,
and `StoryLoadDsmHandler` exist in `Hrot.SimHost` (complete) and a subset in
`Hrot.CGF` (header-peek-only variants). The duplicated CGF variants show the
maintenance burden. Introducing `IScenarioStorageProvider` lets the handlers become
FDP types, covering both subsystem variants with a single parameterized implementation.

**Work to do:**

1. Add `Hrot.Common/Orchestration/LocalDiskStorageProvider.cs` implementing
   `IScenarioStorageProvider` as described in §4.5 of the addendum. The
   `localTempRoot` defaults to `@"C:\FDP_Temp"`.

2. Migrate `PrefetchFilesDsmHandler` →
   `FDP.Toolkit.Orchestration.Handlers.ReferencePrefetchHandler`:
   - Replace `DdsWriter<NodeOpStatus>?` parameter with `IOrchestrationTransport?`.
   - Replace raw `string localTempRoot` parameter with `IScenarioStorageProvider`.
   - Call `storageProvider.EnsureStagingDirectory(scenarioId)` in `PrepareAsync`.
   - Call `transport?.PublishStatus(...)` in `Commit`.

3. Migrate `ScenarioLoadDsmHandler` (SimHost variant) →
   `FDP.Toolkit.Orchestration.Handlers.ReferenceScenarioLoadHandler`:
   - Constructor: `(ScenarioSerializer, IScenarioStorageProvider, EntityRepository?)`.
   - Use `storageProvider.EnumerateScenarioFiles(scenarioId)` and
     `storageProvider.OpenScenarioFile(scenarioId, fileName)` instead of
     `Directory.GetFiles` / `File.ReadAllText`.
   - The CGF "header-peek-only" path is a natural specialisation: when
     `EntityRepository? world == null`, `Commit` skips `Deserialize` (exactly what
     the CGF variant did). This eliminates the need for a separate CGF copy.

4. Migrate `EditLoadDsmHandler` →
   `FDP.Toolkit.Orchestration.Handlers.ReferenceEditLoadHandler`:
   - Same `IScenarioStorageProvider` substitution.

5. Migrate `StoryLoadDsmHandler` (both SimHost and CGF variants) →
   `FDP.Toolkit.Orchestration.Handlers.ReferenceStoryLoadHandler`:
   - Constructor: `(ScenarioSerializer, IScenarioStorageProvider, EntityRepository?,
     IOrchestrationTransport?, int nodeId)`.
   - When `EntityRepository? world == null`, the handler behaves as the CGF
     header-peek-only path (no entity operations, participates based on type match).

6. Update `Hrot.SimHost/NodeBootstrapper.BuildOrchestration`: replace all four
   handler instantiations with their `Reference*` equivalents; inject
   `new LocalDiskStorageProvider(localTempRoot)` and `transport`.

7. Update `Hrot.CGF/CgfApplication`: inject `LocalDiskStorageProvider` and
   `transport` into the relevant handlers; delete the duplicate CGF-specific
   `ScenarioLoadDsmHandler` and `StoryLoadDsmHandler` files.

8. Add `FDP.Toolkit.Orchestration` project reference to `Hrot.SimHost.csproj`,
   `Hrot.CGF.csproj`, `Hrot.ExCon.csproj`.

**Success conditions:**

- `Fact: ReferencePrefetchHandler ACKs via transport` — unit test injects a mock
  `IOrchestrationTransport`; runs Prepare + Commit; asserts `PublishStatus` was
  called with `StatusCode = OrchestrationStatusCode.Success`.

- `Fact: ReferenceScenarioLoadHandler loads entities` — unit test seeds a temp dir
  with a valid scenario JSON (same format as existing `ScenarioLoadDsmHandler`
  tests); constructs a real `ScenarioSerializer` + `LocalDiskStorageProvider`;
  runs Prepare + Commit; asserts entities were spawned in `EntityRepository`.

- `Fact: ReferenceScenarioLoadHandler no-ops for non-matching subsystem` — a JSON
  file with a different `SubsystemType` causes Prepare + Commit to be no-ops;
  no entities spawned.

- `Fact: ReferenceStoryLoadHandler ECS-less path participates but spawns nothing` —
  pass `world = null`; call Prepare(`StartEpisode`); assert
  `IsParticipatingForTest == true` when type matches, `false` otherwise.

- `Fact: All existing scenario handler unit tests pass` — migrate all test files in
  `Hrot.SimHost.Tests` that reference `ScenarioLoadDsmHandler`,
  `EditLoadDsmHandler`, `StoryLoadDsmHandler`, `PrefetchFilesDsmHandler` to use
  the new `Reference*` names; all tests remain green.

---

### CGF1-G0405 — Reference DryRun, Checkpoint, and RecordReplay Handlers

**Design ref:** [§4.7 — Reference Handler Catalogue](./CGF-1-GENERALIZATION.md#47-reference-handler-catalogue)

**Depends on:** CGF1-G0401, CGF1-G0402, CGF1-G0404

**Context:** `DryRunDsmHandler` is already in `Hrot.Common` — one step from
FDP. `CheckpointDsmHandler`, `LiveLoadDsmHandler`, and `ReplayLoadDsmHandler` are
SimHost-specific but all depend only on FDP toolkit types (`EntityRepository`,
`FDP.Toolkit.Replay.*`, `ModuleHost.Core.*`). This task moves them to the toolkit
and relocates `CheckpointIOWorker` (which has no Hrot dependencies) to join them.

**Work to do:**

1. Relocate `Hrot.SimHost/Modules/Orchestration/CheckpointIOWorker.cs` →
   `FDP/Toolkits/FDP.Toolkit.Orchestration/CheckpointIOWorker.cs`. Update
   namespace to `FDP.Toolkit.Orchestration`. Update the `using` in `Hrot.SimHost`
   sources that reference it.

2. Migrate `DryRunDsmHandler` from `Hrot.Common/Orchestration/Handlers/` →
   `FDP.Toolkit.Orchestration.Handlers.ReferenceDryRunHandler`:
   - Rename class; update namespace. No logic changes needed.
   - Leave `Hrot.Common/Orchestration/Handlers/DryRunDsmHandler.cs` as a one-line
     empty stub with a migration comment (preserves git history).

3. Migrate `CheckpointDsmHandler` →
   `FDP.Toolkit.Orchestration.Handlers.ReferenceCheckpointHandler`:
   - Replace `DdsWriter<NodeOpStatus>?` with `IOrchestrationTransport?`.
   - `PrepareAsync` calls `transport?.PublishStatus(InProgress)`.
   - `DrainDeferredAcks` calls `transport?.PublishStatus(Success/Failure)`.

4. Migrate `LiveLoadDsmHandler` →
   `FDP.Toolkit.Orchestration.Handlers.ReferenceLiveLoadHandler`:
   - Replace `ClusterSlave slave` (the Hrot type) with the toolkit `ClusterSlave`.
   - The existing call `_slave.PublishClusterStateChanged(...)` becomes a no-op since
     the toolkit ClusterSlave publishes `TkClusterStateChangedEvent` automatically on
     `CommitState`. Remove that guard call; the `_eventBus.Publish` guard in
     `Commit` can also be removed as the state-change event is already published.
     If an explicit redundant guard is desired, publish `TkClusterStateChangedEvent`
     directly on the bus.

5. Migrate `ReplayLoadDsmHandler` →
   `FDP.Toolkit.Orchestration.Handlers.ReferenceReplayLoadHandler`:
   - Replace `DdsWriter<NodeOpStatus>?` with `IOrchestrationTransport?`.
   - Call `transport?.PublishStatus(...)` instead of `_statusWriter?.Write(...)`.

6. Update `Hrot.SimHost/NodeBootstrapper.BuildOrchestration` to use all five
   renamed `Reference*` handlers from the toolkit.

7. Register `FDP.Toolkit.Orchestration` as a project reference in
   `Hrot.SimHost.csproj` (if not already added in G0404).

**Success conditions:**

- `Fact: ReferenceCheckpointHandler publishes InProgress then Success` — unit test
  with a real `CheckpointIOWorker` (temp dir) and mock transport; calls Prepare
  + Commit; calls `DrainDeferredAcks` in a polling loop until the background write
  completes; asserts transport received `InProgress` then `Success` for the same
  `TransactionId`.

- `Fact: ReferenceDryRunHandler rewrites live repo` — existing `DryRunDsmHandler`
  tests (renamed to `ReferenceDryRunHandlerTests`) stay green with only a name
  substitution; logic unchanged.

- `Fact: ReferenceLiveLoadHandler starts recording on PrepareLive` — inject mock
  `EcsRecordReplayController`; call PrepareAsync(PrepareLive); assert
  `PrepareRecordingAsync` was called once.

- `Fact: ReferenceReplayLoadHandler publishes MaxNetworkId status` — inject mock
  `EcsRecordReplayController` returning `MaxNetworkId=42`; call
  PrepareAsync(PrepareReplay); assert transport received a `Success` status with
  `ResultJson` containing `"MaxNetworkId":42`.

- `Fact: All existing SimHost handler integration tests pass` — the full
  `Hrot.SimHost.Integration.Tests` suite stays green after the renames.

---

### CGF1-G0406 — Final Wiring Cleanup and CI Validation

**Design ref:** [§7 — Migration Playbook](./CGF-1-GENERALIZATION.md#7-migration-playbook)
and [§8 — Files Deleted vs Retained](./CGF-1-GENERALIZATION.md#8-files-deleted-vs-retained)

**Depends on:** CGF1-G0401 through CGF1-G0405

**Context:** Tasks G0401–G0405 perform the mechanical moves. This cleanup task
eliminates all dead code, verifies the architectural layer boundary is intact, and
ensures no test regression has been introduced across the full solution.

**Work to do:**

1. Delete all Hrot application-layer source files whose functionality has been
   superseded by FDP toolkit reference implementations (see §8 of the addendum).
   Specifically:
   - `Hrot.SimHost/Modules/Orchestration/ClusterSlave.cs`
   - `Hrot.CGF/Modules/Orchestration/ClusterSlave.cs`
   - `Hrot.IG/Modules/Orchestration/ClusterSlave.cs`
   - `Hrot.ExCon/Orchestration/ClusterSlave.cs`
   - `Hrot.SimHost/Modules/Orchestration/IDsmHandler.cs` (was already a stub)
   - `Hrot.SimHost/Modules/Orchestration/Handlers/PrefetchFilesDsmHandler.cs`
   - `Hrot.SimHost/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs`
   - `Hrot.SimHost/Modules/Orchestration/Handlers/EditLoadDsmHandler.cs`
   - `Hrot.SimHost/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs`
   - `Hrot.SimHost/Modules/Orchestration/Handlers/CheckpointDsmHandler.cs`
   - `Hrot.SimHost/Modules/Orchestration/LiveLoadDsmHandler.cs`
   - `Hrot.SimHost/Modules/Orchestration/Handlers/ReplayLoadDsmHandler.cs`
   - `Hrot.CGF/Modules/Orchestration/Handlers/ScenarioLoadDsmHandler.cs`
   - `Hrot.CGF/Modules/Orchestration/Handlers/StoryLoadDsmHandler.cs`
   - `Hrot.Orchestrator/TransitionPlanner.cs`

2. Verify no `FDP.Toolkit.*` project contains a `using Hrot.*` directive:
   - Run `rg "using Hrot\." FDP/` in the terminal; assert zero matches.

3. Update `IOS-IG-SimHost.sln` to include the new
   `FDP.Toolkit.Orchestration.csproj`.

4. Run the full solution build (`dotnet build IOS-IG-SimHost.sln`) and assert
   zero warnings of type CS0234 (missing type), CS0246 (type not found), or
   CS0535 (interface not fully implemented).

5. Run the full test suite (`dotnet test IOS-IG-SimHost.sln`); assert all tests pass.
   Failures are P1 blockers for this task.

6. Update `CGF-1-DESIGN.md` §6 (New Projects & File Map) to include
   `FDP.Toolkit.Orchestration` and its contained files.

**Success conditions:**

- `Fact: No Hrot.* references in FDP.Toolkit.Orchestration` — `dotnet build`
  produces zero dependency-layer-violation warnings; a Roslyn analysis
  (`FDP.*` projects have no `<ProjectReference>` to `Hrot.*`) confirms clean
  separation.

- `Fact: Solution builds with zero new warnings` — `dotnet build --no-incremental
  IOS-IG-SimHost.sln` exits 0.

- `Fact: Full test suite green` — `dotnet test IOS-IG-SimHost.sln` exits 0; all
  tests that existed before Phase 4 still pass; new tests added in G0401–G0405
  also pass.

- `Fact: ClusterSlave class count = 1` — a grep of the solution source for
  `public sealed class ClusterSlave` returns exactly one match in
  `FDP.Toolkit.Orchestration`.
---

## Phase 5 — Operational UI, Real Network Dispatch & CQRS Architecture

See **[CGF-1-ADDENDUM-3.md](./CGF-1-ADDENDUM-3.md)** for full design specifications.

---

### CGF1-S0501 — Orchestrator ImGui Window & 2PC History Overhaul

**Design ref:** [§2](./CGF-1-ADDENDUM-3.md#2-orchestrator-imgui-window--2pc-history-overhaul)

**Depends on:** CGF1-S0106 (existing OrchestratorScenarioPanel), CGF1-S0502 should
follow directly (fan-out fix needs S0501's DistributedTransaction extensions).

**Work to do:**

1. **`OrchestratorSubsystem.cs` — beige title bar:**  
   Change `TitleBarColor` to `new(0.72f, 0.64f, 0.47f, 1f)`.

2. **`OrchestratorSubsystem.cs` — add `ImGui.Begin` / `ImGui.End` wrapper:**  
   Wrap the entire body of `DrawUI()` in:
   ```csharp
   if (!ImGui.Begin("Orchestrator")) { ImGui.End(); return; }
   // ... existing content ...
   ImGui.End();
   ```

3. **`OrchestratorScenarioPanel.cs` — remove `BeigeChildBg`:**  
   Delete `private static readonly Vector4 BeigeChildBg`. Remove every
   `ImGui.PushStyleColor(ImGuiCol.ChildBg, BeigeChildBg)` and its matching
   `ImGui.PopStyleColor()` from all six `Render*` helpers.

4. **`DistributedTransaction.cs` — new properties:**  
   Add:
   ```csharp
   public string PayloadJson { get; set; } = string.Empty;
   public Dictionary<int, string> NodeResponses { get; } = new();
   public ClusterState SourceClusterState { get; set; }
   ```

5. **`ClusterMaster.cs` — populate new fields:**  
   - Capture `ClusterState sourceState = _currentClusterState;` before any optimistic advance
     in `ProcessSingleClusterOpRequest`. Assign `SourceClusterState = sourceState` in the new
     transaction.  
   - Assign `tx.PayloadJson = req.PayloadJson ?? string.Empty` when creating the
     transaction object.  
   - In `ConsumeNodeOpStatuses`: `tx.NodeResponses[status.NodeId] = status.ResultJson`
     when processing each ACK.

6. **`OrchestratorSubsystem.cs` — overhaul the 2PC history table:**  
   Replace the existing 4-column `BeginTable("TxHistory", 4, ...)` block with a 5-column
   table (`TransactionId`, `Target State`, `Result`, `ACK Latency`, `Payload`) using:
   - `ImGuiTableFlags.ScrollY` + `ImGui.TableSetupScrollFreeze(0, 1)`.
   - Height capped at `rowHeight * 11.5f` (~10 data rows + header).
   - Column 1 rendered via `ImGui.TreeNodeEx(tx.TransactionId.ToString(), ...)` (full GUID).
   - Context menu on right-click: `ImGui.BeginPopupContextItem` with
     `MenuItem("Copy line to clipboard")` → `ImGui.SetClipboardText(...)`.
   - Column 5 shows first 25 chars + `"..."` of `tx.PayloadJson`; on hover:
     `ImGui.BeginTooltip()` showing `FormatPrettyJson(payloadStr)`.
   - When `TreeNodeEx` expanded, render child rows per `tx.NodeResponses` entry
     (indent `↳ Node {id}`, per-node latency, `ResultJson`).

7. **`OrchestratorSubsystem.cs` — add `FormatPrettyJson` static helper** (see addendum
   §2.3 for implementation).

8. **`OrchestratorScenarioPanel.cs` — Source→Target banner:**  
   Update `RenderStatusBanner` to show `{activeTx.SourceClusterState} → {activeTx.TargetClusterState}`
   when an in-flight transaction with differing source/target is active.

**Success conditions:**

- `Fact: Window wraps UI` — opening the Orchestrator panel shows a titled ImGui window;
  all child sections appear inside it; `ImGui.End()` is balanced.

- `Fact: No BeigeChildBg in child sections` — grep for `BeigeChildBg` in
  `OrchestratorScenarioPanel.cs` returns zero matches.

- `Fact: 2PC table caps at 10 rows` — a unit test (or manual verification) with 15
  completed transactions shows only 10 in the table with a scroll bar.

- `Fact: Full GUID displayed` — `TransactionId` column shows 36-char UUID string
  (not the truncated 8-char form).

- `Fact: JSON tooltip on hover` — hovering the Payload column calls
  `FormatPrettyJson`; a unit test feeding `{"a":1}` returns indented JSON.

- `Fact: Clipboard context menu` — right-clicking a row shows "Copy line to clipboard"
  menu item; clicking it calls `ImGui.SetClipboardText` with a non-empty string.

- `Fact: Source→Target banner` — when `SourceClusterState != TargetClusterState` and
  `HasInFlightTransaction`, status banner text is `"State: Standby → LoadingLive"`.

- `Fact: SourceClusterState populated` — unit test: call `HandleClusterOpRequest` for
  transition from `Standby` to `LoadingLive`; assert resulting transaction's
  `SourceClusterState == ClusterState.Standby`.

---

### CGF1-S0502 — Real Network Dispatch + ClusterMaster Fan-out

**Design ref:** [§3](./CGF-1-ADDENDUM-3.md#3-real-network-dispatch-fix)

**Depends on:** CGF1-S0501 (DistributedTransaction fields must exist before fan-out
assigns `txId`).

**Context:** Pressing any state-transition button in the Orchestrator UI currently
does nothing to the cluster because `ClusterMaster.ProcessSingleClusterOpRequest` plans
the trajectory and optimistically advances its local state but never sends any
`NodeOpCommand` DDS messages. All nodes remain in Standby.

**Work to do:**

1. **`OrchestratorSubsystem.cs` — add `DdsWriter<ClusterOpRequest>`:**  
   ```csharp
   private DdsWriter<ClusterOpRequest>? _sysOpWriter;
   ```
   In `Initialize`: `_sysOpWriter = new DdsWriter<ClusterOpRequest>(_participant);`  
   In `Shutdown`: `_sysOpWriter?.Dispose(); _sysOpWriter = null;`

2. **`OrchestratorSubsystem.cs` — pass writer to panel:**  
   `_scenarioPanel = new OrchestratorScenarioPanel(_drillMaster, _sysOpWriter);`

3. **`OrchestratorScenarioPanel.cs` — update constructor:**  
   ```csharp
   private readonly DdsWriter<ClusterOpRequest> _sysOpWriter;
   public OrchestratorScenarioPanel(ClusterMaster drillMaster,
                                    DdsWriter<ClusterOpRequest> sysOpWriter) { ... }
   ```

4. **`OrchestratorScenarioPanel.cs` — replace all direct ClusterMaster calls:**  
   Every `_drillMaster.HandleClusterOpRequest(new ClusterOpRequest { ... })` call in
   `RenderDrillControl`, `RenderCheckpointSection`, `RenderScenarioSection`,
   `RenderReplaySection`, and `RenderStoriesSection` is replaced with
   `_sysOpWriter.Write(new ClusterOpRequest { RequestId = Guid.NewGuid(), ... })`.
   Refer to addendum §3.2 for the exact payload mapping per operation type.

5. **`OrchestratorSubsystem.cs` — implement TODO simulation buttons:**  
   Replace the empty TODO Pause/Resume/Initialize Live placeholders in `DrawUI()`
   with `_sysOpWriter.Write(new ClusterOpRequest { OperationType = ClusterOpType.xxx, ... })`.

6. **`ClusterMaster.cs` — add fan-out loop:**  
   After the `DistributedTransaction tx` is created in `ProcessSingleClusterOpRequest`,
   iterate the planned `trajectory` and for each `TransitionStep`, call
   `FanOutNodeOp` twice:
   - A `PrepareXxx` command (`NodeOpType.PrepareLive` for `LoadingLive`,
     `NodeOpType.PrepareReplay` for `LoadingReplay`, etc.) carrying the original
     `req.PayloadJson` so handlers can extract `ScenarioId` / `ExerciseId`.
   - A `NodeOpType.CommitState` command carrying `((int)tStep.TargetState).ToString()`
     as the payload.  
   For each `OperationStep` with `ClusterOpType.ReplaySeek`, fan out
   `NodeOpType.NodeReplaySeek` with `opStep.PayloadJson`.
   See addendum §3.3 for the complete switch expression mapping target state →
   `NodeOpType`.

**Success conditions:**

- `Fact: DdsWriter created and disposed` — `OrchestratorSubsystem.Shutdown` calls
  `_sysOpWriter.Dispose()`; no leak in test teardown.

- `Fact: Panel uses writer, not direct ClusterMaster` — grep for `HandleClusterOpRequest`
  in `OrchestratorScenarioPanel.cs` returns zero matches after this task.

- `Fact: Button click publishes ClusterOpRequest` — integration test simulates clicking
  the "Standby → LoadingLive" button; asserts a `ClusterOpRequest` with
  `OperationType == ClusterOpType.TransitionState` and payload containing `LoadingLive`
  was written to the DDS topic within 200 ms.

- `Fact: Node receives PrepareXxx after button click` — in a headless integration
  test with one live `SimHostSubsystem`, clicking "LoadingLive" results in the
  `ClusterSlave` on that node receiving `NodeOpType.PrepareLive` within 3 s
  (`Timeout = 10000`).

- `Fact: Fan-out sends CommitState` — after `PrepareLive` ACK arrives, the same
  `SimHostSubsystem` node receives `NodeOpType.CommitState` with the target state
  integer; `LocalClusterState` in the next heartbeat transitions to `LoadingLive`.

---

### CGF1-S0503 — Time Control Section + Remote Time Commands

**Design ref:** [§4](./CGF-1-ADDENDUM-3.md#4-time-control-section)

**Depends on:** CGF1-S0502 (DdsWriter must exist), CGF1-S0205 (DistributedTimeCoordinator
must be operational).

**Work to do:**

1. **`OrchestrationMessages.cs` — extend `ClusterOpType`:**  
   Add:
   ```csharp
   CancelOperation = 13,
   StepTime        = 14,
   SetTimeScale    = 15,
   ```

2. **`ClusterMaster.cs` — `TimeControlRequested` event + early return:**  
   ```csharp
   public event Action<ClusterOpType, string>? TimeControlRequested;
   ```
   At the start of `ProcessSingleClusterOpRequest`, before the main dispatch:
   ```csharp
   if (req.OperationType is ClusterOpType.PauseTime or ClusterOpType.ResumeTime
                         or ClusterOpType.StepTime  or ClusterOpType.SetTimeScale)
   {
       TimeControlRequested?.Invoke(req.OperationType, req.PayloadJson ?? string.Empty);
       return;
   }
   ```

3. **`OrchestratorSubsystem.cs` — subscribe to event:**  
   In `Initialize`, after `_drillMaster` is created, wire the event to
   `_timeCoordinator`/`_timeKernel` (see addendum §4.2 for full handler body).

4. **`OrchestratorSubsystem.cs` — "Time Control" section in `DrawUI()`:**  
   Add `ImGui.CollapsingHeader("Time Control", ImGuiTreeNodeFlags.DefaultOpen)` block
   containing:
   - `ImGui.Text("Master Time: {wallTimeStr}")` and `ImGui.Text("Drill Time: {drillTime:F2} s")`
   - `Button(isPaused ? "Resume" : "Pause")` → dispatches `PauseTime` or `ResumeTime`
     via `_sysOpWriter`.
   - `Button("Step")` (wrapped in `BeginDisabled`/`EndDisabled` when not paused) →
     dispatches `StepTime`.
   - `SliderFloat("Speed", ref timeScale, 0.1f, 10.0f, "%.1fx")` → on change,
     dispatches `SetTimeScale` with `scale.ToString()` as payload.
   - `isPaused` is read from `_uiCache.IsPaused` (populated from `SwitchTimeModeWireDto`).

5. **`OrchestratorScenarioPanel.cs` — add `Update(float dt)` and pause callback:**  
   - Add `private readonly Action? _requestPause;` and accept it in constructor.
   - Add `_seekDebounceTimer`, `_seekPending` fields.
   - `Update(float dt)`: decrements debounce timer; when expired, writes
     `ClusterOpType.ReplaySeek` request and invokes `_requestPause?.Invoke()`.
   - `OrchestratorSubsystem.Update` calls `_scenarioPanel?.Update(deltaTime)`.

6. **`OrchestratorScenarioPanel.cs` — replay seek with debounce and dynamic cap:**  
   - `RenderReplaySection(ClusterState, bool, float currentDrillTime)` signature.
   - When slider moves: `_seekPending = true; _seekDebounceTimer = 0.5f;`  
     When not pending: `_seekSliderValue = currentDrillTime;` (passive tracking).
   - `_replayDuration` loaded from the selected drill's `*.meta.json` when "Load
     Replay" is clicked; `GetReplayDuration(string drillId)` reads `TotalFrames`
     from the JSON and divides by 60 to get seconds; fallback 3600.

7. **`OrchestratorSubsystem.cs` — pass drill time to panel:**  
   `float drillTime = (float)(_timeKernel?.CurrentTime.TotalTime ?? 0.0);`  
   `_scenarioPanel?.Render(drillTime);` (update `Render` signature accordingly).

**Success conditions:**

- `Fact: ClusterOpType values correct` — unit test asserts
  `(int)ClusterOpType.StepTime == 14` and `(int)ClusterOpType.SetTimeScale == 15`.

- `Fact: TimeControlRequested fires on PauseTime` — unit test: call
  `HandleClusterOpRequest(new ClusterOpRequest { OperationType = ClusterOpType.PauseTime })`;
  assert `TimeControlRequested` was invoked once with `ClusterOpType.PauseTime`.

- `Fact: TimeControlRequested bypasses 2PC` — same call as above; assert no
  `DistributedTransaction` was appended to `TransactionHistory`.

- `Fact: Pause/Resume toggle` — Time Control section shows "Pause" when
  `IsPaused == false` and "Resume" when `IsPaused == true`; clicking either
  dispatches the corresponding `ClusterOpRequest`.

- `Fact: Step disabled when running` — when `IsPaused == false`, the Step button
  is in `BeginDisabled` state; clicking it does not dispatch any request.

- `Fact: Seek debounce delays write` — drag slider; assert no `ClusterOpRequest` is
  written within 400 ms; after 600 ms assert exactly 1 `ReplaySeek` request written.

- `Fact: Replay duration capped` — `GetReplayDuration` for a fake `.meta.json`
  with `"TotalFrames": 3600` returns `60.0f` seconds.

---

### CGF1-S0504 — Asset Combo Selection (Local Filesystem Scan)

**Design ref:** [§5](./CGF-1-ADDENDUM-3.md#5-asset-combo-selection-local-scan)

**Depends on:** CGF1-S0502.

**Work to do:**

1. **`OrchestratorScenarioPanel.cs` — replace text input fields:**  
   Remove `_loadScenarioId`, `_replayExerciseId`, `_injectScenarioId`, `_injectStoryId`
   string fields.  
   Add:
   ```csharp
   private string[] _availableScenarios    = Array.Empty<string>();
   private string[] _availableStories      = Array.Empty<string>();
   private string[] _availableDrills       = Array.Empty<string>();
   private int      _selectedLoadScenarioIdx = -1;
   private int      _selectedExerciseIdx        = -1;
   private int      _selectedStoryIdx        = -1;
   ```

2. **`OrchestratorScenarioPanel.cs` — add `RefreshLocalAssets()` method:**  
   Scan `C:\FDP_Temp` (or parameterized root): directories containing `.fdp` files
   → drills; directories containing `.json` files → scenarios. Stories share the
   same array as scenarios.  
   Add `using System.IO;`. Clamp selection indices after refresh.  
   Call `RefreshLocalAssets()` from the constructor.

3. **`OrchestratorScenarioPanel.cs` — update `RenderScenarioSection`:**  
   Replace the `_loadScenarioId` `InputText` with
   `ImGui.Combo("Select Scenario##OrcLoadId", ref _selectedLoadScenarioIdx,
   _availableScenarios, _availableScenarios.Length)` + a `"⟳##RefScen"` button.  
   Use `_availableScenarios[_selectedLoadScenarioIdx]` as `scenId` in the two
   load button payloads; guard with `_selectedLoadScenarioIdx >= 0`.  
   Keep the Save section's `_saveScenarioId` `InputText` unchanged (creating new
   scenario names still requires free text).

4. **`OrchestratorScenarioPanel.cs` — update `RenderReplaySection`:**  
   Replace `_replayExerciseId` `InputText` with
   `ImGui.Combo("Select Drill##OrcReplayId", ref _selectedExerciseIdx, _availableDrills,
   _availableDrills.Length)` + `"⟳##RefDrill"` button.  
   Use `_availableDrills[_selectedExerciseIdx]` as `drillId`; guard as above.

5. **`OrchestratorScenarioPanel.cs` — update `RenderStoriesSection`:**  
   Remove `_injectScenarioId` and `_injectStoryId` text inputs.  
   Add `ImGui.Combo("Story Package##OrcInjectScen", ref _selectedStoryIdx,
   _availableStories, _availableStories.Length)` + `"⟳##RefStory"` button.  
   On "Inject Story" click: `string scenId = _availableStories[_selectedStoryIdx];
   string newStoryId = Guid.NewGuid().ToString();`; include both in payload.

**Success conditions:**

- `Fact: Combos populated at construction` — instantiate panel against a test
  tmp directory containing one `<id>/entities.json` subfolder and one
  `<id>/node_1.fdp` subfolder; assert `_availableScenarios.Length == 1` and
  `_availableDrills.Length == 1` after construction.

- `Fact: Refresh button updates list` — write a new scenario folder after
  construction; click refresh; assert `_availableScenarios.Length` increased.

- `Fact: Story StoryId auto-generated` — two successive "Inject Story" clicks
  produce two `ClusterOpRequest` payloads with different `StoryId` values.

- `Fact: Empty selection disables Load buttons` — with `_selectedLoadScenarioIdx = -1`,
  clicking "Load into Live" results in no `ClusterOpRequest` being written.

---

### CGF1-S0505 — Archive Export/Import Pipeline

**Design ref:** [§6](./CGF-1-ADDENDUM-3.md#6-archive-exportimport-pipeline)

**Depends on:** CGF1-S0502 (fan-out mechanism), CGF1-G0401 (toolkit
`IDsmHandler`/`OrchestrationCommand`).

**Work to do:**

1. **`OrchestrationMessages.cs` — `ClusterOpType.CancelOperation = 13`** (if not already
   added in S0503; ensure it is present regardless of ordering).

2. **`StorageGatewayModule.cs` — thread `CancellationToken` through bulk methods:**  
   - `PullToNasAsync` and `PushToNodesAsync` gain `CancellationToken ct = default`.  
   - Pass `ct` to `ParallelOptions.CancellationToken`.  
   - On `OperationCanceledException`: delete any partially-written output files (NAS
     side for Pull; local SSD side for Push); rethrow.  
   - Add `PrefetchArchiveAsync(string drillId, IReadOnlyList<NodeDistributionTarget>
     targets, string nasBasePath, CancellationToken ct = default)`:  
     reads `<nasBasePath>/<drillId>/node_<target.NodeId>.fdp` for each target node
     and copies to `target.DestinationPath`.

3. **Add `ScanLocalDrills(string root)`, `ScanNasDrills(string nasRoot)`,
   `ScanLocalScenarios(string root)` helper methods to `StorageGatewayModule`**
   (pure filesystem scan, no DDS). Used by both `ClusterMaster.PublishAssetInventory`
   and the fallback `RefreshLocalAssets`.

4. **`FDP.Toolkit.Orchestration` — add `ReferenceArchiveHandler.cs`:**  
   Implement `IDsmHandler`:
   - `CanHandle`: returns `true` for `NodeOpType.SerializeLocal` when payload
     contains `"ExerciseId"` key.
   - `PrepareAsync`: returns `null` immediately.
   - `Commit`: locates local `.fdp` file, builds `FileManifestEntry`, serialises it
     as `ResultJson` in the transport `PublishStatus` call.
   - `Abort`: deletes any partial `.fdp` file for the given `ExerciseId`.

5. **`ClusterMaster.cs` — `_activeCancellations` registry:**  
   ```csharp
   private readonly Dictionary<Guid, CancellationTokenSource> _activeCancellations = new();
   ```

6. **`ClusterMaster.cs` — handle `ExportArchive`, `ImportArchive`, `CancelOperation`**
   in `ProcessSingleClusterOpRequest`:
   - `ExportArchive`: create `CancellationTokenSource`; store; call
     `FanOutSerializeLocal(txId, activeNodeIds, req.PayloadJson)`.
     In `ConsumeNodeOpStatuses`, pass the CTS token to `_gateway.PullToNasAsync`.
   - `ImportArchive`: create CTS; call `_gateway.PrefetchArchiveAsync` with CT;
     on completion publish `ClusterOpStatus` with `Success` or `Timeout` code.
   - `CancelOperation`: parse target Guid from payload; cancel CTS if present;
     fan out `NodeOpType.AbortTransaction`.

7. **`NodeBootstrapper.BuildOrchestration` — register `ReferenceArchiveHandler`:**  
   `handlers.Add(new ReferenceArchiveHandler(localTempRoot, nodeId));`

8. **`OrchestratorScenarioPanel.cs` — add Archive Management section:**  
   - New state fields: `_archivedDrills`, `_unarchivedLocalDrills`,
     `_selectedArchiveIdx`, `_selectedUnarchivedIdx`, `_activeArchiveOpId`,
     `_archiveProgress`.
   - `RefreshLocalAssets` extended: also reads NAS drill list via
     `_gateway.ScanNasDrills` (or deferred to `ClusterUiCache` in S0506).
   - `RenderArchiveSection(ClusterState, bool)` (see addendum §6.5 for layout).
   - Archives section added to `Render()` call sequence.

**Success conditions:**

- `Fact: PullToNasAsync cleans up on cancel` — unit test: start a pull of 3 files;
  cancel after 1 completes; assert the 2 incomplete destination files are deleted;
  no exception escapes.

- `Fact: ReferenceArchiveHandler Commit produces manifest` — unit test: create a
  fake `.fdp` file; call `Commit`; assert `ResultJson` deserialises to a
  `FileManifestEntry` with the correct path.

- `Fact: ReferenceArchiveHandler Abort deletes partial file` — unit test: create a
  partial `.fdp` file; call `Abort`; assert file no longer exists.

- `Fact: CancelOperation kills gateway task` — integration test: start ExportArchive;
  immediately send CancelOperation; assert `PullToNasAsync` throws
  `OperationCanceledException`; assert `AbortTransaction` was fanned out to nodes.

- `Fact: Archive UI progress visible` — when `_activeArchiveOpId != Guid.Empty`,
  `ProgressBar` is rendered and "CANCEL OPERATION" button is active regardless of
  `disableAll`.

---

### CGF1-S0506 — CQRS Decoupling: AssetInventoryTopic + ClusterUiCache

**Design ref:** [§7](./CGF-1-ADDENDUM-3.md#7-cqrs-decoupling-assetinventorytopic--clusteruicache)

**Depends on:** CGF1-S0501, CGF1-S0502, CGF1-S0503, CGF1-S0504, CGF1-S0505.

**Work to do:**

1. **`OrchestrationMessages.cs` — add `AssetInventoryTopic` struct:**  
   DDS topic with `NodeId` key; four `[DdsManaged] string` fields:
   `LocalScenariosJson`, `LocalDrillsJson`, `ArchivedDrillsJson`,
   `UnarchivedLocalDrillsJson`. `TransientLocal` QoS, `KeepLast` history depth 1.
   See addendum §7.1 for full struct definition and attributes.

2. **`ClusterMaster.cs` — `_inventoryWriter` + `PublishAssetInventory()`:**  
   - Add `DdsWriter<AssetInventoryTopic>? _inventoryWriter`. Initialise/dispose.  
   - Add `public string NasBasePath => _nasBasePath;` property.  
   - In `Tick()`, throttle to every 5 s (compare `DateTime.UtcNow` to
     `_lastInventoryScan`); call `PublishAssetInventory()`.  
   - `PublishAssetInventory` calls the three `ScanXxx` methods from
     `StorageGatewayModule` (added in S0505) and writes `AssetInventoryTopic`.

3. **`Hrot.ClusterRunner.Services` — create `ClusterUiCache.cs`:**  
   Construct all 8 required `DdsReader`s (see addendum §7.3 for the full list).
   Implement `Update()` that drains readers, updates all public properties, and
   calls `Process2PcNetworkTraffic()`. Cap `TxHistory` at 10 entries.
   Implement `Dispose()`.

4. **`OrchestratorScenarioPanel.cs` → rename to `ClusterScenarioPanel.cs`:**  
   - Remove `private readonly ClusterMaster _drillMaster` field entirely.  
   - Replace constructor with `(DdsWriter<ClusterOpRequest>, ClusterUiCache,
     Action? requestPause = null)`.  
   - All reads from `_drillMaster.*` properties replaced by the equivalent
     `_uiCache.*` property.  
   - `RefreshLocalAssets()` is superseded by `_uiCache.AvailableScenarios` etc.;
     the method may be removed or kept as a local fallback.
   - `Render(ClusterUiCache cache, bool disableAll)` becomes the public entry point.

5. **`OrchestratorSubsystem.cs` — use `ClusterUiCache` and `ClusterScenarioPanel`:**  
   - Add `ClusterUiCache? _uiCache;` field. Instantiate in `Initialize`; dispose in
     `Shutdown`.  
   - Replace `OrchestratorScenarioPanel` field/instantiation with `ClusterScenarioPanel`.  
   - `DrawUI()` reads `_uiCache.IsBootstrapped`, `_uiCache.HasInFlightTransaction`,
     `_uiCache.ActiveNodes`, `_uiCache.TxHistory`, `_uiCache.MasterWallTicks`,
     `_uiCache.MasterSimTime`, `_uiCache.IsPaused` — **no direct `_drillMaster`
     property access inside `DrawUI`**.  
   - `Update()` calls `_uiCache?.Update()`.
   - Keep `internal ClusterMaster? TestHook_ClusterMaster` for E2E tests.

**Success conditions:**

- `Fact: AssetInventoryTopic published by ClusterMaster` — unit test: tick ClusterMaster
  6 seconds; assert `_inventoryWriter` made at least one `Write` call.

- `Fact: ClusterUiCache reflects SystemStateTopic` — write a `SystemStateTopic`
  sample with `CurrentState = LoadingLive`; call `Update()`; assert
  `cache.CurrentState == LoadingLive`.

- `Fact: ClusterUiCache sniffs 2PC traffic` — write a `NodeOpCommand` with
  `Operation = PrepareState`; call `Update()`; assert `cache.TxHistory.Count == 1`.

- `Fact: OrchestratorSubsystem.DrawUI has no _drillMaster reads` — static analysis
  (or grep): `DrawUI()` method body contains no direct access to `_drillMaster`
  fields or methods except `TestHook_ClusterMaster`.

- `Fact: ClusterScenarioPanel compiles with ClusterUiCache` — solution builds with
  zero errors after the rename/refactoring.

- `Fact: No regression in E2E DSM test suite` — `DsmE2eScriptTests` all pass.

---

### CGF1-S0507 — IOS Remote Cluster Control Panel

**Design ref:** [§8](./CGF-1-ADDENDUM-3.md#8-ios-remote-cluster-control-panel)

**Depends on:** CGF1-S0506 (`ClusterScenarioPanel` and `ClusterUiCache` must exist).

**Work to do:**

1. **`Hrot.ExCon` — add `TimePulseIngressHandler.cs`:**  
   `IIngressHandler, IDisposable`; polls `DdsReader<TimePulseDescriptor>`;
   invokes `Action<TimePulseDescriptor>` callback.

2. **`Hrot.ExCon` — add `TimeModeIngressHandler.cs`:**  
   Same pattern; polls `DdsReader<SwitchTimeModeWireDto>`;
   invokes `Action<SwitchTimeModeWireDto>` callback.

3. **`Hrot.ExCon/Abstractions/IIosLogic.cs` — extend interface:**  
   Add `double MasterSimTime`, `long MasterWallTicks`, `float MasterTimeScale`,
   `bool IsPaused` read-only properties and `RequestPause()`, `RequestResume()`,
   `RequestStep()`, `SetTimeScale(float)` methods.

4. **`Hrot.ExCon/IosLogic.cs` — implement new members:**  
   - Back the four properties with private setters populated by the ingress handler
     callbacks.  
   - Implement the four command methods by writing a `ClusterOpRequest` via
     `_sysOpWriter`:
     - `RequestPause()` → `ClusterOpType.PauseTime`
     - `RequestResume()` → `ClusterOpType.ResumeTime`
     - `RequestStep()` → `ClusterOpType.StepTime`
     - `SetTimeScale(float s)` → `ClusterOpType.SetTimeScale`, payload `s.ToString()`

5. **`Hrot.ExCon/IosSubsystem.cs` — wire new components in `Initialize`:**  
   - Construct `ClusterUiCache(_participant)` and store as `_uiCache`.
   - Construct `_sysOpWriter = new DdsWriter<ClusterOpRequest>(_participant)`.
   - Construct `ClusterScenarioPanel(_sysOpWriter, _uiCache)` and store.
   - Register `TimePulseIngressHandler` and `TimeModeIngressHandler` in the IOS
     ingress handler list.
   - Wire handler callbacks to update `IosLogic.MasterSimTime` etc.
   - Dispose all in `Shutdown`.

6. **`Hrot.ExCon/IosSubsystem.cs` — `DrawUI()` renders cluster panel:**  
   ```csharp
   if (ImGui.Begin("Cluster Control", ImGuiWindowFlags.None))
   {
       _clusterPanel?.Render(_uiCache!, !_uiCache!.IsBootstrapped);
   }
   ImGui.End();
   ```

**Success conditions:**

- `Fact: IOS TimePulse updates MasterSimTime` — write a `TimePulseDescriptor` with
  `SimTimeSnapshot = 42.5` to the DDS bus; poll `TimePulseIngressHandler`; assert
  `iosLogic.MasterSimTime == 42.5`.

- `Fact: IOS RequestPause dispatches ClusterOpRequest` — call `iosLogic.RequestPause()`;
  assert `ClusterOpRequest { OperationType = ClusterOpType.PauseTime }` was written.

- `Fact: IOS renders cluster panel` — `IosSubsystem.DrawUI()` does not throw;
  the "Cluster Control" window contains State Banner, Drill Control, and Time Control
  sections sourced from `ClusterUiCache`.

- `Fact: IOS Drill Control targets match Orchestrator` — both `IosSubsystem` and
  `OrchestratorSubsystem` render reachable targets from the same `ClusterUiCache.CurrentState`;
  assert that `GetReachableTargets(currentState)` called with the cached state
  produces the same list from `HrotStateGraph`.

- `Fact: No direct ClusterMaster reference in IosSubsystem` — grep: `IosSubsystem.cs`
  does not import or reference the `Hrot.Orchestrator` namespace.