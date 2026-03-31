# MOD1 Onboarding — Modularising SimHost: Brain/Muscle Split & Node Composition

Welcome to the **MOD1** workstream. This document gives you the context and orientation you need before writing your first line of code.

---

## 1. What Are We Building?

The SimHost is currently a **monolithic all-in-one process** that runs the AI brain (doctrine assignment, behavior tree evaluation, locomotion intent output) and the physics muscles (vehicle kinematics, formation management) in the same executable. You cannot deploy the Brain on one machine and the Muscles on another without significant rework.

**MOD1 refactors this into composable, independently-deployable modules** that can be snapped together at process startup to create any desired deployment topology.

The key ideas are:

1. **CQRS Navigation Contract** — The Brain node communicates movement orders to the Muscle node via two new DDS descriptors (`NavigationIntent` → Brain-to-Sim, `NavigationStatus` ← Sim-to-Brain). This makes the boundary explicit and engine-agnostic (Unreal/Unity can participate too).

2. **Module Decomposition** — The single `SimulationLogicModule` is replaced by five focused `IModule` classes (`MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule`, `GroundKinematicsModule`, `CombatModule`).

3. **NodeBootstrapper** — A role-based composition root replaces the `SimHostApp.OnLoad` God-Class initialiser. A single executable can be configured as `--role brain`, `--role muscleground`, or `--role all_in_one`.

4. **Authority Guard Fix** — Two existing FDP toolkit systems incorrectly check `PrimaryOwnerId == LocalNodeId` instead of using the ECS-native `.WithOwned<T>()` query. This silently breaks split-authority deployments and is fixed in Phase 1.

5. **Presentation Module Split** — The IG end-user map and the Sim developer debug map become separate `IModule` implementations with dynamic switching support.

6. **Component ID Distributed Registries** — Project-specific component IDs are moved out of the shared `Fdp.Kernel.GlobalComponentIds` into domain-bounded registries.

---

## 2. Design and Task Documents

| Document | Purpose |
|----------|---------|
| [`docs/modularizing/design-talk.md`](./design-talk.md) | Raw design conversations (historical context) |
| [`docs/modularizing/MOD1-DESIGN.md`](./MOD1-DESIGN.md) | **Start here.** Full phased design with architecture rationale |
| [`docs/modularizing/MOD1-TASK-DETAIL.md`](./MOD1-TASK-DETAIL.md) | Per-task specs with success conditions and test descriptions |
| [`docs/modularizing/MOD1-TASK-TRACKER.md`](./MOD1-TASK-TRACKER.md) | Brief phase/task checklist — update as tasks complete |
| [`docs/modularizing/MOD1-DEBT-TRACKER.md`](./MOD1-DEBT-TRACKER.md) | Technical debt accumulating during this workstream |

**Read [MOD1-DESIGN.md](./MOD1-DESIGN.md) in full before starting any task.** Each task in [MOD1-TASK-DETAIL.md](./MOD1-TASK-DETAIL.md) references the relevant design section.

---

## 3. Key Code Locations

### Simulation Kernel (the current monolith)

```
Hrot.SimHost/
├── SimHostApp.cs                          ← God-Class entry point; target of NodeBootstrapper refactor (P3T3)
├── SimHostComponentRegistry.cs            ← Component registration; gains domain sub-registries (P3T2)
├── Modules/
│   └── SimulationLogicModule.cs           ← Monolith being decomposed (P2T1–P2T5)
├── Systems/
│   ├── MissionAdapterSystem.cs
│   ├── MissionControlRequestSystem.cs
│   └── ...
├── Brains/
│   └── SimHostNodes.cs                    ← Doctrine definitions + parameter parsers
└── Components/                            ← Target for new NavigationIntent/NavigationStatus (P1T1)
```

### Navigation & Locomotion (FDP Toolkits)

```
FDP/Toolkits/FDP.Toolkit.Navigation/
├── Components/
│   ├── NavigationIntent.cs            ← ECS component (P1T1); Cartesian Vector2 destination; toolkit ID block
│   └── NavigationStatus.cs            ← ECS component (P1T1); toolkit ID block
├── NavigationMode.cs              ← Engine-side enum (P1T1); NOT ENavigationMode (DDS wire enum)
├── NavigationResult.cs            ← Engine-side enum (P1T1); NOT ENavigationResult (DDS wire enum)
├── Executors/
│   └── MoveToExecutor.cs              ← Refactored to CQRS in P1T2; writes Cartesian Vector2 directly
└── ...

Hrot.NED/
└── SimDescriptors.cs              ← DDS wire enums ENavigationMode/ENavigationResult + DDS descriptors (P1T1)

# KEY RULE: ECS components use engine-side enums (FDP.Toolkit.Navigation).
# Translators in Hrot.SimHost.Network convert engine enums ↔ wire enums and Cartesian ↔ GeoPoint.

FDP/Toolkits/FDP.Toolkit.CarKinem/
├── Systems/NavigationExecutionSystem.cs  ← Writes NavigationStatus; pure Cartesian arrival check (P1T4)
└── CarKinematicsSystem.cs (or modules)  ← Integrates bicycle model; reads NavState internally
```

### Authority Guard Bugs (FDP Geographic Toolkit)

```
FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/
├── CoordinateTransformSystem.cs           ← Uses PrimaryOwnerId check — fix in P1T3
└── GeodeticSmoothingSystem.cs             ← Uses PrimaryOwnerId check — fix in P1T3
```

### DDS Data Model

```
Hrot.NED/
└── SimDescriptors.cs                      ← Add NavigationIntent/NavigationStatus descriptors in P1T1
```

### Presentation & Visualization

```
Hrot.SimHost/
├── SimHostVisualization.cs                ← Current visualization; wrapped by SimPresentationModule (P4T1)
Hrot.IG/
├── IgApplication.cs                       ← IG entry point; IgPresentationModule mirrors its map setup (P4T1)
FDP/Toolkits/FDP.Toolkit.Vis2D/            ← Shared map canvas, layers, tools infrastructure
```

### Brain, Muscle & AI Modules (Phases 2 + 6)

```
FDP/Toolkits/FDP.Toolkit.Behavior/
├── Modules/MissionControlModule.cs        ← Created P2T1 (FDP.Toolkit.Behavior)
├── Modules/CognitiveRuntimeModule.cs      ← Created P2T2 (FDP.Toolkit.Behavior)
├── Modules/ActionDispatchModule.cs        ← Created P2T3 (FDP.Toolkit.Behavior)
└── BTreeContext.cs                        ← RequestRaycast/RequestPath stubs → wired P6T4, P6T5

FDP/Toolkits/FDP.Toolkit.CarKinem/
└── Modules/GroundKinematicsModule.cs      ← Created P2T4 (FDP.Toolkit.CarKinem)
   CarKinematicsSystem.cs                 ← P1T4 adds NavigationStatus fulfillment logic

Hrot.SimHost/Modules/
├── CombatModule.cs                        ← Created P2T5 (stays Hrot — weapon domain)
└── SimulationLogicModule.cs               ← Refactored to delegation facade (P2T5)
```

### Ground Clamping (Phase 7)

```
FDP/Toolkits/FDP.Toolkit.Geographic/
├── EClampingMode.cs                       ← Engine-side enum (P7T1; separate from DDS wire enum)
├── ITerrainProvider.cs                    ← Terrain query abstraction (P7T3)
├── Components/GroundClampingConfig.cs     ← ECS component (P7T2)
├── Components/GroundClampingState.cs      ← ECS component (P7T2)
├── Components/TerrainQueryBatchData.cs    ← NativeArray singleton (P7T2)
├── Systems/TerrainQueryInitializationSystem.cs  ← allocates singleton (P7T4)
├── Systems/TerrainQuerySubmitSystem.cs          ← forward-predicts+batches (P7T4)
├── Systems/TerrainQuerySolverSystem.cs          ← calls ITerrainProvider (P7T4)
└── Systems/TerrainQueryResolutionSystem.cs      ← applies Z offset (P7T4)

Hrot.IG/Modules/
└── IgGroundClampingModule.cs              ← IG-specific wiring, conditionally installed (P7T5)

Hrot.NED.Descriptors/
├── GroundClampingOverride.cs              ← DDS wire descriptor (P7T1)
└── EClampingMode.cs                       ← DDS wire enum (P7T1; separate from engine-side)

Hrot.IG/Network/
└── GroundClampingOverrideTranslator.cs    ← ingress-only translator (P7T3)
```

```
FDP/Toolkits/FDP.Toolkit.Perception/
├── Modules/AutonomousPerceptionModule.cs  ← Created in P6T6 (FDP.Toolkit.Perception)
├── Modules/PhysicsQueryModule.cs           ← Created in P6T6 (FDP.Toolkit.Perception)
├── Components/TargetMemory.cs             ← Gains Modalities[] fixed array (P6T1)
├── Components/PerceptionReceptor.cs       ← Existing visual sensor params
├── Components/VisualReceptor.cs           ← New per-modality receptor (P6T1)
├── Components/RadarReceptor.cs            ← New per-modality receptor (P6T1)
├── Components/SensorModality.cs           ← New flags enum (P6T1)
├── Systems/VisionBroadphaseSystem.cs      ← Wrapped by AutonomousPerceptionModule (P6T6)
├── Systems/ThreatEvaluationSystem.cs      ← Wrapped by AutonomousPerceptionModule (P6T6)
└── Systems/LosRequestBatchingSystem.cs    ← Wrapped by AutonomousPerceptionModule (P6T6)

FDP/Toolkits/FDP.Toolkit.Navigation/
├── Modules/NavigationSolverModule.cs      ← Created in P6T7 (FDP.Toolkit.Navigation)
├── Systems/PathfindingSolverSystem.cs     ← Created in P6T7 (FDP.Toolkit.Navigation)
└── PathfindingBatchData.cs               ← Created in P6T3 (FDP.Toolkit.Navigation)

FDP/Toolkits/FDP.Toolkit.Physics/
├── Components/RaycastBatchData.cs         ← Existing singleton (already in codebase)
└── Systems/RaycastSolverSystem.cs         ← Wrapped by PhysicsQueryModule

Hrot.SimHost/Network/
├── BrainPerceptionTranslatorPack.cs       ← Created in P6T8
├── SimPerceptionTranslatorPack.cs         ← Created in P6T8
├── BrainPathfindingTranslatorPack.cs      ← Created in P6T8
└── SimPathfindingTranslatorPack.cs        ← Created in P6T8

Hrot.NED/
└── SimDescriptors.cs                      ← Navigation descriptors (P1T1) + Perception/Path descriptors (P6T2)
```

### Component IDs

```
FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs      ← FDP + toolkit IDs (0–159); includes
                                                    NavigationIntent/NavigationStatus (20–49 toolkit block),
                                                    GroundClampingConfig/State/TerrainQueryBatchData (20–49),
                                                    StoryTag/StoryReplayTag (20–49)
Hrot.Map.Definitions/HrotComponentIds.cs      ← Created in P5T1; Hrot-specific IDs (160–255)
                                                    e.g. ActivePerspective=160
```

### Recording / Replay (Phase 8)

```
# Existing FDP kernel infrastructure (do not move these)
FDP/Kernel/Fdp.Kernel/FlightRecorder/AsyncRecorder.cs       ← owns LZ4 front-buffer + BG worker
FDP/Kernel/Fdp.Kernel/FlightRecorder/RecorderSystem.cs      ← 60 Hz memcpy tick; P8T2 adds EntityFilter
FDP/Kernel/Fdp.Kernel/FlightRecorder/PlaybackController.cs  ← dual-strategy seek; owned by ReplayModule

# New artefacts created in Phase 8
FDP/Toolkits/FDP.Toolkit.Replay/RecordingConfiguration.cs   ← injection contract (FilePath, EntityFilter, ExerciseId)
FDP/Toolkits/FDP.Toolkit.Replay/RecordingModule.cs          ← IModule + IDisposable; owns AsyncRecorder
FDP/Toolkits/FDP.Toolkit.Replay/StoryRecorderModule.cs      ← filtered recorder; multiple run concurrently
FDP/Toolkits/FDP.Toolkit.Replay/ReplayModule.cs             ← IModule + IDisposable; owns PlaybackController
FDP/Toolkits/FDP.Toolkit.Replay/StoryTag.cs                 ← IModule-agnostic story entity marker
FDP/Toolkits/FDP.Toolkit.Replay/StoryReplayTag.cs           ← marks hologram ghost entities during story replay
Hrot.SimHost/Modules/Orchestration/EcsRecordReplayController.cs ← IDsmHandler + factory (Control Plane; Hrot-specific)
```

---

### Application Lifecycle / Runner (Phase 9)

```
FDP/Framework/FDP.Framework.Runner/
├── ISubsystem.cs                          ← Core contract: Initialize/Update/DrawWorld/DrawUI/Shutdown + TitleBarColor (P9T1)
├── SubsystemConfig.cs                     ← Headless, OwnWindow flags (P9T1)
├── IMapCameraProvider.cs                  ← Camera-snap interface (P9T1)
├── SubsystemOrchestrator.cs               ← 60 Hz loop, Raylib, ImGui; no Hrot coupling (P9T2)
├── WaitingRoomCoordinator.cs              ← DDS peer-startup sync; fully generic (P9T3)
├── RunnerConfiguration.cs                 ← Base CLI flags: --headless, --domain, --no-wait (P9T3)
└── Testing/
    ├── HeadlessTestExecutor.cs              ← Background-thread JSON script engine (P9T4)
    ├── TestScript.cs / TestStep.cs           ← Script models (P9T4)
    ├── TestReport.cs / ITestActionHandler.cs ← Result models + handler contract (P9T4)
    ├── WaitActionHandler.cs                 ← Generic: wait N frames (P9T4)
    ├── TickActionHandler.cs                 ← Generic: advance one tick (P9T4)
    └── AssertAllActionHandler.cs             ← Generic: assert condition (P9T4)

Hrot.ClusterRunner/
├── Program.cs                             ← Composition root: parse --mode, inject subsystems (P9T5)
├── HrotRunnerConfiguration.cs           ← Extends RunnerConfiguration: --mode, --role flags (P9T3)
├── Subsystems/
│   ├── SimHostSubsystem.cs                 ← Hrot ECS world + SimHost modules (stays here)
│   ├── IgSubsystem.cs                      ← Hrot IG bootstrap (stays here)
│   └── IosSubsystem.cs                     ← Hrot IOS bootstrap (stays here)
└── Testing/
    ├── SpawnActionHandler.cs               ← Hrot domain: spawns entities by type (stays here)
    ├── MoveActionHandler.cs                ← Hrot domain: issues movement commands (stays here)
    └── AssertPositionActionHandler.cs      ← Hrot domain: checks entity Cartesian position (stays here)
```

---

Before coding, familiarise yourself with these FDP ECS primitives:

### Component Authority

- **`.WithOwned<T>()`** in a query builder filters to entities where this node has write authority over component `T`. Use this in all new kinematic systems instead of `NetworkOwnership.PrimaryOwnerId` checks.
- **`.WithoutOwned<T>()`** — the inverse; use for ghost/smoothing systems.
- `EntityRepository.SetAuthority<T>(entity)` — called by the network ingress layer, not by simulation systems.

### `IModule` Pattern

```csharp
public sealed class MyModule : IModule
{
    public string Name => "MyModule";
    public void RegisterSystems(ISystemRegistry reg) { reg.AddToGroup(SystemPhase.Simulation, new MySystem()); }
    public void Tick(ISimulationView view, float dt) { }
}
```

Modules are registered via `kernel.RegisterModule(new MyModule())`.

### `IActionExecutor` Pattern

`MoveToExecutor` is the executor for the `MoveTo` locomotion action. It implements `IActionExecutor<LocomotionChannel>` with `OnEnter`, `Execute`, and `OnExit` lifecycle methods. After P1T2 it must contain **zero references to `SimTransform`, `SimVelocity`, or distance math**.

### `RaycastBatchData` Singleton Pattern

`RaycastBatchData` is a pre-allocated ECS singleton holding two `NativeArray<T>` fields: `Requests` and `Hits`. Systems write into `Requests` and read from `Hits` with zero GC overhead. After Phase 6, `BTreeContext.RequestRaycast` writes directly into this singleton rather than returning `-1`.

### `PathfindingBatchData` Singleton Pattern (Phase 6)

Mirrors `RaycastBatchData` exactly but for path queries. `BTreeContext.RequestPath` writes a `PathRequest` into `Requests`; the `PathfindingSolverSystem` fills `Results` with `PathResult` values containing a lightweight `RouteHandle` integer. The Brain BTree node persists the `RequestId` in `BrainBlackboard` and calls `GetPathResult` on subsequent ticks until a result arrives.

### Control Plane / Data Plane Split (Phase 8)

Recording and replay follow a two-tier architecture:
- **Control Plane** (`EcsRecordReplayController`): reacts to 2PC DSM commands; constructs `RecordingConfiguration`; calls `ModuleHostKernel.InstallModuleAsync` / `UninstallModuleAsync`. Never directly touches `AsyncRecorder`.
- **Data Plane** (`RecordingModule`, `StoryRecorderModule`, `ReplayModule`): each is an `IModule` + `IDisposable`; constructs and owns `AsyncRecorder` or `PlaybackController` in `RegisterSystems()`; `Dispose()` blocks until buffers are flushed (`AsyncRecorder.Dispose()`).

Key invariants:
- A node with no active recording module has **zero CPU overhead** on the 60 Hz path — there is no `if (isRecording)` guard anywhere.
- Multiple `StoryRecorderModule` instances may coexist with the global `RecordingModule`; each owns its own `AsyncRecorder` and LZ4 worker — no shared lock.
- `Dispose()` is **always blocking** — `NodeOpStatus(Success)` is never sent before `.meta.json` is written to disk.

### FDP vs Hrot Boundary — The Fundamental Rule

See [MOD1-DESIGN.md §2.5](./MOD1-DESIGN.md#25-fdp-vs-hrot--namespace-assignment-principles) for the full assignment table. The one-sentence rule:

> **Code belongs in `FDP.*` if it is ignorant of what entities _are_.** The moment a module or system needs to know the entity is "a tank", references a Hrot DDS topic, or uses a Hrot component registry, it belongs in `Hrot.*`.

Practical checklist before placing a new artefact:
1. Does it reference `Hrot.*` directly? → `Hrot.*` assembly.
2. Does it need a specific DDS topic struct (e.g. `EntityMaster`, `NavigationIntentTopic`)? → `Hrot.*` assembly.
3. Otherwise → the appropriate `FDP.Toolkit.*` library.

---

## 5. How to Build

```powershell
# Build the full solution
dotnet build IOS-IG-SimHost.sln

# Build just SimHost
dotnet build Hrot.SimHost\Hrot.SimHost.csproj

# Run SimHost unit tests
dotnet test Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj

# Run SimHost integration tests
dotnet test Hrot.SimHost.Integration.Tests\Hrot.SimHost.Integration.Tests.csproj
```

All PRs must pass `dotnet build` and `dotnet test` on the `Hrot.SimHost.Tests` and `Hrot.SimHost.Integration.Tests` projects before merging.

---

## 6. Developer Workflow

This workstream follows the **batch-based development** process. Read the developer workflow guide before starting any task:

📖 [`.dev-workstream/guides/DEV-GUIDE.md`](../../.dev-workstream/guides/DEV-GUIDE.md)

Key points:
- Each batch of tasks comes with explicit instructions in `.dev-workstream/batches/`.
- Implement tasks against the success conditions in [MOD1-TASK-DETAIL.md](./MOD1-TASK-DETAIL.md).
- Submit a batch report when done; do not self-merge without a review.
- If you discover technical debt during implementation, record it in [MOD1-DEBT-TRACKER.md](./MOD1-DEBT-TRACKER.md).
- Check [`.dev-workstream/guides/CODE-STANDARDS.md`](../../.dev-workstream/guides/CODE-STANDARDS.md) for coding conventions.

---

## 7. Phase Sequencing

Phases are designed to be implemented in order because each phase builds on the previous one:

```
Phase 1 (P1T1–P1T4)  — Must be done first: defines the data contract that all modules use
    ↓
Phase 2 (P2T1–P2T5)  — Module decomposition; depends on NavigationIntent/Status from P1
    ↓
Phase 3 (P3T1–P3T3)  — Translator packs and NodeBootstrapper; depends on modules from P2
    ↓
Phase 4 (P4T1–P4T2)  — Presentation modules; can overlap with P3 if resources allow
    ↓
Phase 5 (P5T1)       — Component ID cleanup; purely mechanical; can be done last with low risk
    ↓
Phase 6 (P6T1–P6T8)  — Perception & Pathfinding modules; builds on NodeBootstrapper from P3
                       P6T1–P6T3 (data contracts) can start after P1 completes
                       P6T4–P6T5 (BTreeContext wiring) require P6T3
                       P6T6–P6T7 (modules) require P2 module pattern to be established
                       P6T8 (translator packs) requires P3 translator pack pattern + P6T2
    ↓
Phase 7 (P7T1–P7T5)  — IG Ground Clamping; independent of P5/P6; can start after P3
    ↓
Phase 8 (P8T1–P8T5)  — Recording/Replay Module Architecture
                       P8T1 (EcsRecordReplayController skeleton) can start after P2 (needs ModuleHostKernel)
                       P8T2 (RecordingModule) requires P8T1
                       P8T3 (StoryRecorderModule + StoryTag) requires P8T2 + P5T1 (HrotComponentIds)
                       P8T4 (ReplayModule) requires P8T1; can be done in parallel with P8T2/P8T3
                       P8T5 (NodeBootstrapper wiring) requires P8T1–P8T4 + P3 NodeBootstrapper
        ↓
Phase 9 (P9T1–P9T5)  — Runner generalization; largely independent; can start once P2 module pattern
                        is established (P9T1-T3 require no prior MOD1 phases; P9T4-T5 require runner codebase)
```

Within a phase, tasks labelled T1, T2, T3 … are usually sequential but check [MOD1-TASK-DETAIL.md](./MOD1-TASK-DETAIL.md) for explicit dependencies.
