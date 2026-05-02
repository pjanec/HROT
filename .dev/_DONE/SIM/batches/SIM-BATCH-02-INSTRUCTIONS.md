# SIM-BATCH-02: Behavior Toolkit Integration (Phase S4.1)

**Batch Number:** SIM-BATCH-02  
**Tasks:** TASK-S4.1  
**Phase:** S4  
**Estimated Effort:** 4 hours (0.5 days)  
**Priority:** HIGH  
**Dependencies:** S3 (Geographic Module Integration)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the next phase of SimHost. This batch focuses on registering the Behavior, Navigation, and Physics systems into `SimulationLogicModule`, laying the foundations for entity mission processing.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `docs/design/TASK-DETAILS-SIMHOST.md#phase-s4-behavior-toolkit-integration-5-days`
3. **Previous Review:** `.dev-workstream/reviews/SIM-BATCH-01-REVIEW.md` - Please remember to submit the report file for this batch!

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/`
- **Test Project:** `Hrot.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/SIM-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/SIM-BATCH-02-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

With `SimTransform` being successfully mapped back and forth via `GeographicModule`, we need the underlying simulation logic fully initialized so entities can actually process tasks. `FDP.Toolkit.Behavior` and `FDP.Toolkit.Navigation` hold the core modules (for routing, spatial hashing, and state machines).

Currently, `SimulationLogicModule` in SimHost does not register the logic processors. You will inject these systems ensuring that `LinearKinematicsSystem` runs exclusively on non-wheeled platforms while `CarKinematicsSystem` handles the rest.

---

## 🎯 Batch Objectives
- Fully configure `SimulationLogicModule.RegisterSystems()` with all behavior and physics update loops.
- Pass required shared data parameters like `BehaviorRegistry` and `NetworkEntityMap` into the constructor.
- Add an empty world test proving all systems load without exception.

---

## ✅ Tasks

### Task 1: Register Behavior / Navigation / Physics Systems (TASK-S4.1)

**File:** `[Replace or Create the SimulationLogicModule inside Hrot.SimHost]`  (If it exists, update it. Otherwise, create it)
**Task Definition:** See [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md#task-s41-register-behavior--navigation--physics-systems)

**Description:**
Wire the `FDP.Toolkit.Behavior` and `FDP.Toolkit.Navigation` systems into `SimulationLogicModule`. 

**Requirements:**
In `SimulationLogicModule.RegisterSystems()`, register in **strict order**:
1. `new MissionAdapterSystem(_behaviorRegistry, _entityMap)` — runs first each frame. *(Note: Leave this commented out or as a dummy stub for now, since it is implemented in S4.3. You can create an empty stub `MissionAdapterSystem : ComponentSystem` to satisfy compilation.)*
2. `new ChannelArbitrationSystem()`
3. `new BTreeTickSystem(_behaviorRegistry)`
4. `new LocomotionDispatcherSystem()`
5. `new MoveToExecutor()`
6. `new FollowRouteExecutor()`
7. `new JoinFormationExecutor(_vehicleAPI, _entityMap)` *(Note: Leave this as a dummy stub as it is implemented in S4.4)*
8. `new SpatialHashSystem()`, `new FormationTargetSystem()`, `new VehicleCommandSystem()`, `new CarKinematicsSystem(...)` (provide valid stub/dummy parameters if necessary)
9. `new LinearKinematicsSystem()`

*Requirement Updates*: Some systems may not yet exist in `Hrot.SimHost` directly or may require external APIs. For `MissionAdapterSystem` and `JoinFormationExecutor`, create empty class stubs extending `ComponentSystem` or using `IActionExecutor` if needed, so it compiles. 

You must also update `SimulationLogicModule` constructor to accept `BehaviorRegistry` and `NetworkEntityMap` parameters.

**Tests Required:**
- ✅ Unit test instantiating `SimulationLogicModule` with dummy parameters, registering to an empty `EntityRepository`, and calling `kernel.Update()` once ensuring it runs without throwing exception.

---

## 🧪 Testing Requirements
A single comprehensive initialization test will suffice here. No deep execution logic needs asserting yet, but you must assert the `EntityRepository` correctly handles the system topology mapping without cyclic dependency breaks.

---

## 📊 Report Requirements

**Q1 Initialization Blockers:** Did you need to construct any mock arguments to satisfy system constructors?
- **Q2 Structure Concerns:** Is `SimulationLogicModule` getting too bloated? Would you recommend breaking it down further for clarity?
- **Q3 Stubs:** Which specific empty stubs (`MissionAdapterSystem` / `JoinFormationExecutor`) did you have to create to finalize the list?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] TASK-S4.1 completed.
- [ ] Empty world kernel update test runs successfully without throwing topology or null reference exceptions.
- [ ] Report submitted via markdown file.

---

## ⚠️ Common Pitfalls to Avoid
- Omitting `LinearKinematicsSystem` or placing it before `SpatialHashSystem`. Order is strictly as laid out in the spec.
- Failing to use `ComponentSystem` inheritance for the placeholder systems making them incompatible with the module registry list.

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAILS-SIMHOST.md](../../docs/design/TASK-DETAILS-SIMHOST.md) - See TASK-S4.1
