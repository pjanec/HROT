# EyesAndMuscle Design

> **Scope of this document**
> Four phases that must all be completed in this workstream:
> 1. **Phase 1 — DRY Initialization** — shared infrastructure for bootstrapping any Hrot node
> 2. **Phase 2 — NedReplicationModule** — single `IEcsModule` that co-locates NED translators with their required smoothing/DR systems
> 3. **Phase 3 — EyesAndMuscle Subsystem** — a combined Muscle+Eyes `ISubsystem` that proves the SoD async-module pattern
> 4. **Phase 4 — Migrate Existing Subsystems** — apply the new builders and `NedReplicationModule` universally to `SimHostApp`, `IgApplication`, and `CgfSubsystem`
>
> **Not in scope:** Stride engine integration (postponed). Stride is referenced here for context only.
>
> **Why Phase 4 must be in the same pass:** Building `HrotNodeBuilder` and only applying it to `EyesAndMuscle` would temporarily fragment the architecture — the codebase would have two entirely different node-boot paths (the new clean builder alongside ~300 lines of legacy boilerplate in `SimHostApp.OnLoad`, `IgApplication.InitializeEmbedded`, and `CgfSubsystem`). The migration must be universal to fulfil the DRY promise.

---

## Background: Why these three things belong together

### The problem: overlapping initialization boilerplate

Every Hrot node (`SimHostApp`, `IgApplication`, and future nodes) bootstraps the same infrastructure independently:
- `EntityRepository` + `ModuleHostKernel` + `EventAccumulator`
- `FdpEventBus` + `TimeControllerFactory` (SlaveSyncController for slave nodes)
- `DdsParticipant` + sender tracking (`HrotEnvironment.CreateParticipant`)
- `ClusterSlave` + `NodeOpSlaveTranslator` + standard handler set (Preview, Prefetch, Archive, Live, Replay)
- `NetworkEntityMap` + `DdsIdAllocator`

`SimHostApp.OnLoad` currently contains ~300 lines of this setup. `IgApplication` duplicates most of it. Every future standalone node (EyesAndMuscle, future Stride node) would copy the same code.

### The problem: fragmented replication modules

Currently, the NED network translators (e.g., `KinematicTranslatorPack`, `EntityStatesIngressPack`) and the simulation systems that depend on their ECS components (e.g., `DeadReckoningSyncSystem`) are registered separately and independently. This means:
- Swapping or adding a data format (e.g., replacing NED with BDC) requires knowing which systems to replace alongside the translators.
- The relationship between "these translators write `NetworkTransform` / `NetworkVelocity`" and "this system reads those components for smoothing" is implicit and fragile.

### Why NedReplicationModule is the solution

A `NedReplicationModule : IEcsModule` bundles together:
1. The role-appropriate NED translators (ingress + egress)
2. The ECS systems that are architecturally coupled to those specific NED components (dead-reckoning, transform sync, ghost lifecycle)

Because both live in one `IEcsModule`, replacing the data format later (BDC, or Stride) means replacing one module, not hunting for interdependent registrations scattered across `SimHostApp.OnLoad`.

### Why EyesAndMuscle first, Stride later

EyesAndMuscle is a **tracer bullet**: it proves the Snapshot-on-Demand (SoD) async-module pattern, the `NedReplicationModule`, and the DRY builder work correctly — all in pure C#, without the complexity of a 3D engine. When Stride integration begins, these building blocks are already stress-tested.

---

## Architecture overview

```
┌─────────────────────────────────────────────────────────────────┐
│ SubsystemOrchestrator                                           │
│  ┌──────────────────┐  ┌────────────────┐  ┌─────────────────┐ │
│  │ OrchestratorSubsystem │ CgfSubsystem  │ │EyesAndMuscle   │ │
│  └──────────────────┘  └────────────────┘  │   Subsystem    │ │
│                                            └────────┬────────┘ │
└────────────────────────────────────────────────────┼───────────┘
                                                     │ owns
                    ┌────────────────────────────────▼──────┐
                    │        ModuleHostKernel (60 Hz)        │
                    │                                        │
                    │  ┌─────────────────────────────────┐  │
                    │  │  NedReplicationModule           │  │
                    │  │  (translators + DR/smoothing    │  │
                    │  │   + ghost lifecycle systems)    │  │
                    │  └─────────────────────────────────┘  │
                    │  ┌───────────────────────────────┐    │
                    │  │  SimHostCoreLogicPack (Muscle) │    │
                    │  │  GroundKinematics, Combat,     │    │
                    │  │  ActionDispatch, Damage        │    │
                    │  └───────────────────────────────┘    │
                    │  ┌───────────────────────────────┐    │
                    │  │  IG Presentation Modules       │    │
                    │  │  StyleResolution, MapLayer,    │    │
                    │  │  EventEffect, Culling          │    │
                    │  └───────────────────────────────┘    │
                    │  ┌────────────────────────────────┐   │
                    │  │  EyesAndMuscleModule (async)   │   │
                    │  │  Policy: SoD, Asynchronous     │   │
                    │  │  Eyes: reads snapshot          │   │
                    │  │  Muscle: writes command buffer │   │
                    │  └────────────────────────────────┘   │
                    │                                        │
                    │        EntityRepository (ECS)          │
                    └────────────────────────────────────────┘
```

---

## Phase 1 — DRY Initialization Infrastructure

### Goal
Extract the repeated Hrot node bootstrap sequence into reusable building blocks so that each new subsystem (EyesAndMuscle today, Stride tomorrow) needs only a handful of lines to stand up the full infrastructure stack.

### Architectural decisions

**Separation of concerns:** The initialization is split into two strict layers, mirroring the existing FDP/Hrot project boundary:

| Layer | Class | Location | Knows about |
|---|---|---|---|
| Generic engine | `FdpKernelBuilder` | `FDP.Framework.Runner` or `Hrot.Common` | `EntityRepository`, `ModuleHostKernel`, `FdpEventBus`, `TimeControllerFactory` — no DDS, no NED |
| Hrot application | `HrotNodeBuilder` | `Hrot.ClusterRunner` | `DdsParticipant`, `NetworkEntityMap`, `DdsIdAllocator`, `ClusterSlave`, `NodeOpSlaveTranslator`, NED orchestration message types |

**Transient builders:** Both builders are single-use. `Build()` produces an immutable `HrotNodeContext` (a record). The builder is then discarded. The subsystem retains the `HrotNodeContext` and the individual `IEcsModule` references it needs for runtime hot-swap operations.

**Subsystem as state tracker:** Each subsystem must store the `IEcsModule` instances it registered with the kernel (e.g., `_nedReplicationModule`, `_simLogicModule`) as private fields. This is the only mechanism by which `SubsystemOrchestrator` can later request a role change — the subsystem passes those stored references directly to `_kernel.UninstallModulesAsync(new[] { _nedReplicationModule })`. Without retained references, the kernel cannot determine which instances to drain and dispose.

**`HrotNodeContext` record:** The output of `HrotNodeBuilder.Build()`. Contains:
- `EntityRepository World`
- `ModuleHostKernel Kernel`
- `DdsParticipant Participant`
- `FdpEventBus EventBus`
- `NetworkEntityMap EntityMap`
- `ClusterSlave ClusterSlave`
- `NodeOpSlaveTranslator? SlaveTranslator` (null only in headless tests without DDS)
- `IReadOnlyList<IEcsModule> BaseModules` — the infrastructure modules created by the builder (e.g., `EntityLifecycleModule`); retained so the subsystem can pass them to `_kernel.UninstallModulesAsync` during role changes without having to track those references separately

### What the DRY builder covers (vs. what stays per-subsystem)

**Shared (moved into builders):**
- `EntityRepository` + `ModuleHostKernel` + `EventAccumulator`
- `FdpEventBus` + `TimeControllerFactory` → `SlaveSyncController`
- `DdsParticipant` + sender tracking
- `DdsIdAllocator`
- `NetworkEntityMap`
- `ClusterSlave` construction + `NodeOpSlaveTranslator` wiring
- Standard `ClusterSlave` handlers: `ReferencePreviewHandler`, `ReferencePrefetchHandler`, `ReferenceArchiveHandler`, `ReferenceLiveLoadHandler` — wired **inline** inside `HrotNodeBuilder` (see constraint 9; `NodeBootstrapper.BuildOrchestration` must NOT be called)

**Stays per-subsystem (NOT shared):**
- Component registration (`RegisterSimComponents`, `RegisterIgComponents`)
- Behavior registry (domain-specific)
- Road network loading
- Scenario serializer building (depends on domain component set)
- `CheckpointIOWorker` (only Brain/AllInOne roles need it)
- Replay/Edit/Episode handlers (require the scenario serializer)

### Usage pattern (after this phase)

```csharp
// In EyesAndMuscleSubsystem.Initialize()
_context = new HrotNodeBuilder(config)
    .WithRole("EyesAndMuscle", NodeRole.MuscleGround | NodeRole.ImageGenerator)
    .Build();
// _context.Kernel, _context.World, _context.EntityMap, etc. are all ready
```

The builders are validated first through EyesAndMuscle (Phase 3), then applied universally to `SimHostApp`, `IgApplication`, and `CgfSubsystem` in Phase 4.

---

## Phase 2 — NedReplicationModule

### Goal
Replace the fragmented, individually-registered translator packs and their associated ECS systems with a single `NedReplicationModule : IEcsModule`. This module is the Anti-Corruption Layer between the NED DDS network and the FDP ECS world.

### What it encapsulates

The `NedReplicationModule` bundles three categories that are architecturally inseparable:

```
NedReplicationModule
│
├── Translators (role-filtered)
│   ├── SharedTranslatorPack (ALL roles)           — entity lifecycle ingress/egress
│   ├── KinematicTranslatorPack (Muscle)           — NavIntent ingress, WorldPos egress
│   ├── CognitiveTranslatorPack (Brain)            — cognitive state ingress/egress
│   └── EntityStatesIngressPack (ImageGenerator)   — visual state ingress
│
├── Replication systems (role-filtered within)
│   ├── GhostCreationSystem          (ALL roles)   — creates replica entities on ingress
│   ├── CycloneNetworkCleanupSystem  (ALL roles)   — fires DDS Dispose when entity destroyed locally
│   ├── DisposalMonitoringSystem     (ALL roles)   — cleans up NetworkEntityMap on entity removal
│   ├── SmartEgressSystem   (Muscle + Brain)       — suppresses redundant egress packets
│   └── DeadReckoningSyncSystem      (ImageGenerator) — applies NED-specific interpolation/DR
│       (currently in Hrot.IG/Systems — moved here or made accessible)
│
└── Network infrastructure
    ├── CycloneNetworkModule registration
    └── Standard NetworkLifecycleSystemGroup(GhostCreationSystem)
```

### Why bundling is necessary

`DeadReckoningSyncSystem` reads `NetworkTransform` and `NetworkVelocity` components that are **written exclusively by NED translators**. If you swap out the NED translators for a different data model, `DeadReckoningSyncSystem` must also be swapped — otherwise it operates on stale or missing data. By co-locating them in one `IEcsModule`:
- The dependency is explicit and enforced by construction.
- A future `BdcReplicationModule` will contain its own `BdcDeadReckoningSystem` and BDC translators, with zero risk of component-system mismatch.

### Granularity by `NodeRole`

| Role flag | Translators registered | Systems registered |
|---|---|---|
| `MuscleGround` | SharedTranslatorPack + KinematicTranslatorPack | GhostCreationSystem, SmartEgressSystem |
| `ImageGenerator` | SharedTranslatorPack + EntityStatesIngressPack | GhostCreationSystem, DeadReckoningSyncSystem (`driveFromNetwork: true`) |
| `Brain` | SharedTranslatorPack + CognitiveTranslatorPack | GhostCreationSystem, SmartEgressSystem |
| Combined `MuscleGround \| ImageGenerator` | Union of translators above | GhostCreationSystem, SmartEgressSystem, DeadReckoningSyncSystem (`driveFromNetwork: false`) |

> **All roles also register:** `CycloneNetworkCleanupSystem(translators)` and `DisposalMonitoringSystem(entityMap)`. These ensure that when an entity is destroyed in the ECS world, a DDS `Dispose` signal is fired and the `NetworkEntityMap` entry is cleaned up. They are part of the ACL contract for every role.

### Combined-role collision guard (`driveFromNetwork` flag)

When `NedReplicationModule` is created with `NodeRole.MuscleGround | NodeRole.ImageGenerator` (the `EyesAndMuscle` case), two things that superficially conflict are active simultaneously:

- **Muscle:** The node computes authoritative physics locally and publishes `WorldPos` egress packets via `GeoSpatialEgressTranslator`.
- **Eyes:** The node also receives `WorldPos` ingress packets from other nodes and must smooth the remote entities' visual positions via `DeadReckoningSyncSystem`.

The collision risk: if `DeadReckoningSyncSystem` runs unconditionally, it will attempt to overwrite the `SimTransform` of locally-simulated entities with stale or dead-reckoned values, fighting the `GroundKinematicsModule`.

**Resolution:** `DeadReckoningSyncSystem` is constructed with `driveFromNetwork: false` in the combined role. In this mode, it filters its entity query to only process entities where `NetworkIdentity.IsGhost == true` (i.e., remote replica entities created by `GhostCreationSystem` from incoming DDS packets). Locally-owned entities (`IsGhost == false`) are skipped entirely. The `SmartEgressSystem` independently tracks dirty state only for locally-simulated entities and suppresses redundant egress packets.

This means:
- `driveFromNetwork: true` → all entities with `NetworkTransform` are smoothed (pure IG node, no local physics).
- `driveFromNetwork: false` → only ghost entities are smoothed (combined Muscle+IG node, local physics is authoritative for owned entities).

### Constructor

```csharp
public NedReplicationModule(
    DdsParticipant participant,
    NodeRole role,
    NetworkEntityMap entityMap,
    IGeographicTransform geoTransform,
    FdpEventBus eventBus,
    int localNodeId)
```

### Project home

`NedReplicationModule` lives in `Hrot.ClusterRunner` (in a new `Replication/` subfolder). This is the composition tier that already references both `Hrot.SimHost` (kinematic translators) and `Hrot.IG` (DR system). It is the right place before a potential future extraction to a dedicated shared project.

---

## Phase 3 — EyesAndMuscle Subsystem

### Goal
Implement a combined Muscle+Eyes `ISubsystem` that:
1. Runs both the physics simulation (SimHost muscle logic packs) and 2D presentation (IG visualization) in a single process.
2. Uses the Phase 1 `HrotNodeBuilder` and Phase 2 `NedReplicationModule`.
3. Creates a dedicated `EyesAndMuscleModule : IEcsModule` running asynchronously via the Snapshot-on-Demand (SoD) policy — the exact pattern the future Stride integration will use.

### Why one combined subsystem

In the current architecture, SimHost (Muscle) and IG (Eyes) are separate subsystems communicating via DDS. For development, testing, and Stride pre-integration, it is useful to have a single subsystem that:
- Owns one local ECS world with both muscle and presentation logic running against it.
- Validates that SoD async modules can safely operate alongside synchronous logic packs on the same kernel.
- Provides a natural stepping stone: when Stride arrives, the `EyesAndMuscleModule` is replaced by a `StrideEcsModule`, while NedReplicationModule, HrotNodeBuilder, and the initialization remain unchanged.

### Module composition

| Module | Type | Policy | Responsibility |
|---|---|---|---|
| `NedReplicationModule` | `IEcsModule` | Synchronous | DDS ↔ ECS translation, ghost lifecycle, DR smoothing |
| `SimHostCoreLogicPack` (Muscle subset) | `IEcsModule` | Synchronous | GroundKinematics, Combat, ActionDispatch, DamageAssessment |
| IG presentation modules | `IEcsModule` | Synchronous | StyleResolution, MapLayer, EventEffect, HistoryTrail, Culling |
| `EyesAndMuscleModule` (new) | `IEcsModule` | **Asynchronous, SoD** | PoC bridge: reads ECS snapshot (Eyes), writes command buffer (Muscle) |

### EyesAndMuscleModule — SoD async design

This is the conceptual centerpiece of the PoC. It runs on a background thread and receives an immutable `ISimulationView` (SoD snapshot) each tick.

**Eyes (reads):** Iterates entities with `SimTransform` to confirm visual positions are propagated (in the PoC: logs/asserts; in Stride: updates 3D scene graph).

**Muscle (writes):** Reads `NavigationIntent` from the snapshot; applies simplified physics; writes the updated `SimTransform` back via `IEntityCommandBuffer`. The FDP kernel merges the command buffer into the live ECS deterministically at the end of the tick.

**Role-driven behavior:** The module constructor accepts `NodeRole`. When the role includes `ImageGenerator` only, the Muscle write path is skipped. This is the same knob that the future Stride integration will use to select whether it is acting as pure renderer, pure physics engine, or both.

**Why SoD async (not Synchronous Direct):** The SoD policy is chosen to prove thread safety across the brain–muscle–eyes boundary. Synchronous direct would avoid snapshot overhead but loses the architectural validation that is the whole point of the PoC.

**DataStrategy is configurable:** `EyesAndMuscleModule` accepts a `DataStrategy` constructor parameter (defaulting to `SoD`). Passing `DataStrategy.Direct` switches to synchronous execution on the main thread, useful for debugging. This same knob generalises to any module in the system — e.g., the future StrideEcsModule can be configured Direct (tight coupling, lower latency, for editor tooling) or SoD (decoupled, production).

### Subsystem interface

```csharp
public class EyesAndMuscleSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar
{
    public string Name => "EyesAndMuscle";
    public Vector4 TitleBarColor => ...;

    public void Initialize(SubsystemConfig config);
    public void Update(float deltaTime);
    public void DrawWorld();
    public void DrawUI();
    public void Shutdown();

    // IMapCameraProvider — exposes MapCanvas camera for the orchestrator
    public MapCamera? GetMapCamera();
    // IWindowRegistrar — registers ImGui panels with WindowManager
    public void RegisterWindows(WindowManager wm);
}
```

### Headless / test support

`EyesAndMuscleSubsystem` skips Raylib window creation when `config.Headless = true` — identical to how `SimHostSubsystem` handles it. Integration tests use the headless path.

### Integration test

A new `EyesAndMuscleIntegrationTests` class verifies end-to-end:
1. Subsystem bootstraps without exception.
2. After `N` frames with a spawned entity and a `NavigationIntent`, the entity's `SimTransform` position changes (muscle command-buffer writes applied).
3. `EyesAndMuscleModule.Tick` was called (counters or spy injected in test).

---

## Phase 4 — Migrate Existing Subsystems

### Goal
Apply `HrotNodeBuilder` and `NedReplicationModule` universally to all existing node entry points, eliminating the legacy boilerplate and completing the DRY promise.

### Targets

| Entry point | Role | Net change |
|---|---|---|
| `SimHostApp.OnLoad` | `NodeRole.AllInOne` (or narrower via `_role` field) | ~300 lines of manual init replaced with `HrotNodeBuilder` + `NedReplicationModule` |
| `IgApplication.InitializeEmbedded` | `NodeRole.ImageGenerator` | DDS/time/cluster setup extracted; `EntityStatesIngressPack` + `DeadReckoningSyncSystem` consolidated into `NedReplicationModule(ImageGenerator)` |
| `CgfSubsystem` / `CgfApplication` | `NodeRole.Brain` | `CognitiveTranslatorPack` consolidated; `HrotNodeBuilder` replaces manual wiring |

### Migration principles

- **Pure refactor.** No behavioral change. Every existing integration test must pass before and after the migration.
- **One subsystem at a time.** Migrate `SimHostApp` first (most complex, best test coverage), then `IgApplication`, then `CgfSubsystem`.
- **Validate after each.** Run the full integration test suite after each migration. Do not proceed to the next if tests fail.
- **Retain module references.** Each migrated subsystem stores its `NedReplicationModule` instance as a private field (state tracker pattern from Phase 1 design).

### What stays per-subsystem after migration

The same list as Phase 1 (component registration, behavior registry, road network, scenario serializer, `CheckpointIOWorker`, Replay/Edit/Episode handlers). `HrotNodeBuilder` does not absorb these because they are genuinely domain-specific.

---

## Architectural constraints

1. **FDP / Hrot boundary must be respected.** `FdpKernelBuilder` must not reference `CycloneDDS`, `Hrot.NED`, or any application-layer types. `HrotNodeBuilder` must not reference `Hrot.SimHost` or `Hrot.IG` internal types.
2. **NedReplicationModule must not create an ingress+egress collision.** When two translator packs both subscribe or publish overlapping DDS topics, entity state will corrupt. The role guard and the `driveFromNetwork` flag on `DeadReckoningSyncSystem` prevent this.
3. **NedReplicationModule execution policy must be `Synchronous`.** CycloneDDS memory polling and ECS smoothing must happen synchronously on the main FDP thread. Registering `NedReplicationModule` with any asynchronous or SoD policy is prohibited.
4. **Builders are transient.** After `Build()`, the builder instance must be discarded. Subsequent `Build()` calls are not supported.
5. **SoD module must not hold strong references to `ISimulationView` beyond `Tick()`.** The kernel returns the view to the pool after `Tick` returns.
6. **DeadReckoningSyncSystem must remain inside `NedReplicationModule`.** It must not be registered separately by any consuming subsystem, to avoid double-registration.
7. **`EyesAndMuscleModule` must use `NodeRole.MuscleGround | NodeRole.ImageGenerator`.** Muscle and Eyes are both active in the PoC; role suppression is available for testing but not for production use.
8. **Phase 4 migration must not change behavior.** All existing integration tests in `Hrot.ClusterRunner.Integration.Tests`, `Hrot.SimHost.Tests`, and `Hrot.IG.Tests` must pass unchanged after each subsystem is migrated.
9. **`HrotNodeBuilder` must wire `ClusterSlave` inline.** It must NOT call `NodeBootstrapper.BuildOrchestration`. That method registers domain-specific handlers (`ReferenceScenarioLoadHandler`, `ReferenceEditLoadHandler`) that depend on the SimHost scenario serializer. Calling it from the generic builder would drag domain logic into shared infrastructure. The builder registers only the four generic handlers (`ReferencePreviewHandler`, `ReferencePrefetchHandler`, `ReferenceArchiveHandler`, `ReferenceLiveLoadHandler`); each subsystem registers its own domain-specific handlers after `Build()` returns.
