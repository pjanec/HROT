# BATCH-06: Advanced Demos (Phase 4)

**Batch Number:** BATCH-06  
**Tasks:** DEM1-D006, DEM1-D007 + Debt Correctives  
**Phase:** Phase 4 — Advanced Demos  
**Estimated Effort:** ~10-14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-05

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to BATCH-06. We are advancing to Phase 4, the Advanced Demo phase! You will develop two significant scenarios bridging disjoint architectural boundaries:
1. `DEM1-D006` (MissionCommandScenario) proving Dynamic Plan Queues orchestrating behavior layer Preemption rules.
2. `DEM1-D007` (TerrainClampingScenario) simulating rigid Z-height smoothing across complex topological environments.
Additionally, you are tackling specific technical debt found in BATCH-05. Specifically, `LocalGridBuilderSystem` requires optimization via dirty-flags, and `AutonomousPerceptionModule` requires a scalable production pattern for managing event loops safely if utilized synchronously.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Previous Review:** `.dev-workstream/reviews/BATCH-05-REVIEW.md` - Context for the technical debt
3. **Task Definitions:** `docs/demos-1/DEM1-TASK-DETAIL.md` - See `DEM1-D006`, `DEM1-D007`

### Source Code Location
- **Primary Work Areas:** 
  - `FDP/Examples/Fdp.Examples.Scenarios/Cognitive/`
  - `FDP/Examples/Fdp.Examples.Scenarios/Perception/`
  - `FDP/Toolkits/FDP.Toolkit.Physics/Systems/LocalGridBuilderSystem.cs` (or equivalent file)
  - `FDP/Toolkits/FDP.Toolkit.Perception/AutonomousPerceptionModule.cs` (or equivalent structure containing events)
- **Test Project:** 
  - `FDP/Examples/Fdp.Examples.Scenarios.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-06-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-06-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
...and so on.

---

## ✅ Tasks

### Task 1: [CORRECTIVE] Dirty-Flag Spatial Hash System
**File:** `FDP/Toolkits/FDP.Toolkit.Physics/Systems/LocalGridBuilderSystem.cs`
**Description:** Modify grid generation to track spatial movements rather than conducting total O(n) scale redraws.
**Requirements:**
- Implement state tracking mechanisms (dirty flags, timestamp comparisons, or explicit move events) so `LocalGridBuilderSystem` executes entity removals/replacements isolated to items that have genuinely adjusted positions since the previous build interval.

### Task 2: [CORRECTIVE] Autonomous Perception Event Bus Decoupling
**File:** `FDP/Toolkits/FDP.Toolkit.Perception/AutonomousPerceptionModule.cs`
**Description:** As found in `SensorGridScenario`, the `AutonomousPerceptionModule` pipeline produces severe systemic impacts when utilizing synchronous execution hooks by forcing global Bus swapping.
**Requirements:**
- Design a pattern to construct an explicit internal event queue (dedicated internal bus or non-reentrant snapshot layers) so that perception modules utilizing `Execute()` bounds do not unilaterally corrupt foreign layer state machines sharing the engine.

### Task 3: MissionCommand Scenario (DEM1-D006)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Cognitive/MissionCommandScenario.cs`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-D006](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d006--missioncommand-scenario)

**Description:** Demonstrate a hierarchy of multi-phase Mission Plans executing behavioral state preemption correctly against current states.
**Design Reference:** [DEM1-DESIGN.md §6.3](docs/demos-1/DEM1-DESIGN.md#dem1-d006-missioncommand-dynamic-mission--preemption)

**Requirements:**
- Form `MissionControlModule` and `CognitiveRuntimeModule`.
- Bind a queue of dummy phases (`MissionPlanQueue`) and use `Span<MissionPhase>` cast mappings to alter parameters safely.
- Force threat ingestion and evaluate whether arbitration protocols successfully eject stale locomotion orders upon Phase 1 boundary crossings.

**Tests Required:**
- ✅ `MissionCommand_RunToCompletion_ExitsZero`
- ✅ `MissionCommand_Phase3_DirectorAdvancesPhase_WhenThreated`
- ✅ `MissionCommand_Phase4_ArbitrationPreemptsStaleLocoCommand`

---

### Task 4: TerrainClamping Scenario (DEM1-D007)

**File:** `FDP/Examples/Fdp.Examples.Scenarios/Perception/TerrainClampingScenario.cs`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-D007](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-d007--terrainclamping-scenario)

**Description:** Validate rigid Z-elevation smoothing mechanisms mapped against Network transforms across terrain boundaries.
**Design Reference:** [DEM1-DESIGN.md §6.3](docs/demos-1/DEM1-DESIGN.md#dem1-d007-terrainclamping-z-height-smoothing--jump-rejection)

**Requirements:**
- Form the strict Phase pipeline utilizing mock inputs `TerrainQuerySolverSystem(new MockTerrainProvider())`.
- Configure the test mock so anomalies (i.e. massive Z spikes denoting cliffs or false pings) are accurately buffered via clamping profiles to assert target `GroundClampingState` altitudes smooth transitions properly.

**Tests Required:**
- ✅ `TerrainClamping_RunToCompletion_ExitsZero`
- ✅ `TerrainClamping_Phase1_NoClampingOnFlatGround`
- ✅ `TerrainClamping_Phase2_SmoothingActiveOnRamp`
- ✅ `TerrainClamping_Phase3_JumpRejectionRejectsSpike`
- ✅ `TerrainClamping_Phase4_RecoverAfterAnomaly`

---

## 🧪 Testing Requirements
- Confirm all technical debt fixes do NOT trigger assertions spanning old frameworks. If tests fail, update them to match the new correct behavior or rewrite logic parameters to stay within expected API bounds, and thoroughly report deviations in your BATCH-06 report document.

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
- [ ] Correctives 1 and 2 implemented
- [ ] DEM1-D006 and DEM1-D007 Scenarios written and passing.
- [ ] **ALL specified xUnit tests passing** verifying correct functionality and behavior.
- [ ] Developer `.dev-workstream/reports/BATCH-06-REPORT.md` written completely.
