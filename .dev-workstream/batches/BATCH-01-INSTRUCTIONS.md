# BATCH-01: Demo Framework Foundation

**Batch Number:** BATCH-01  
**Tasks:** DEM1-F001, DEM1-F002, DEM1-F003, DEM1-F004, DEM1-F005  
**Phase:** Phase 0 — Demo Framework Foundation  
**Estimated Effort:** 10-12 hours  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the team! This first batch sets up the foundational framework for running our deterministic scenarios, essentially an orchestrator built on top of `FDP.Framework.Runner` that can process tests offline in CI or interactively. 

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Onboarding Guide:** `docs/demos-1/DEM1-ONBOARDING.md` - Newcomer guide
3. **Design Document:** `docs/demos-1/DEM1-DESIGN.md` - Architecture and phase overview (specifically Sections 4.1 to 4.6)
4. **Task Definitions:** `docs/demos-1/DEM1-TASK-DETAIL.md` - Full specification for DEM1-F001 through DEM1-F005

### Source Code Location
- **Primary Work Areas:** 
  - `FDP/Framework/FDP.Framework.Runner/`
  - `FDP/Examples/Fdp.Examples.Common/`
  - `FDP/Examples/Fdp.Examples.Runner/`
- **Test Project:** 
  - `FDP/Examples/Fdp.Examples.Scenarios.Tests/` (To be created)
  - `FDP/Framework/FDP.Framework.Runner.Tests/` (Existing)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BATCH-01-QUESTIONS.md`

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

## Context

This batch constructs the core infrastructure needed to run deterministic scenario tests. It introduces the `IScenario` concept, a `ScenarioSubsystem` that manages an EntityRepository and steps it predictably, and an entry point (`fdp-demo-runner`) to parse CLI arguments, route execution, and provide rich trace logging for the CI systems to digest.

**Related Tasks:**
- [DEM1-F001](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f001--deterministic-mode-in-runneroptions-and-runnerconfiguration) - Deterministic Orchestrator execution
- [DEM1-F002](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f002--iscenario-interface-and-scenariosubsystem) - IScenario and ScenarioSubsystem
- [DEM1-F003](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f003--scenarioregistry-cli-programcs-and-runner-project) - Demo Runner CLI app
- [DEM1-F004](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f004--nlog-trace-logging-setup) - Robust programmatic NLog configuration
- [DEM1-F005](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f005--scenarionames-constants-and-base-test-infrastructure) - Constants and Harness

---

## 🎯 Batch Objectives
Build the CI test harness, ensuring that `IScenario` can be run completely independent of real-time clocks, with all state isolated per run and detailed NLog output written out immediately for diagnosis.

---

## ✅ Tasks

### Task 1: Deterministic Mode in RunnerOptions (DEM1-F001)

**File:** `FDP/Framework/FDP.Framework.Runner/RunnerOptions.cs` and `RunnerConfiguration.cs`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-F001](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f001--deterministic-mode-in-runneroptions-and-runnerconfiguration)

**Description:** Implement `Deterministic` toggle and `FixedDeltaSeconds` values propagating from CLI configuration down into `SubsystemOrchestrator` execution loops.
**Design Reference:** [DEM1-DESIGN.md §4.1](docs/demos-1/DEM1-DESIGN.md#41-deterministic-mode-in-runneroptions--runnerconfiguration)

**Tests Required:**
- ✅ `DeterministicOrchestratorPassesFixedDt_ToSubsystemUpdate`
- ✅ `NonDeterministicHeadlessOrchestratorPassesZeroDt`
- ✅ `SubsystemConfigPropagatesDeterministicFlag`

---

### Task 2: IScenario Interface and ScenarioSubsystem (DEM1-F002)

**File:** `FDP/Examples/Fdp.Examples.Common/` (New Project)  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-F002](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f002--iscenario-interface-and-scenariosubsystem)

**Description:** Define the `IScenario` interface and implement `ScenarioSubsystem` which acts as the bridge managing the ECS Kernel, injecting commands, and evaluating win/loss conditions deterministically.
**Design Reference:** [DEM1-DESIGN.md §4.3](docs/demos-1/DEM1-DESIGN.md#43-iscenario-interface-fdpexamplescommon) & [§4.4](docs/demos-1/DEM1-DESIGN.md#44-scenariosubsystem-fdpexamplescommon)

**Tests Required:**
- ✅ `ScenarioSubsystem_ExitsZero_WhenScenarioSucceeds`
- ✅ `ScenarioSubsystem_ExitsOne_WhenAssertionFails`
- ✅ `ScenarioSubsystem_ExitsTwo_OnTimeout`
- ✅ `ScenarioSubsystem_Deterministic_GlobalTimeHasCorrectDelta`

---

### Task 3: ScenarioRegistry, CLI Program.cs, and Runner (DEM1-F003)

**File:** `FDP/Examples/Fdp.Examples.Runner/` (New Project)  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-F003](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f003--scenarioregistry-cli-programcs-and-runner-project)

**Description:** Implements the `fdp-demo-runner` executable with `CommandLine.Parser` bindings mapping scenarios string to runtime objects via `ScenarioRegistry`.
**Design Reference:** [DEM1-DESIGN.md §4.5](docs/demos-1/DEM1-DESIGN.md#45-scenarioregistry-fdpexamplesrunner) & [§4.6](docs/demos-1/DEM1-DESIGN.md#46-programcs-cli)

**Tests Required:**
- ✅ `Runner_WithUnknownScenario_ExitsNonZero`
- ✅ `Runner_PrintsLogFilePath_ToStdout`

---

### Task 4: NLog Trace Logging Setup (DEM1-F004)

**File:** `FDP/Examples/Fdp.Examples.Runner/Program.cs`  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-F004](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f004--nlog-trace-logging-setup)

**Description:** Setup robust programmatic NLog logic that dynamically creates log files based on scenario name and current timestamp. Embed scenarios in diagnostic trace elements.
**Design Reference:** [DEM1-DESIGN.md §4.2](docs/demos-1/DEM1-DESIGN.md#42-fdplog-file-target-setup-in-the-runner)

**Tests Required:**
- ✅ `AfterRun_LogFileExists_AndContainsExpectedLines`
- ✅ `OnFailure_LogFileContains_DiagnosticValues`

---

### Task 5: ScenarioNames Constants and Test Infrastructure (DEM1-F005)

**File:** `FDP/Examples/Fdp.Examples.Common/Constants/` & `FDP/Examples/Fdp.Examples.Scenarios.Tests/` (New Project)  
**Task Definition:** See [DEM1-TASK-DETAIL.md - DEM1-F005](docs/demos-1/DEM1-TASK-DETAIL.md#dem1-f005--scenarionames-constants-and-base-test-infrastructure)

**Description:** Provides string literal definitions for scenarios and implements `ScenarioTestHarness` for simple orchestration in the xUnit environments.
**Design Reference:** [DEM1-DESIGN.md §5.2](docs/demos-1/DEM1-DESIGN.md#52-fdpexamplescommon--shared-state-and-tooling)

**Tests Required:**
- ✅ `ScenarioTestHarness_WithSucceedingScenario_ReturnsZero`
- ✅ `ScenarioTestHarness_WithFailingScenario_ReturnsOne`
- ✅ `ScenarioTestHarness_WithTimingOutScenario_ReturnsTwo`

---

## 🧪 Testing Requirements
- **Testing Approach:** Every logical class must have unit tests.
- **Test Projects:** 
   - `FDP.Framework.Runner.Tests` for Task 1.
   - `Fdp.Examples.Scenarios.Tests` for Task 2-5 elements if applicable, or their own project tests.
- **⚠️ TEST QUALITY EXPECTATIONS:** 
   - **NOT ACCEPTABLE:** Tests that only verify object instantiation or string presence.
   - **REQUIRED:** Tests that verify actual behavior, state changes, delta time injection properly propagated, robust handling of exceptions from failure routes, and edge cases. Make sure generated outputs exist. 

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
- [ ] DEM1-F001 completed
- [ ] DEM1-F002 completed
- [ ] DEM1-F003 completed
- [ ] DEM1-F004 completed
- [ ] DEM1-F005 completed
- [ ] **ALL specified xUnit tests passing** verifying correct functionality and behavior.
- [ ] Developer `.dev-workstream/reports/BATCH-01-REPORT.md` written completely.

---

## ⚠️ Common Pitfalls to Avoid
- Failing to properly register NLog.LogManager.Configuration (it must work without specific NLog.config external files to not break across CI hosts).
- `ScenarioSubsystem` needs to capture orchestrator stop sequences well so `Environment.Exit` defaults aren't called dynamically in xUnit tests (`ScenarioTestHarness`).
- Getting lost making `Fdp.Examples.Common` overly deep: It only needs the bare minimum components required for Phase 0 execution.

---

## 📚 Reference Materials
- **Task Defs:** [DEM1-TASK-DETAIL.md](docs/demos-1/DEM1-TASK-DETAIL.md)
- **Design:** [DEM1-DESIGN.md](docs/demos-1/DEM1-DESIGN.md)
