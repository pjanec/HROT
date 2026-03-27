# MOD1-BATCH-12: Debt Burndown — All Remaining Actionable Items

**Batch Number:** MOD1-BATCH-12  
**Tasks:** DB-MOD1-26 (P1), DB-MOD1-02, DB-MOD1-04, DB-MOD1-05, DB-MOD1-06, DB-MOD1-09, DB-MOD1-12, DB-MOD1-18, DB-MOD1-23, DB-MOD1-25  
**Phase:** Full debt burndown  
**Estimated Effort:** 14-16 hours  
**Priority:** HIGH (P1 bug must be fixed first)  
**Dependencies:** MOD1-BATCH-11

---

## 📋 Onboarding & Workflow

### Who You Are
You are a developer implementing the modularization of the IOS-IG-SimHost application. Read this section entirely before touching code.

### Non-Negotiable Rules
1. **Application must keep working.** All integration tests must pass after every task.
2. **Tests must check real behaviour** — verify observable outcomes, not call counts.
3. **`FDP.*` assemblies may never reference `Bagira.*` assemblies.**
4. **Do not modify third-party submodules** under `FDP\ExtDeps\`.
5. **The two failing integration tests from BATCH-11 are P1. They must be the first thing fixed. No code review will pass with failing tests.**

### Deferred Items (DO NOT TOUCH)
- **DB-MOD1-19** (`DrillSlave` 2PC): blocked — drill state machine design not ready.

### Required Reading (IN ORDER)
1. `.dev-workstream/README.md`
2. `.dev-workstream/reviews/MOD1-BATCH-11-REVIEW.md` — read the corrected root-cause for DB-MOD1-26
3. `docs/modularizing/MOD1-DEBT-TRACKER.md`

### Report Submission
`.dev-workstream/reports/MOD1-BATCH-12-REPORT.md`

---

## 🚨 TASK 1 (P1 — MANDATORY FIRST): DB-MOD1-26 — Diagnose and Fix Failing Drag/GeoSpatial Tests

### How GeoSpatial Dirty Detection Works
`GeoSpatialEgressTranslator.ScanAndPublish` does **not** use `SmartEgressUtil`. It compares `SimTransform.Position` against `NetworkTransform.LastPosition` (a shadow `Vector3` stored on the ECS entity, line 98 of `GeoSpatialEgressTranslator.cs`). If the position has moved more than 1 cm², it publishes immediately on the next frame. No `MarkDirty` call is needed.

This means the fix is NOT about adding a `MarkDirty` call. The tests are failing because either:
- Spawned entities are **missing the `NetworkTransform` shadow component** — excluded from the translator's query entirely.
- **`DescriptorOwnership` for `dtGeoSpatial`** is not set for locally-owned entities — the `HasAuthority(entity, GeoSpatialOrdinal)` guard at line 91 skips them.
- For `DragDropIntegrationTests` (full round-trip path): `UpdateEntityDescriptorRequestSystem` is not applying the position change or the response is not published within the timeout window.

### What to do
1. Run both failing tests with `dotnet test --logger "console;verbosity=detailed"` to capture output. The `DragDropIntegrationTests` already log `[D2c]` (NetworkAuthority) and `[D2d]` (HasAuthority for GeoSpatial) diagnostics — read those lines first.
2. Identify the exact failure point from the logs.
3. Fix the root cause. Do NOT add a `SmartEgressUtil.MarkDirty` call — that is the wrong mechanism for GeoSpatial.
4. Both `SimHostDrag_IgReceivesPositionUpdateWithinFewFrames` and all `DragDropIntegrationTests` must pass.

---

## TASK 2: DB-MOD1-02 — Compile-Time Uniqueness Guard for `GlobalComponentIds`

The 20–49 toolkit ID block is full. There is currently no compile-time check preventing two components from accidentally sharing the same ID.

**What to do:**
- In `Fdp.Kernel.Tests`, add a unit test `ComponentIdAttributeTests.GlobalComponentIds_NoToolkitBlockDuplicates` that uses reflection to enumerate all `const byte` fields on `GlobalComponentIds` and asserts each value appears exactly once.
- Also add an equivalent test for `BagiraComponentIds`.
- If any duplicate IDs are discovered during this exercise, flag them in the report (Q1) — do not silently fix them.

---

## TASK 3: DB-MOD1-04 — Fix `System_AvoidanceMovesVehicle` Vacuous Test

**What to do:**
- Open the `System_AvoidanceMovesVehicle` test.
- Add `world.SetAuthority<SimTransform>(entity, true)` (or equivalent) before the system tick.
- Replace the vacuous assertion with `Assert.True(Vector3.Distance(before, after) > 0.01f)`.
- Temporarily remove the `SetAuthority` call, re-run, and confirm the test now **fails** — then restore it. This proves the test is actually gating the right behaviour.

---

## TASK 4: DB-MOD1-05 — Lazy-Register `BrainHsm64` to Skip Empty Frames

`BrainHsm64` updates every frame regardless of whether any entity has an active HSM doctrine.

**What to do:**
- Check the `BrainHsm64` system update method. If the entity query returns zero results, the system should early-exit without cost.
- If the system is registered unconditionally in a tight loop, consider gating its loop-body behind `if (query.IsEmpty) return;`.
- This is a low-risk performance micro-optimization. Add a comment documenting the intent. No new test required — existing tests covering HSM behaviour must continue to pass.

---

## TASK 5: DB-MOD1-06 — Lazy-Allocate in `GroundKinematicsModule`

`GroundKinematicsModule` unconditionally allocates `TrajectoryPoolManager` and `FormationTemplateManager` at construction time, even for roles that do not need them (e.g. pure navigation solver nodes).

**What to do:**
- Change construction of `TrajectoryPoolManager` and `FormationTemplateManager` inside `GroundKinematicsModule` (or its factory) to be lazy — only instantiate when the module is actually registered for a role that uses them.
- `SimulationLogicModule` already gates module creation by role (from DB-MOD1-08 fix). Verify that the pattern is consistent — `GroundKinematicsModule` should not be constructed for roles that don't need it.
- Existing tests must pass.

---

## TASK 6: DB-MOD1-09 — Unify `NodeConfiguration` and `SimHostConfig`

Two config types serve overlapping roles. **What to do:**
- Compare all fields side-by-side and choose a unification strategy (see BATCH-12 instructions for options).
- For simplicity, prefer whichever type is used in more places and absorb the other into it.
- Update all callers. All tests must pass.
- Document the strategy and changed field list in the report (Q2).

---

## TASK 7: DB-MOD1-12 — Wire `IgPresentationModule` to Real `SstVisualizerAdapter`

**What to do:**
- Locate `IgPresentationModule` construction in `IgApplication`.
- Replace the headless `MapCanvas` fallback with `new SstVisualizerAdapter(...)` for production mode.
- Guard with a `headless` flag or factory so integration tests (headless) still use the stub.
- Add a test asserting the production path creates a non-stub `IMapCanvas`.

---

## TASK 8: DB-MOD1-18 — Strengthen `SeekToFrameAsync` Off-Thread Test

**What to do:**
- In `ReplayModule_SeekToFrameAsync_IsOffMainThread`: immediately after calling `SeekToFrameAsync`, assert `task.IsCompleted == false` before `await`ing it.
- This proves the task was genuinely running asynchronously, not completing synchronously on the calling thread.

---

## TASK 9: DB-MOD1-25 — Dirty-Flag `HealthData` Sync in `DamageSystem`

**What to do:**
- In `DamageSystem`, before writing `HealthData.Current`, compare the new `health.Current` to the existing `HealthData.Current`. Only write if the value has changed.
- Add a test that ticks `DamageSystem` twice with the same damage applied and asserts that `HealthData` is only dirty-written once (if your ECS supports dirty tracking), or use a counter mock to verify the write count.

---

## TASK 10: DB-MOD1-23 — Introduce `FDP.Toolkit.Navigation.Contracts` Thin Assembly

### Why
`NavigationIntent` (ID 67) and `NavigationStatus` (ID 68) currently live in `Fdp.Kernel` because they were needed by both `FDP.Toolkit.Navigation` and `FDP.Toolkit.CarKinem`, and placing them in either created a circular dependency. The correct solution is to extract the shared contract types into a new thin assembly that both toolkits can reference.

### What to do

**Step 1 — Create the new project `FDP.Toolkit.Navigation.Contracts`** under `FDP/Toolkits/FDP.Toolkit.Navigation.Contracts/`.
- This assembly must reference **only** `Fdp.Kernel` (for `[ComponentId]` and `GlobalComponentIds`).
- It must have **no** references to `FDP.Toolkit.Navigation`, `FDP.Toolkit.CarKinem`, `Bagira.*`, or any other toolkit.

**Step 2 — Move the two types** from `Fdp.Kernel/CoreComponents/NavigationComponents.cs` into `FDP.Toolkit.Navigation.Contracts`:
- `NavigationIntent` struct
- `NavigationStatus` struct
- `EngineNavigationMode` enum (if it lives alongside them)
- Their component IDs reassigned from the **20–49 toolkit block**. Pick the next two free IDs in that range (check `GlobalComponentIds` for which are taken). Remove IDs 67 and 68 from `GlobalComponentIds` (add tombstone comments: `// 67–68 freed — moved to FDP.Toolkit.Navigation.Contracts`).
- Create a `NavigationContractsComponentIds` class (or `NavigationComponentIds`) in the new assembly for the new IDs.

**Step 3 — Update project references:**
- `FDP.Toolkit.Navigation.csproj` → add reference to `FDP.Toolkit.Navigation.Contracts`.
- `FDP.Toolkit.CarKinem.csproj` → add reference to `FDP.Toolkit.Navigation.Contracts`.
- Remove references to `NavigationIntent`/`NavigationStatus` from `Fdp.Kernel.csproj` (the types are gone).
- Any `Bagira.*` projects or tests that reference `NavigationIntent`/`NavigationStatus` from `Fdp.Kernel` must now reference `FDP.Toolkit.Navigation.Contracts`.

**Step 4 — Run `dotnet build` on the full solution.** Fix all compilation errors.

**Step 5 — Run all tests.** All suites must be green.

> ⚠️ **ID range note:** The 20–49 block is flagged as full (DB-MOD1-02). Do the uniqueness guard task (Task 2 / DB-MOD1-02) **first**, so you know exactly which IDs in that range are free before assigning new ones for `NavigationIntent` / `NavigationStatus`.

---

## 🔄 MANDATORY WORKFLOW

1. **DB-MOD1-26:** Fix failing tests → `Bagira.Runner.Integration.Tests` 31/31 ✅
2. **DB-MOD1-02:** Add uniqueness guard tests → know which IDs are free ✅
3. **DB-MOD1-23:** Create `FDP.Toolkit.Navigation.Contracts`; move `NavigationIntent`/`NavigationStatus` out of `Fdp.Kernel` → **full solution builds** ✅
4. **DB-MOD1-04:** Fix vacuous avoidance test ✅
5. **DB-MOD1-05:** Lazy `BrainHsm64` ✅
6. **DB-MOD1-06:** Lazy `GroundKinematicsModule` allocations ✅
7. **DB-MOD1-09:** Unify configs ✅
8. **DB-MOD1-12:** Wire `IgPresentationModule` production canvas ✅
9. **DB-MOD1-18:** Strengthen `SeekToFrameAsync` test ✅
10. **DB-MOD1-25:** Dirty-flag `HealthData` sync ✅
11. **Final:** All suites green ✅

---

## 📊 Report Requirements

`.dev-workstream/reports/MOD1-BATCH-12-REPORT.md`

**Developer Insights**

**Q1:** For DB-MOD1-26 — what was the actual root cause? Which diagnostic log line (`[D2c]`/`[D2d]`) revealed it? What was missing: `NetworkTransform`, `DescriptorOwnership`, or something else?

**Q2:** For DB-MOD1-02 — did the uniqueness test find any actual duplicate IDs in `GlobalComponentIds` or `BagiraComponentIds`?

**Q3:** For DB-MOD1-09 — which config type was chosen as the survivor, and how many files changed?

**Q4:** For DB-MOD1-23 — which IDs were assigned to `NavigationIntent` and `NavigationStatus` in the new contracts assembly? Confirm the 20–49 block still has no duplicates after the move.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `SimHostDrag_IgReceivesPositionUpdateWithinFewFrames` passes unconditionally.
- [ ] All `DragDropIntegrationTests` pass unconditionally.
- [ ] `GlobalComponentIds_NoToolkitBlockDuplicates` and `BagiraComponentIds_NoDuplicates` tests exist and pass.
- [ ] `System_AvoidanceMovesVehicle` asserts non-trivial displacement and fails without authority.
- [ ] `BrainHsm64` early-exits when the entity query is empty.
- [ ] `GroundKinematicsModule` does not allocate pools for roles that don't need them.
- [ ] `NodeConfiguration` and `SimHostConfig` unified into one type; all callers compile.
- [ ] `IgPresentationModule` uses real `SstVisualizerAdapter` in production; headless tests still pass.
- [ ] `SeekToFrameAsync` test asserts `task.IsCompleted == false` before awaiting.
- [ ] `DamageSystem` only writes `HealthData` when `health.Current` actually changes.
- [ ] `FDP.Toolkit.Navigation.Contracts` assembly exists; `NavigationIntent` and `NavigationStatus` are in it with IDs in the 20–49 toolkit block; `Fdp.Kernel` no longer contains these types.
- [ ] Full solution builds with no compiler errors after the assembly restructure.
- [ ] `Bagira.Runner -x all` integration tests pass with 0 failures.
- [ ] All unit and integration test suites pass with 0 failures.
