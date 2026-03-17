# BATCH-03: Simple Deterministic Demos (Phase 2)

**Batch Number:** BATCH-03  
**Tasks:** DEM1-D001, DEM1-D002 + Debt Correctives  
**Phase:** Phase 2 — Simple Demos  
**Estimated Effort:** ~10 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-02

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back! With the Phase 0 orchestration framework and Phase 1 shared components tested and ready, we are now implementing real deterministic scenarios. This batch focuses on the absolute basics: verifying Ground Kinematics routing and the Combat/Damage components in isolation.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Previous Review:** `.dev-workstream/reviews/BATCH-02-REVIEW.md` - Context for the corrective API upgrade
3. **Task Definitions:** `docs/demos-1/DEM1-TASK-DETAIL.md` - See `DEM1-D001` and `DEM1-D002` 

### Source Code Location
- **Primary Work Areas:** 
  - `FDP/Examples/Fdp.Examples.Scenarios/Kinematics/`
  - `FDP/Examples/Fdp.Examples.Runner/Program.cs` 
  - `FDP/ModuleHost/ModuleHost.Core/ModuleHostKernel.cs` 
- **Test Project:** 
  - `FDP/Examples/Fdp.Examples.Scenarios.Tests/ScenarioTests.cs` (or separated equivalent spec file inside the testing project)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-03-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## 🎯 Batch Objectives
Ensure the runtime can perfectly deterministically orchestrate two independent agents hitting RVO (Reciprocal Velocity Obstacles) recovery, and that a single entity properly receives damage from an event without racing.

---

## ✅ Tasks

### Task 1: [CORRECTIVE] Migrate to NLog ScopeContext
**File:** `FDP/Examples/Fdp.Examples.Runner/Program.cs`

**Description:** As discovered in BATCH-02, `MappedDiagnosticsContext` raises a `CS0618` Obsolete warning in NLog 5.x.
**Requirements:**
- Change `NLog.MappedDiagnosticsContext.Set("scenario", opts.Scenario)` to `NLog.ScopeContext.PushProperty("scenario", opts.Scenario)`.
- Change the `FileName` argument in the `FileTarget` to explicitly read `${scopeproperty:scenario}` instead of `${scenario}` (if needed) to ensure parity with the new API.

---

### Task 2: [CORRECTIVE] Obsolete ModuleHostKernel Float Overload
**File:** `FDP/ModuleHost/ModuleHost.Core/ModuleHostKernel.cs`

**Description:** Calling `.Update(float deltaTime)` overwrites time singleton unpredictably for deterministic runs utilizing external manual steppers like ScenarioSubsystem.
**Requirements:**
- Decorate `public void Update(float deltaTime)` with `[Obsolete("Use Update() utilizing SteppingTimeController instead. This legacy overload will cause deterministic desync.", false)]`. Do not delete the method, as we need downstream projects to be warned instead of immediately halting compilation.

---

### Task 3: AutoDrive Scenario (DEM1-D001)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Kinematics/AutoDriveScenario.cs`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-D001](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d001--autodrive-scenario)

**Description:** Ground kinematics collision avoidance evaluation.
**Design Reference:** [DEM1-DESIGN.md §6.1](docs/demos-1/DEM1-DESIGN.md#dem1-d001-autodrive-kinematics--avoidance)

**Requirements:**
- Implement `IScenario` specifically publishing routing requests to Alpha/Bravo vehicles spawned on a direct collision path.
- Check explicitly that RVO steering shifts Alpha off the 0.0 Y-axis.

**Tests Required:**
- ✅ `AutoDrive_RunToCompletion_ExitsZero`
- ✅ `AutoDrive_Phase1_VehiclesAccelerate_ByTick20`
- ✅ `AutoDrive_Phase2_RVOActivates_ByTick70`
- ✅ `AutoDrive_Phase4_BothVehiclesArrive_WithinBudget`

---

### Task 4: ComponentDamage Scenario (DEM1-D002)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Kinematics/ComponentDamageScenario.cs`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-D002](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d002-componentdamage-partial-kill-pipeline)

**Description:** Validates pipeline for partial entity kills (stripping mobility but retaining weapon components).
**Design Reference:** [DEM1-DESIGN.md §6.1](docs/demos-1/DEM1-DESIGN.md#dem1-d002-componentdamage-partial-kill-pipeline)

**Requirements:**
- Register and step components ensuring a `HitEvent` appropriately degrades `Health` via the standard `DamageSystem`.
- Assert the engine sequentially strips the `CanMove` actor capability but honors injected fire commands to static endpoints.

**Tests Required:**
- ✅ `ComponentDamage_RunToCompletion_ExitsZero`
- ✅ `ComponentDamage_Phase2_HealthDecreases_AfterHit`
- ✅ `ComponentDamage_Phase3_MoveFlagStripped_AfterDamage`
- ✅ `ComponentDamage_Phase4_LocomotionCleared_ByHSM`
- ✅ `ComponentDamage_Phase5_WeaponStillFires_AfterMobilityKill`

---

## 🧪 Testing Requirements
- When authoring testing harnesses, utilize `ScenarioTestHarness.Run(..., maxTicks: ...)` exactly natively. Do not circumvent its isolation design. 
- You MUST verify that the tests natively crash/fail when modifying the constants manually (i.e. changing hit damage points) to confirm coverage works. Ensure tests don't just assert true by default without evaluating values.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please capture your valuable insights and experience. Answer these in your report:

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] Correctives completed
- [ ] DEM1-D001 and DEM1-D002 Scenarios written
- [ ] **ALL specified xUnit tests passing** verifying correct functionality and behavior.
- [ ] Developer `.dev-workstream/reports/BATCH-03-REPORT.md` written completely.

---

## ⚠️ Common Pitfalls to Avoid
- Trying to wire `AutoDriveScenario` tests to physical clock threads instead of using `_tick` references internally inside `EvaluateTick`.
- `ModuleHost.Core` update might cause minor syntax problems if its internal usages internally referenced the `float dt` override instead of resolving `_timeController.DeltaTime`.

---

## 📚 Reference Materials
- **Task Defs:** [DEM1-TASK-DETAIL.md](docs/demos-1/DEM1-TASK-DETAIL.md)
- **Design:** [DEM1-DESIGN.md](docs/demos-1/DEM1-DESIGN.md)
