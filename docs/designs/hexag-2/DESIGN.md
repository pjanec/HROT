# Design: OrchestratorSubsystem Hexagonal Architecture & Bus Unification

**Status:** In Design
**Folder:** `.dev/hexag-2/`
**Design Talk:** [design-talk.md](./design-talk.md)

---

## 1. Background and Motivation

### 1.1 The IsPaused UI Bug

The Orchestrator cluster control panel pauses simulated time (the numerical counter halts) but
continues to display "RUNNING" instead of "[PAUSED]".

Root cause is a strict bus-routing mismatch inside `OrchestratorSubsystem`:

| Component | Uses this bus |
|-----------|---------------|
| `ClusterUiCache` (subscribes to `SwitchTimeModeEvent`) | `_orchestrationBus` |
| `MasterSyncController` (publishes `SwitchTimeModeEvent`) | `_eventBus` |

`OrchestratorSubsystem` owns **two separate `FdpEventBus` instances** and never bridges time-mode
events between them.  When a pause is triggered, `MasterSyncController` successfully transitions
to `MasterMode.BarrierPending` and broadcasts `SwitchTimeModeEvent` onto `_eventBus`.
`ClusterUiCache`, however, only drains `_orchestrationBus`, so its `IsPaused` flag stays
permanently `false`.

The numerical time counter does halt (correctly) because `ClusterUiCache.MasterSimTime` bypasses
the bus entirely and polls `_localTimeController.GetCurrentState().TotalTime` directly.  This
produces the observed split behaviour: frozen sim-time number, stuck "RUNNING" label.

### 1.2 Historical Note: Deleted Bridging Loop

The older codebase contained an explicit bridging loop inside `OrchestratorSubsystem.Update()`:
it consumed `SwitchTimeModeEvent` from `_eventBus` and re-published it to `_orchestrationBus`.
During a refactoring to eliminate 1-frame propagation delays this loop was deleted without
replacing it, making the `ClusterUiCache` permanently blind to time-mode transitions.

### 1.3 The Split-Bus Anti-Pattern

The root cause is architectural: a single process owns multiple `FdpEventBus` instances with no
shared state.  This fragments the event backbone, forces error-prone manual bridging, and will
continue to produce subtle data-flow bugs.  The design decision to give `OrchestratorSubsystem`
its own isolated orchestration bus (CMC-S016 Option C) is a textbook integration anti-pattern
inside a monolithic process.

The problem is not limited to the two named buses.  `ClusterScenarioPanel` in the master
(Orchestrator) node is constructed with a direct reference to `ClusterMaster` and calls
`_master.HandleClusterOpRequest(req)` to send commands.  This bypasses the event bus entirely,
making the panel invisible to any infrastructure adapter that listens on the bus.  The same
split-bus pattern exists on the slave side (`ExConSubsystem`) where `_clusterOpEgressBus` and
`_uiCacheBus` duplicate the fragmentation.

The fix must be total: every component in the orchestrator scope that currently writes to an
isolated bus or calls directly into a domain object must instead publish to the single unified
`_bus`.  `ClusterMaster` and `MasterSyncController` must each consume a single canonical
vocabulary of strongly-typed intent events from the bus — the same events produced by both the
network boundary translators and the local UI panel, with no intermediate `ClusterOpIntent`
wrapper type.

### 1.4 Hexagonal Architecture Violations

Beyond the bus problem, multiple subsystems break the Hexagonal Architecture rule that
domain logic must not touch CycloneDDS infrastructure:

**OrchestratorSubsystem (master node)**
1. **Rogue participant creation.** `OrchestratorSubsystem.Initialize()` calls
   `HrotEnvironment.CreateParticipant(config.DomainId)` directly, bypassing the composition root
   and making headless offline testing impossible.  Rule 3 of the project DESIGN explicitly
   forbids any subsystem from calling `new DdsParticipant()` or `HrotEnvironment.CreateParticipant()`
   internally.

2. **Concrete translator instantiation.** `ClusterOpMasterTranslator` and
   `NodeOpMasterTranslator` are `new`-ed directly inside `OrchestratorSubsystem`.  `NodeOpSlaveTranslator`
   was already relocated to `Hrot.Network.Orchestration`; the master translators must follow.

3. **DdsIdAllocatorServer lifecycle.** The allocator server is a pure infrastructure component
   tied to CycloneDDS, but it is spun up on a background thread directly inside
   `OrchestratorSubsystem.Initialize()`.  It must be encapsulated in `NedNetworkFactory`.

4. **The existing `INetworkFactory` injection point is a stub.** The constructor
   `OrchestratorSubsystem(INetworkFactory)` exists but contains only a TODO comment; none of the
   infrastructure decoupling it promises has been implemented.

5. **C# event coupling for time control.** `OrchestratorSubsystem.Initialize()` subscribes to
   `_clusterMaster.TimeControlRequested` (a C# event delegate) to route pause/resume/step
   commands to `MasterSyncController`.  Even if the callback is removed from the DDS translator,
   having `ClusterMaster` fire a C# event from `Tick()` to drive `MasterSyncController` keeps a
   tight structural coupling between two domain objects.  `MasterSyncController` must instead
   consume time-control intent events natively from the unified `_bus` read buffer during
   Phase 3, completely eliminating the C# event delegate.

**Slave subsystems (ExCon, SimHost, CGF)**
6. **Direct concrete translator instantiation in slave subsystems.** `ExConSubsystem`,
   `SimHostApp`/`NodeBootstrapper`, and `CgfApplication` each instantiate `NodeOpSlaveTranslator`
   and (in ExCon) `OrchestrationObserverTranslator` / `ClusterOpEgressTranslator` using direct
   `new` with `DdsReader<T>` / `DdsWriter<T>` inline — the same pattern that is being eliminated
   on the master side.  `INetworkFactory` must be extended with dedicated slave-side ports so
   all subsystems obtain their orchestration adapters exclusively through the factory.

---

## 2. Target Architecture

### 2.1 Layered Overview

```
+--------------------------------------------------------------+
| Composition Root (ClusterRunner / Program.cs)                |
|  - creates INetworkFactory                                   |
|  - passes it to OrchestratorSubsystem constructor            |
|  - passes it to ExConSubsystem / SimHostApp constructors     |
+--------------------------------------------------------------+
            |
            | INetworkFactory
            v
+--------------------------------------------------------------+
| OrchestratorSubsystem (master)               DOMAIN          |
|  - single FdpEventBus _bus                                   |
|  - ClusterMaster(_bus)                                       |
|  - MasterSyncController(_bus, ...)   <-- drains intents      |
|  - ClusterUiCache(_bus, _masterSync)                         |
|  - ClusterScenarioPanel(_bus, _uiCache)                      |
|  - IOrchestrationTranslator _translator        (port)        |
|  - IDisposable _idAllocatorServerHandle        (port)        |
+--------------------------------------------------------------+
        |                              |
        | IOrchestrationTranslator     | INetworkFactory.CreateIdAllocatorServer()
        v                              v
+---------------------+    +----------------------------------+
| NedOrchestration-   |    | HostedIdAllocatorServer          |
| Translator          |    |  - DdsIdAllocatorServer          |
|  - ClusterOp/NodeOp |    |  - background Thread             |
|    translators      |    |  - Dispose() blocks Thread.Join  |
+---------------------+    +----------------------------------+

+--------------------------------------------------------------+
| ExConSubsystem / SimHostApp / CgfApplication (slave nodes)   |
|  DOMAIN                                                      |
|  - single FdpEventBus _bus (per subsystem)                   |
|  - ClusterSlave(_bus)                                        |
|  - ClusterUiCache(_bus, ...)                                 |
|  - ClusterScenarioPanel(_bus, ...)                           |
|  - ISlaveOrchestrationTranslator _slaveTranslator  (port)   |
|  - IOrchestrationObserver _observer                (port)   |
+--------------------------------------------------------------+
            |
            | INetworkFactory.CreateSlaveOrchestratorTranslators() /
            |                 CreateOrchestrationObserver()
            v
+--------------------------------------------------------------+
| NED slave adapters (Hrot.Network.Orchestration /             |
|                     Hrot.Network.NED)                        |
|  - NodeOpSlaveTranslator (existing, now factory-created)     |
|  - OrchestrationObserverTranslator (existing, factory-made)  |
|  - ClusterOpEgressTranslator (existing, factory-made)        |
+--------------------------------------------------------------+
```

### 2.2 Single Unified Bus with Strict Phase Discipline

The two bus instances (`_orchestrationBus`, `_eventBus`) are collapsed into one `_bus`.
`Update()` follows a precise 4-phase sequence so that every component sees the right events
at the right time, within a single `SwapBuffers()` call per frame:

```
Phase 1 -- Network boundary (egress + ingress)
    _timeTranslators.ScanAndPublish()         // time-mode, lockstep: read managed_read -> DDS egress
    _timeTranslators.PollIngress()            // time-mode, lockstep: DDS ingress -> write buffer
    _translator.Tick()                        // orchestration DDS egress + ingress

Phase 2 -- Single frame boundary swap (ONE call only)
    _bus.SwapBuffers()

Phase 3 -- Core logic
    _masterSync.Update()
    _clusterMaster.Tick()

Phase 4 -- Local observation (UI)
    _uiCache.Update()                         // now sees SwitchTimeModeEvent on same _bus
    _scenarioPanel.Update(deltaTime)

Phase 5 -- Time-sync NTP ingress (late-binding)
    _timeTranslators.PollNtpIngress()
```

This eliminates the IsPaused bug: `MasterSyncController` publishes `SwitchTimeModeEvent` to the
write buffer in Phase 3.  The next frame's Phase 2 swap promotes it to the read buffer.  Phase 4
reads it and sets `IsPaused = true`.  (One-frame display delay is acceptable and consistent with
the existing 1-frame propagation already in the codebase.)

---

## 3. Implementation Phases and Tasks

### Phase 1: Unify Event Buses (Fix IsPaused Bug)

**Goal:** Collapse all secondary buses into one and restore correct pause UI state — across every orchestrator-aware subsystem.

| Task | Description |
|------|-------------|
| HEXAG2-S001 | Merge ALL secondary buses in `OrchestratorSubsystem` into `_bus`; re-wire `ClusterScenarioPanel` to bus path |
| HEXAG2-S001b | Merge ALL secondary buses in `ExConSubsystem` (`_orchestrationBus`, `_uiCacheBus`, `_clusterOpEgressBus`, `_timeEventBus`) into a single `_bus` |
| HEXAG2-S002 | Rewrite `OrchestratorSubsystem.Update()` with strict 4-phase single-swap sequence |

### Phase 2: Hexagonal Architecture Compliance

**Goal:** Remove all CycloneDDS dependencies from `OrchestratorSubsystem` domain logic.

| Task | Description |
|------|-------------|
| HEXAG2-S003 | Define `IOrchestrationTranslator` interface (tick, dispose) in `Hrot.Core.Network` |
| HEXAG2-S004 | Extend `INetworkFactory` with `CreateOrchestratorTranslators(FdpEventBus, int)` and `CreateIdAllocatorServer()` |
| HEXAG2-S005 | Physically move `ClusterOpMasterTranslator` + `NodeOpMasterTranslator` to `Hrot.Network.Orchestration` |
| HEXAG2-S010 | Sever `unhandledRequestCallback`; add time-control intent types; refactor `ClusterMaster` to consume exclusively from bus |
| HEXAG2-S011 | Eliminate `ClusterMaster.TimeControlRequested` C# event; wire `MasterSyncController` to consume time-control intents natively from bus |
| HEXAG2-S006 | Implement `CreateOrchestratorTranslators` and `NedOrchestrationTranslator` in NED layer; stubs in offline factory |
| HEXAG2-S007 | Extract `DdsIdAllocatorServer` behind `INetworkFactory.CreateIdAllocatorServer()` with `HostedIdAllocatorServer` |
| HEXAG2-S008 | Refactor `OrchestratorSubsystem` to use `INetworkFactory`; remove rogue participant creation |
| HEXAG2-S012 | Extend `INetworkFactory` with slave-side ports; refactor `ExConSubsystem`, `SimHostApp`/`NodeBootstrapper`, `CgfApplication` to use factory |
| HEXAG2-S009 | Verify composition root (`ClusterRunner`/`Program.cs`) wires `INetworkFactory` into `OrchestratorSubsystem` |

---

## 4. Detailed Design

### 4.1 Phase 1 — Unify Event Buses

#### 4.1.1 Single Bus (HEXAG2-S001 / HEXAG2-S001b)

**OrchestratorSubsystem (HEXAG2-S001)**

Remove ALL secondary `FdpEventBus` fields.  The complete list to delete:
```csharp
// BEFORE -- delete all of these:
private FdpEventBus? _orchestrationBus;   // orchestration events
private FdpEventBus? _eventBus;           // time-controller events
// (any further rogue FdpEventBus field found during inspection must also be removed)
```

Replace with one field:
```csharp
// AFTER -- the only FdpEventBus in OrchestratorSubsystem:
private FdpEventBus? _bus;
```

`Initialize()` becomes:
```csharp
_bus = new FdpEventBus();
_clusterMaster    = new ClusterMaster(_bus, _config);
_masterSync       = new MasterSyncController(_bus, new HashSet<int>(), TimeConfig.Default);
_bus.SwapBuffers();   // promote initial SwitchTimeModeEvent{Continuous} published by ctor
_uiCache          = new ClusterUiCache(_bus, _masterSync);
_scenarioPanel    = new ClusterScenarioPanel(_bus, _uiCache);  // bus path, not _clusterMaster
```

`ClusterUiCache`, `MasterSyncController`, `ClusterMaster`, and `ClusterScenarioPanel` all share
the same bus instance.  `ClusterScenarioPanel` must be constructed with `_bus` (not
`_clusterMaster`) so that its user actions publish strongly-typed intent events directly to the
bus — the same canonical types that the network boundary produces (`TransitionStateIntent`,
`ManageEpisodeIntent`, `PauseTimeIntent`, `ResumeTimeIntent`, `StepTimeIntent`,
`SetTimeScaleIntent`, etc.).  No `ClusterOpIntent` wrapper type is introduced; both the UI panel
and the DDS translator speak the same vocabulary.  `ClusterMaster.Tick()` drains
`TransitionStateIntent`, `ManageEpisodeIntent`, and similar domain-operation intents;
`MasterSyncController.Update()` drains `PauseTimeIntent`, `ResumeTimeIntent`, `StepTimeIntent`,
and `SetTimeScaleIntent`.

The `TimeBusForTest` test hook must be updated to return `_bus`.

**ExConSubsystem (HEXAG2-S001b)**

`ExConSubsystem` currently owns four isolated buses:
```csharp
// BEFORE -- all four must be deleted:
private FdpEventBus? _orchestrationBus;    // ClusterSlave / NodeOpSlaveTranslator
private FdpEventBus? _uiCacheBus;          // ClusterUiCache / OrchestrationObserverTranslator
private FdpEventBus? _clusterOpEgressBus;  // ClusterScenarioPanel / ClusterOpEgressTranslator
private FdpEventBus? _timeEventBus;        // SlaveSyncController / time translators
```

Replace with one field:
```csharp
private FdpEventBus? _bus;
```

`Initialize()` must pass `_bus` to every component that previously received an isolated bus:
- `ClusterSlave(_bus)`
- `NodeOpSlaveTranslator(..., bus: _bus, ...)`
- `ClusterUiCache(_bus, _slaveSyncController)`
- `ClusterScenarioPanel(_bus, _uiCache)`
- `OrchestrationObserverTranslator(_participant, _bus)`
- `ClusterOpEgressTranslator(_bus, _participant)`
- `SlaveSyncController(_bus, nodeId, TimeConfig.Default)`
- All time translators

`Update()` must contain exactly one `_bus?.SwapBuffers()` call, replacing the current
three separate `_orchestrationBus?.SwapBuffers()`, `_uiCacheBus?.SwapBuffers()`, and
`_clusterOpEgressBus?.SwapBuffers()` calls.

The split across `SimHostApp`/`NodeBootstrapper` and `CgfApplication` follows the same pattern
but is tracked under HEXAG2-S012 (the full factory port task).

#### 4.1.2 Strict Phase Discipline in Update() (HEXAG2-S002)

See Section 2.2 for the exact sequence.  The main change is:
- Remove the secondary orchestration-bus swap that previously occurred mid-frame.
- Ensure `_translator.Tick()` (once it exists) fires in Phase 1 alongside the time translators.
- Keep a single `_bus.SwapBuffers()` call.
- Remove the manual heartbeat bridging loop (it will be absorbed into `_translator.Tick()`).
- **Relocate `_masterSync?.Update()` to Phase 3**, after `SwapBuffers()`.  In the current
  implementation `_masterSync?.Update()` runs at the top of `Update()`, before any network
  ingress and before any swap, so time-control intents arriving via Phase 1 are never visible
  to it in the same frame.  Phase 3 placement guarantees it reads from the freshly promoted
  read buffer.

### 4.2 Phase 2 — Hexagonal Architecture Compliance

#### 4.2.1 IOrchestrationTranslator Interface (HEXAG2-S003)

Define in `Hrot.Core.Network` (same assembly as `INetworkFactory`):
```csharp
/// <summary>
/// Ticks all DDS ingress/egress for orchestrator master transport.
/// Called once per frame inside OrchestratorSubsystem.Update(), before SwapBuffers.
/// </summary>
public interface IOrchestrationTranslator : IDisposable
{
    void Tick();
}
```

#### 4.2.2 INetworkFactory Extension (HEXAG2-S004)

Add two methods to `INetworkFactory`.  Neither method accepts any domain object — the
infrastructure layer must remain entirely ignorant of `ClusterMaster` and all other domain
types.  The `FdpEventBus` is the sole integration mechanism.

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
/// and _masterTimeSyncTranslator.  All owned DDS resources are released on Dispose().
/// Returns a no-op implementation when there is no DDS participant.
/// </summary>
IMasterTimeTranslators CreateMasterTimeTranslators(FdpEventBus bus, int nodeId);
```

`IMasterTimeTranslators` is a new interface in `Hrot.Core.Network`:
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

All implementations of `INetworkFactory` must be updated:
- `NedNetworkFactory` — real DDS implementations for all three methods.
- Offline / null factory — no-op stubs for all three methods.
- `BdcNetworkFactory` (if applicable) — no-op stubs for all three methods.

**Slave-side ports (HEXAG2-S012)**

Add two further methods to `INetworkFactory` for slave nodes:

```csharp
/// <summary>
/// Creates the slave-side orchestration translator (NodeOpCommand ingress,
/// NodeOpStatus + NodeHeartbeat egress) for the given node role and ID.
/// Returns a no-op translator when there is no DDS participant.
/// </summary>
ISlaveOrchestrationTranslator CreateSlaveOrchestratorTranslators(
    FdpEventBus bus,
    int nodeId);

/// <summary>
/// Creates the cluster observer translator (SystemStateTopic, AssetInventoryTopic
/// ingress -> bus events).  Used by ExCon and other observer nodes.
/// Returns a no-op translator when there is no DDS participant.
/// </summary>
IOrchestrationObserver CreateOrchestrationObserver(FdpEventBus bus);
```

A corresponding `ISlaveOrchestrationTranslator` interface (defined alongside
`IOrchestrationTranslator` in `Hrot.Core.Network`) provides:
```csharp
public interface ISlaveOrchestrationTranslator : IDisposable
{
    void Tick();
}
```

The `ClusterOpEgressTranslator` (ExCon panel -> DDS) is absorbed into
`ISlaveOrchestrationTranslator.Tick()` so that only one translator call is needed per slave
frame, preserving the single-swap phase discipline.  Because `ClusterScenarioPanel` no longer
publishes `ClusterOpIntent` (eliminated in HEXAG2-S001), the egress translator implementation
must be rewritten to consume the canonical strongly-typed intent vocabulary directly from the
bus read buffer (`TransitionStateIntent`, `ManageEpisodeIntent`, `PauseTimeIntent`,
`ResumeTimeIntent`, `StepTimeIntent`, `SetTimeScaleIntent`, etc.) and map each intent to the
corresponding outbound `ClusterOpRequest` DDS message.  `ClusterOpIntent` is not consumed.

#### 4.2.3 Move Master Translators (HEXAG2-S005)

Physically move:
- `Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs`
- `Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`
- `Hrot.Orchestrator/Translators/Payloads/` (if any payloads are specific to master side)

Destination: `Hrot.Network.Orchestration` assembly (where `NodeOpSlaveTranslator` and
`OrchestrationObserverTranslator` already live).

Update the `.csproj` files accordingly.  The `Hrot.Orchestrator` project must no longer reference
any DDS types in its translator files.

During the translator move, the `_unhandledRequestCallback` pattern in
`ClusterOpMasterTranslator` must also be eliminated (see HEXAG2-S010).  The translator no longer
accepts `Action<ClusterOpRequest>? unhandledRequestCallback`.  Time-control DDS operations
(`PauseTime`, `ResumeTime`, `StepTime`, `SetTimeScale`) that currently fall through to the
callback must instead publish typed intent events directly to the `FdpEventBus`.
`ClusterMaster` must remain entirely ignorant of time manipulation; these intents are drained
exclusively by `MasterSyncController.Update()` in Phase 3.

Because `MasterSyncController` lives in `FDP.Toolkit.Time` — a foundational assembly that must
not reference any `Hrot.*` assemblies — the four intent structs must be defined in the
`FDP.Toolkit.Time` layer (e.g. `FDP/Toolkits/Fdp.Toolkits/Time/TimeLocalEvents.cs` in the
`Fdp.Toolkits.Time.Domain` namespace).  Translators in `Hrot.Network.Orchestration` take a
downward dependency on `Fdp.Toolkits` to publish them; `MasterSyncController` (already in
`Fdp.Toolkits`) consumes them without violating the dependency direction.

#### 4.2.4 NedNetworkFactory Implementation (HEXAG2-S006)

In `NedNetworkFactory.CreateOrchestratorTranslators(bus, nodeId)`:
1. Create `DdsReader<NodeHeartbeat>`, `DdsReader<ClusterOpRequest>`,
   `DdsWriter<ClusterOpStatus>`, `DdsReader<NodeOpStatus>`.
2. Instantiate `ClusterOpMasterTranslator` and `NodeOpMasterTranslator`, passing only the
   bus and DDS readers/writers.  No domain object reference (`ClusterMaster` or otherwise)
   is passed; integration is exclusively via bus events.
3. Return a `NedOrchestrationTranslator` composite whose `Tick()` polls the heartbeat reader
   (publishing `NodeHeartbeatEvent` to the bus), then ticks both translators.
4. `Dispose()` tears down all owned DDS resources.  It does not touch the allocator server,
   which has its own lifecycle via `CreateIdAllocatorServer()`.

`NedNetworkFactory.CreateIdAllocatorServer()` instantiates and returns a
`HostedIdAllocatorServer` (see Section 4.2.5) wrapping the factory's owned `DdsParticipant`.

The offline/unit-test factory returns `NullOrchestrationTranslator` from
`CreateOrchestratorTranslators` and a no-op `IDisposable` from `CreateIdAllocatorServer`.

#### 4.2.5 DdsIdAllocatorServer Lifecycle (HEXAG2-S007)

The `DdsIdAllocatorServer` is a global cluster infrastructure service — its lifecycle must not
be entangled with message translators.  Embedding it inside `NedOrchestrationTranslator` would
hide a background thread behind a generic `IDisposable` and make deterministic shutdown brittle.

**HostedIdAllocatorServer (new class in `Hrot.Network.NED`)**
```csharp
/// <summary>
/// Owns the DdsIdAllocatorServer background polling thread.
/// Dispose() cancels the loop and blocks via Thread.Join before any DDS teardown.
/// </summary>
internal sealed class HostedIdAllocatorServer : IDisposable
{
    private readonly DdsIdAllocatorServer         _server;
    private readonly CancellationTokenSource      _cts    = new();
    private readonly Thread                       _thread;

    public HostedIdAllocatorServer(DdsParticipant participant)
    {
        _server = new DdsIdAllocatorServer(participant);
        _thread = new Thread(() =>
        {
            while (!_cts.IsCancellationRequested)
            {
                _server.ProcessRequests();
                Thread.Sleep(1);
            }
        }) { IsBackground = true, Name = "Orchestrator-IdAllocServer" };
        _thread.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}
```

**OrchestratorSubsystem ownership**

`OrchestratorSubsystem` stores the returned handle in a dedicated field and disposes it
explicitly as the first step of `Shutdown()`, before tearing down translators, to guarantee
the polling thread is fully joined before any DDS resource destruction:
```csharp
private IDisposable? _idAllocatorServerHandle;

// In Initialize():
_idAllocatorServerHandle = _networkFactory.CreateIdAllocatorServer();

// In Shutdown() -- first line of teardown:
_idAllocatorServerHandle?.Dispose();
_idAllocatorServerHandle = null;
```

Remove from `OrchestratorSubsystem` the inline fields `_idAllocatorServer`, `_idServerCts`,
`_idServerThread` and all associated thread start/join logic.

#### 4.2.6 Refactor OrchestratorSubsystem Constructor (HEXAG2-S008)

The existing constructor stub:
```csharp
public OrchestratorSubsystem(INetworkFactory networkFactory)
{
    // TODO: decouple ...
}
```

Becomes:
```csharp
public OrchestratorSubsystem(INetworkFactory networkFactory)
{
    _networkFactory = networkFactory ?? throw new ArgumentNullException(nameof(networkFactory));
}
```

In `Initialize()`:
```csharp
// Remove:
_participant = HrotEnvironment.CreateParticipant(config.DomainId);

// The participant is now available via the factory if any component still needs it:
// var participant = _networkFactory.Participant;

// After bus + ClusterMaster + ClusterScenarioPanel creation:
_translator              = _networkFactory.CreateOrchestratorTranslators(_bus!, config.NodeId);
_idAllocatorServerHandle = _networkFactory.CreateIdAllocatorServer();
_timeTranslators         = _networkFactory.CreateMasterTimeTranslators(_bus!, config.NodeId);
```

In `Shutdown()` — dispose in this order to guarantee deterministic thread teardown before DDS
destruction:
```csharp
_idAllocatorServerHandle?.Dispose();   // Thread.Join blocks here -- must be first
_idAllocatorServerHandle = null;
_translator?.Dispose();                // tear down DDS readers/writers
_translator = null;
_timeTranslators?.Dispose();           // tear down time-sync DDS resources
_timeTranslators = null;
// ... remainder of Shutdown() ...
```

Remove from `OrchestratorSubsystem`:
- `_participant` field
- Dead field `_sysOpWriter` (`DdsWriter<ClusterOpRequest>`, marked TODO PACK-E001)
- Direct DDS reader/writer fields: `_clusterOpTranslator`, `_nodeOpTranslator`,
  `_sysOpRequestReader`, `_sysOpStatusWriter`, `_nodeOpStatusReader`, `_heartbeatReader`
- Inline thread fields: `_idAllocatorServer`, `_idServerCts`, `_idServerThread`
- Time translator fields: `_timeModeTranslator`, `_lockstepTranslator`, `_masterTimeSyncTranslator`
- All associated DDS/thread start and join logic in `Initialize()` and `Shutdown()`

Keep the parameterless `public OrchestratorSubsystem()` constructor for unit tests that use the
offline factory or inject mocks.

#### 4.2.8 Eliminate TimeControlRequested C# Event (HEXAG2-S011)

After HEXAG2-S010 adds `PauseTimeIntent`, `ResumeTimeIntent`, `StepTimeIntent`, and
`SetTimeScaleIntent` to the bus, the `ClusterMaster.TimeControlRequested` C# event becomes
redundant.  Keeping it means `ClusterMaster.Tick()` still fires a delegate into
`OrchestratorSubsystem`, maintaining a tight structural coupling.

To fully sever this:
1. **`MasterSyncController.Update()`** must drain the four time-control intent types directly
   from the bus read buffer.  It already holds a reference to the bus; the drain is analogous
   to how `ClusterSlave` drains `ExecuteNodeOpIntent`.

```csharp
// Inside MasterSyncController.Update():
foreach (var _ in _bus.ConsumeManaged<PauseTimeIntent>())
    SwitchToDeterministic(_slaveNodeIds);
foreach (var _ in _bus.ConsumeManaged<ResumeTimeIntent>())
    SwitchToContinuous();
foreach (var ev in _bus.ConsumeManaged<StepTimeIntent>())
    Step(ev.DeltaSeconds);
foreach (var ev in _bus.ConsumeManaged<SetTimeScaleIntent>())
    SetTimeScale(ev.TimeScale);
```

2. **Delete `ClusterMaster.TimeControlRequested`** event declaration and all sites that raise or
   subscribe to it (including the subscription block in `OrchestratorSubsystem.Initialize()`).

3. **Delete `OrchestratorSubsystem._isPaused`** field.  The paused state is now authoritative
   in `ClusterUiCache.IsPaused`, which already reads `SwitchTimeModeEvent` from the unified bus.

#### 4.2.9 Slave Subsystem Factory Refactor (HEXAG2-S012)

The three slave subsystems (`ExConSubsystem`, `SimHostApp`/`NodeBootstrapper`,
`CgfApplication`) must obtain their orchestration translators from `INetworkFactory` instead of
instantiating DDS types directly.

**Per-subsystem changes:**

| Subsystem | Current direct instantiation | Replaced by |
|-----------|------------------------------|-------------|
| `ExConSubsystem` | `new NodeOpSlaveTranslator(new DdsReader<NodeOpCommand>(...), ...)` | `_networkFactory.CreateSlaveOrchestratorTranslators(_bus, nodeId)` |
| `ExConSubsystem` | `new OrchestrationObserverTranslator(_participant, _uiCacheBus)` | `_networkFactory.CreateOrchestrationObserver(_bus)` |
| `ExConSubsystem` | `new ClusterOpEgressTranslator(_clusterOpEgressBus, _participant)` | absorbed into `ISlaveOrchestrationTranslator.Tick()` |
| `SimHostApp`/`NodeBootstrapper` | `new NodeOpSlaveTranslator(...)` | `_networkFactory.CreateSlaveOrchestratorTranslators(_bus, nodeId)` |
| `CgfApplication` | `new NodeOpSlaveTranslator(...)` | `_networkFactory.CreateSlaveOrchestratorTranslators(_bus, nodeId)` |

Each subsystem stores the returned handle(s) in appropriately named fields and disposes them
in `Shutdown()` / `Dispose()`.  After the refactor, no slave subsystem should have a direct
import of `using CycloneDDS.Runtime;` motivated by orchestration translators.

**Required Update() loop wiring for slave subsystems:**

Every slave subsystem must call `Tick()` on both returned handles in Phase 1, before the single
`SwapBuffers()`:

```
Phase 1 -- Network boundary
    _slaveTranslator?.Tick()     // NodeOpCommand ingress, NodeOpStatus + heartbeat egress
    _observer?.Tick()            // SystemState + AssetInventory ingress

Phase 2 -- Single frame boundary swap
    _bus?.SwapBuffers()          // exactly once

Phase 3 -- Core logic
    ...
```

Failure to call `Tick()` before `SwapBuffers()` means no DDS ingress arrives in the same frame
it was received, breaking the 1-frame latency contract.

#### 4.2.7 Composition Root Verification (HEXAG2-S009)

Verify (and update if needed) `ClusterRunner` / `Program.cs` so that when
`OrchestratorSubsystem` is instantiated with `INetworkFactory`, the factory is already configured
for the correct DDS domain ID and node role.  No DDS participant should be created inside the
subsystem anymore.

---

## 5. Invariants and Constraints

- **One `SwapBuffers()` per frame.** The unified bus must be swapped exactly once per `Update()`
  call.  Any additional swap is a bug.
- **`OrchestratorSubsystem` must compile and pass all tests in headless (no-DDS) mode** after
  Phase 2 is complete.  The parameterless constructor + offline factory must fulfil this.
- **`INetworkFactory.Participant` is the only permitted way** for a subsystem to obtain a
  `DdsParticipant` reference.  Direct calls to `HrotEnvironment.CreateParticipant()` inside any
  `ISubsystem` implementation are forbidden.
- **No new `FdpEventBus` instances may be created inside a subsystem.** Buses are created at the
  composition root or (for the unified bus) directly in `OrchestratorSubsystem.Initialize()` once
  only.
- **`INetworkFactory` ports must accept no domain objects.** No domain type (`ClusterMaster`,
  `ClusterSlave`, etc.) may appear as a parameter in any `INetworkFactory` method.  Integration
  is exclusively via `FdpEventBus` events.
- **`unhandledRequestCallback` must not exist.** `ClusterOpMasterTranslator` must handle all
  `NedClusterOpType` values inline, publishing typed intent events to the bus.  No fallback
  callback to a domain object is permitted.
- **`CreateIdAllocatorServer()` handle ownership is explicit.** The subsystem retains the handle
  in a dedicated field and disposes it as the first step of `Shutdown()`, before any translator
  or DDS participant teardown.
- **`ClusterMaster.TimeControlRequested` C# event must not exist** after HEXAG2-S011.
  Time control is driven exclusively by bus intents drained by `MasterSyncController.Update()`.
- **Slave subsystems must not `new` any orchestration translator.** All slave translators
  (`NodeOpSlaveTranslator`, `OrchestrationObserverTranslator`, `ClusterOpEgressTranslator`)
  must be obtained exclusively via `INetworkFactory` ports.
- **Slave subsystems must call `Tick()` before `SwapBuffers()`.** Both `ISlaveOrchestrationTranslator.Tick()`
  and `IOrchestrationObserver.Tick()` must be called in Phase 1, before the single
  `_bus?.SwapBuffers()`.  Calling `Tick()` after swap, or not calling it at all, is a bug.
- **`IMasterTimeTranslators` covers all three master-side time translator fields.**
  `_timeModeTranslator`, `_lockstepTranslator`, and `_masterTimeSyncTranslator` must not exist
  as separate fields in `OrchestratorSubsystem` after HEXAG2-S008.  All three are obtained via
  `INetworkFactory.CreateMasterTimeTranslators()` and held as a single `_timeTranslators` handle.
- **`_sysOpWriter` must be deleted.**  The dead `DdsWriter<ClusterOpRequest>? _sysOpWriter`
  field (marked TODO PACK-E001) must be removed in HEXAG2-S008.  It must not be retained as a
  placeholder.
