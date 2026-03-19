# BD1-BATCH-02: Zero Alloc Events & Right-Click UX

**Batch Number:** BD1-BATCH-02  
**Tasks:** CORRECTIVE-0, CORRECTIVE-1, CORRECTIVE-2, BD1-P2T1  
**Phase:** Corrective + Phase 2  
**Estimated Effort:** ~6-8 hours  
**Priority:** HIGH  
**Dependencies:** BD1-BATCH-01

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch starts by fixing a critical allocation issue raised during the BATCH-01 review: system events must not be managed classes. After fixing this tech debt, you will implement the Phase 2 Right-Click Mission UX handler.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Tracker:** `docs/brain-death/BD1-TASK-TRACKER.md` - Overall progress context
3. **Task Definitions:** `docs/brain-death/BD1-TASK-DETAIL.md` (# Phase 2 - Right-Click Mission UX)
4. **Design Document:** `docs/brain-death/BD1-DESIGN.md` (Lines 208-226)

### Source Code Location
- **Primary Work Areas:**
  - `FDP/Toolkits/FDP.Toolkit.Behavior/`
  - `Bagira.SimHost/`
- **Test Projects:**
  - `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/`
  - `Bagira.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BD1-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BD1-BATCH-02-QUESTIONS.md`

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

---

## Context
During BATCH-01, `ClearDoctrineEvent` and `DoctrineFinishedEvent` were implemented as managed classes causing GC allocation on every publish. We will convert them to unmanaged structs. Then, we will fix the SimHost right-click interaction to properly differentiate between brain-active entities (routes through mission command) and brain-dead entities (routes straight to muscle).

---

## 🎯 Batch Objectives
- Ensure zero-allocation on the ECS event bus.
- Prevent looping and flickering during right-click interactions.

---

## ✅ Tasks

### Task 1: CORRECTIVE-0 — Zero-Allocation Events
**Files:**
- `FDP/Toolkits/FDP.Toolkit.Behavior/Events/ClearDoctrineEvent.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Events/DoctrineFinishedEvent.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DoctrineIngressSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`
- `Bagira.SimHost/Systems/MissionControlRequestSystem.cs`
- The associated Test files.

**Description:**
Change `ClearDoctrineEvent` and `DoctrineFinishedEvent` from `sealed class` to `struct`. Replace all usages of `PublishManaged` and `ConsumeManaged` for these events with `PublishUnmanaged` and `ConsumeUnmanaged`. Update tests accordingly.

**Tests Required:**
- ✅ Verify existing tests compile and pass. Add assertion or validation that the struct copying behaves correctly if needed.

### Task 2: CORRECTIVE-1 — BTreeTickSystem Memory Leak
**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs`

**Description:**
`_publishedTerminalForInstanceId` accumulates entries for entities indefinitely. Observe entity destruction events or prune the dictionary over time so it does not leak memory for long-running simulations.

**Tests Required:**
- ✅ Validate that destroying an entity clears its entry from the tracking collection.

### Task 3: CORRECTIVE-2 — MissionDirectorSystem Delegated State Mutation
**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`

**Description:**
For triggers other than `DoctrineFinished`, Phase transitions currently mutate `DoctrineState` directly. Standardize this by using `AssignDoctrineEvent` (or a similar mechanism) instead of directly mutating it, restoring single-ownership to `DoctrineIngressSystem`.

**Tests Required:**
- ✅ Existing tests must pass, proving phase transitions behave correctly but correctly delegate state mutation.

### Task 4: Brain-Aware Right-Click Handler (BD1-P2T1)
**File:** `Bagira.SimHost/SimHostVisualization.cs`
**Task Definition:** See [BD1-TASK-DETAIL.md](docs/brain-death/BD1-TASK-DETAIL.md#bd1-p2t1-simhostvisualization--brain-aware-right-click-handler)
**Design Reference:** [BD1-DESIGN.md](docs/brain-death/BD1-DESIGN.md#21-simhostvisualization--brain-aware-right-click-handler)

**Description:**
Rewrite the right-click handler in `SimHostVisualization.cs` with two explicit paths: a brain-dead path (calls `_scenario.SetDestination`) and a brain-active path (sends `CMD_REPLACE_MISSION` with a `ReachedDestination` trigger). 

**Tests Required:**
- ✅ `RightClick_BrainDead_CallsSetDestination`
- ✅ `ShiftRightClick_BrainDead_CallsAddWaypoint`
- ✅ `RightClick_BrainActive_WritesMissionWithTrigger`

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **REQUIRED:** Tests that verify actual simulated system pipeline behavior and edge cases. Make sure to check the *exact rules* laid out under each task definition.
- **REQUIRED:** You must run tests locally. Verify tests can detect broken behavior.

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them.
- **REQUIRED:** Document design decisions YOU made beyond the spec.

---

## 📊 Report Requirements

In your `.dev-workstream/reports/BD1-BATCH-02-REPORT.md`, answer:

**Developer Insights**

**Q1:** What issues did you encounter during the unmanaged conversion? How were they resolved?

**Q2:** Are there any edge cases with the Right-Click path determination?

---

## 🎯 Success Criteria
- [ ] Task CORRECTIVE-0 completed (Tests passed, no `PublishManaged` remaining for these events)
- [ ] Task CORRECTIVE-1 completed (Tests passed)
- [ ] Task CORRECTIVE-2 completed (Tests passed)
- [ ] Task BD1-P2T1 completed (Tests passed)
- [ ] ALL tests passing.
- [ ] Report submitted.

---

## 📚 Reference Materials
- **Task Defs:** `docs/brain-death/BD1-TASK-DETAIL.md`
- **Design:** `docs/brain-death/BD1-DESIGN.md`
