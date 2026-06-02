# BATCH-04 Report

**Tasks:** STR-P1-T1 (`StrideKinematicsModule`), STR-P1-T2 (`PhysicsBodyReference` + `PhysicsBodyLifecycleSystem`)

---

## HEADLINE: Headless-Bullet-Simulation Finding

**RESULT: Headless Bullet Simulation is NOT feasible in the Stride 4.2.1.2487 API for test purposes.**

### What was verified (probe program `HeadlessBulletProbe`)

`Stride.Physics.Simulation` has a constructor `Simulation(PhysicsProcessor, PhysicsSettings)` that is **internal** — visible in the XML docs, not in the public API. By reflection this constructor can be called, and the `Simulation` object initialises successfully (gravity, timestep, etc. are readable). `CapsuleColliderShape` and `BoxColliderShape` can be created with no GPU or window present (BulletSharp is fully CPU-side).

However, all body lifecycle methods — `AddRigidBody`, `RemoveRigidBody`, `AddCharacter`, `RemoveCharacter`, and `Simulate(float dt)` — are also **internal** to `Stride.Physics`. They are owned by and designed for `PhysicsProcessor` alone. Calling `AddRigidBody` via reflection fails with a `NullReferenceException` because the `RigidbodyComponent.OnAttach()` method (also internal) requires context from `PhysicsProcessor.AssociatedData` (the per-entity struct that links a component to the simulation's internal state). Without a running Stride `Scene` + `PhysicsProcessor`, the entity context is null.

### API facts confirmed

| API | Public? | Notes |
|-----|---------|-------|
| `new PhysicsSettings { ... }` | Yes | Data struct, no GPU dep |
| `new PhysicsProcessor()` | Yes | Default ctor |
| `new Simulation(PhysicsProcessor, PhysicsSettings)` | **Internal** | Accessible via reflection; no GPU needed for the ctor itself |
| `Simulation.AddRigidBody(...)` | **Internal** | Fails at runtime without `PhysicsProcessor.AssociatedData` context |
| `Simulation.Simulate(float dt)` | **Internal** | Only callable from within `PhysicsProcessor`'s game loop |
| `new CapsuleColliderShape(...)` | Yes | Works headlessly |
| `new BoxColliderShape(...)` | Yes | Works headlessly |

### Decision: `IPhysicsBodyService` seam (identical to `IStrideVisualFactory` from BATCH-03)

Since body add/remove/step APIs are owned exclusively by `PhysicsProcessor` (which requires a running `Game` and `Scene`), we mirror the BATCH-03 pattern exactly:

- **`IPhysicsBodyService`** in `Hrot.Stride.Core`: `CreateBody(entity, shapeKind, dims, initialPose) → handle` + `RemoveBody(handle)`. Defines the seam.
- **`PhysicsBodyLifecycleSystem`** in `Hrot.Stride.Core`: authority-keyed lifecycle logic; uses the interface for all body operations. Tested headlessly with `RecordingFakePhysicsBodyService`.
- **Concrete `BulletPhysicsBodyService`** (deferred): lives in `HrotStrideApp.Game` where a real `PhysicsProcessor` + `Simulation` is running. Exercised during GPU bring-up (same as `StrideVisualFactory`).

This is the clean, high-fidelity outcome: the authority/lifecycle logic is fully proven headlessly. Recorded as **STR-D11** in the debt tracker.

---

## Implementation Summary

### STR-P1-T1: `StrideKinematicsModule`

**File:** `Stride/Hrot.Stride.Core/StrideKinematicsModule.cs` (new)

Implements the `StrideKinematicsModule` class (not `IEcsModule` — mirrors `GroundKinematicsModule`'s data-class shape with `SimulationSystems` / `PostSimulationSystems` lists).

**SimulationSystems (5):** `SpatialHashSystem`, `FormationTargetSystem`, `VehicleCommandSystem`, `NavigationExecutionSystem`, `CrowdAgentUpdateSystem`.  
**PostSimulationSystems (1):** `DeadReckoningSyncSystem(driveFromNetwork: false)`.  
**Absent (topological exclusion):** `CarKinematicsSystem`, `LinearKinematicsSystem`, `TerrainQuerySubmitSystem`, `TerrainQuerySolverSystem`, `TerrainQueryResolutionSystem`.

Exposes `TrajectoryPool` for `RouteTrajectorySyncSystem` reuse.

**Wire-in to `EditorStrideSubsystem`** (P1 seam at line 233 comment):

The P0 `SimHostCoreLogicPack` was replaced by a manual recomposition that exactly mirrors `SimHostCoreLogicPack`'s phase lists but substitutes `StrideKinematicsModule` for `GroundKinematicsModule`:

- `CombatModule` (input + post-sim)  
- `DamageAssessmentModule` (sim phase)
- `NavigationIntentBridgeSystem`, `RouteTrajectorySyncSystem` (sim phase, nav-bridge)
- `StrideKinematicsModule.SimulationSystems` (replaces Ground's sim systems; no integrators)
- `UnitHierarchySystem`, `EqsResultUpdateSystem` (sim phase)
- `PersonalRouteAuthoringSystem` (input)
- `StrideKinematicsModule.PostSimulationSystems` (DeadReckoningSyncSystem, DriveFromNetwork=false)

`FakeDtCrowdProvider` is wired as the Phase-1 no-op crowd provider (replaced by `DotRecastDtCrowdProvider` in P2-T3). `KinematicsModule` is exposed as a public property on `EditorStrideSubsystem` for test inspection.

### STR-P1-T2: `PhysicsBodyReference` + `PhysicsBodyLifecycleSystem`

**Files:**
- `Stride/Hrot.Stride.Core/IPhysicsBodyService.cs` (new)
- `Stride/Hrot.Stride.Core/PhysicsBodyReference.cs` (new)
- `Stride/Hrot.Stride.Core/PhysicsBodyLifecycleSystem.cs` (new)

**`IPhysicsBodyService`**: seam interface (2 methods: `CreateBody` / `RemoveBody`). Mirrors `IStrideVisualFactory`.

**`PhysicsBodyReference`**: shadow component stored in a parallel `Dictionary<Entity, PhysicsBodyReference>` (same pattern as `StrideVisualBindingSystem._visuals`). Carries `BodyHandle` + `ShapeKind` + `Dims` for test inspection.

**`PhysicsBodyLifecycleSystem`** (`[UpdateInPhase(Simulation)]`):
1. **Step 1 — Destructions:** reads `DestructionOrder` events; calls `TeardownBody`; records torn-down entities in `_destroyedThisFrame` set.
2. **Step 2 — Revocations:** queries `.WithoutOwned<SimTransform>()` for alive entities in `_bodies`; tears them down.
3. **Step 3 — Creations:** queries `.WithOwned<SimTransform>()`; skips entities in `_bodies` (idempotency) and entities in `_destroyedThisFrame` (avoid re-create in same frame); reads `ShapeKind`+`Dims` from `StrideVisualBindingSystem.Visuals`; calls `CreateBody`.

A key bug was found and fixed during development: after processing a `DestructionOrder` event in step 1, the entity (still alive and owned in the ECS, since ECS destruction is deferred) would be found again by the creation query in step 3 and immediately re-created. The `_destroyedThisFrame` set prevents this.

---

## Design Decisions

1. **`StrideKinematicsModule` is a data class, not `IEcsModule`**: mirrors `GroundKinematicsModule`'s pattern exactly. The kernel's `RegisterModule(IEcsModule)` path is not used here — the systems are extracted from the lists and registered individually, following `EditorStrideSubsystem`'s existing pattern.

2. **Recomposition strategy for `EditorStrideSubsystem`**: rather than trying to suppress individual systems inside `SimHostCoreLogicPack`, we decompose it manually. The new code mirrors `SimHostCoreLogicPack`'s phase-list construction with `StrideKinematicsModule` substituted for `GroundKinematicsModule`. This is transparent and easy to diff.

3. **`_destroyedThisFrame` HashSet in `PhysicsBodyLifecycleSystem`**: prevents re-creation of a body for an entity that received a `DestructionOrder` in the same frame. Without this, the entity (still alive in the ECS because `EntityRepository.DestroyEntity` is deferred) would be found by the creation query and a new body would be created immediately after the old one was torn down.

4. **`IPhysicsBodyService` seam design**: `CreateBody` takes `entity`, `shapeKind`, `dims`, and `initialPose` — the concrete implementation will use `entity` for naming/diagnostics, `shapeKind`+`dims` for shape construction, and `initialPose` for the initial physics body world transform. The handle is opaque; `RemoveBody(handle)` is the only teardown API needed.

5. **`CrowdAgentUpdateSystem` included as-is (P2-T4 deferral)**: the system still writes `SimTransform.Position` (integrates velocity×dt). This was explicitly called out as a P2-T4 deferral in the design. Recorded as **STR-D12**.

---

## Deviations

None. All deviations from the batch spec are documented design choices:
- `CrowdAgentUpdateSystem` P2-T4 deferral: explicitly stated in spec ("its velocity-only refactor is P2-T4, not now").
- No concrete `BulletPhysicsBodyService`: headless Bullet is not feasible; seam approach was the conditional fallback specified in the batch.

---

## Test Results

```
Hrot.Stride.Core.Tests    : Passed 88/88 (was 65/65 in BATCH-03)
  → +14 T1 unit tests (StrideKinematicsModuleTests)
  → +9  T2 unit tests (PhysicsBodyLifecycleSystemTests)

Hrot.Stride.Animation.Tests : Passed 4/4 (unchanged)

HrotStrideApp.Game.Tests  : Passed 24/24 (was 17/17 in BATCH-03)
  → +7  T1 integration tests (StrideKinematicsModuleIntegrationTests)

TOTAL: 116 green (was 86 in BATCH-03), 0 failed, 0 skipped
```

### Key assertions that prove real behavior

**T1 — StrideKinematicsModule:**
- System lists contain/exclude by **actual runtime type** (`s is SpatialHashSystem`, etc.), not name strings.
- `PostSimulationSystems` has exactly 1 entry (no integrators).
- `DeadReckoningSyncSystem.DriveFromNetwork` is `false` (property value asserted, not just non-null).
- **Position-unchanged integration test**: spawns owned entity with `Linear = (10,0,0)` velocity, pumps 10 extra frames after spawn, asserts `X` unchanged within 0.01 m tolerance. Before P1 (`LinearKinematicsSystem` registered), X would advance ~1.67 m. After P1, X stays fixed.
- `DeadReckoning_DoesNotMutate_OwnedEntityTransform`: same invariant specifically for the DR system path.

**T2 — PhysicsBodyLifecycleSystem:**
- Shape kind/dims passed to `CreateBody` match exactly the values in the TKB descriptor (Capsule: r=0.3 h=1.8; Box: hx=1.0 hy=0.5 hz=2.0). Not "not null" — exact float values.
- `RemoveBody` call carries the **exact same handle** that `CreateBody` returned.
- Idempotency: second `Execute` produces exactly 1 `Creates` call (not 2).
- Destruction guard: `DestructionOrder` event tears down the body and the entity is NOT re-created in the same frame.

---

## Developer Insights

1. **Headless-Bullet investigation cost**: The feasibility probe took significant exploration — the `Simulation` ctor is internal (in XML docs but not public), `AddRigidBody` etc. are internal, and `OnAttach()` requires `PhysicsProcessor.AssociatedData`. The probe program conclusively ruled out direct headless Bullet stepping. The seam decision is solid.

2. **`_destroyedThisFrame` bug**: This bug would have been invisible in a GPU integration test because there `EntityRepository.DestroyEntity` is called immediately before/after the `DestructionOrder` event, making the entity dead in the ECS. In headless tests the entity stays alive between steps, exposing the re-create race. The fix is robust in both cases.

3. **`PhysicsBodyLifecycleSystem.Execute` sequence ordering**: Destructions must run before revocations before creations. The current ordering (D → R → C) is correct. If creations ran before destructions, a destroyed entity could get a new body. If revocations ran before destructions, a destruction+revocation in the same frame could cause double-teardown (idempotent — `TryGetValue` guards it).

4. **`CrowdAgentUpdateSystem` position integration fight**: In Phase 1, if any entity has a `CrowdAgent` component and a navigation target, the `CrowdAgentUpdateSystem` will still integrate `tf.Position += velocity * dt`. When P1-T5 (reverse-sync) is wired, this will fight the Bullet-sourced position. The fix is P2-T4. Note that none of the UrbanCombat demo templates are `CrowdAgent`-typed, so this doesn't affect the current demo.

5. **`StrideVisualBindingSystem.Sync` must be called before `PhysicsBodyLifecycleSystem.Execute`**: `PhysicsBodyLifecycleSystem` reads `_visualBindingSystem.Visuals` to get the shape. If a visual hasn't been created yet (e.g. entity spawned but sync not yet run), body creation is silently skipped and retried next frame. This is the correct behavior per design §5.6.

---

## Known Issues

- **STR-D11** (OPEN, added this batch): Concrete `BulletPhysicsBodyService` is not implemented. The lifecycle logic is fully tested via the seam. The concrete implementation awaits GPU bring-up in `HrotStrideApp.Game`.
- **STR-D12** (OPEN, added this batch): `CrowdAgentUpdateSystem` still integrates `SimTransform.Position`. Fix deferred to P2-T4.
- **STR-D4** (PARTIAL, carried from BATCH-03): GPU render unverified. No change.
- **STR-D9, STR-D10** (OPEN, carried from BATCH-03): Procedural visuals are mesh-less; `Content.Load` failures swallowed. No change.

---

## Suggested Commit Message

```
feat(stride): StrideKinematicsModule + PhysicsBodyLifecycleSystem seam (BATCH-04)

Completes STR-P1-T1, STR-P1-T2 — Phase 1 kinematic foundations
- StrideKinematicsModule: SpatialHash/FormationTarget/VehicleCommand/
  NavigationExecution/CrowdAgentUpdate (Simulation) + DeadReckoningSyncSystem
  (DriveFromNetwork=false, PostSimulation); CarKinematics+LinearKinematics+
  TerrainQueryPipeline excluded by topological omission
- EditorStrideSubsystem: recomposed around StrideKinematicsModule; FDP integrators
  topologically absent; combat/damage/nav-bridge systems preserved; proven by
  position-unchanged integration test (10 m/s velocity, 10 frames, ΔX < 0.01 m)
- HEADLINE (shapes BATCH-05/06): Stride.Physics.Simulation ctor + AddRigidBody/
  RemoveRigidBody/Simulate are all internal to PhysicsProcessor; headless Bullet
  stepping not feasible → IPhysicsBodyService seam (mirrors BATCH-03 IStrideVisualFactory)
- IPhysicsBodyService + PhysicsBodyReference + PhysicsBodyLifecycleSystem:
  authority-keyed (WithOwned/WithoutOwned/DestructionOrder); shape from
  StrideVisualReference; _destroyedThisFrame guard prevents same-frame re-create
Tests: 116 total (88 Core, 4 Animation, 24 Game); +30 new; all green
```
