# BATCH-05: Sensor Perception & Selected Correctives

**Batch Number:** BATCH-05  
**Tasks:** DEM1-D005 + Selected Debt Correctives  
**Phase:** Phase 3 — Mid-Complexity Demos  
**Estimated Effort:** ~10-12 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-04

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to BATCH-05. You will complete the final Phase 3 demo scenario, evaluating Line-of-Sight perception grids with moving targets. You will also resolve the technical debt found in the prior batches involving proper entity array dispose scopes, performance bottlenecks with `SpatialHashSystem`, and tweaking the `RVO` (Reciprocal Velocity Obstacle) avoidance parameters.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Previous Review:** `.dev-workstream/reviews/BATCH-04-REVIEW.md` - Context for the technical debt
3. **Task Definitions:** `docs/demos-1/DEM1-TASK-DETAIL.md` - See `DEM1-D005`

### Source Code Location
- **Primary Work Areas:** 
  - `FDP/Examples/Fdp.Examples.Scenarios/Perception/`
  - `FDP/Toolkits/FDP.Toolkit.Physics/Systems/SpatialHashSystem.cs` (for Debt Task)
  - `FDP/Toolkits/FDP.Toolkit.Navigation/Systems/RvoSystem.cs` (or equivalent file handling RVO scaling)
  - `FDP/Toolkits/FDP.Toolkit.Physics/PhysicsToolkitModule.cs` (or equivalent file handling array disposal)
- **Test Project:** 
  - `FDP/Examples/Fdp.Examples.Scenarios.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-05-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-05-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
...and so on.

---

## ✅ Tasks

### Task 1: [CORRECTIVE] Physics NativeArray Memory Leak
**Description:** As found in BATCH-04, `PhysicsToolkitModule.Initialize()` allocates `NativeArray` and passes ownership to `EntityRepository` which does not dispose of them. This creates leaks in test environments mapping to single lifetimes.
**Requirements:**
- Implement a way for either the repository to recognize and clean up external singletons mapping to unmanaged data, or natively enforce explicit disposal of the `RaycastBatchData` struct. Wrapping it in an IDisposable mechanism on teardown is acceptable.

---

### Task 2: [CORRECTIVE] SpatialHashSystem Filtering Optimization
**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/SpatialHashSystem.cs` (or equivalent)
**Description:** Modifying the grid hash population layer to explicitly exclude entities that don't possess a `PhysicsCollider`.
**Requirements:**
- Add an explicit component query filter for `PhysicsCollider` alongside `SimTransform` when populating the broadphase grid structure, ensuring non-collidable entities (like observation cameras, raw waypoints, or decoupled projectiles) do not cost CPU insertion cycles.

---

### Task 3: [CORRECTIVE] RVO Avoidance Velocity Biasing
**Description:** Identified in BATCH-03; high velocities encounter overly-rigid lateral forces, causing them to jitter instead of smoothly diverging. 
**Requirements:**
- Replace the fixed-magnitude lateral avoidance vector application with a velocity-relative bias, scaling proportionally to the interacting objects’ relative speeds to smooth avoidance routes. Check existing system documentation for guidance.

---

### Task 4: SensorGrid Scenario (DEM1-D005)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Perception/SensorGridScenario.cs`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-D005](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d005--sensorgrid-scenario)

**Description:** Validate that the perception module's line-of-sight correctly identifies entities within a view frustum, evaluates occluders (a wall), and writes targets to memory.
**Design Reference:** [DEM1-DESIGN.md §6.2](docs/demos-1/DEM1-DESIGN.md#dem1-d005-sensorgrid-perception--los)

**Requirements:**
- Register `PhysicsToolkitModule` and `AutonomousPerceptionModule`.
- Spawn an Observer, a Wall, and a Target.
- Increment the target position artificially (bypassing CarKinem velocity models).
- Read the agent's `TargetMemory` component memory arrays iteratively. Write a helper method that proves if the targeted Entity ID currently sits on the observed target array with a valid threat score.
- **IMPORTANT:** Document all execution boundaries or frame drift (such as `SpatialHash` updating the tick prior to a perception trace picking up the wall occulsion). Adjust testing tick assertions accordingly. 

**Tests Required:**
- ✅ `SensorGrid_RunToCompletion_ExitsZero`
- ✅ `SensorGrid_Phase1_TargetDetectedInOpenField`
- ✅ `SensorGrid_Phase2_TargetOccludedByWall`
- ✅ `SensorGrid_Phase3_TargetReacquiredAfterWall`

---

## 🧪 Testing Requirements
- Confirm all technical debt fixes do NOT trigger assertions spanning BATCH-01 -> BATCH-04 limits. If you alter the timing behavior of RVO matrices, double check the tick assertions on `AutoDriveScenario.cs`. If tests fail, update them to match the new correct behavior, and document the change in the report deviations.

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
- [ ] Correctives 1, 2, and 3 are completed and documented.
- [ ] DEM1-D005 Scenario written and assertions accurately reflect engine tick delays.
- [ ] **ALL specified xUnit tests passing** verifying correct functionality and behavior.
- [ ] Developer `.dev-workstream/reports/BATCH-05-REPORT.md` written completely.
