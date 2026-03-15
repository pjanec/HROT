# MOD1-BATCH-01: CQRS Navigation Contract + Authority Bug Fixes

**Batch Number:** MOD1-BATCH-01  
**Tasks:** MOD1-P1T1, MOD1-P1T2, MOD1-P1T3, MOD1-P1T4  
**Phase:** Phase 1 — CQRS Navigation Contract + Authority Bug Fixes  
**Estimated Effort:** 10-12 hours  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to MOD1 Phase 1. This batch introduces the engine-agnostic `NavigationIntent` and `NavigationStatus` ECS components and their corresponding DDS descriptors. It also refactors the `MoveToExecutor` to fully align with the CQRS pattern, fixes legacy authority guard bugs in Geographic Systems, and delegates the fulfillment of navigation to the `CarKinematicsSystem`.

### Required Reading (IN ORDER)
1. **Developer workflow guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs/modularizing/MOD1-TASK-DETAIL.md` - See Phase 1 details
3. **Design Document:** `docs/modularizing/MOD1-DESIGN.md` - See §3.1
4. **Task Tracker:** `docs/modularizing/MOD1-TASK-TRACKER.md`

### Source Code Location
- **Primary Work Areas:**
  - `FDP/Toolkits/FDP.Toolkit.Navigation/`
  - `FDP/Kernel/Fdp.Kernel/`
  - `Bagira.DDS.DataModel/`
  - `FDP/Toolkits/Fdp.Toolkit.Geographic/`
  - `FDP/Toolkits/FDP.Toolkit.CarKinem/`
- **Test Projects:**
  - Corresponding unit test projects for the above toolkits.

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/MOD1-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/MOD1-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅
4. **Task 4:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context

This batch focuses on establishing the core data flow for distributed navigation commands. By establishing engine-side and network-side structures for intent and status, we begin uncoupling the application domain from simulation mechanics. The refactoring of executors and fixes to geographic systems directly support this effort by clearing obsolete patterns.

**Related Tasks:**
- [MOD1-P1T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p1t1--define-navigationintent-and-navigationstatus-ecs-components--dds-descriptors) - Component and Descriptor Definitions
- [MOD1-P1T2](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p1t2--refactor-movetoexecutor-to-cqrs-pattern) - MoveToExecutor CQRS Refactoring
- [MOD1-P1T3](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p1t3--fix-authority-guard-bugs-in-geographic-systems) - Ownership Guard Fixes
- [MOD1-P1T4](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p1t4--add-navigation-fulfillment-logic-to-carkinematicssystem) - Navigation Execution logic

---

## 🎯 Batch Objectives
- Establish the dual-enum pattern for Navigation types across engine and DDS layers.
- Solidify CQRS boundaries by removing geographic conversion and logic from `MoveToExecutor`.
- Enable split-authority deployments by switching from explicit ID checks to correct `.WithOwned<T>()` query filters.
- Connect kinematics updates to update the `NavigationStatus` reliably.

---

## ✅ Tasks

### Task 1: MOD1-P1T1 

**Files:**
- **NEW:** `FDP/Toolkits/FDP.Toolkit.Navigation/Components/NavigationIntent.cs`
- **NEW:** `FDP/Toolkits/FDP.Toolkit.Navigation/Components/NavigationStatus.cs`
- **NEW:** `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationMode.cs`
- **NEW:** `FDP/Toolkits/FDP.Toolkit.Navigation/NavigationResult.cs`
- **UPDATE:** `FDP/Kernel/Fdp.Kernel/GlobalComponentIds.cs`
- **UPDATE:** `Bagira.DDS.DataModel/SimDescriptors.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P1T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p1t1--define-navigationintent-and-navigationstatus-ecs-components--dds-descriptors)

**Description:**
Implement engine-side enums, struct definitions for `NavigationIntent` and `NavigationStatus` components assigned to the exact toolkit block component ID. Update `SimDescriptors.cs` for the DDS dual representations of these structures and enums. Do NOT introduce any dependency from `FDP.Toolkit.Navigation` to `Bagira.*`.

**Tests Required:**
- ✅ Verify `NavigationIntent.Mode` defaults to `NavigationMode.None` for zero-initialized struct.
- ✅ Assert `FDP.Toolkit.Navigation` contains zero references to `Bagira.*` dependencies.

---

### Task 2: MOD1-P1T2

**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/Executors/MoveToExecutor.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P1T2](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p1t2--refactor-movetoexecutor-to-cqrs-pattern)

**Description:**
Strip physics awareness from `MoveToExecutor`. It must act purely as an observer, writing `NavigationIntent` (converting `MoveToParams` properly) during `OnEnter`, observing `NavigationStatus` for CQRS fulfillment during `Execute`, and exiting gracefully in `OnExit`. Ensure zero geographic conversions within the executor.

**Tests Required:**
- ✅ `MoveToExecutor_OnEnter_WritesNavigationIntentWithIncrementedId`
- ✅ `MoveToExecutor_Execute_ReturnsSuccessWhenStatusArrived`
- ✅ `MoveToExecutor_Execute_IgnoresStaleStatus`
- ✅ `MoveToExecutor_Execute_ReturnsFailureWhenBlocked`

---

### Task 3: MOD1-P1T3

**Files:**
- `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/CoordinateTransformSystem.cs`
- `FDP/Toolkits/Fdp.Toolkit.Geographic/Systems/GeodeticSmoothingSystem.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P1T3](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p1t3--fix-authority-guard-bugs-in-geographic-systems)

**Description:**
Replace explicit `PrimaryOwnerId == LocalNodeId` checks in query systems with precise ECS ownership filters using `.WithOwned<T>()` and `.WithoutOwned<T>()`. This solves broken authority guards in separated environments.

**Tests Required:**
- ✅ `CoordinateTransformSystem_SkipsGhostEntities`
- ✅ `GeodeticSmoothingSystem_ProcessesOnlyGhostEntities`

---

### Task 4: MOD1-P1T4

**File:** `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/NavigationExecutionSystem.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P1T4](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p1t4--add-navigation-fulfillment-logic-to-carkinematicssystem)

**Description:**
Provide execution side navigation logic inside the kinematics system space. Verify arrival and frustration limits without translating geometry structures. Write into `NavigationStatus`.

**Tests Required:**
- ✅ `NavigationExecution_WritesArrivedWhenEntityReachesTarget`
- ✅ `NavigationExecution_WritesFailedWhenEntityStuck`
- ✅ `NavigationExecution_IntentIdMismatch_ResetsOnNewCommand`

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Tests that only verify "can I set this value", "object is not null", or checks for strings.
- **REQUIRED:** Tests that verify actual behavior, precise system iteration skip/include logic, and accurate intent ID tracking.
- Test implementations must construct valid world contexts and run system `OnUpdate()` correctly.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please submit `.dev-workstream/reports/MOD1-BATCH-01-REPORT.md` completing the following:

**Developer Insights**

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] MOD1-P1T1 completed cleanly without violating `Bagira.*` boundaries inside `FDP.Toolkit`.
- [ ] MOD1-P1T2 completed with raw Cartesian copy of `MoveToParams` with zero geo dependencies.
- [ ] MOD1-P1T3 completed with `WithOwned<Position>()` filter implementations fully functioning.
- [ ] MOD1-P1T4 completed, writing deterministic execution status to `NavigationStatus`.
- [ ] All required tests passing under stringent quality expectations.
- [ ] Report submitted answering the Developer Insight questions.

---

## ⚠️ Common Pitfalls to Avoid
- Falling back to `IGeographicTransform` in `MoveToExecutor`. Keep it clean.
- Setting `.With<NetworkOwnership>()` incorrectly - rely exclusively on `Owned/WithoutOwned`.
- Confusing the engine enums (`NavigationMode`, etc.) with DDS enums (`ENavigationMode`). Double check your mappings.

---

## 📚 Reference Materials
- **Task Defs:** `docs/modularizing/MOD1-TASK-DETAIL.md` - (See Phase 1)
- **Architecture Strategy:** `docs/modularizing/MOD1-DESIGN.md`
