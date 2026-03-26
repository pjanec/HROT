# BATCH-13 Report

**Batch:** BATCH-13  
**Developer:** GitHub Copilot  
**Date:** 2026-03-27  
**Status:** Complete (Tasks 1–3 delivered; Task 4 deferred)

---

## Task Completion

| Task | Status | Notes |
|------|--------|-------|
| Task 1 — DEM1-D009 spec alignment | ✅ Complete | Implement path chosen; DemoTkbSetup + DemoLocomotionMsg DDS path delivered |
| Task 2 — `SteppingTimeController` first-frame `DeltaTime` | ✅ Complete | XML documentation added to class, `SeedState`, and `Update()` |
| Task 3 — `ModuleHostKernel` `IDisposable` modules | ✅ Complete | Contract documented in `RegisterModule` and `Dispose` XML |
| Task 4 — Optional P3 debt item | ⏭ Deferred | Budget consumed by Tasks 1–3 |

---

## Testing Results

| Project | Before | After | Notes |
|---------|--------|-------|-------|
| `Fdp.Examples.Scenarios.Tests` | 58/58 | **58/58** | All existing tests pass with new DDS locomotion path |
| `FDP.Toolkit.Time.Tests` | 52+1skip | **52+1skip** | No regressions from `SteppingTimeController` doc changes |
| Solution build | Clean | **Clean** | Zero new errors or warnings in touched projects |

---

## Implementation Details

### Task 1 — DEM1-D009 Implement-or-Trim Decision

**Decision: Implement minimum spec-compliant changes.**

Rationale: The three concrete gaps (DemoTkbSetup, LocomotionChannel on Brain, DemoLocomotionMsg DDS path) are all achievable with low blast radius. The fourth gap (`BehaviorToolkit`/`ReplicationLogicModule` on Brain) is documented as intentionally out-of-scope — see below.

#### 1a. `DemoTkbSetup.RegisterAll` in `Fdp.Examples.Common`

**New file:** `FDP/Examples/Fdp.Examples.Common/Setup/DemoTkbSetup.cs`

Static class with `RegisterAll(ITkbDatabase tkb)` that registers the `CommandTank` (TKB 100) template for Muscle-side ghost promotion. The template contains:
- `SimTransform`, `SimVelocity` — spatial primitives
- `VehicleState`, `VehicleParams` (Tank preset) — CarKinematics inputs
- `NavState` — navigation target (populated via DemoLocomotionMsg translation)
- `LocomotionChannel` — command channel (set by DDS translator)

The `TankTurret` (TKB 101) template is **not** registered because TankTurret is a Brain-only entity and is never ghost-promoted on the Muscle node.

**csproj changes:**
- `Fdp.Examples.Common.csproj`: added `FDP.Toolkit.Behavior` and `FDP.Toolkit.Tkb` references
- `Fdp.Examples.Scenarios.csproj`: added `Fdp.Examples.DDS` reference (provides `DemoLocomotionMsg`)

#### 1b. Brain hull `LocomotionChannel` + `DemoLocomotionMsg` DDS path

**Changed file:** `FDP/Examples/Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs`

**New DDS channel:** `DdsWriter<DemoLocomotionMsg>` on Brain participant, `DdsReader<DemoLocomotionMsg>` on Muscle participant, both on Domain 0 (loopback).

**Tick-20 Brain side:**
1. Sets `LocomotionChannel.ActiveAction = NavigationConstants.ActionIdMoveTo` on Brain hull entity.
2. Writes `DemoLocomotionMsg { NetworkId = BrainHullNetId, ActiveAction = ActionIdMoveTo }`.

**Muscle translation (before Muscle kernel update):**
`EvaluateTick` polls `DemoLocomotionMsg` at the **start** of each tick, before `Step + Update`. This ensures that a command written at tick N is translated to `NavState` before the Muscle kernel runs at tick N+1, giving `CarKinematicsSystem` the full tick to integrate velocity. Translation: `NavState.Mode = KinematicsMode.None`, `FinalDestination = (200, 0)`, `TargetSpeed = 15 m/s` (`ArrivalRadius` preserved from TKB default).

**Timing:** Tick-21 Muscle kernel now sees NavState → velocity builds from tick 21 onwards → 4 kinematics ticks before tick-25 assertion (SimVelocity.X > 0.1 m/s). Tests confirmed passing.

**Also replaced:** inline `TkbTemplate` / `TkbDatabase` / `commandTankTemplate.AddComponent(...)` block with `DemoTkbSetup.RegisterAll(muscleTkb)`.

#### 1c. Why `ReplicationLogicModule` is NOT added to Brain

The Brain node is the **authoritative** node: it spawns entities natively and publishes `EntityMasterTopic` manually. Adding `ReplicationLogicModule` to Brain would register `OwnershipIngressSystem` and `GhostCreationSystem` on the Brain kernel. Since both participants share Domain 0 (in-process loopback), the Brain would receive its own `EntityMasterTopic` publications and attempt to ghost itself — creating a degenerate self-replication loop. The `ReplicationLogicModule` is correctly scoped to the **receiving** (Muscle) node only. This decision is documented in the updated `DEM1-TASK-DETAIL.md §D009`.

#### 1d. Why `BehaviorToolkit` (`CognitiveRuntimeModule`) is NOT added to Brain

`CognitiveRuntimeModule` is not an `IEcsModule` — it exposes `RegisterSystems(SystemGroup group)` (for `SimulationLogicModule` delegation), not `RegisterSystems(ISystemRegistry registry)`. There is no `IEcsModule` wrapper for it. More importantly, the demo does not need BTree/HSM execution: locomotion commands are injected by `EvaluateTick` at tick 20 (scenario controller pattern). Adding `CognitiveRuntimeModule` would require `DoctrineRegistry`, entity archetypes with `DoctrineState`/`ActorCapabilityState`, and would effectively fork a second `UrbanCombat` harness — explicitly prohibited by BATCH-13 instructions.

#### 1e. `DEM1-TASK-DETAIL.md` and `DEM1-TASK-TRACKER.md` updated

- `DEM1-TASK-DETAIL.md §D009`: rewrote "What to implement" to describe the **implemented** topology (LocomotionChannel + DemoLocomotionMsg path, DemoTkbSetup, no BehaviorToolkit/ReplicationLogicModule on Brain); added architecture note explaining each omission.
- `DEM1-TASK-TRACKER.md`: DEM1-D009 marked `[x]`.

**Lead acknowledgment requested:** The trimmed Brain-toolkit requirement is a scoped engineering decision. Please confirm in the review that the implemented topology satisfies D009 intent.

---

### Task 2 — `SteppingTimeController` First-Frame `DeltaTime`

**Decision: Document only (no behavior change; no new test required).**

**Changed file:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppingTimeController.cs`

Added XML to:
- **Class summary**: explicit paragraph titled "First-frame DeltaTime contract" that states `Update()` returns `DeltaTime = 0` until `Step()` is called; includes the mandatory `Step → Update` call order example.
- **`SeedState(GlobalTime state)`**: expanded inline comment from "When seeding, reset delta" to explain *why* the reset to zero occurs (seed establishes a temporal baseline, not a completed step) and forward-references the class-level XML.

No behavior change → no unit test added (per task spec: "Add a unit test if behaviour changes").

---

### Task 3 — `ModuleHostKernel` `IDisposable` Modules

**Decision: Document only (no disposal added; smallest blast radius).**

**Rationale:** `ModuleHostKernel.Dispose()` dispatches provider disposal but **not** module disposal by design. Adding module disposal would break `ParallelStoriesScenario`'s `RecordingModule` pattern: the module is declared as `using var recordingModule` outside the kernel's `using` scope; if the kernel also disposed it, `recordingModule.Dispose()` would be called twice (kernel dispose + outer `using`). Other callers with explicit `using` blocks would face the same double-dispose risk.

**Changed file:** `FDP/ModuleHost/ModuleHost.Core/ModuleHostKernel.cs`

Added XML to:
- **`RegisterModule`**: new "Ownership contract" `<para>` explaining the kernel does not dispose registered `IEcsModule` instances; callers must dispose them via `using` blocks or `IScenario.OnShutdown`.
- **`Dispose`**: new `<summary>` XML with a "Module disposal" `<para>` clarifying that only providers are disposed, modules removed via `UninstallModuleAsync` are the exception (disposed on background drain thread).

---

## Design Decisions

| Decision | Rationale |
|---|---|
| Implement path for Task 1 (not trim) | DemoTkbSetup + DemoLocomotionMsg path is achievable; trimming would leave D009 in an ambiguous state |
| No `ReplicationLogicModule` on Brain | DDS loopback self-ghosting; Brain is authoritative, has no incoming ghosts |
| No `BehaviorToolkit` on Brain | Not `IEcsModule`-compatible; would require full UrbanCombat-scale setup; scenario uses direct injection |
| DemoLocomotionMsg polling BEFORE kernel update | Ensures 4 kinematics ticks (21-24) before tick-25 assertion; robust margin |
| Documentation-only for Tasks 2 & 3 | No behavior change = no test required; smallest blast radius |

---

## Files Changed

| File | Change |
|---|---|
| `FDP/Examples/Fdp.Examples.Common/Setup/DemoTkbSetup.cs` | **NEW** — `RegisterAll` for CommandTank (100) |
| `FDP/Examples/Fdp.Examples.Common/Fdp.Examples.Common.csproj` | Added `FDP.Toolkit.Behavior` + `FDP.Toolkit.Tkb` refs |
| `FDP/Examples/Fdp.Examples.Scenarios/Fdp.Examples.Scenarios.csproj` | Added `Fdp.Examples.DDS` ref |
| `FDP/Examples/Fdp.Examples.Scenarios/Network/DistributedTankScenario.cs` | DemoTkbSetup; LocomotionChannel + DemoLocomotionMsg DDS path; loco poll BEFORE kernel update |
| `docs/demos-1/DEM1-TASK-DETAIL.md` | §D009 rewritten to match implemented topology |
| `docs/demos-1/DEM1-TASK-TRACKER.md` | DEM1-D009 marked [x] |
| `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SteppingTimeController.cs` | Class XML + SeedState comment |
| `FDP/ModuleHost/ModuleHost.Core/ModuleHostKernel.cs` | `RegisterModule` + `Dispose` XML |

---

## Outstanding Issues / Carry-over

None. All Task 1–3 items are closed. The optional Task 4 (P3 debt) was not attempted due to budget.

**DEBT-TRACKER rows to close (lead action):**
- Row: `BATCH-12 review` — "DEM1-TASK-DETAIL §D009: Brain BehaviorToolkit..." → **Close** (implemented/trimmed in BATCH-13)
- Row: `BATCH-12 report` — "SteppingTimeController / seed: DeltaTime=0" → **Close** (documented in BATCH-13)
- Row: `BATCH-12 report` — "ModuleHostKernel.Dispose() / IDisposable modules" → **Close** (documented in BATCH-13)
