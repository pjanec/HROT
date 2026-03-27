# BATCH-02: Shared Infrastructure & Logging Fixes

**Batch Number:** BATCH-02  
**Tasks:** DEM1-I001, DEM1-I002 + BATCH-01 Correctives  
**Phase:** Phase 1 — Shared Infrastructure  
**Estimated Effort:** ~10 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to BATCH-02. In this batch, we finalize the foundational shared state classes inside `Fdp.Examples.Common` and create standalone `Fdp.Examples.DDS` schemas representing our scenario network topologies. Additionally, please address two minor formatting/logging omissions left over from BATCH-01.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Previous Review:** `.dev-workstream/reviews/BATCH-01-REVIEW.md` - Context for the corrective tasks below
3. **Task Definitions:** `docs/demos-1/DEM1-TASK-DETAIL.md` - See `DEM1-I001` and `DEM1-I002`

### Source Code Location
- **Primary Work Areas:** 
  - `FDP/Examples/Fdp.Examples.Runner/Program.cs`
  - `FDP/Examples/Fdp.Examples.Common/`
  - `FDP/Examples/Fdp.Examples.DDS/` (New Project)
- **Test Project:** 
  - `FDP/Examples/Fdp.Examples.Scenarios.Tests/` (Add tests here or to their respective component projects)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-02-QUESTIONS.md`

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
Implement the necessary struct dependencies to enable the Phase 2/3 deterministic AI and Physics tests, as well as fixing the trace payload layout to exactly match CI specifications.

---

## ✅ Tasks

### Task 1: [CORRECTIVE] Fix NLog Format in Program.cs
**File:** `FDP/Examples/Fdp.Examples.Runner/Program.cs`

**Description:** Make the programmatic config layout explicitly follow the original batch spec.
**Requirements:**
- Before orchestrator runs, you must add: `NLog.MappedDiagnosticsContext["scenario"] = options.Scenario;`
- Ensure the filename uses NLog cached formatting rather than C# `DateTime.Now`:
  `FileName = "logs/demo-${scenario}-${shortdate}-${cached:cached=true:inner=${date:format=HHmmss}}.log"`
- The layout must exacty include `tick=${event-properties:tick}`:
  `Layout = "${longdate}|${level:uppercase=true}|${logger}|tick=${event-properties:tick}| ${message} ${exception:format=tostring}"`

**Tests Required:**
- ✅ Verify the tests from `NLogFileOutputTests` still pass or accurately check the file names (using wildcards if deterministic generation has been handed over to NLog).

---

### Task 2: [CORRECTIVE] Add Missing Per-Tick Trace Logging
**File:** `FDP/Examples/Fdp.Examples.Common/ScenarioSubsystem.cs`

**Description:** The subsystem needs to dump a trace statement exactly detailing the tick execution.
**Requirements:**
- Add `FdpLog<ScenarioSubsystem>.Trace("[{0}] tick={1}", _scenario.ScenarioName, _tick);` at the top of the `Update` method (right after `_tick` increments).

**Tests Required:**
- ✅ You may want to add to `NLogFileOutputTests` (or implicitly rely on previous tests) to assert that Trace level writes at least one tick debug statement.

---

### Task 3: Fdp.Examples.DDS Project (DEM1-I001)

**File:** `FDP/Examples/Fdp.Examples.DDS/` (New Project)  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-I001](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-i001--fdpexamplesdds-project)

**Description:** Provide definitions for struct bindings spanning Transform, Lock, Faction interactions.
**Design Reference:** [DEM1-DESIGN.md §5.1](docs/demos-1/DEM1-DESIGN.md#51-fdpexamplesdds--cartesian-only-dds-schemas)

**Requirements:**
- Implement the 5 `[DdsTopic]` structures: `DemoSpawnMsg`, `DemoTransformMsg`, `DemoLocomotionMsg`, `DemoWeaponMsg`, `DemoCombatInteractionMsg`.

**Tests Required:**
- ✅ `DemoTransformMsg_Serialization_RoundTrip`
- ✅ `DemoSpawnMsg_Serialization_RoundTrip`
- ✅ `DemoCombatInteractionMsg_Serialization_RoundTrip`
*(Note: Use FDP native `CdrWriter`/`CdrReader` equivalent serializers for these tests)*

---

### Task 4: Fdp.Examples.Common Infrastructure (DEM1-I002)

**File:** `FDP/Examples/Fdp.Examples.Common/`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-I002](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-i002--fdpexamplescommon-infrastructure)

**Description:** Provide basic building block components and helper tools.
**Design Reference:** [DEM1-DESIGN.md §5.2](docs/demos-1/DEM1-DESIGN.md#52-fdpexamplescommon--shared-state-and-tooling)

**Requirements:**
- `DemoScenarioTracker`, `MockBlackboardState`
- `DemoTestLogEvent`, `DemoScenarioTriggerEvent`
- Implement `MockTerrainProvider` and `DemoRoadGraphFactory` helpers precisely to design spec algorithms.

**Tests Required:**
- ✅ `MockTerrainProvider_FlatZone_ReturnsZeroAltitude`
- ✅ `MockTerrainProvider_Ramp_ReturnsCorrectAltitude`
- ✅ `MockTerrainProvider_Spike_ReturnsOneHundred`
- ✅ `DemoRoadGraphFactory_CreatesNonNullBlob`

---

## 🧪 Testing Requirements
- **⚠️ REQUIRED:** Actually inspect internal values, lengths, flags, and deserialized elements of testing boundaries. Do not just assert that `msg != null`.
- Avoid testing implementation semantics (like how road network blobs sort their lists), stick exclusively to what is described in the Acceptance Criteria block.

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
- [ ] Corrective Tasks 1 and 2 completed
- [ ] DEM1-I001 completed
- [ ] DEM1-I002 completed
- [ ] **ALL specified xUnit tests passing** verifying correct functionality and behavior.
- [ ] Developer `.dev-workstream/reports/BATCH-02-REPORT.md` written completely.

---

## ⚠️ Common Pitfalls to Avoid
- Neglecting to apply `[UnmanagedComponent]` exactly where documented.
- Getting side tracked and trying to establish heavy `bagira` dependencies out of our DDS schema layer — keep `Fdp.Examples.DDS` dependency-free or linked only to Cyclone bindings.

---

## 📚 Reference Materials
- **Task Defs:** [DEM1-TASK-DETAIL.md](docs/demos-1/DEM1-TASK-DETAIL.md)
- **Review:** [.dev-workstream/reviews/BATCH-01-REVIEW.md](.dev-workstream/reviews/BATCH-01-REVIEW.md)
