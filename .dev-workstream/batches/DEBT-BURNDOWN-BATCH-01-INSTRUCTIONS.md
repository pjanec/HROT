# DEBT-BURNDOWN-BATCH-01

**Batch Number:** DEBT-BURNDOWN-BATCH-01  
**Tasks:** Debt items logged in DEBT-TRACKER.md  
**Estimated Effort:** 8-10 hours  
**Priority:** HIGH  
**Dependencies:** BUG2-BATCH-02 (must be fully merged to clear its own tracked tech debt logic fixes)

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch is laser-focused on prioritizing testing instability, crash avoidance, and architectural inconsistencies tracked from previous development sequences. Our primary objective is to clean the board of P1 and P2 regressions. 

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Current Debt State:** `.dev-workstream/DEBT-TRACKER.md` - The exact tracking parameters of these bugs

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost.Integration.Tests/Infrastructure/`, `Fdp.Examples.UrbanCombat.Tests/`, `FDP/Toolkits/FDP.Toolkit.Replay.Tests/`
- **Test Projects:** (See individual task specifications below, these are mainly test-layer fixes)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DEBT-BURNDOWN-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DEBT-BURNDOWN-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task X:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context

Because we've achieved 100% bug completion in the BUG2 tracker, we will use this batch to eliminate the highest-friction technical debt.

**Related Debt:**
- Fixing native access violations in `Fdp.Examples.UrbanCombat.Tests`.
- Aligning `SimHostInstance.Tick()` multi-swap behavior to match production to prevent event consumption divergences.
- Eliminating race conditions inside the `FDP.Toolkit.Replay.Tests` tearing down recording modules.
- Removing duplicate `RegisterComponent<MissionAdapterState>()` calls internally inside `SimHostInstance`.

---

## 🎯 Batch Objectives
- Resolve all currently failing test projects, particularly those affected by headless process race conditions and COM/native crashes.
- Align the integration test harness `SimHostInstance` entirely with real `SimHostApp` execution order for identical runtime profiles.

---

## ✅ Tasks

### Task 1: Intercept Native Access Violation in UrbanCombat Tests (P1)
**File:** `Fdp.Examples.UrbanCombat.Tests/` (diagnose root file)
**Description:** `Fdp.Examples.UrbanCombat.Tests` is suffering an access violation crash in native code when executed. You will trace the P/Invoke or memory boundary failure, debug it, and patch it. 
**Requirements:** Ensure `dotnet test Fdp.Examples.UrbanCombat.Tests.csproj` passes without a 0xC0000005.

### Task 2: Align SimHostInstance.Tick() Multi-Swap Architecture (P2)
**File:** `Bagira.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs`
**Description:** `SimHostInstance.Tick()` currently utilizes three pre-sim `Bus.SwapBuffers()` calls for spawn/lifecycle phases. This silently destroyed events, as seen when `EntityMission_MovesEntity` failed previously. Restructure `Tick()` to more closely match `SimHostApp.OnUpdate()` or properly segment event streams so simulation systems don't drop newly published states.
**Tests Required:** Any integration test that leverages `SimHostInstance` where an event is published and then relied upon should succeed cleanly without synchronous bypasses.

### Task 3: Repair Async Race Conditions in Replay Tests (P3)
**File:** `FDP/Toolkits/FDP.Toolkit.Replay.Tests/RecordingModuleTests.cs` (or relevant)
**Description:** Ensure that async task continuations and dispose methods safely block and clean up their file buffers without conflicting across test hosts. Specifically resolve `RecordingModule_Dispose_BlocksUntilAsyncRecorderFlushed` and `TwoStoryRecorderModules_RunConcurrently`.

### Task 4: Remove Duplicate MissionAdapterState Registration (P3)
**File:** `Bagira.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs`
**Description:** Find and remove the duplicate `RegisterComponent<MissionAdapterState>()` call from the constructor to clean up initialization logic.

---

## 🧪 Testing Requirements
- Unit tests MUST actually verify the structural lifecycle assertions avoiding silent faults.
- You must eliminate all test host crashes. `dotnet test IOS-IG-SimHost.sln` will complete fully with **NO ERRORS** and **NO RUNNER CRASHES**.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please capture your valuable insights and experience:

**Q1:** What issues did you encounter during implementation? How did you resolve them?
**Q2:** Did you spot any weak points in the existing codebase? What would you improve?
**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?
**Q4:** What edge cases did you discover that weren't mentioned in the spec?
**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## 🎯 Success Criteria
- [ ] Task 1 completed (UrbanCombat native crash resolved)
- [ ] Task 2 completed (SimHostInstance multi-swap architecture reconciled)
- [ ] Task 3 completed (Replay testing race conditions stabilized)
- [ ] Task 4 completed (Duplicate component registration purged)
- [ ] All overall solution tests passing
- [ ] Report submitted answering the 5 questions
