# BATCH-04: StrideKinematicsModule + PhysicsBodyLifecycleSystem (Phase 1 start)
**Tasks:** STR-P1-T1, STR-P1-T2   **Phase:** P1 (Bullet movement + reverse-sync)   **Est:** ~9–11h
**Dependencies:** Phase 0 complete (BATCH-01/02/03): `FdpStrideTransform`, `EditorStrideSubsystem`, `StrideVisualBindingSystem` + `StrideVisualReference` (carries resolved `ShapeKind`+`ShapeDims`).

Goal: (T1) build `StrideKinematicsModule` — the kept spatial/command/nav systems with the **FDP integrators omitted** — and wire it into `editor_stride` so movement is no longer FDP-integrator-driven; (T2) build `PhysicsBodyReference` + `PhysicsBodyLifecycleSystem` that reactively creates/destroys Bullet bodies on the **authority bit**, using the shape from `StrideVisualReference`/`StrideRenderModelDefDto`. **This batch's pivotal unknown — resolve it first in T2:** *can Stride's Bullet `Simulation` + collider shapes + bodies be created and stepped headlessly (no `GraphicsDevice`)?* Bullet is CPU-side, so this may well be possible; the answer decides how all of Phase 1 is tested.

There is **no Corrective Task 0** — BATCH-03 was approved with no P1 issues. Carried context: STR-D4 (real GPU render unverified — does not block this batch), STR-D5 (togglable groups — relevant when T5 reverse-sync lands, not here).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract (test-quality binding).
2. `.dev/stride-1/Stride-Integration_v0_3.md` §5.1 (kept/excluded systems table), §5.2 (`StrideKinematicsModule`), §5.4 (`DeadReckoningSyncSystem` `DriveFromNetwork=false`), §5.5 (terrain-query pipeline excluded), §5.6 (`PhysicsBodyLifecycleSystem`), §6.1 (Simulation config / velocity invariant — context), §6.5 (shape source).
3. `.dev/stride-1/TASK-DETAIL.md` — STR-P1-T1, STR-P1-T2 (success conditions authoritative).
4. `reviews/BATCH-03-REVIEW.md` + `DEBT-TRACKER.md`.

Use the **codebase-memory MCP first** (project `D-Work-IOS-IG-SimHost-FDP`).

### Verified facts & exact references
- **System membership** (read both): `GroundKinematicsModule` ([GroundKinematicsModule.cs](../../../FDP/Toolkits/Fdp.Toolkits/CarKinem/Modules/GroundKinematicsModule.cs)) — Sim = `SpatialHashSystem`, `FormationTargetSystem`, `VehicleCommandSystem`, `NavigationExecutionSystem`; PostSim = `CarKinematicsSystem`, `LinearKinematicsSystem` ← **the two integrators to OMIT**. `SimHostCoreLogicPack` ([SimHostCoreLogicPack.cs](../../../Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs)) bundles CombatModule + DamageAssessmentModule + nav-bridge systems + `GroundKinematicsModule` + UnitHierarchySystem + EqsResultUpdateSystem. **`StrideKinematicsModule` replaces the `GroundKinematicsModule` role** (kept systems minus the two integrators); the combat/damage/nav-bridge systems still run.
- **Kept systems for `StrideKinematicsModule`** (design §5.1/§5.2): `SpatialHashSystem`, `FormationTargetSystem`, `VehicleCommandSystem`, `NavigationExecutionSystem`, the existing `CrowdAgentUpdateSystem` ([CrowdAgentUpdateSystem.cs](../../../FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/CrowdAgentUpdateSystem.cs) — its velocity-only refactor is **P2-T4, not now**), and `DeadReckoningSyncSystem` ([DeadReckoningSyncSystem.cs](../../../Hrot/Engine/Hrot.Core/Systems/Common/DeadReckoningSyncSystem.cs)) constructed with **`DriveFromNetwork = false`** ([VERIFY] the exact ctor param). **Omit** `CarKinematicsSystem`, `LinearKinematicsSystem`, and the terrain-query pipeline (`TerrainQuerySubmitSystem`/`TerrainQuerySolverSystem`/`TerrainQueryResolutionSystem`, §5.5).
- **[VERIFY] the `IEcsModule` registration API** and how the existing modules expose phase-typed system lists (mirror `GroundKinematicsModule`'s `SimulationSystems`/`PostSimulationSystems` shape).
- **`DestructionOrder`** event: `Fdp.Toolkit.Lifecycle.Events.LifecycleEvents.DestructionOrder` ([LifecycleEvents.cs](../../../FDP/Toolkits/Fdp.Toolkits/Lifecycle/Events/LifecycleEvents.cs)) — [VERIFY] its exact shape/fields.
- **Authority queries** `.WithOwned<T>()`/`.WithoutOwned<T>()`/`.Without<T>()` in `Fdp.Core.QueryBuilder` (confirmed); authority in `EntityMetadataCold.AuthorityMask`.
- **Shape source for the body:** `StrideVisualReference` (BATCH-03, in `Hrot.Stride.Core`) already carries the resolved `ShapeKind` + `ShapeDims` per entity. `PhysicsBodyLifecycleSystem` reads it (don't re-resolve the descriptor). `CollisionShapeKind` is in `Fdp.Toolkit.Tkb.Domain`.
- `Hrot.Stride.Core` already references `Stride.Physics` (BATCH-01).

**Complete tasks in sequence; do NOT start T2 until T1 is implemented, tested, and ALL tests (incl. Phase 0's) pass.** Work autonomously. Only stop on a genuine breaking design flaw or unrecoverable blocker.

---

## Task 1: `StrideKinematicsModule` (STR-P1-T1)
**File:** `Stride/Hrot.Stride.Core/StrideKinematicsModule.cs` (NEW). Spec: design §5.1–§5.2, §5.4, §5.5.
Build `StrideKinematicsModule` (an `IEcsModule`, or the phase-typed-list shape the composition uses — match how `EditorStrideSubsystem` consumes packs) that registers the **kept** systems and **omits** `CarKinematicsSystem`/`LinearKinematicsSystem` and the terrain-query pipeline. Include `DeadReckoningSyncSystem` with `DriveFromNetwork = false`. Entities still carry `SimTransform`/`SimVelocity` — exclusion is **topological** (which systems are registered), never component removal.
Then **wire it into `EditorStrideSubsystem`** in place of the integrator-bearing path (the BATCH-02 seam comment at `EditorStrideSubsystem.cs:207-211`): the FDP integrators must no longer be registered, while combat/damage/nav-bridge systems continue to run. Choose the cleanest recomposition (e.g. register `StrideKinematicsModule` + `CombatModule`/`DamageAssessmentModule`/nav-bridge individually instead of the whole `SimHostCoreLogicPack`) and explain it in the report.

**Tests required:**
- **Membership (unit):** the module's registered systems **include** `SpatialHashSystem`, `FormationTargetSystem`, `VehicleCommandSystem`, `NavigationExecutionSystem`, `CrowdAgentUpdateSystem`, and `DeadReckoningSyncSystem`; and **exclude** `CarKinematicsSystem` and `LinearKinematicsSystem`. Assert against the real system instances/types, not names in a string.
- **Terrain pipeline absent (unit):** none of `TerrainQuerySubmitSystem`/`TerrainQuerySolverSystem`/`TerrainQueryResolutionSystem` is registered.
- **Integrators off (integration, via `EditorStrideSubsystem`):** spawn an owned entity, set a non-zero `SimVelocity.Linear`, pump several frames, and assert its `SimTransform.Position` is **unchanged** (no FDP integrator moves it now — Bullet isn't wired until T2–T5). This proves the integrators are topologically gone.
- **Components retained:** the spawned entity still has both `SimTransform` and `SimVelocity`.
- **DeadReckoning (unit):** with `DriveFromNetwork=false`, an **owned** entity is **not** dead-reckoning-smoothed (assert the owned entity's transform isn't mutated by `DeadReckoningSyncSystem`). [VERIFY] how to drive/observe that system in isolation.

## Task 2: `PhysicsBodyReference` + `PhysicsBodyLifecycleSystem` (STR-P1-T2)
**Files:** `Stride/Hrot.Stride.Core/PhysicsBodyReference.cs`, `Stride/Hrot.Stride.Core/PhysicsBodyLifecycleSystem.cs` (NEW). Spec: design §5.6.

**FIRST — resolve the headless-Bullet question and report it.** Determine whether a Stride `Stride.Physics.Simulation` (and `ColliderShape`/`RigidbodyComponent`/`CharacterComponent`) can be **instantiated and stepped without a `GraphicsDevice`** (Bullet is CPU-only via BulletSharp, so this is plausible). [VERIFY] the `Simulation` access point/construction. Then choose:
- **If headless Bullet works:** build `PhysicsBodyLifecycleSystem` against the real `Simulation`, and test body creation/teardown with the real bodies. Preferred — highest fidelity.
- **If it does NOT work headlessly:** abstract body creation/removal behind an interface seam (e.g. `IPhysicsBodyService` with `CreateBody(shape, dims, in SimTransform) → handle`, `RemoveBody(handle)`), mirroring the BATCH-03 `IStrideVisualFactory` approach, so the **lifecycle/authority logic** is tested headlessly with a recording fake while the concrete Bullet implementation lives in the Game project (exercised on a real sim/GPU). 

Either way:
- `PhysicsBodyReference` = a shadow component (or parallel-dictionary entry, matching how `StrideVisualReference` is stored) mapping ECS entity ↔ Bullet body handle.
- `PhysicsBodyLifecycleSystem` runs **pre-physics** and is keyed on the authority bit:
  - Creation: `.WithOwned<SimTransform>().Without<PhysicsBodyReference>()` → build the body from the entity's `StrideVisualReference` shape (`Capsule` → capsule shape; `OrientedBox` → box shape; size from `ShapeDims`), add `PhysicsBodyReference`.
  - Revocation: `.WithoutOwned<SimTransform>()` with a `PhysicsBodyReference` → remove body + ref.
  - Destruction: consume `DestructionOrder` events → tear down body + remove ref.

**Tests required** (real bodies if headless Bullet works; recording fake otherwise — assert real state/calls):
- `.WithOwned<SimTransform>().Without<PhysicsBodyReference>()` entity → body created + `PhysicsBodyReference` added (assert the ref exists and points at a body).
- Authority revoked (entity becomes `.WithoutOwned` while holding a ref) → body removed + ref removed.
- `DestructionOrder` consumed → body torn down + ref removed.
- Capsule shape built for `ShapeKind=Capsule`; oriented box for `OrientedBox` (assert the shape kind/dimensions handed to the body match the `StrideVisualReference` dims).
- Idempotency: a second pre-physics pass does not double-create a body for an entity that already has a `PhysicsBodyReference`.

---

## Success Criteria
- [ ] STR-P1-T1: `StrideKinematicsModule` built with correct membership (5 kept, 2 integrators excluded, terrain pipeline excluded, `DeadReckoningSyncSystem` `DriveFromNetwork=false`); wired into `EditorStrideSubsystem` so FDP integrators no longer move owned entities (proven by the "integrators off" integration test); `SimTransform`/`SimVelocity` retained.
- [ ] STR-P1-T2: headless-Bullet feasibility determined + documented; `PhysicsBodyReference` + `PhysicsBodyLifecycleSystem` create/revoke/destroy bodies on the authority bit using `StrideVisualReference` shapes; all lifecycle tests pass.
- [ ] Full test suite green (Phase 0 + this batch); Stride solution builds clean (no new warnings beyond pre-existing NU1608); report submitted.

## Report Requirements (`reports/BATCH-04-REPORT.md`)
Answer: **the headless-Bullet-Simulation finding** (can a `Simulation`/colliders/bodies be created+stepped without a GraphicsDevice? exact construction API; what you chose — real sim vs seam — and why) — this is the headline that shapes BATCH-05/06; the `EditorStrideSubsystem` recomposition you used to turn the integrators off while keeping combat/perception, and any friction; how you proved "integrators off" (the position-unchanged test); the `DestructionOrder` shape and `DeadReckoningSyncSystem` `DriveFromNetwork` ctor ([VERIFY] results); whether `CrowdMotorIntent` needed to exist yet (it's first consumed in T3 — note if you created it); weak points; suggested one-line commit message. Report actual test counts/output. Do NOT ask comprehension questions.
