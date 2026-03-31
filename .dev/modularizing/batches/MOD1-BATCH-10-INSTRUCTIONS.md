# MOD1-BATCH-10: Deferred Phase 6 Translator Packs + High-Priority Debt Resolution

**Batch Number:** MOD1-BATCH-10  
**Tasks:** DB-MOD1-03, DB-MOD1-07, DB-MOD1-10, DB-MOD1-21, MOD1-P6T4, MOD1-P6T5, MOD1-P6T6, MOD1-P6T7, MOD1-P6T8  
**Phase:** Phase 6 completion + Debt burndown  
**Estimated Effort:** 10-12 hours  
**Priority:** HIGH  
**Dependencies:** MOD1-BATCH-09

---

## 📋 Onboarding & Workflow

### Who You Are
You are a developer implementing the modularization of the IOS-IG-SimHost application. Read this section entirely before touching code.

### Project Goal
Refactoring towards better modularization and generalization. **What should be generic must come under FDP, not be left in the Hrot domain.** Phase 6 was partially delivered — the modules exist but the translator packs and the BTreeContext stub migrations were skipped. This batch completes Phase 6 and clears several high-priority debt items.

### Non-Negotiable Rules
1. **Application must keep working.** `Hrot.ClusterRunner -x all` integration tests must pass after every task.
2. **Tests must check real behaviour** — verify observable outcomes, not call counts.
3. **`FDP.*` assemblies may never reference `Hrot.*` assemblies.**
4. **Component IDs belong in toolkit-local registries** — never add IDs to `GlobalComponentIds` directly; use the appropriate `*ComponentIds` class.
5. **Do not modify third-party submodules** under `FDP\ExtDeps\`.

### Required Reading (IN ORDER)
1. `.dev-workstream/README.md` — developer workflow
2. `docs/modularizing/MOD1-DESIGN.md` — Phase 6 §3.6 (especially §3.6.6 BTreeContext Cleanup, §3.6.7 Route Handle Pattern, §3.6.8 NavigationSolverModule, §3.6.9 Translator Packs)
3. `docs/modularizing/MOD1-TASK-DETAIL.md` — MOD1-P6T4 through MOD1-P6T8
4. `.dev-workstream/reviews/MOD1-BATCH-09-REVIEW.md` — previous review
5. `docs/modularizing/MOD1-DEBT-TRACKER.md`

### Source Code Locations
- **Phase 6 work:** `FDP/Toolkits/FDP.Toolkit.Behavior/`, `FDP/Toolkits/FDP.Toolkit.Physics/`, `FDP/Toolkits/FDP.Toolkit.Navigation/`
- **Translator packs:** `Hrot.SimHost/Network/`
- **Debt targets:** `Hrot.SimHost.Tests/`, `Hrot.NED/`

### Report Submission
`.dev-workstream/reports/MOD1-BATCH-10-REPORT.md`

---

## 🚨 DEBT FIXES (Complete These First)

### DB-MOD1-03: Audit `NetworkOwnership.PrimaryOwnerId` Residue

Early in the project, ownership was tracked via a `PrimaryOwnerId` field on `NetworkOwnership`. The standard was later changed to use `WithOwned<T>()` queries, but the old field may still be read in some systems, creating a two-path ownership model that can produce inconsistent behaviour.

**What to do:**
- `grep -r "PrimaryOwnerId" --include="*.cs"` across the entire solution.
- For every system that reads `PrimaryOwnerId` instead of using `WithOwned<T>()`, replace the access with the correct ECS ownership query.
- Verify that no system writes `NetworkOwnership.PrimaryOwnerId` directly (it should only be written by the replication ingress translator).
- Document the list of changed systems in the report.
- All tests must continue to pass after each change.

### DB-MOD1-07: Fix `EntityMasterEgressTranslatorTests` CycloneDDS Daemon Dependency

The test currently fails when the CycloneDDS daemon is not available because it tries to create a real DDS participant. The fix is to introduce a `DdsParticipantFactory` seam (or use an existing test double) so the test can run without a live daemon.

**What to do:**
- Identify the exact test(s) failing due to CycloneDDS daemon absence.
- Introduce a mock/stub DDS participant for the test scope, or gate the test with `[Trait("Category", "Integration")]` and skip it in unit test runs.
- Ensure the test validates the actual translator serialization logic (field mapping, key assignment) using an in-memory stub.

### DB-MOD1-10: Fix Missing DDS Participant Cleanup in Component Registry Tests

Component registry tests that create real `DdsParticipant` instances without disposing them can cause domain-ID collisions when the full test suite runs in parallel, producing intermittent failures in unrelated tests.

**What to do:**
- Identify all component registry test classes that construct a `DdsParticipant` (or any DDS entity) without calling `Dispose()`.
- Wrap each in an `IDisposable` teardown (`xUnit` `IDisposable.Dispose()` or `[ClassCleanup]`) that calls `participant.Dispose()`.
- Alternatively, introduce a `using var participant = ...` scope per test method.
- After the fix, run the full suite 3× in parallel to confirm the flakiness is gone.

### DB-MOD1-21: Verify `TestMetricsCollector` Has No Hrot References

`TestMetricsCollector.cs` appeared in `FDP.Framework.Runner.Testing` without being in the design spec.

**What to do:**
- Open `FDP/Framework/FDP.Framework.Runner/Testing/TestMetricsCollector.cs`.
- Confirm zero `Hrot.*` using statements or references.
- Add a brief description of its purpose and usage to `docs/modularizing/MOD1-DESIGN.md` §3.9.4.
- If it has any `Hrot.*` references, move those parts to `Hrot.ClusterRunner`.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **DB-MOD1-03:** Audit and replace `PrimaryOwnerId` usages → **ALL tests pass** ✅
2. **DB-MOD1-07:** Fix CycloneDDS daemon test → **ALL tests pass** ✅
3. **DB-MOD1-10:** Fix DDS participant cleanup in component registry tests → **no parallel flakiness** ✅
4. **DB-MOD1-21:** Verify `TestMetricsCollector` → no Hrot references ✅
5. **MOD1-P6T4:** Delete `BTreeContext.RequestRaycast`/`GetRaycastResult` stubs → **ALL tests pass** ✅
6. **MOD1-P6T5:** Delete `BTreeContext.RequestPath`/`GetPathResult` stubs → **ALL tests pass** ✅
7. **MOD1-P6T6:** `AutonomousPerceptionModule` + `PhysicsQueryModule` → **ALL tests pass** ✅
8. **MOD1-P6T7:** `NavigationSolverModule` → **ALL tests pass** ✅
9. **MOD1-P6T8:** Four translator packs + `NodeBootstrapper` wiring → **`-x all` integration tests pass** ✅

---

## ✅ Phase 6 Tasks

### Task 1: MOD1-P6T4 — Delete `BTreeContext` Raycast Stubs

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P6T4](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t4--wire-btreecontextrequestraycast--getraycastresult-to-raycastbatchdata)

**⚠️ CRITICAL — READ THIS FIRST:** The design (§3.6.6) has been updated since the original task was written. The task title says "wire to `RaycastBatchData`" but the correct action is **DELETION**, not wiring. The circular dependency problem means `BTreeContext` (in `FDP.Toolkit.Behavior`) must NOT reference `RaycastBatchData` (in `FDP.Toolkit.Physics`).

**What to do:**
- Remove `RequestRaycast` and `GetRaycastResult` from `BTreeContext.cs`.
- Remove them from `IAIContext` if they exist there (but remember: `IAIContext` is in `Fbt.Kernel`, a third-party submodule. If it is there, implement them as throwing `NotSupportedException` in `BTreeContext` — do NOT modify the submodule).
- Concrete BTree nodes that need raycasts must already subclass `PhysicsQueryActionNode` (from `FDP.Toolkit.Physics`) which provides `RequestRaycast` / `GetRaycastResult` via `RaycastBatchData` — confirm this is in place.
- Update any affected tests.

### Task 2: MOD1-P6T5 — Delete `BTreeContext` Pathfinding Stubs

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P6T5](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t5--wire-btreecontextrequestpath--getpathresult-to-pathfindingbatchdata)

Same rule as P6T4: **DELETION** not wiring. `BTreeContext` must not reference `PathfindingBatchData` (in `FDP.Toolkit.Navigation`). Concrete nodes subclass `PathfindingActionNode` from `FDP.Toolkit.Navigation` instead.

### Task 3: MOD1-P6T6 — `AutonomousPerceptionModule` + `PhysicsQueryModule`

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P6T6](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t6--create-autonomousperceptionmodule-and-physicsquerymodule)

**Key constraint:** Both modules must already exist from BATCH-06/07 work. Verify they are fully wired and their success conditions (from the task detail) are met. If any success condition is not met, implement it now.

**Key things to verify:**
- `AutonomousPerceptionModule.Policy` is `ExecutionPolicy.SlowBackground(10)`.
- All 4 systems are registered exclusively via `ISystemRegistry` (no public field exposure).
- `PhysicsQueryModule.Policy` is `ExecutionPolicy.Synchronous`.
- `NodeBootstrapper` registers these modules for the appropriate roles (`Perception`, `AllInOne`).

### Task 4: MOD1-P6T7 — `NavigationSolverModule`

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P6T7](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t7--create-navigationsolvermodule)

**Assembly:** `FDP.Toolkit.Navigation`. Contains `PathfindingSolverSystem` which reads `PathfindingBatchData.Requests`, queries `RoadNetworkBlob`, calls `TrajectoryPoolManager.RegisterTrajectory`, and writes `PathfindingBatchData.Results`. Module is registered by `NodeBootstrapper` for `NavigationSolver` and `AllInOne` roles.

### Task 5: MOD1-P6T8 — Four Translator Packs + `NodeBootstrapper` Wiring

**Task Definition:** See [MOD1-TASK-DETAIL.md §MOD1-P6T8](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p6t8--create-perception--pathfinding-translator-packs)

**The four packs (all in `Hrot.SimHost.Network`):**

| Pack | Role | Direction |
|---|---|---|
| `BrainPerceptionTranslatorPack` | Brain / AllInOne | SensorConfig egress, RaycastBatch egress, SensorTargets ingress, RaycastBatch ingress |
| `SimPerceptionTranslatorPack` | Perception / AllInOne | SensorConfig ingress, RaycastBatch ingress, SensorTargets egress, RaycastBatch egress |
| `BrainPathfindingTranslatorPack` | Brain / AllInOne | PathRequest egress, PathResponse ingress |
| `SimPathfindingTranslatorPack` | NavigationSolver / AllInOne | PathRequest ingress, PathResponse egress |

Translator implementations may be **stubs** (log-and-discard) so long as the pack structure compiles and is wired into `NodeBootstrapper`. The DDS descriptors for these topics already exist from P6T2.

`NodeBootstrapper` updated:
- `NodeRole.Brain` / `AllInOne` → `BrainPerceptionTranslatorPack` + `BrainPathfindingTranslatorPack`
- `NodeRole.Perception` / `AllInOne` → `SimPerceptionTranslatorPack`
- `NodeRole.NavigationSolver` / `AllInOne` → `SimPathfindingTranslatorPack`

---

## 📊 Report Requirements

`.dev-workstream/reports/MOD1-BATCH-10-REPORT.md`

**Developer Insights**

**Q1:** For DB-MOD1-03 — how many systems were still reading `PrimaryOwnerId` directly? List them. Were any cases genuinely intentional (i.e. the `WithOwned<T>()` pattern does not cover the use case)?

**Q2:** For P6T4/P6T5 — did `BTreeContext` still have the raycast/pathfinding stubs, or had they already been removed in a prior batch? What is the current state of `IAIContext` in `Fbt.Kernel` regarding these methods?

**Q3:** For P6T8 — how did you handle the `AllInOne` role registering both Brain and Sim translator packs? Are there any ordering concerns when both packs subscribe to the same DDS topic?

**Q4:** Were there any new circular dependency risks discovered when completing the Phase 6 translator pack work?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `EntityMasterEgressTranslatorTests` passes without a live CycloneDDS daemon.
- [ ] All `PrimaryOwnerId` direct reads replaced with `WithOwned<T>()` queries; no regressions.
- [ ] Component registry tests dispose their `DdsParticipant` instances; parallel runs are stable.
- [ ] `TestMetricsCollector` has zero `Hrot.*` references; its purpose is documented in the design doc.
- [ ] `BTreeContext` has no `RequestRaycast`, `GetRaycastResult`, `RequestPath`, or `GetPathResult` methods.
- [ ] `AutonomousPerceptionModule` and `PhysicsQueryModule` meet all P6T6 success conditions.
- [ ] `NavigationSolverModule` is in `FDP.Toolkit.Navigation` and registered for `NavigationSolver`/`AllInOne` roles.
- [ ] All four translator packs compile and are registered in `NodeBootstrapper` for their respective roles.
- [ ] `Hrot.ClusterRunner -x all` integration tests pass unconditionally.
- [ ] All unit and integration test suites pass with 0 failures.
