# Fdp.ModuleHost

**Path:** `FDP/Engine/Fdp.ModuleHost/`
**Project file:** `FDP/Engine/Fdp.ModuleHost/Fdp.ModuleHost.csproj`
**Date:** 2026-05-23

---

## README Validation

**Missing.** No `README.md` exists in `FDP/Engine/Fdp.ModuleHost/` or in the immediate parent
`FDP/Engine/`. The parent folder does have a `README.md` but it describes the whole FDP engine
layer at a high level and contains no module-host-specific content. Documentation status: **Missing**.

---

## Executive Overview

`Fdp.ModuleHost` is the **module lifecycle, scheduling, and execution orchestration** layer of
the FDP simulation framework. It sits directly above the ECS kernel (`Fdp.Core`) and provides:

- A structured way to partition simulation logic into independently-managed **modules**
- A multi-mode execution pipeline covering synchronous, frame-synced background, and fully
  asynchronous execution
- Thread-safe **snapshot isolation**: each background module receives a private or shared
  read-only view of ECS state so it can run freely without touching the live world
- **RCU (Read-Copy-Update) hot-plugging**: modules can be installed or uninstalled at runtime
  without pausing the 60 Hz main loop; the only main-thread cost is an O(1) atomic pointer swap
- A dependency-sorted **system scheduler** that executes `IEcsModuleSystem` instances in
  deterministic order across well-defined simulation phases
- A **circuit breaker** per module that absorbs transient failures and prevents a crashing module
  from stalling the simulation
- A **time-control abstraction** (`ITimeController`) that decouples wall-clock management from
  frame execution, enabling continuous, deterministic, and stepped time modes

### Role in the FDP Framework

```
+--------------------------------------------------+
|            Application / Scenario Layer          |
+--------------------------------------------------+
              |                    |
     RegisterModule()       InstallModuleAsync()
              |                    |
+--------------------------------------------------+
|              Fdp.ModuleHost                      |  <-- THIS PROJECT
|  ModuleHostKernel                                |
|  SystemScheduler / DependencyGraph               |
|  SnapshotProviders (GDB, SoD, Shared)            |
|  ModuleCircuitBreaker                            |
|  ITimeController                                 |
+--------------------------------------------------+
              |
       ISimulationView / SyncFrom()
              |
+--------------------------------------------------+
|              Fdp.Core                            |
|  EntityRepository (live world)                   |
|  EventAccumulator / FdpEventBus                  |
|  ComponentTypeRegistry / BitMask256              |
+--------------------------------------------------+
```

### Architectural Layer

`Fdp.ModuleHost` occupies the **framework orchestration layer**. It is not a domain module
itself; it is the runtime that executes domain modules. Its only dependency is `Fdp.Core`.

---

## Architecture

### High-Level Design Decisions

**1. Immutable execution topology (RCU pattern)**

The set of active modules and the system execution order are captured in a
`KernelExecutionTopology` object. This object is treated as immutable once published.
Hot-plug operations compile a brand-new topology on a background thread and perform a single
`Volatile.Write` swap on the main thread at a safe `BeforeSync` phase boundary. Zero
allocations occur on the 60 Hz hot path; the compile cost is entirely on a background thread.

**2. Three snapshot strategies**

Background modules cannot safely read the live ECS world while it is being mutated. Three
isolation strategies are provided:

| Strategy | Class | Usage |
|---|---|---|
| `Direct` | none (live `EntityRepository`) | Synchronous main-thread modules |
| `GDB` (Global Double Buffer) | `DoubleBufferProvider` | Frame-synced background — replica synced at the sync point |
| `SoD` (Snapshot-on-Demand) | `OnDemandProvider` | Async background — fresh snapshot taken at dispatch time |

When multiple background modules share the same strategy and frequency, their providers are
promoted to a **convoy** (`SharedSnapshotProvider` for SoD, shared `DoubleBufferProvider`
for GDB) so a single sync operation serves multiple modules.

**3. Deterministic system scheduling**

`SystemScheduler` performs a topological sort per `SystemPhase` using `[UpdateBefore]` and
`[UpdateAfter]` attributes from `Fdp.Core`. Circular dependencies throw
`CircularDependencyException` at initialization time, preventing silent ordering bugs.

**4. Reactive scheduling**

Modules can declare `WatchComponents` or `WatchEvents` to skip ticking entirely when neither
has changed, reducing CPU budget for idle subsystems.

**5. Circuit breaker per module**

Each module entry owns a `ModuleCircuitBreaker`. After `FailureThreshold` consecutive failures
the circuit opens and the module is skipped until `CircuitResetTimeoutMs` has elapsed, at which
point a single probe execution is allowed (`HalfOpen`). This isolates a faulty module from the
rest of the simulation.

> ⚠ **Corrected 2026-09-04 (`CE-189`).** The paragraph above described the ASYNC path only. The
> **synchronous** path caught its exception, wrote one line to stderr, and did nothing else — no
> `RecordFailure`, so the circuit never opened and `GetExecutionStats()` reported a healthy module
> however many times it threw. Measured consequence: `StatelessGizmoSystem` faulted on every frame of
> every editor run for an entire working session while the node kept answering healthy (`CE-188`).
> Both paths now route through one `ReportModuleFault` handler.
>
> ⛔ **Recording a sync failure does NOT skip the module.** The sync path has no `CanRun()` gate (the
> async path does, at the top of its dispatch), so opening the circuit is *reporting*, not execution
> control. That asymmetry is deliberate and load-bearing: closing it would change which modules tick.

**5b. Fail-fast is the DEFAULT — a module fault is fatal unless you opt out**

`FdpConfig.FailFastOnModuleException` makes `ReportModuleFault` **rethrow** with the original stack
instead of catching. The per-module catch exists so one faulty module cannot take down a distributed
simulation; that is right in production and exactly wrong while debugging, where a system throwing on
its first frame otherwise "runs" forever with every later system in its phase group silently skipped.

> 🔒 **User ruling, 2026-09-04, verbatim:** *"the fail fast should be on by default as we are still in
> a wild development phase, not even close to production."*

⇒ **the default is `true`.** The opt-out is the environment variable `FDP_FAIL_FAST` set to
`0` / `false` / `off` (case-insensitive on the latter two), which needs no rebuild. Nothing else reads it.

| | |
|---|---|
| **who opts out today** | `ResilienceIntegrationTests` — its *subject* is the catch path, so it turns the flag off in its constructor and restores it in `Dispose`. ⛔ Its type-level comment forbids "fixing" a red there by weakening the shipped default instead |
| **blast radius, measured** | flipping the default took `Fdp.ModuleHost.Tests` from **6 → 10 reds**; all 4 new ones were the `Resilience_*` family and the opt-out returned the project to **206 passed / 6 failed — exactly the baselined convoy+SoD six.** `Fdp.Toolkits.Tests` was unaffected at **2064/0** |
| ⚠ **testing the switch** | it is a **mutable process-global**, and xUnit runs test classes in **parallel** ⇒ a rail that asserts *"the shipped default is ON"* may **not** read the live property — it races the opt-out's constructor and fails for the wrong reason (measured: red in 2/2 runs). `Fdp.ModuleHost.Tests/ShippedDefaults.cs` snapshots it in a `[ModuleInitializer]`, before any test can run, and the rail asserts the snapshot |
| **live gate** | the editor booted on a real scenario (`hill-attack-close`), stepped ~290 frames to `simTime 7.59` with 8 entities and **zero module faults** — with fail-fast on by default and no env var set. A fault under this default is now a crash, so a green live run is part of the evidence, not a nicety |

⚠ **This is not "no exceptions are swallowed ever" yet.** It covers `Fdp.ModuleHost`'s two module-fault
paths only. `SystemScheduler`, the translators and the debug API still have their own `catch (Exception)`
sites; a sweep of those is open follow-up work.

**5c. Fault reporting is de-duplicated, because volume is a form of hiding**

A module that faults every frame used to print a full stack every frame. `CE-188` produced 8 000–16 000
identical lines per run and read as background noise for a whole session — the fault was never hidden,
it was *drowned*. The handler now reports the first occurrence of each distinct signature (exception
type + top frame) in full, counts repeats, and re-reports at powers of ten with the running total.

⚠ **This path is only reachable with fail-fast OFF** (§5b) — under the shipped default the first fault
is fatal, so there is no second occurrence to de-duplicate. It is the resilience mode's reporting, and
every report names `FDP_FAIL_FAST` so a reader who arrived here via the opt-out knows which mode they
are in and how to get back to a fatal one.

**6. Time-controller injection**

The kernel does not manage wall-clock time itself. An `ITimeController` must be injected before
`Initialize()`. This separates concerns: the toolkit layer can provide continuous, deterministic,
or stepped controllers without modifying the kernel.

### Constraints

- **Zero allocations on the hot path**: no `new`, no LINQ, no boxing during `UpdateInternal`.
  Lists are pre-allocated; topology reads use `Volatile.Read` with no locking.
- **Thread safety**: the live `EntityRepository` is only touched by the main thread.
  Background tasks always work through a leased snapshot view.
- **Ownership contract**: the kernel does not dispose registered `IEcsModule` instances.
  Only snapshot providers (and modules removed via `UninstallModuleAsync`) are disposed by
  the kernel.
- `unsafe` blocks are allowed (`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`) to enable direct
  memory access inherited from `Fdp.Core`.
- The project targets **net8.0** with nullable reference types and C# 12.

---

## ASCII Block Diagrams

### Diagram 1 — Internal Component Map

```
+----------------------------------------------------------------------+
|                         ModuleHostKernel                             |
|                                                                      |
|  _activeTopology (volatile ptr)                                      |
|  +---------------------------+                                       |
|  | KernelExecutionTopology   |                                       |
|  |  Modules: [ModuleEntry*]  |<----+                                 |
|  |  Scheduler: SystemScheduler    |                                  |
|  +---------------------------+    |                                  |
|                                   | RCU swap at BeforeSync           |
|  _pendingOperation (volatile)-----+                                  |
|  +---------------------------+                                       |
|  | PendingTopologyOperation  |                                       |
|  |  NewTopology              | <--- background compile thread        |
|  |  SwapCompletion (TCS)     |                                       |
|  |  DrainEntries             |                                       |
|  +---------------------------+                                       |
|                                                                      |
|  _drainingModules [ModuleEntry]  -- harvest loop                     |
|  _registeredGlobalSystems [IEcsModuleSystem]                         |
|  _snapshotPool (SnapshotPool)                                        |
|  _timeController (ITimeController)                                   |
|  _topologyChangeSemaphore (SemaphoreSlim 1,1)                        |
+----------------------------------------------------------------------+
          |                               |
          | SystemScheduler               | ModuleEntry
          v                               v
+---------------------+     +--------------------------------------+
| SystemScheduler     |     | ModuleEntry                          |
| _systemsByPhase     |     |  Module: IEcsModule                  |
| _sortedSystems      |     |  Provider: ISnapshotProvider         |
| _profileData        |     |  CircuitBreaker: ModuleCircuitBreaker|
| BuildExecutionOrders|     |  CurrentTask: Task?                  |
| ExecutePhase()      |     |  LeasedView / LeasedProvider         |
| ExecuteSystem()     |     |  RegisteredSystems / SimulationSystems|
+---------------------+     |  LifecycleState                      |
          |                  |  DrainCompletionSource               |
          | DependencyGraph  +--------------------------------------+
          v
+-------------------+
| DependencyGraph   |
| _nodes (HashSet)  |
| _edges (Dict)     |
| AddEdge()         |
| GetInDegree()     |
+-------------------+
```

### Diagram 2 — Module Lifecycle (State Machine + Frame Flow)

```
  RegisterModule()                InstallModuleAsync()
       |                                  |
       v                                  v
  [Loading] ----background compile---> [Loading]
       |                                  |
  Initialize()                   RCU swap (BeforeSync)
       |                                  |
       v                                  v
   [Ready] <--------------------------------+
       |
       | every frame
       v
  ShouldRunThisFrame()?
       |
  Yes  +--------> AcquireView()
       |               |
       |          (Sync / Async Task)
       |               |
       |          Module.Tick() + system execution
       |               |
       |          HarvestEntry()
       |               |
       |          ReleaseView()
       |
  UninstallModuleAsync() / Dispose()
       |
       v
  [Draining] -- harvest loop drains in-flight task
       |
  Background disposal worker (IDisposable.Dispose)
       |
       v
  [Disposed]
```

### Diagram 3 — Snapshot Provider Selection

```
  ExecutionPolicy.Strategy?
       |
       +-- Direct -----> live EntityRepository (main thread only)
       |
       +-- GDB ----------> Same mode/freq convoy members exist?
       |                         |
       |                    Yes: share DoubleBufferProvider
       |                    No:  new DoubleBufferProvider (exclusive)
       |                         |
       |                    UnionMask expanded?
       |                         Yes: replace provider for whole convoy
       |
       +-- SoD ----------> Same mode/freq convoy members exist?
                                 |
                            No:  new OnDemandProvider (exclusive)
                            Yes: SharedSnapshotProvider already in convoy?
                                   Yes: reuse (or expand mask)
                                   No:  promote convoy to SharedSnapshotProvider
```

### Diagram 4 — Per-Frame Execution Sequence

```
Update()
  |
  +-> ITimeController.Update() -> GlobalTime
  |
  +-> UpdateInternal(deltaTime, globalTime)
        |
        +-> _liveWorld.Tick()             [version increment]
        +-> SetSimulationTime / GlobalTime singleton
        |
        +-> ExecutePhase(Input)           [main thread]
        |
        +-> RCU swap (if _pendingOperation != null)
        |
        +-> ExecutePhase(BeforeSync)      [main thread]
        |
        +-> CommandBuffer.Playback()
        +-> _liveWorld.Bus.SwapBuffers()
        +-> EventAccumulator.CaptureFrame()
        +-> Provider.Update() per module   [sync point]
        |
        +-> HARVEST active module tasks (IsCompleted)
        +-> HARVEST draining modules
        |
        +-> DISPATCH (per module):
        |      ShouldRunThisFrame()?
        |         Yes -> AcquireView
        |               ExecuteModuleSafe (async) OR inline (sync)
        |               FrameSynced -> add to wait list
        |
        +-> Task.WaitAll(FrameSynced tasks)  [sync barrier]
        |
        +-> ExecutePhase(PostSimulation)  [main thread]
        +-> ExecutePhase(Export)          [main thread]
```

---

## Source Structure Analysis

### Namespaces

| Namespace | Location |
|---|---|
| `Fdp.ModuleHost` | Root — kernel and lifecycle types |
| `Fdp.ModuleHost.Abstractions` | Contracts, enums, attributes |
| `Fdp.ModuleHost.Scheduling` | Scheduler, dependency graph, system groups |
| `Fdp.ModuleHost.Providers` | Snapshot provider implementations |
| `Fdp.ModuleHost.Resilience` | Circuit breaker |
| `Fdp.ModuleHost.Time` | Time controller abstractions |
| `Fdp.ModuleHost.Diagnostics` | Diagnostics service and DTOs |

### File-by-File Responsibility

**Root**

| File | Class / Type | Responsibility |
|---|---|---|
| `ModuleHostKernel.cs` | `ModuleHostKernel` | Central orchestrator. Owns registration, initialization, per-frame execution loop, hot-plug API, provider allocation, and module harvesting. ~2000 LOC. |
| `KernelExecutionTopology.cs` | `KernelExecutionTopology` | Immutable snapshot of active modules and compiled system scheduler. The unit exchanged during RCU swaps. |
| `ModuleLifecycleState.cs` | `ModuleLifecycleState` | Enum: `Loading`, `Ready`, `Draining`, `Disposed`. |

**Abstractions**

| File | Class / Type | Responsibility |
|---|---|---|
| `IEcsModule.cs` | `IEcsModule` | Primary extension point. Declares `Name`, `Policy`, `RegisterSystems`, `Tick`, `WatchComponents`, `WatchEvents`, `GetRequiredComponents`. |
| `IEcsModuleSystem.cs` | `IEcsModuleSystem` | Single-method system interface: `Execute(view, deltaTime)`. |
| `ISystemRegistry.cs` | `ISystemRegistry` | Contract for registering systems during module init: `RegisterSystem<T>`, `RegisterManualSystem<T>`. |
| `ISystemGroup.cs` | `ISystemGroup` | Hierarchical group of systems with `Enabled` flag; derives from `IEcsModuleSystem`. |
| `ISnapshotProvider.cs` | `ISnapshotProvider`, `SnapshotProviderType` | AcquireView / ReleaseView / Update contract; `GDB`, `SoD`, `Shared` variants. |
| `IProfiledSystem.cs` | `IProfiledSystem` | Optional interface for adapter types to expose a clean display name to the diagnostics window. |
| `ExecutionPolicy.cs` | `ExecutionPolicy`, `RunMode`, `DataStrategy` | Value-type policy struct with factory methods: `Synchronous()`, `FastReplica()`, `SlowBackground(hz)`, `Custom()`. Fluent builder support. |
| `ModuleExecutionPolicy.cs` | `ModuleExecutionPolicy`, `ModuleMode`, `TriggerType` | Legacy policy struct kept for backward compatibility. |
| `SystemAttributes.cs` | `UpdateInPhaseAttribute` | Attribute that tags a system with a `SystemPhase`. |
| `AdvancedAttributes.cs` | `ExecutionPolicyAttribute`, `SnapshotPolicyAttribute`, `WatchEventsAttribute` | Additional class-level attributes for advanced scheduling and snapshot mode declaration. |
| `SystemPhase.cs` | `SystemPhase` | Enum of execution phases: `Input(1)`, `BeforeSync(2)`, `Simulation(10)`, `PostSimulation(20)`, `Export(40)`, `Manual(255)`. |

**Scheduling**

| File | Class | Responsibility |
|---|---|---|
| `SystemScheduler.cs` | `SystemScheduler` | Implements `ISystemRegistry`. Collects systems per phase, builds dependency graphs, performs topological sort, executes phases, and records per-system profiling data. |
| `DependencyGraph.cs` | `DependencyGraph` | Directed graph of `IEcsModuleSystem` nodes; supports topological sort via Kahn's algorithm. |
| `SystemProfileData.cs` | `SystemProfileData` | Rolling performance statistics per system: execution count, min/max/avg/last milliseconds, error count, recent sample ring-buffer (60 samples). |
| `TogglableSimulationGroup.cs` | `TogglableSimulationGroup` | `ISystemGroup` for the `Simulation` phase with an `Enabled` toggle. Used by replay to freeze simulation during playback. |
| `TogglablePostSimulationGroup.cs` | `TogglablePostSimulationGroup` | Same concept for `PostSimulation` phase (physics integration systems disabled during replay). |
| `TogglableInputGroup.cs` | `TogglableInputGroup` | Same concept for `Input` phase (network ingress disabled during replay). |
| `NetworkLifecycleSystemGroup.cs` | `NetworkLifecycleSystemGroup` | Non-`ISystemGroup` helper (direct execute pattern) grouping network lifecycle systems under a single enable gate. |

**Providers**

| File | Class | Responsibility |
|---|---|---|
| `SnapshotPool.cs` | `SnapshotPool` | Thread-safe `ConcurrentStack<EntityRepository>` with warm-up and `SoftClear`-on-return to eliminate GC pressure. |
| `DoubleBufferProvider.cs` | `DoubleBufferProvider` | GDB strategy. Keeps a persistent `EntityRepository` replica synced at the frame sync point via `SyncFrom`. Zero-copy acquire. |
| `OnDemandProvider.cs` | `OnDemandProvider` | SoD strategy. Pops from `ConcurrentStack`, syncs with `_componentMask`, returns on release. Time-travel guard resets event cursor on rollback. |
| `SharedSnapshotProvider.cs` | `SharedSnapshotProvider` | Convoy strategy for SoD. First reader in a group creates the snapshot; subsequent readers share it via ref-count. Pool return occurs when ref-count reaches zero. |

**Resilience**

| File | Class | Responsibility |
|---|---|---|
| `Resilience/ModuleCircuitBreaker.cs` | `ModuleCircuitBreaker`, `CircuitState` | Three-state circuit breaker (`Closed`, `Open`, `HalfOpen`). Thread-safe via `lock`. Configurable failure threshold and reset timeout. |

**Time**

| File | Interface | Responsibility |
|---|---|---|
| `Time/ITimeController.cs` | `ITimeController`, `TimeMode` | Contract for time management: `Update()`, `SetTimeScale()`, `GetTimeScale()`, `GetMode()`, `GetCurrentState()`, `SeedState()`. |
| `Time/ISteppableTimeController.cs` | `ISteppableTimeController` | Extends `ITimeController` with `Step(deltaTime)` for deterministic frame-by-frame control. |

**Diagnostics**

| File | Class / Interface | Responsibility |
|---|---|---|
| `Diagnostics/IArchitectureDiagnosticsService.cs` | `IArchitectureDiagnosticsService`, `ArchitectureSnapshotDto`, `ModuleDiagnosticsDto`, `SystemDiagnosticsRow`, `TranslatorDiagnosticsDto` | Headless diagnostics contract plus DTOs for modules, systems, and network translators. |
| `Diagnostics/ArchitectureDiagnosticsService.cs` | `ArchitectureDiagnosticsService` | Default implementation wrapping a kernel getter delegate; collects module diagnostics, system profiles, and translator rows via reflection. |
| `Diagnostics/EventHistoryCaptureSystem.cs` | `EventHistoryCaptureSystem` | `IEcsModuleSystem` in `PostSimulation` phase that snapshots `FdpEventBus` events into `IDiagnosticEventHistoryService` once per tick. |

### Design Patterns Employed

| Pattern | Where |
|---|---|
| Read-Copy-Update (RCU) | `KernelExecutionTopology` + `_pendingOperation` + `Volatile.Write` |
| Circuit Breaker | `ModuleCircuitBreaker` per `ModuleEntry` |
| Object Pool | `SnapshotPool` / `ConcurrentStack<EntityRepository>` |
| Strategy | `ISnapshotProvider` implementations (Direct / GDB / SoD) |
| Convoy / Shared Snapshot | `SharedSnapshotProvider` with reference counting |
| Template Method | `IEcsModule.Tick` + `RegisterSystems` dual-mode |
| Observer / Reactive | `WatchComponents`, `WatchEvents` on `IEcsModule` |
| Topology Compilation | `BuildTopology` → `KernelExecutionTopology` |
| Topological Sort (Kahn) | `DependencyGraph` + `SystemScheduler.TopologicalSort` |
| Command Buffer Playback | `EntityRepository._perThreadCommandBuffer.Playback(_liveWorld)` |

---

## Public API Reference

### `ModuleHostKernel` (class, sealed)

Central orchestrator. Created by application code with a live `EntityRepository` and
`EventAccumulator`. All module management goes through this class.

**Constructor**

```csharp
public ModuleHostKernel(EntityRepository liveWorld, EventAccumulator eventAccumulator)
```

Creates the kernel. Neither argument may be null.

**Setup methods (call before `Initialize`)**

| Signature | Description |
|---|---|
| `void SetTimeController(ITimeController controller)` | Injects the time controller. Required before `Initialize()`. |
| `void SetSchemaSetup(Action<EntityRepository> setup)` | Action called on each new snapshot `EntityRepository` to register component types. |
| `void SetTimeScale(float scale)` | Pre-`Initialize` time scale; applied to controller when it is injected. |
| `void RegisterModule(IEcsModule module, ISnapshotProvider? provider = null)` | Adds a module. Optional manual provider override. Must be called before `Initialize()`. |
| `void RegisterGlobalSystem<T>(T system) where T : IEcsModuleSystem` | Registers a main-thread global system. Phase must be one of `Input`, `BeforeSync`, `PostSimulation`, `Export`. |

**Lifecycle**

| Signature | Description |
|---|---|
| `void Initialize()` | Validates policies, registers component types, assigns providers, builds initial topology, and topologically sorts systems. Throws on misconfiguration. |
| `void Update()` | Advances the `ITimeController` and runs one full simulation frame. |
| `void StepFrame(float deltaTime)` | Requires `ISteppableTimeController`. Advances one manual frame without advancing wall clock. |
| `void Dispose()` | Waits up to 2 s for in-flight tasks; disposes all providers; disposes time controller; clears module lists. |

**Runtime hot-plug API**

| Signature | Description |
|---|---|
| `Task InstallModuleAsync(IEcsModule module)` | Installs a single module at runtime. Background compile + O(1) swap. Returns when module is live. |
| `Task UninstallModuleAsync(IEcsModule module)` | Removes a module at runtime. Returns only after full drain and disposal. |
| `Task InstallModulesAsync(IReadOnlyList<IEcsModule> modules)` | Atomically installs a batch of modules in one swap. |
| `Task UninstallModulesAsync(IReadOnlyList<IEcsModule> modules)` | Atomically removes a batch of modules. Returns after all are drained. |
| `bool IsModuleInstalled(IEcsModule module)` | Returns `true` if the module is in the active topology. |
| `ModuleLifecycleState? GetModuleLifecycleState(IEcsModule module)` | Returns the lifecycle state (including `Draining`) or `null` if unknown. |

**Time control**

| Signature | Description |
|---|---|
| `GlobalTime CurrentTime { get; }` | Current frame time as a `GlobalTime` struct. |
| `void SuspendGlobalTimePush()` | Prevents the kernel from writing `GlobalTime` to the ECS world (used during replay). |
| `void ResumeGlobalTimePush()` | Restores normal time propagation. |
| `void SwapTimeController(ITimeController newController)` | Replaces the time controller at runtime; transfers state and scale. |
| `ITimeController GetTimeController()` | Returns the active time controller. |

**Diagnostics**

| Signature | Description |
|---|---|
| `SystemScheduler SystemScheduler { get; }` | Access to the active scheduler (profiling, tests). |
| `IReadOnlyList<string> GetRegisteredModuleNames()` | Snapshot of all module names (any lifecycle state). |
| `IReadOnlyList<string> GetRegisteredModuleTypeNames()` | Snapshot of module type names. |
| `List<ModuleStats> GetExecutionStats()` | Execution counts and circuit states per module. Resets counters. |
| `IReadOnlyList<ModuleDiagnostics> GetModuleDiagnostics()` | Full diagnostics snapshot without resetting counters. |
| `string GetModuleNameForSystem(IEcsModuleSystem system)` | Reverse lookup: finds which module owns a given system instance. |

---

### `ModuleStats` (struct)

```csharp
public struct ModuleStats
{
    public string ModuleName;
    public int ExecutionCount;
    public CircuitState CircuitState;
    public int FailureCount;
}
```

Lightweight diagnostics value returned by `GetExecutionStats()`.

---

### `ModuleDiagnostics` (struct)

```csharp
public struct ModuleDiagnostics
{
    public string ModuleName;
    public string ModuleTypeName;
    public RunMode RunMode;
    public DataStrategy DataStrategy;
    public int TargetFrequencyHz;
    public ModuleLifecycleState LifecycleState;
    public CircuitState CircuitState;
    public int ExecutionCount;
    public int FailureCount;
}
```

Full per-module diagnostics snapshot returned by `GetModuleDiagnostics()`.

---

### `ModuleLifecycleState` (enum)

| Value | Meaning |
|---|---|
| `Loading` | Background compilation in progress; not yet dispatching. |
| `Ready` | Live in the active topology; receives ticks. |
| `Draining` | Unhooked from topology; waiting for in-flight tasks to complete. |
| `Disposed` | Fully drained and disposed. Kernel no longer holds a reference. |

---

### `KernelExecutionTopology` (class, sealed, internal)

Immutable snapshot of the kernel's execution state. Holds:

- `IReadOnlyList<ModuleHostKernel.ModuleEntry> Modules` — active module entries
- `SystemScheduler Scheduler` — compiled topological sort for this topology

Never mutated after construction; new instances are produced by `BuildTopology`.

---

### `IEcsModule` (interface)

The primary extension point for simulation logic.

| Member | Type | Description |
|---|---|---|
| `Name` | `string` | Human-readable name for diagnostics. Must be unique per kernel. |
| `Policy` | `ExecutionPolicy` | Execution mode, data strategy, frequency, timeout, and resilience parameters. |
| `RegisterSystems(ISystemRegistry)` | method | Called once during init to register sub-systems. Default: empty. |
| `Tick(ISimulationView, float)` | method | Per-frame custom logic. Called after all system phases complete. |
| `WatchComponents` | `IReadOnlyList<Type>?` | Optional reactive filter: skip tick unless these components changed. |
| `WatchEvents` | `IReadOnlyList<Type>?` | Optional reactive filter: skip tick unless these events fired. |
| `GetRequiredComponents()` | `IEnumerable<Type>?` | Snapshot mask hint: only sync these component types. `null` means all. |
| `Tier` | `[Obsolete]` | Legacy tier classification. Use `Policy.Mode` instead. |
| `UpdateFrequency` | `[Obsolete]` | Legacy frequency as frame divisor. Use `Policy.TargetFrequencyHz` instead. |

---

### `IEcsModuleSystem` (interface)

```csharp
public interface IEcsModuleSystem
{
    void Execute(ISimulationView view, float deltaTime);
}
```

Single-responsibility unit of simulation logic. Stateless by convention. Tagged with
`[UpdateInPhase]` and optionally `[UpdateBefore]` / `[UpdateAfter]` from `Fdp.Core`.

---

### `ISystemRegistry` (interface)

```csharp
public interface ISystemRegistry
{
    void RegisterSystem<T>(T system) where T : IEcsModuleSystem;
    IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem;
}
```

Passed to `IEcsModule.RegisterSystems`. `RegisterManualSystem` registers the system in the
`Manual` phase (tracked by the profiler but not auto-ticked; the module must call it explicitly
via the returned wrapper).

---

### `ISystemGroup` (interface)

```csharp
public interface ISystemGroup : IEcsModuleSystem
{
    string Name { get; }
    bool Enabled { get; }           // default: true
    IReadOnlyList<IEcsModuleSystem> GetSystems();
}
```

Hierarchical container. `SystemScheduler` recurses into groups when executing, profiling each
inner system individually.

---

### `ISnapshotProvider` (interface)

```csharp
public interface ISnapshotProvider
{
    SnapshotProviderType ProviderType { get; }
    ISimulationView AcquireView();
    void ReleaseView(ISimulationView view);
    void Update();
}
```

`AcquireView` / `ReleaseView` must always be paired. `Update` is called by the kernel at the
sync point (after `BeforeSync`, before dispatch).

**`SnapshotProviderType` enum:** `GDB`, `SoD`, `Shared`.

---

### `ExecutionPolicy` (struct)

| Member | Type | Description |
|---|---|---|
| `Mode` | `RunMode` | Threading model. |
| `Strategy` | `DataStrategy` | Snapshot strategy. |
| `TargetFrequencyHz` | `int` | 0 = every frame (60 Hz). |
| `MaxExpectedRuntimeMs` | `int` | Timeout for circuit breaker. |
| `FailureThreshold` | `int` | Consecutive failures before circuit opens. |
| `CircuitResetTimeoutMs` | `int` | Cooldown before `HalfOpen` probe. |

**Factory methods:**

```csharp
ExecutionPolicy.Synchronous()          // main thread, Direct, 60 Hz, 16 ms timeout
ExecutionPolicy.FastReplica()          // FrameSynced, GDB, 60 Hz, 15 ms timeout
ExecutionPolicy.SlowBackground(int hz) // Asynchronous, SoD, hz target
ExecutionPolicy.Custom()               // fully configurable via fluent builder
```

**Fluent builder:** `WithMode()`, `WithStrategy()`, `WithFrequency()`, `WithTimeout()`.

**Validation:** `Validate()` enforces that `Synchronous` requires `Direct` and `Direct` requires
`Synchronous`; frequency must be 0–60 Hz.

---

### `RunMode` (enum)

| Value | Threading | Main waits? |
|---|---|---|
| `Synchronous` | Main thread | N/A |
| `FrameSynced` | Background thread | Yes (`Task.WaitAll`) |
| `Asynchronous` | Background thread | No (fire-and-forget until harvested) |

---

### `DataStrategy` (enum)

| Value | Provider | Cost |
|---|---|---|
| `Direct` | None — live `EntityRepository` | Zero copy, main-thread only |
| `GDB` | `DoubleBufferProvider` | Persistent replica, synced once per frame |
| `SoD` | `OnDemandProvider` / `SharedSnapshotProvider` | Fresh copy per dispatch, pooled |

---

### `SystemPhase` (enum)

| Value | Int | Thread | Description |
|---|---|---|---|
| `Input` | 1 | Main | Hardware input, early frame processing |
| `BeforeSync` | 2 | Main | Pre-sync preparation; RCU swap applied here |
| `Simulation` | 10 | Background | Module dispatch (not executed for global systems) |
| `PostSimulation` | 20 | Main | Physics integration, coordinate transforms |
| `Export` | 40 | Main | Network send, recording, telemetry |
| `Manual` | 255 | N/A | Registered but never auto-ticked; module-driven |

---

### `SystemScheduler` (class)

```csharp
public class SystemScheduler : ISystemRegistry
```

| Method | Description |
|---|---|
| `RegisterSystem<T>(T system)` | Adds system; reads `[UpdateInPhase]`; creates `SystemProfileData` entry. |
| `RegisterManualSystem<T>(T system)` | Registers in `Manual` phase; returns a profiled wrapper. |
| `BuildExecutionOrders()` | Performs per-phase topological sort. Must be called after all registrations. Throws `CircularDependencyException` on cycles. |
| `ExecutePhase(SystemPhase, ISimulationView, float)` | Executes all systems in sorted order for a phase. Skips `Manual`. |
| `ExecuteSystem(IEcsModuleSystem, ISimulationView, float)` | (internal) Executes one system or group with profiling. |
| `GetAllProfileData()` | Returns `Dictionary<SystemPhase, List<(System, Profile)>>` for diagnostics UI. |
| `GetAllSystems()` | Enumerates all registered systems across all phases. |

---

### `SystemProfileData` (class)

Per-system rolling statistics. Properties: `SystemName`, `ExecutionCount`, `TotalMs`,
`AverageMs`, `MinMs`, `MaxMs`, `LastMs`, `ErrorCount`, `LastError`,
`GetRecentAverageMs()` (last 60 samples), `Reset()`.

---

### `TogglableSimulationGroup` / `TogglablePostSimulationGroup` / `TogglableInputGroup` (sealed classes)

All three implement `ISystemGroup` and carry `[UpdateInPhase(phase)]`. They wrap an array of
inner systems behind an `Enabled` flag. When `false`, `Execute` is a no-op. The replay
subsystem uses these to freeze specific phases during playback without re-registering systems.

| Class | Phase |
|---|---|
| `TogglableInputGroup` | `Input` |
| `TogglableSimulationGroup` | `Simulation` |
| `TogglablePostSimulationGroup` | `PostSimulation` |

Constructor: `(string name, params IEcsModuleSystem[] innerSystems)` or
`(string name, IReadOnlyList<IEcsModuleSystem> innerSystems)`.

---

### `NetworkLifecycleSystemGroup` (sealed class)

Non-`ISystemGroup` (does not implement the interface). Wraps three network lifecycle systems
behind a single `Enabled` gate. Disabled during replay (CGF1-S0304) to suppress lifecycle
transitions. Executed manually by the owning module's `Tick` or registered system.

---

### `SnapshotPool` (class)

```csharp
public class SnapshotPool(Action<EntityRepository>? schemaSetup, int warmupCount = 0)
```

Thread-safe pool using `ConcurrentStack<EntityRepository>`. `Get()` pops or creates; `Return()`
calls `SoftClear()` before pushing. Exposes `PooledCount` for monitoring.

---

### `DoubleBufferProvider` (sealed class)

GDB strategy. Persistent `_replica` synced at the sync point. Zero-copy `AcquireView` returns
the replica directly. `ReleaseView` is a no-op. `Dispose` calls `_replica.Dispose()`.

---

### `OnDemandProvider` (sealed class)

SoD strategy. Pops from `ConcurrentStack`, applies `_componentMask` during `SyncFrom`, flushes
events via `EventAccumulator.FlushToReplica`. Returns snapshot to stack on `ReleaseView` after
`SoftClear`. Implements time-travel guard (`_lastSeenTick` reset if `GlobalVersion` goes backward).

---

### `SharedSnapshotProvider` (sealed class)

Convoy SoD strategy. First `AcquireView` caller creates the snapshot; subsequent callers
increment `_activeReaders` and receive the same object. Last `ReleaseView` returns snapshot
to the pool. Exposes `UnionMask` and `ActiveReaders` internally.

---

### `ModuleCircuitBreaker` (class)

```csharp
public class ModuleCircuitBreaker(int failureThreshold = 3, int resetTimeoutMs = 5000)
```

Thread-safe circuit breaker. `CanRun()` returns `false` when `Open`. Transitions:
`Closed` → `Open` (on threshold breach), `Open` → `HalfOpen` (after timeout), `HalfOpen`
→ `Closed` (on success) or back to `Open` (on failure).

**`CircuitState` enum:** `Closed`, `Open`, `HalfOpen`.

---

### `ITimeController` (interface)

| Member | Description |
|---|---|
| `GlobalTime Update()` | Advance clock; return frame time data. Called once per frame. |
| `void SetTimeScale(float)` | Change simulation speed. |
| `float GetTimeScale()` | Current time scale. |
| `TimeMode GetMode()` | Returns `Continuous` or `Deterministic`. |
| `GlobalTime GetCurrentState()` | Snapshot of current time for transfer or save. |
| `void SeedState(GlobalTime)` | Initialize from a saved state. |

Extends `IDisposable`.

**`TimeMode` enum:** `Continuous` (PLL-based, real-time), `Deterministic` (lockstep via ACKs).

---

### `ISteppableTimeController` (interface)

Extends `ITimeController` with:

```csharp
GlobalTime Step(float deltaTime);
```

Returns a `GlobalTime` for a single manual step. Required by `ModuleHostKernel.StepFrame`.

---

### `IArchitectureDiagnosticsService` (interface)

```csharp
public interface IArchitectureDiagnosticsService
{
    ArchitectureSnapshotDto GetSnapshot();
}
```

Returns a fresh `ArchitectureSnapshotDto` containing `Modules`, `Systems`, and `Translators`
lists. Allocates on each call; intended for UI frame-rate polling only.

**DTOs:** `ModuleDiagnosticsDto`, `SystemDiagnosticsRow`, `TranslatorDiagnosticsDto`,
`ArchitectureSnapshotDto`.

---

### `ArchitectureDiagnosticsService` (sealed class)

Default implementation. Accepts `Func<ModuleHostKernel?>` (lazy kernel getter) or a direct
`ModuleHostKernel` reference. Uses reflection to collect network translator data from systems
that expose a `Translators` property of type `IEnumerable<INetworkTranslator>`.

---

### `EventHistoryCaptureSystem` (sealed class)

`IEcsModuleSystem` registered in `PostSimulation`. Captures `FdpEventBus` events into
`IDiagnosticEventHistoryService` each tick, labelled with a caller-supplied provider name.

---

### `IProfiledSystem` (interface)

```csharp
public interface IProfiledSystem
{
    string ProfileName { get; }
}
```

Optional interface for adapter/wrapper types so the diagnostics window shows a human-readable
name instead of the generic adapter class name.

---

### `ModuleExecutionPolicy` (struct, legacy)

```csharp
public struct ModuleExecutionPolicy
{
    public ModuleMode Mode;
    public TriggerType Trigger;
    public int IntervalMs;
    public Type TriggerArg;
    ...
}
```

Kept for backward compatibility. Prefer `ExecutionPolicy`. Static factories:
`DefaultFast`, `DefaultSlow`, `OnEvent<T>()`, `OnComponentChange<T>()`, `FixedInterval(ms)`.

---

## Dependencies

### Project References

| Project | Role |
|---|---|
| `Fdp.Core` | ECS kernel: `EntityRepository`, `EventAccumulator`, `FdpEventBus`, `GlobalTime`, `BitMask256`, `ComponentTypeRegistry`, `UpdateBeforeAttribute`, `UpdateAfterAttribute`, logging. |

### NuGet PackageReferences

None. All runtime dependencies are covered by the `Fdp.Core` project reference and the .NET 8
BCL (BCL types used: `Task`, `SemaphoreSlim`, `ConcurrentStack<T>`, `Volatile`,
`Interlocked`, `CancellationTokenSource`).

### `InternalsVisibleTo`

| Assembly | Purpose |
|---|---|
| `Fdp.ModuleHost.Tests` | Unit / integration tests for the module host |
| `Fdp.Core.Tests` | ECS tests that may exercise module host internals |

---

## Usage Examples

### Example 1 — Basic Module Setup (System-Based Pattern)

```csharp
// 1. Define a system
[UpdateInPhase(SystemPhase.Simulation)]
public class VelocityIntegrationSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        var query = view.Query().With<Position>().With<Velocity>().Build();
        foreach (var entity in query)
        {
            ref var pos = ref view.GetComponentRW<Position>(entity);
            var vel = view.GetComponentRO<Velocity>(entity);
            pos.X += vel.X * deltaTime;
            pos.Y += vel.Y * deltaTime;
        }
    }
}

// 2. Define a module
public class PhysicsModule : IEcsModule
{
    public string Name => "Physics";
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(30); // 30 Hz async

    public IEnumerable<Type>? GetRequiredComponents() => new[]
    {
        typeof(Position),
        typeof(Velocity)
    };

    public void RegisterSystems(ISystemRegistry registry)
    {
        registry.RegisterSystem(new VelocityIntegrationSystem());
    }

    public void Tick(ISimulationView view, float deltaTime)
    {
        // Empty — systems handle all logic
    }
}

// 3. Wire up the kernel
var world = new EntityRepository();
world.RegisterComponent<Position>();
world.RegisterComponent<Velocity>();

var accumulator = new EventAccumulator();
var kernel = new ModuleHostKernel(world, accumulator);

kernel.SetTimeController(new MyTimeController());
kernel.SetSchemaSetup(repo =>
{
    repo.RegisterComponent<Position>();
    repo.RegisterComponent<Velocity>();
});
kernel.RegisterModule(new PhysicsModule());
kernel.Initialize();

// 4. Main loop
while (running)
{
    kernel.Update(); // drives time + executes all phases + dispatches modules
}
```

---

### Example 2 — Global System with Phase Control and Reactive Module

```csharp
// Global system runs every frame on the main thread in the Export phase
[UpdateInPhase(SystemPhase.Export)]
public class TelemetryExportSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        // send telemetry over network
    }
}

// Module that only re-runs when Health changes (reactive scheduling)
public class HealthMonitorModule : IEcsModule
{
    public string Name => "HealthMonitor";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public IReadOnlyList<Type>? WatchComponents => new[] { typeof(Health) };

    public void Tick(ISimulationView view, float deltaTime)
    {
        // Only executed when Health component has changed
        var query = view.Query().With<Health>().Build();
        foreach (var entity in query)
        {
            var health = view.GetComponentRO<Health>(entity);
            if (health.Current <= 0)
            {
                // queue destruction
            }
        }
    }
}

var kernel = new ModuleHostKernel(world, accumulator);
kernel.SetTimeController(myTimeController);
kernel.RegisterGlobalSystem(new TelemetryExportSystem());
kernel.RegisterModule(new HealthMonitorModule());
kernel.Initialize();
```

---

### Example 3 — Hot-Plug: Install and Uninstall at Runtime

```csharp
// Kernel is already running at 60 Hz
using var kernel = new ModuleHostKernel(world, accumulator);
kernel.SetTimeController(myTimeController);
kernel.Initialize();

// ... simulation is running ...

// Install a new AI module at runtime (non-blocking)
var aiModule = new AiPathfindingModule();
await kernel.InstallModuleAsync(aiModule);
// aiModule is now live — ticking on background threads

// Later, swap it out cleanly
await kernel.UninstallModuleAsync(aiModule);
// Guaranteed: all in-flight AI tasks have completed,
// all snapshot views are released, memory is freed.

// Or install/uninstall multiple modules atomically
var newModules = new IEcsModule[] { new RadarModule(), new EcmModule() };
await kernel.InstallModulesAsync(newModules);
// Both become live in the same frame — no torn state
```

---

### Example 4 — Toggling System Groups During Replay

```csharp
// During module setup, wrap physics systems in a togglable group
public class PhysicsModule : IEcsModule
{
    private TogglablePostSimulationGroup? _physicsGroup;

    public string Name => "Physics";
    public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

    public void RegisterSystems(ISystemRegistry registry)
    {
        _physicsGroup = new TogglablePostSimulationGroup(
            "PhysicsIntegration",
            new BallisticsSystem(),
            new LinearKinematicsSystem()
        );
        registry.RegisterSystem(_physicsGroup);
    }

    public void Tick(ISimulationView view, float deltaTime) { }

    // Called by ReplayModule when entering replay mode
    public void PrepareReplay()  => _physicsGroup!.Enabled = false;
    public void FinalizeReplay() => _physicsGroup!.Enabled = true;
}
```

---

## Best Practices

### Thread Safety

- **Never read or write `_liveWorld` from a module's `Tick` or system's `Execute` method** when
  the module uses `GDB` or `SoD` strategy. The view passed to `Execute` is a snapshot;
  mutations via `CommandBuffer` are safe because they are committed by `HarvestEntry` on the
  main thread.
- Synchronous (`Direct`) modules may write to the live world directly but must complete within
  the frame budget (default 16 ms) or the simulation will stall.
- `ISnapshotProvider.AcquireView` and `ReleaseView` must always be paired. A leaked view in
  a `SoD` convoy will prevent the snapshot from returning to the pool and eventually exhausts
  the pool.
- `ModuleCircuitBreaker` is internally synchronized with a `lock`; access from multiple threads
  (timer thread vs harvest thread) is safe.

### Performance

- Use `GetRequiredComponents()` on every non-trivial background module to reduce snapshot size.
  A 5-component filter on a 100-component world cuts snapshot time and memory by ~95%.
- Prefer `ExecutionPolicy.SlowBackground` over `FastReplica` for anything that can tolerate
  one frame of latency. `FastReplica` (`RunMode.FrameSynced`) adds a `Task.WaitAll` barrier
  every frame.
- Avoid LINQ inside `IEcsModuleSystem.Execute`. Use the `ISimulationView` query API directly.
- Pre-warm the `SnapshotPool` by setting `warmupCount > 0` to avoid first-frame allocations.
- Do not call `GetRegisteredModuleNames()`, `GetModuleDiagnostics()`, or
  `GetExecutionStats()` on the hot path — they allocate `List<T>` and do LINQ internally.

### Common Pitfalls

- **Missing `[UpdateInPhase]` attribute on a system**: `SystemScheduler.RegisterSystem` throws
  `InvalidOperationException` at initialization time. Every `IEcsModuleSystem` must declare its
  phase.
- **Registering a `Simulation`-phase system as global**: `RegisterGlobalSystem` validates phases
  and throws if a `Simulation`-phase system is passed. Global systems only run in
  `Input`, `BeforeSync`, `PostSimulation`, `Export`.
- **Calling `RegisterModule` after `Initialize`**: throws `InvalidOperationException`. Use
  `InstallModuleAsync` for runtime hot-plug.
- **Circular system dependencies**: `BuildExecutionOrders` throws `CircularDependencyException`.
  Always run tests after adding `[UpdateBefore]` / `[UpdateAfter]` constraints.
- **Not calling `SetTimeController` before `Initialize`**: kernel throws
  `InvalidOperationException("TimeController not set.")`.
- **Disposing a module before the kernel**: the kernel holds a `ModuleEntry` reference. Dispose
  the kernel first (it waits for tasks), then dispose the module. The LIFO `using` pattern
  achieves this naturally when both are declared in the same scope.
- **`DataStrategy.Direct` with non-`Synchronous` mode**: `ExecutionPolicy.Validate()` throws.
  Background modules must use `GDB` or `SoD`.

---

## Related Projects

### Direct Dependency

| Project | Relationship |
|---|---|
| `Fdp.Core` | The ECS kernel. Provides `EntityRepository`, `EventAccumulator`, `GlobalTime`, `BitMask256`, logging, and ordering attributes. |

### Known Consumers (projects that reference `Fdp.ModuleHost`)

| Project | Role |
|---|---|
| `Fdp.ModuleHost.Tests` | Unit and integration tests for this project. |
| `Fdp.ModuleHost.Benchmarks` | Performance benchmarks. |
| `Fdp.Presentation` | Application layer; creates the `ModuleHostKernel` and registers domain modules. |
| Domain modules (`Fdp.Examples.*`, `Hrot.*`, network modules) | Implement `IEcsModule` and `IEcsModuleSystem` to register their logic with the kernel. |
| `Fdp.Toolkits` / `FDP.Toolkit.*` | Provide `ITimeController` implementations and toolkit-level modules. |
| `Fdp.Network.Cyclone` | Registers network systems (e.g. lifecycle, gateway) via `IEcsModule.RegisterSystems`. |
