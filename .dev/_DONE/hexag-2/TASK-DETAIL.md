# Task Details: OrchestratorSubsystem Hexagonal Architecture & Bus Unification

**Design Reference:** [DESIGN.md](./DESIGN.md)
**Task Tracker:** [TASK-TRACKER.md](./TASK-TRACKER.md)

---

## Phase 1: Unify Event Buses (Fix IsPaused Bug)

### HEXAG2-S001 — Collapse Dual Buses into Single _bus

**Design reference:** [Section 4.1.1](./DESIGN.md#411-single-bus-hexag2-s001)

**Context:**  
`OrchestratorSubsystem` currently holds two separate `FdpEventBus` instances:
`_orchestrationBus` (used by `ClusterUiCache` and `ClusterMaster`) and `_eventBus` (used by
`MasterSyncController` and the time translators).  Because `SwitchTimeModeEvent` is published to
`_eventBus` but `ClusterUiCache` only drains `_orchestrationBus`, the `IsPaused` flag never
updates.

**Scope of changes:**

| File | Change |
|------|--------|
| `Hrot.Orchestrator/OrchestratorSubsystem.cs` | Remove ALL secondary `FdpEventBus` fields (`_orchestrationBus`, `_eventBus`, and any others found); add `private FdpEventBus? _bus` |
| `OrchestratorSubsystem.cs` | In `Initialize()`: create one `new FdpEventBus()`, pass it to `ClusterMaster`, `MasterSyncController`, `ClusterUiCache`, and `ClusterScenarioPanel` |
| `OrchestratorSubsystem.cs` | Change `new ClusterScenarioPanel(_clusterMaster, _uiCache)` to `new ClusterScenarioPanel(_bus, _uiCache)` |
| `OrchestratorSubsystem.cs` | Update `TimeBusForTest` test hook to return `_bus` |

**Success conditions:**

1. `OrchestratorSubsystem` has exactly one `FdpEventBus` field (`_bus`); ALL secondary bus
   fields (`_orchestrationBus`, `_eventBus`, and any others) are deleted.
2. `ClusterMaster`, `MasterSyncController`, `ClusterUiCache`, and `ClusterScenarioPanel` all
   receive the same bus instance at construction time.
3. `ClusterScenarioPanel` is constructed with `_bus` (not `_clusterMaster`), so UI commands
   bypass any `ClusterOpIntent` wrapper and publish strongly-typed intent events directly to
   the bus (e.g. `TransitionStateIntent`, `ManageEpisodeIntent`, `PauseTimeIntent`, etc.),
   using the same canonical vocabulary as the network-boundary translators.
4. **Unit test `OrchestratorSubsystem_PauseUpdatesIsPaused`:**  
   - Construct `OrchestratorSubsystem` with the parameterless constructor.
   - Call `Initialize()` with a minimal `SubsystemConfig` (no DDS participant needed).
   - Publish `PauseTimeIntent` to the bus and swap; call `Update(0f)`.
   - Assert `_uiCache.IsPaused == true`.
5. **Unit test `OrchestratorSubsystem_ResumeClears_IsPaused`:**  
   - Same setup; pause then resume.
   - Assert `IsPaused == false` after the resume update cycle.

---

### HEXAG2-S001b — Collapse All Buses in ExConSubsystem into Single _bus

**Design reference:** [Section 4.1.1](./DESIGN.md#411-single-bus-hexag2-s001--hexag2-s001b)

**Context:**  
`ExConSubsystem` owns four isolated `FdpEventBus` instances: `_orchestrationBus`,
`_uiCacheBus`, `_clusterOpEgressBus`, and `_timeEventBus`.  Each requires its own
`SwapBuffers()` call and manual bridging, creating the same fragmentation that caused the
`IsPaused` bug in the master node.

**Scope of changes:**

| File | Change |
|------|--------|
| `Hrot.ExCon/ExConSubsystem.cs` | Remove `_orchestrationBus`, `_uiCacheBus`, `_clusterOpEgressBus`, `_timeEventBus` fields; add `private FdpEventBus? _bus` |
| `ExConSubsystem.cs` | In `Initialize()`: create one `new FdpEventBus()`; pass it to every component that previously received an isolated bus |
| `ExConSubsystem.cs` | Replace three `SwapBuffers()` calls in `Update()` with one `_bus?.SwapBuffers()` at the frame boundary |

All of these must receive `_bus`:
- `new ClusterSlave(nodeId, SubsystemName, _bus)`
- `new NodeOpSlaveTranslator(..., bus: _bus, ...)`
- `new ClusterUiCache(_bus, _slaveSyncController)`
- `new ClusterScenarioPanel(_bus, _uiCache)`
- `new OrchestrationObserverTranslator(_participant, _bus)`
- `new ClusterOpEgressTranslator(_bus, _participant)`
- `new SlaveSyncController(_bus, nodeId, TimeConfig.Default)`
- All time translators

**Success conditions:**

1. `ExConSubsystem` has exactly one `FdpEventBus` field (`_bus`); all four secondary bus
   fields are deleted.
2. `ExConSubsystem.Update()` contains exactly one `_bus?.SwapBuffers()` call.
3. **Unit test `ExConSubsystem_ClusterUiCache_UpdatesIsPaused_AfterSwitchTimeModeEvent`:**
   - Construct headlessly (no participant).
   - Publish `SwitchTimeModeEvent{Deterministic}` to `_bus`, swap, call `Update(0f)`.
   - Assert `_uiCache.IsPaused == true`.

---

### HEXAG2-S002 — Strict 4-Phase Single-Swap Update Loop

**Design reference:** [Section 4.1.2](./DESIGN.md#412-strict-phase-discipline-in-update-hexag2-s002) and [Section 2.2](./DESIGN.md#22-single-unified-bus-with-strict-phase-discipline)

**Context:**  
`OrchestratorSubsystem.Update()` currently calls `_eventBus?.SwapBuffers()` twice and
`_orchestrationBus?.SwapBuffers()` once, in interleaved order.  With a unified bus, there may
only be one `SwapBuffers()` call per frame.  The order of operations must follow the 4-phase
discipline to preserve correct DDS egress timing and the 1-frame propagation contract.

**Scope of changes:**

| File | Change |
|------|--------|
| `OrchestratorSubsystem.cs` | Rewrite the `Update()` method body; relocate `_masterSync?.Update()` from the top of `Update()` to Phase 3 (after `SwapBuffers()`) |

**Note on `_masterSync` relocation:** In the current code, `_masterSync?.Update()` executes at
the very top of `Update()`, before any network ingress and before any bus swap.  Leaving it
there means time-control intents that arrive via Phase 1 DDS ingress are never visible in the
same frame — they are still in the write buffer when `_masterSync.Update()` runs.  Moving it
to Phase 3 ensures it reads from the freshly promoted read buffer.

**Required Update() sequence:**

```
Phase 1: Network boundary
    _timeModeTranslator?.ScanAndPublish(null!)  // managed_read -> DDS egress
    _timeModeTranslator?.PollIngress(null!, null!)
    _lockstepTranslator?.ScanAndPublish(null!)
    _lockstepTranslator?.PollIngress(null!, null!)
    _translator?.Tick()                    // (introduced by HEXAG2-S008, no-op until then)

Phase 2: Single frame boundary swap
    _bus?.SwapBuffers()                    // exactly once

Phase 3: Core logic
    _masterSync?.Update()
    _clusterMaster?.Tick()

Phase 4: Local observation
    _uiCache?.Update()
    _scenarioPanel?.Update(deltaTime)

Phase 5: Time-sync NTP ingress
    _masterTimeSyncTranslator?.PollIngress(null!, null!)
```

**Note:** The individual `_timeModeTranslator`, `_lockstepTranslator`, and
`_masterTimeSyncTranslator` fields used above are the Phase 1 fields that exist in the
codebase when this task is implemented.  `HEXAG2-S008` (Phase 2) will replace all three with
a single `_timeTranslators` handle and update the `Update()` calls accordingly.  Do not
pre-emptively reference `_timeTranslators` here; it does not exist yet.

The manual heartbeat bridging loop (DDS `_heartbeatReader.Take()` -> `PublishManaged`) must be
removed from `Update()` because it will be absorbed into `_translator.Tick()` in HEXAG2-S008.
Until that task is done, it may remain in Phase 1 as a temporary shim.

**Success conditions:**

1. `Update()` contains exactly one `_bus?.SwapBuffers()` call and zero references to
   the old `_orchestrationBus` or `_eventBus` names.
2. Phases 1-5 execute in the documented order (verified by code inspection).
3. **Integration test `ContinuousMode_AllNodes_SimTimesWithinTolerance`** continues to pass
   (verifies that the timing contract for `ScanAndPublish` before swap is preserved).
4. **Integration test `PauseStepResume_SimTimeAdvancesByStepAmount`** continues to pass
   (verifies that `AdvanceFrameIntent` is correctly read and forwarded after the single swap).

---

## Phase 2: Hexagonal Architecture Compliance

### HEXAG2-S003 — Define IOrchestrationTranslator Interface

**Design reference:** [Section 4.2.1](./DESIGN.md#421-iOrchestrationtranslator-interface-hexag2-s003)

**Context:**  
`OrchestratorSubsystem` must depend on an interface, not a concrete DDS class.  The interface
must be thin enough to be trivially mocked in tests.

**Scope of changes:**

| File | Change |
|------|--------|
| `Hrot.Core/Network/IOrchestrationTranslator.cs` | New file |

```csharp
namespace Hrot.Core.Network;

/// <summary>
/// Ticks all DDS ingress/egress for the orchestrator master transport (one call per frame).
/// Called inside OrchestratorSubsystem.Update() during Phase 1, before SwapBuffers.
/// </summary>
public interface IOrchestrationTranslator : IDisposable
{
    void Tick();
}
```

**Success conditions:**

1. `IOrchestrationTranslator.cs` compiles with no warnings in `Hrot.Core`.
2. A `NullOrchestrationTranslator` test double can implement it with a no-op `Tick()` and
   empty `Dispose()`, and compiles in a test project without any DDS assembly references.

---

### HEXAG2-S004 — Extend INetworkFactory with CreateOrchestratorTranslators

**Design reference:** [Section 4.2.2](./DESIGN.md#422-inetworkfactory-extension-hexag2-s004)

**Context:**  
`INetworkFactory` is the single port through which subsystems access network infrastructure.
Adding the orchestrator factory method completes the hexagonal interface contract for
`OrchestratorSubsystem`.

**Scope of changes:**

| File | Change |
|------|--------|
| `Hrot.Core/Network/INetworkFactory.cs` | Add three methods |
| `Hrot.Core/Network/IMasterTimeTranslators.cs` | New file — interface |

**Methods to add:**

The `INetworkFactory` port must accept no domain objects.  Three methods are added:

```csharp
/// <summary>
/// Creates the orchestrator master-side DDS translators (ClusterOp, NodeOp, heartbeat).
/// All created DDS resources are owned by the returned translator and released on Dispose().
/// Returns a no-op translator when there is no DDS participant (headless / test mode).
/// No domain types (ClusterMaster, etc.) are accepted; integration is via bus events only.
/// </summary>
IOrchestrationTranslator CreateOrchestratorTranslators(FdpEventBus bus, int nodeId);

/// <summary>
/// Creates and starts the hosted DDS ID allocator server background thread.
/// The caller owns the returned handle; Dispose() blocks via Thread.Join to guarantee
/// clean teardown before the shared DdsParticipant is destroyed.
/// Returns a no-op IDisposable when there is no DDS participant.
/// </summary>
IDisposable CreateIdAllocatorServer();

/// <summary>
/// Creates the master-side time-sync DDS translators (time-mode broadcast,
/// lockstep barrier, master NTP sync).  Absorbs _timeModeTranslator, _lockstepTranslator,
/// and _masterTimeSyncTranslator.  Returns a no-op implementation when there is no
/// DDS participant.
/// </summary>
IMasterTimeTranslators CreateMasterTimeTranslators(FdpEventBus bus, int nodeId);
```

**`IMasterTimeTranslators` interface (new file in `Hrot.Core.Network`):**

```csharp
namespace Hrot.Core.Network;

/// <summary>
/// Groups the three master-side time-sync translators behind a single per-frame call surface.
/// </summary>
public interface IMasterTimeTranslators : IDisposable
{
    /// <summary>Read managed write-buffer -> DDS egress (time-mode + lockstep).</summary>
    void ScanAndPublish();
    /// <summary>DDS ingress -> write buffer (time-mode + lockstep).</summary>
    void PollIngress();
    /// <summary>Late NTP ingress poll (Phase 5, after SwapBuffers).</summary>
    void PollNtpIngress();
}
```

All classes implementing `INetworkFactory` must be updated to add all three methods:
- `NedNetworkFactory` — real implementations (wired in HEXAG2-S006, HEXAG2-S007, and this task).
- Offline / null factory — no-op stubs for all three methods.
- Any other concrete factory in the codebase.

**Success conditions:**

1. `INetworkFactory` compiles with all three new methods.
2. `IMasterTimeTranslators.cs` compiles with no warnings in `Hrot.Core`.
3. All implementing classes compile (no missing member errors).
4. A test-double implementation returning `NullOrchestrationTranslator`, a no-op `IDisposable`,
   and a no-op `IMasterTimeTranslators` satisfies the interface contract without any DDS
   assembly reference.

---

### HEXAG2-S005 — Move Master Translators to Hrot.Network.Orchestration

**Design reference:** [Section 4.2.3](./DESIGN.md#423-move-master-translators-hexag2-s005)

**Context:**  
`ClusterOpMasterTranslator` and `NodeOpMasterTranslator` currently live in
`Hrot.Orchestrator/Translators/`.  `NodeOpSlaveTranslator` and `OrchestrationObserverTranslator`
are already in `Hrot.Network.Orchestration`.  All network adapter code must reside in the
`Hrot.Network.*` layer.

**Scope of changes:**

| File | Change |
|------|--------|
| `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs` | Move to `Hrot.Network.Orchestration/` |
| `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs` | Move to `Hrot.Network.Orchestration/` |
| `Hrot.Orchestrator/Translators/Payloads/` | Move folder content to `Hrot.Network.Orchestration/Payloads/` if master-specific |
| `Hrot.Orchestrator.csproj` | Remove moved files; remove any DDS package references that are no longer needed |
| `Hrot.Network.Orchestration.csproj` | Add moved files |

Namespace adjustment: change `namespace Hrot.Orchestrator.Translators` to
`namespace Hrot.Network.Orchestration` (or an appropriate sub-namespace).

**Success conditions:**

1. `Hrot.Network.Orchestration` assembly contains `ClusterOpMasterTranslator` and
   `NodeOpMasterTranslator`.
2. `Hrot.Orchestrator` assembly no longer contains those files.
3. `Hrot.Orchestrator` project compiles without any reference to `CycloneDDS.Runtime` types
   (if the moved translators were the only reason for that reference).
4. `Hrot.Network.Orchestration` compiles.

---

### HEXAG2-S010 — Sever unhandledRequestCallback from ClusterOpMasterTranslator

**Design reference:** [Section 4.2.3](./DESIGN.md#423-move-master-translators-hexag2-s005)

**Context:**  
`ClusterOpMasterTranslator` accepts an `Action<ClusterOpRequest>? unhandledRequestCallback` in
its constructor.  Time-control operations (`PauseTime`, `ResumeTime`, `StepTime`,
`SetTimeScale`) currently fall through to this callback, which is wired directly to
`_clusterMaster.HandleClusterOpRequest`.  This is a hidden direct dependency that bypasses the
bus and makes the infrastructure adapter structurally aware of a domain object.

After this task the translator must handle every `NedClusterOpType` value by publishing a
typed intent to the bus.  `MasterSyncController.Update()` is the exclusive consumer of the four
time-control intents; `ClusterMaster` remains entirely ignorant of them.

**Scope of changes:**

| File | Change |
|------|--------|
| `Hrot.Network.Orchestration/ClusterOpMasterTranslator.cs` | Remove `_unhandledRequestCallback` field and constructor parameter |
| `ClusterOpMasterTranslator.cs` | Add case handlers for `PauseTime`, `ResumeTime`, `StepTime`, `SetTimeScale`; publish new intent types to bus |
| `Fdp.Toolkits/Time/TimeLocalEvents.cs` (namespace `Fdp.Toolkits.Time.Domain`) | Add intent structs: `PauseTimeIntent`, `ResumeTimeIntent`, `StepTimeIntent`, `SetTimeScaleIntent` |
| `Hrot.Orchestrator/OrchestratorSubsystem.cs` | Remove `unhandledRequestCallback: _clusterMaster.HandleClusterOpRequest` from translator construction (already removed in HEXAG2-S008, but verify) |

**Suggested intent structs:**
```csharp
public struct PauseTimeIntent    { }
public struct ResumeTimeIntent   { }
public struct StepTimeIntent     { public float DeltaSeconds; }
public struct SetTimeScaleIntent { public float TimeScale; }
```

`ClusterMaster` must remain entirely ignorant of time manipulation.  The four time-control
intent types are routed exclusively to `MasterSyncController.Update()` during Phase 3, which
drains them natively from the bus read buffer after `SwapBuffers()`.  No re-publication step
through `ClusterMaster` is required.

**Success conditions:**

1. The four intent structs are defined in `Fdp.Toolkits.Time.Domain` (inside the
   `Fdp.Toolkits` project); no `Hrot.*` assembly defines or re-declares them.
2. `Hrot.Network.Orchestration` has a downward project reference to `Fdp.Toolkits` (or the
   assembly that owns the intents) so it can publish them; no upward reference is introduced.
3. `ClusterOpMasterTranslator` constructor has no `unhandledRequestCallback` parameter.
4. `ClusterOpMasterTranslator.ProcessRequest()` has no `default:` fall-through case that calls
   a callback; every `NedClusterOpType` value is handled by publishing an intent.
5. **Unit test `ClusterOpMasterTranslator_PauseTime_PublishesIntentToBus`:**  
   - Construct translator with a bus and mock DDS readers/writers (no ClusterMaster reference).
   - Feed a `ClusterOpRequest{OperationType = PauseTime}` via the mock reader.
   - Call `Tick()`.
   - Assert `bus.ConsumeManaged<PauseTimeIntent>()` contains one item.

---

### HEXAG2-S011 — Eliminate ClusterMaster.TimeControlRequested C# Event

**Design reference:** [Section 4.2.8](./DESIGN.md#428-eliminate-timecontrolrequested-c-event-hexag2-s011)

**Context:**  
Even after HEXAG2-S010 adds typed intent events to the bus, `ClusterMaster.Tick()` currently
still fires the `TimeControlRequested` C# event delegate, and `OrchestratorSubsystem` subscribes
to it to drive `MasterSyncController`.  This means `MasterSyncController` is reactively
controlled via a delegate callback rather than being a native, first-class consumer of the
unified bus.  The C# event keeps a structural coupling alive between `ClusterMaster` and
`OrchestratorSubsystem` that belongs in the bus.

After this task, `MasterSyncController.Update()` reads time-control intents directly from the
bus read buffer during Phase 3.  No delegate fires.

**Scope of changes:**

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/MasterSyncController.cs` | In `Update()`: drain `PauseTimeIntent`, `ResumeTimeIntent`, `StepTimeIntent`, `SetTimeScaleIntent` from bus |
| `Hrot.Orchestrator/ClusterMaster.cs` | Delete `public event Action<ClusterOpType, string>? TimeControlRequested` declaration |
| `ClusterMaster.cs` | Delete all sites that raise `TimeControlRequested` |
| `ClusterMaster.cs` | Remove `_slaveNodeIds` parameter or otherwise make it available to `MasterSyncController` via bus/config |
| `Hrot.Orchestrator/OrchestratorSubsystem.cs` | Delete the `_clusterMaster.TimeControlRequested += ...` subscription block from `Initialize()` |
| `OrchestratorSubsystem.cs` | Delete `private bool _isPaused` field (authoritative pause state lives in `ClusterUiCache.IsPaused`) |

To drain with the correct slave node set, `MasterSyncController` must receive the set of
participating slave IDs at the point it processes `PauseTimeIntent`.  Two options:
- Keep a `HashSet<int>` field on `MasterSyncController` and update it each frame by draining
  a `SlaveNodeSetUpdatedEvent` that `ClusterMaster` publishes instead of the C# event.
- Pass the slave IDs as a field on `PauseTimeIntent` itself (`IReadOnlySet<int> SlaveNodeIds`).

Either approach is acceptable; the choice must be documented in the implementing PR.

**Success conditions:**

1. `ClusterMaster` has no `TimeControlRequested` event member.
2. `OrchestratorSubsystem` has no `_isPaused` field and no `TimeControlRequested` subscription.
3. `MasterSyncController.Update()` contains drain loops for all four time-control intent types.
4. **Unit test `MasterSyncController_DrainsPauseTimeIntent_SwitchesToDeterministic`:**  
   - Publish `PauseTimeIntent` (with slave IDs) to the bus; swap; call `masterSync.Update()`.
   - Assert `masterSync.CurrentMode == MasterMode.BarrierPending`.
5. **Unit test `MasterSyncController_DrainsResumeTimeIntent_SwitchesToContinuous`:**  
   - Pause; then publish `ResumeTimeIntent`; swap; call `Update()`.
   - Assert mode returns to `MasterMode.Continuous`.

---

### HEXAG2-S012 — Slave Subsystem Factory Refactor

**Design reference:** [Section 4.2.9](./DESIGN.md#429-slave-subsystem-factory-refactor-hexag2-s012)

**Context:**  
`ExConSubsystem`, `SimHostApp`/`NodeBootstrapper`, and `CgfApplication` all instantiate
orchestration translators directly using `new` with `DdsReader<T>` / `DdsWriter<T>` inline.
This is the same violation being fixed on the master side.  Two new `INetworkFactory` ports
(`CreateSlaveOrchestratorTranslators`, `CreateOrchestrationObserver`) must be defined and
implemented so slave subsystems are 100% clean of direct translator instantiation.

**Scope of changes:**

| File | Change |
|------|--------|
| `Hrot.Core/Network/ISlaveOrchestrationTranslator.cs` | New file — interface with `Tick()` and `Dispose()` |
| `Hrot.Core/Network/IOrchestrationObserver.cs` | New file — interface with `Tick()` and `Dispose()` |
| `Hrot.Core/Network/INetworkFactory.cs` | Add `CreateSlaveOrchestratorTranslators(FdpEventBus, int)` and `CreateOrchestrationObserver(FdpEventBus)` |
| `Hrot.Network.NED/Factory/NedNetworkFactory.cs` | Implement both methods; composite slave adapter includes `NodeOpSlaveTranslator` and `ClusterOpEgressTranslator`; observer wraps `OrchestrationObserverTranslator` |
| `Hrot.Network.Orchestration/ClusterOpEgressTranslator.cs` | Rewrite to consume canonical strongly-typed intents from the bus read buffer (`TransitionStateIntent`, `ManageEpisodeIntent`, `PauseTimeIntent`, `ResumeTimeIntent`, `StepTimeIntent`, `SetTimeScaleIntent`, etc.) and map each to the corresponding outbound `ClusterOpRequest` DDS message; remove any `ClusterOpIntent` consumption |
| All other `INetworkFactory` implementors | No-op stubs |
| `Hrot.ExCon/ExConSubsystem.cs` | Replace all direct `new NodeOpSlaveTranslator(...)`, `new OrchestrationObserverTranslator(...)`, `new ClusterOpEgressTranslator(...)` with factory calls; store returned handles; dispose in `Shutdown()` |
| `Hrot.SimHost/NodeBootstrapper.cs` | Replace `new NodeOpSlaveTranslator(...)` with `_networkFactory.CreateSlaveOrchestratorTranslators(_bus, nodeId)` |
| `Hrot.CGF/CgfApplication.cs` | Replace `new NodeOpSlaveTranslator(...)` with `_networkFactory.CreateSlaveOrchestratorTranslators(_bus, nodeId)` |

Note: HEXAG2-S001b must be completed first (the bus is unified before the factory calls are
made).

**Required Update() loop wiring:**

After the factory calls, each slave subsystem's `Update()` must call `Tick()` on both handles
in Phase 1, before the single `SwapBuffers()`:

```
Phase 1 -- Network boundary
    _slaveTranslator?.Tick()     // NodeOpCommand ingress, NodeOpStatus + heartbeat egress
    _observer?.Tick()            // SystemState + AssetInventory ingress

Phase 2 -- Single frame boundary swap
    _bus?.SwapBuffers()          // exactly once
```

Failure to call `Tick()` before `SwapBuffers()` means no DDS ingress arrives in the frame it
was received, breaking the 1-frame latency contract.

**Success conditions:**

1. `ISlaveOrchestrationTranslator` and `IOrchestrationObserver` exist in `Hrot.Core.Network`.
2. `INetworkFactory` compiles with both new slave-side methods.
3. `ExConSubsystem` contains zero `new NodeOpSlaveTranslator`, `new OrchestrationObserverTranslator`,
   or `new ClusterOpEgressTranslator` expressions.
4. `NodeBootstrapper` and `CgfApplication` contain zero `new NodeOpSlaveTranslator` expressions.
5. Every slave `Update()` method calls `_slaveTranslator?.Tick()` and `_observer?.Tick()`
   before the single `_bus?.SwapBuffers()`.
6. `ClusterOpEgressTranslator` (via `ISlaveOrchestrationTranslator.Tick()`) contains zero
   references to `ClusterOpIntent`; it consumes only the canonical strongly-typed intents.
7. **Unit test `ExConSubsystem_HeadlessMode_InitializesWithoutException`:**  
   - Construct with `ExConSubsystem()` (parameterless / no factory).
   - Call `Initialize()` with minimal config; call `Update(0.016f)` several times.
   - Assert no exception is thrown.
8. All existing integration tests in `Hrot.ClusterRunner.Integration.Tests` continue to pass.

---

### HEXAG2-S006 — Implement NedNetworkFactory Orchestration Translators

**Design reference:** [Section 4.2.4](./DESIGN.md#424-nednetworkfactory-implementation-hexag2-s006)

**Context:**  
`NedNetworkFactory` must provide the real DDS implementation.  All existing DDS resource
creation that was scattered in `OrchestratorSubsystem.Initialize()` is gathered here.

**Scope of changes:**

| File | Change |
|------|--------|
| `Hrot.Network.NED/Factory/NedNetworkFactory.cs` | Add `CreateOrchestratorTranslators` implementation |
| `Hrot.Network.NED/Factory/NedOrchestrationTranslator.cs` | New file (composite adapter) |

`NedOrchestrationTranslator` owns:
- `DdsReader<NodeHeartbeat>`, `DdsReader<ClusterOpRequest>`,
  `DdsWriter<ClusterOpStatus>`, `DdsReader<NodeOpStatus>`
- `ClusterOpMasterTranslator` instance
- `NodeOpMasterTranslator` instance

It does NOT own the `DdsIdAllocatorServer`; that has its own lifecycle via
`CreateIdAllocatorServer()` (HEXAG2-S007).  No domain object is passed into this factory
method; the bus is the sole integration point.

`Tick()` must:
1. Poll `_heartbeatReader` and publish `NodeHeartbeatEvent` to the bus (heartbeat bridge).
2. Call `_clusterOpTranslator.Tick()`.
3. Call `_nodeOpTranslator.Tick()`.

`Dispose()` tears down all owned DDS readers/writers.

The offline/null factory must return `NullOrchestrationTranslator` (no-op `Tick()`, no-op
`Dispose()`).

**Success conditions:**

1. `NedNetworkFactory.CreateOrchestratorTranslators(bus, nodeId)` returns a
   non-null `IOrchestrationTranslator`.
2. `NullOrchestrationTranslator` compiles and does nothing on `Tick()` and `Dispose()`.
3. **Integration test:** With a real DDS participant, calling `Tick()` on the returned translator
   processes a waiting `ClusterOpRequest` DDS message and publishes the corresponding intent to
   the bus.

---

### HEXAG2-S007 — Extract DdsIdAllocatorServer Behind Dedicated Factory Port

**Design reference:** [Section 4.2.5](./DESIGN.md#425-ddsidAllocatorserver-lifecycle-hexag2-s007)

**Context:**  
Embedding the `DdsIdAllocatorServer` background thread inside the composite orchestration
translator (`NedOrchestrationTranslator`) would conflate two unrelated lifecycles and make
deterministic shutdown brittle.  The server is a global cluster infrastructure service; its
thread must have an independently managed, explicitly owned handle.  A dedicated factory port
(`INetworkFactory.CreateIdAllocatorServer()`) returns an `IDisposable` whose `Dispose()` blocks
via `Thread.Join` before any DDS participant is destroyed.

**Scope of changes:**

| File | Change |
|------|--------|
| `Hrot.Network.NED/Factory/HostedIdAllocatorServer.cs` | New file — owns `DdsIdAllocatorServer`, `Thread`, `CancellationTokenSource`; `Dispose()` cancels and joins |
| `Hrot.Network.NED/Factory/NedNetworkFactory.cs` | Implement `CreateIdAllocatorServer()` returning a new `HostedIdAllocatorServer` |
| `OrchestratorSubsystem.cs` | Add `private IDisposable? _idAllocatorServerHandle` field |
| `OrchestratorSubsystem.cs` | In `Initialize()`: `_idAllocatorServerHandle = _networkFactory.CreateIdAllocatorServer()` |
| `OrchestratorSubsystem.cs` | In `Shutdown()`: dispose `_idAllocatorServerHandle` **first**, before translator |
| `OrchestratorSubsystem.cs` | Remove inline `_idAllocatorServer`, `_idServerCts`, `_idServerThread` fields and all associated logic |

**Shutdown sequence in OrchestratorSubsystem.Shutdown():**
```csharp
_idAllocatorServerHandle?.Dispose();   // Thread.Join blocks here -- must be first
_idAllocatorServerHandle = null;
_translator?.Dispose();                // DDS reader/writer teardown
_translator = null;
// ... remaining Shutdown() cleanup ...
```

**Success conditions:**

1. `HostedIdAllocatorServer` exists in `Hrot.Network.NED`; its `Dispose()` cancels the
   polling thread and calls `Thread.Join` with a timeout before returning.
2. `NedNetworkFactory.CreateIdAllocatorServer()` returns a new `HostedIdAllocatorServer`.
3. `OrchestratorSubsystem` has a `private IDisposable? _idAllocatorServerHandle` field and
   disposes it as the first action in `Shutdown()`.
4. `OrchestratorSubsystem` no longer has `_idAllocatorServer`, `_idServerCts`, or
   `_idServerThread` fields or any background thread loop code.
5. ID allocation works end-to-end: integration tests that spawn entities and validate their IDs
   pass without regression.

---

### HEXAG2-S008 — Refactor OrchestratorSubsystem to Use INetworkFactory

**Design reference:** [Section 4.2.6](./DESIGN.md#426-refactor-orchestratorsubsystem-constructor-hexag2-s008)

**Context:**  
This is the capstone task for Phase 2.  It implements the `INetworkFactory` constructor fully,
removes the rogue participant creation, and wires the `IOrchestrationTranslator` into `Update()`.

**Scope of changes:**

| File | Change |
|------|--------|
| `OrchestratorSubsystem.cs` | Store `_networkFactory` in the field; remove `// TODO` comment |
| `OrchestratorSubsystem.cs` | Remove `_participant` field and `HrotEnvironment.CreateParticipant()` call |
| `OrchestratorSubsystem.cs` | Add `private IOrchestrationTranslator? _translator` field |
| `OrchestratorSubsystem.cs` | Add `private IDisposable? _idAllocatorServerHandle` field |
| `OrchestratorSubsystem.cs` | Add `private IMasterTimeTranslators? _timeTranslators` field |
| `OrchestratorSubsystem.cs` | In `Initialize()`: call `_networkFactory.CreateOrchestratorTranslators(_bus!, config.NodeId)` and store result |
| `OrchestratorSubsystem.cs` | In `Initialize()`: call `_networkFactory.CreateIdAllocatorServer()` and store result in `_idAllocatorServerHandle` |
| `OrchestratorSubsystem.cs` | In `Initialize()`: call `_networkFactory.CreateMasterTimeTranslators(_bus!, config.NodeId)` and store result in `_timeTranslators` |
| `OrchestratorSubsystem.cs` | Remove dead field `_sysOpWriter` (`DdsWriter<ClusterOpRequest>?`, TODO PACK-E001) |
| `OrchestratorSubsystem.cs` | Remove DDS reader/writer fields: `_clusterOpTranslator`, `_nodeOpTranslator`, `_sysOpRequestReader`, `_sysOpStatusWriter`, `_nodeOpStatusReader`, `_heartbeatReader` |
| `OrchestratorSubsystem.cs` | Remove time translator fields: `_timeModeTranslator`, `_lockstepTranslator`, `_masterTimeSyncTranslator` and all their construction / call sites |
| `OrchestratorSubsystem.cs` | In `Update()` Phase 1: replace `_timeModeTranslator?.ScanAndPublish` / `_lockstepTranslator?.ScanAndPublish` / `_lockstepTranslator?.PollIngress` / `_timeModeTranslator?.PollIngress` calls with `_timeTranslators?.ScanAndPublish()` and `_timeTranslators?.PollIngress()`; replace Phase 5 `_masterTimeSyncTranslator?.PollIngress` with `_timeTranslators?.PollNtpIngress()` |
| `OrchestratorSubsystem.cs` | In `Shutdown()`: dispose `_idAllocatorServerHandle` first, then `_translator`, then `_timeTranslators` |
| `OrchestratorSubsystem.cs` | Remove the temporary heartbeat bridging loop from `Update()` |

The `GlobalContextClusterOpHandler` still uses `_participant` for context loading.  If it
requires a `DdsParticipant`, obtain it via `_networkFactory.Participant`.

**Success conditions:**

1. `OrchestratorSubsystem.cs` contains zero references to `HrotEnvironment`,
   `DdsParticipant`, `DdsReader<T>`, or `DdsWriter<T>`.  Specifically, the following fields
   must not exist: `_participant`, `_sysOpWriter`, `_clusterOpTranslator`, `_nodeOpTranslator`,
   `_sysOpRequestReader`, `_sysOpStatusWriter`, `_nodeOpStatusReader`, `_heartbeatReader`,
   `_timeModeTranslator`, `_lockstepTranslator`, `_masterTimeSyncTranslator`.
2. `OrchestratorSubsystem.cs` imports no `CycloneDDS.Runtime` namespace.
3. `Hrot.Orchestrator` project compiles without a direct reference to `CycloneDDS.Runtime`.
4. **Headless unit test `OrchestratorSubsystem_HeadlessMode_InitializesWithoutException`:**
   - Construct with `OrchestratorSubsystem()` (parameterless).
   - Call `Initialize()` with a stub config.
   - Call `Update(0.016f)` several times.
   - Assert no exception is thrown.
5. All existing integration tests in `Hrot.ClusterRunner.Integration.Tests` pass.

---

### HEXAG2-S009 — Verify Composition Root Wiring

**Design reference:** [Section 4.2.7](./DESIGN.md#427-composition-root-verification-hexag2-s009)

**Context:**  
After all Phase 2 changes, the application entry point must pass a properly configured
`INetworkFactory` to `OrchestratorSubsystem`.  This task is a verification / correction step
that ensures the real application boots correctly after the decoupling.

**Scope of changes:**

| File | Change |
|------|--------|
| ClusterRunner `Program.cs` (or equivalent startup file) | Confirm/correct `OrchestratorSubsystem` construction with `INetworkFactory` |

Look for the instantiation site:
```csharp
new OrchestratorSubsystem(networkFactory)
```
Confirm `networkFactory` is already configured for the correct domain and node role before the
subsystem is created.  If `OrchestratorSubsystem()` (parameterless) is used in the real
ClusterRunner startup, update it to use the factory constructor.

**Success conditions:**

1. The real ClusterRunner application builds and starts without runtime exceptions related to
   null participant or missing DDS infrastructure.
2. On first frame, the cluster control panel renders with correct "RUNNING" status and the
   pause button triggers the "[PAUSED]" display on the next frame.
3. End-to-end test `ExCon_SendsJumpCommand_SimHostAppliesIt` passes (verifies orchestrator
   boots and DDS messaging works).
