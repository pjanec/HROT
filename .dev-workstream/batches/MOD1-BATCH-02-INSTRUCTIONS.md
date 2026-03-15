# MOD1-BATCH-02: Brain & Muscle Module Decomposition

**Batch Number:** MOD1-BATCH-02  
**Tasks:** CT-MOD1-A, DB-MOD1-01, CT-MOD1-C, MOD1-P2T1, MOD1-P2T2, MOD1-P2T3, MOD1-P2T4, MOD1-P2T5  
**Phase:** Phase 2 — Brain & Muscle Module Decomposition  
**Estimated Effort:** 8-10 hours  
**Priority:** HIGH  
**Dependencies:** MOD1-BATCH-01 (Completed)

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to MOD1 Phase 2. This batch shifts focus to modularizing the monolithic `SimulationLogicModule` into five discrete implementations. This isolates cognitive responsibilities (`FDP.Toolkit.Behavior`), kinematic updates (`FDP.Toolkit.CarKinem`), and combat logic (`Bagira.SimHost.Modules`). Additionally, two critical corrective tasks arising from the BATCH-01 review are prioritized at the very beginning of this batch.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs/modularizing/MOD1-TASK-DETAIL.md` - Review Phase 2
3. **Design Document:** `docs/modularizing/MOD1-DESIGN.md` - See §3.2 and §2.5
4. **Previous Review:** `.dev-workstream/reviews/MOD1-BATCH-01-REVIEW.md` - Learn from prior feedback regarding memory leaks and component ID sizing.

### Source Code Location
- **Primary Work Areas:**
  - `FDP/Toolkits/FDP.Toolkit.Behavior/`
  - `FDP/Toolkits/FDP.Toolkit.CarKinem/`
  - `Bagira.SimHost/Modules/`
- **Test Projects:**
  - `FDP.Toolkit.Behavior.Tests/`
  - `FDP.Toolkit.CarKinem.Tests/`
  - `Bagira.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/MOD1-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/MOD1-BATCH-02-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task CT-A:** Implement → Write tests → **ALL tests pass** ✅
2. **Task CT-B:** Implement → Write tests → **ALL tests pass** ✅
3. **Task CT-C:** Implement → Fix `Bagira.Runner` integration tests → **ALL tests pass** ✅
4. **Task 1:** Implement → Write integration & unit tests → **ALL tests pass** ✅
5. **Task 2:** Implement → Write integration & unit tests → **ALL tests pass** ✅  
6. **Task 3:** Implement → Write integration & unit tests → **ALL tests pass** ✅
7. **Task 4:** Implement → Write integration & unit tests → **ALL tests pass** ✅
8. **Task 5:** Implement → Write integration & unit tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context

This batch breaks the monolithic initialisation block in `SimulationLogicModule` by grouping systems into high-cohesion `IModule` boundaries. You will define the concrete `MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule`, and `GroundKinematicsModule` in their respective toolkits, and transition the `SimulationLogicModule` into a mere facade that delegates down. 

First, however, you will execute two immediate corrective items discovered during the Phase 1 review: fixing a dictionary-based memory leak and addressing a dangerous C# namespace aliasing collision.

**Related Tasks:**
- `CT-MOD1-A` and `DB-MOD1-01` (Debt Tracker)
- [MOD1-P2T1](../docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p2t1--create-missioncontrolmodule) through [MOD1-P2T5](../docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p2t5--refactor-simulationlogicmodule-as-delegation-facade)

---

## 🎯 Batch Objectives
- Close the `_frustrationTicks` dictionary memory leak.
- Eradicate the `NavigationMode` enum collision.
- Fix broken `Bagira.Runner` spawn entity functionality and restore integration test health.
- Organize cognitive logic into `MissionControlModule`, `CognitiveRuntimeModule`, and `ActionDispatchModule`.
- Extract muscle loop systems directly into `GroundKinematicsModule`.
- Wire backward-compatibility inside `SimulationLogicModule`.

---

## ✅ Tasks

### Corrective Task 0.A (CT-MOD1-A)

**File:** `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/NavigationExecutionSystem.cs`

**Description:**
The `Dictionary<int, int> _frustrationTicks` leaks memory over time as entities are destroyed without removing keys. Replace this dictionary with an ECS component `FrustrationTicks { public int Ticks; }`. Initialize this component on the entity when `NavigationExecutionSystem` begins fulfilling an intent, increment it directly against the entity, and allow ECS lifecycle destruction to handle garbage collection automatically.

**Tests Required:**
- ✅ Verify `FrustrationTicks` component correctly increments.
- ✅ Dictionary `_frustrationTicks` completely removed from implementation.

---

### Corrective Task 0.B (DB-MOD1-01)

**Files:** Various (across executors & car kinematics)

**Description:**
`CarKinem.Core.NavigationMode` and `FDP.Toolkit.Navigation.NavigationMode` clash severely using C# namespace shadowing. Globally rename `CarKinem.Core.NavigationMode` to `KinematicsMode`. Update all referencing instances, removing any `CarKinemNavMode` workarounds added in BATCH-01.

**Tests Required:**
- ✅ Verify codebase compiles perfectly with `KinematicsMode` applied.

---

### Corrective Task 0.C (CT-MOD1-C)

**Files:** `Bagira.Runner` entity creation system or blueprint mapping logic, integration tests.

**Description:**
The CQRS refactor from BATCH-01 broke the "Spawn moving entity" button in `Bagira.Runner`. It throws:
```
System.InvalidOperationException: Entity Entity(0, v1) missing NavigationIntent
   at FDP.Toolkit.Navigation.Executors.MoveToExecutor.OnEnter(Entity entity, LocomotionChannel& channel, EntityRepository world)
   at FDP.Toolkit.Behavior.Systems.LocomotionDispatcherSystem.OnUpdate()
```
Entities spawned via the runner are currently missing the new `NavigationIntent` and `NavigationStatus` components natively required by the refactored CQRS executors. 
Fix the entity spawn flow to correctly attach these baseline components.

**Tests Required:**
- ✅ Make sure ALL `Bagira.Runner` integration tests pass.
- ✅ **CRITICAL:** Add integration tests for ANY further task in this batch and ensure they pass. Relying purely on unit tests is insufficient.

---

### Task 1: MOD1-P2T1

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/MissionControlModule.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P2T1](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p2t1--create-missioncontrolmodule)

**Description:** Extract doctrine ingress and mission direction.

**Tests Required:**
- ✅ `MissionControlModule_RegistersSystems`

---

### Task 2: MOD1-P2T2

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/CognitiveRuntimeModule.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P2T2](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p2t2--create-cognitiveruntimemodule)

**Description:** Extract BTree/HSM tick systems and channel arbitration.

**Tests Required:**
- ✅ `CognitiveRuntimeModule_RegistersAllTickSystems`

---

### Task 3: MOD1-P2T3

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Modules/ActionDispatchModule.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P2T3](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p2t3--create-actiondispatchmodule)

**Description:** Extract locomotion and weapon dispatcher systems.

**Tests Required:**
- ✅ `ActionDispatchModule_RegistersLocoAndWeaponDispatchers`
- ✅ Integration verify `MoveTo` action dispatched successfully.

---

### Task 4: MOD1-P2T4

**File:** `FDP/Toolkits/FDP.Toolkit.CarKinem/Modules/GroundKinematicsModule.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P2T4](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p2t4--create-groundkinematicsmodule)

**Description:** Extract ground vehicle physics systems. Enforce `.WithOwned<SimTransform>()` in `CarKinematicsSystem` queries.

**Tests Required:**
- ✅ `GroundKinematicsModule_RegistersAllKinematicSystems`
- ✅ Verify `CarKinematicsSystem` contains zero `NetworkOwnership.PrimaryOwnerId` usages.

---

### Task 5: MOD1-P2T5

**File:** `Bagira.SimHost/Modules/SimulationLogicModule.cs`

**Task Definition:** See [MOD1-TASK-DETAIL.md section MOD1-P2T5](docs/modularizing/MOD1-TASK-DETAIL.md#mod1-p2t5--refactor-simulationlogicmodule-as-delegation-facade)

**Description:** Refactor as delegation facade instantiating the sub-modules backwards-compatibly.

**Tests Required:**
- ✅ `Integration Tests` continue passing without modification.

---

## 🧪 Testing Requirements

**❗ TEST QUALITY EXPECTATIONS**
- **NOT ACCEPTABLE:** Checking if system string names are present.
- **REQUIRED:** Instantiate the actual modules inside `ModuleHostKernel` to rigorously test structural composition and ensure all underlying systems get queued effectively into the `ISystemRegistry`. 
- The corrective tests must specifically isolate component iteration values and compilation integrity.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please submit `.dev-workstream/reports/MOD1-BATCH-02-REPORT.md` completing the following:

**Developer Insights**

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed in the existing component composition blocks?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Memory leak in `_frustrationTicks` eliminated via standard ECS components.
- [ ] Enum clashing fully eradicated by `KinematicsMode` migration.
- [ ] Over 5 independent `IModule` implementations successfully encapsulate `SimulationLogicModule` sub-capabilities.
- [ ] Existing `Bagira.SimHost.Tests` integration dependencies successfully resolve against the refactored facade.
- [ ] Report submitted answering exact Developer Insight queries.

---

## ⚠️ Common Pitfalls to Avoid
- Avoid retaining generic System classes inside the overarching integration logic; every tick must migrate directly to their respective cognitive and kinematic homes.
- Ensure dependency-injection defaults within `GroundKinematicsModule` cleanly mirror previous runtime constants.

---

## 📚 Reference Materials
- **Task Defs:** `docs/modularizing/MOD1-TASK-DETAIL.md` - (See Phase 2)
- **Architecture Strategy:** `docs/modularizing/MOD1-DESIGN.md`
