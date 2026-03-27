# BATCH-04: Mid-Complexity Demos (Phase 3)

**Batch Number:** BATCH-04  
**Tasks:** DEM1-D003, DEM1-D004 + Selected Debt Correctives  
**Phase:** Phase 3 — Mid-Complexity Demos  
**Estimated Effort:** ~10-12 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-03

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to BATCH-04! We are advancing into Phase 3, evaluating mid-complexity systems in isolation. You will confirm the CCD (Continuous Collision Detection) anti-tunneling logic within the ballistic executor, and validate the Cognitive State B-Tree behavior transitions without running any physics layers. You will also resolve minor technical debt found in the prior batch.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Previous Review:** `.dev-workstream/reviews/BATCH-03-REVIEW.md` - Context for the technical debt
3. **Task Definitions:** `docs/demos-1/DEM1-TASK-DETAIL.md` - See `DEM1-D003` and `DEM1-D004`

### Source Code Location
- **Primary Work Areas:** 
  - `FDP/Examples/Fdp.Examples.Scenarios/Physics/`
  - `FDP/Examples/Fdp.Examples.Scenarios/Cognitive/`
  - `FDP/Toolkits/FDP.Toolkit.Navigation/CarKinematicsSystem.cs` (for Debt Task)
  - `FDP/Toolkits/FDP.Toolkit.Navigation/SpeedController.cs` (for Debt Task)
- **Test Project:** 
  - `FDP/Examples/Fdp.Examples.Scenarios.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-04-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-04-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
...and so on.
**DO NOT** move to the next task until the current one is entirely complete and tests are green.

---

## ✅ Tasks

### Task 1: [CORRECTIVE] Fix CarKinematicsSystem HasArrived Bug
**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/CarKinematicsSystem.cs` (or equivalent file handling `KinematicsMode.None`)

**Description:** As found in BATCH-03, `HasArrived` is blindly set to `1` when mode is `None`, which falsely triggers arrival signals for entities spawned with zero velocity.
**Requirements:**
- Update the logic block so `HasArrived` is only set to `1` if the state specifically merits it (e.g., `TargetSpeed > 0 && dist <= radius`). If statically placed, it should default to `0` or remain untouched.
- Verify existing tests do not break.

---

### Task 2: [CORRECTIVE] SpeedController Optimization
**File:** `FDP/Toolkits/FDP.Toolkit.Navigation/SpeedController.cs` (or equivalent file)

**Description:** Provide early exits to prevent mathematical execution when speeds match.
**Requirements:**
- In `CalculateAcceleration()`, add an early exit: `if (MathF.Abs(speedError) < 0.001f) return 0f;` to prevent calculating limits against a zero-error.

---

### Task 3: BallisticsAndHit Scenario (DEM1-D003)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Physics/BallisticsAndHitScenario.cs`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-D003](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d003--ballisticsandhit-scenario)

**Description:** Validate hyper-velocity weapon sweeps crossing targets without tunneling between ticks.
**Design Reference:** [DEM1-DESIGN.md §6.2](docs/demos-1/DEM1-DESIGN.md#dem1-d003-ballisticsandhit-ccd-anti-tunneling)

**Requirements:**
- Spawn a Target and a Shooter, then post a `FireRequestEvent`. 
- Follow strict group registration ordering (`InputSystemGroup` -> `SimulationSystemGroup` -> `PostSimulationSystemGroup`).
- Assert that bullets with velocities exceeding the frame delta depth still detect collisions on the target accurately.

**Tests Required:**
- ✅ `BallisticsAndHit_RunToCompletion_ExitsZero`
- ✅ `BallisticsAndHit_Phase1_BulletSpawnedWithCorrectVelocity`
- ✅ `BallisticsAndHit_Phase3_TargetTakesDamage_NoBulletSwimthrough`
- ✅ `BallisticsAndHit_Phase4_BulletDestroyedAfterImpact`

---

### Task 4: BehaviorValidation Scenario (DEM1-D004)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Cognitive/BehaviorValidationScenario.cs`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-D004](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d004--behaviorvalidation-scenario)

**Description:** Confirm that the B-Tree executor dynamically shifts decision nodes strictly through MockBlackboard states, without executing physics.
**Design Reference:** [DEM1-DESIGN.md §6.2](docs/demos-1/DEM1-DESIGN.md#dem1-d004-behaviorvalidation-cognitive-pipeline)

**Requirements:**
- Register `CognitiveRuntimeModule`.
- Define an inline BTree JSON struct defining a Selector linking to a combat Sequence or Flee string.
- Manually edit the memory on the mock blackboard to trigger the tree's condition changes, then assert the active action channels correctly execute `ActionIdFlee` mapping to `AimAndFire` and back.

**Tests Required:**
- ✅ `BehaviorValidation_RunToCompletion_ExitsZero`
- ✅ `BehaviorValidation_Phase1_AgentFlees_WhenNoThreat`
- ✅ `BehaviorValidation_Phase2_AgentEngages_WhenThreatWithAmmo`
- ✅ `BehaviorValidation_Phase3_AgentFleesAgain_WhenAmmoGone`

---

## 🧪 Testing Requirements
- Confirm that you properly analyze the engine units for tick rates. Adjust the test phase breakpoints within `BallisticsAndHitScenario` accordingly to match exactly what your local execution validates, and document these deviations in your report just like BATCH-03.

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
- [ ] Correctives 1 and 2 completed
- [ ] DEM1-D003 and DEM1-D004 Scenarios written and passing
- [ ] **ALL specified xUnit tests passing** verifying correct functionality and behavior.
- [ ] Developer `.dev-workstream/reports/BATCH-04-REPORT.md` written completely.
