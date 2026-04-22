# INTS-BATCH-02: Architecture Consolidation

**Batch Number:** INTS-BATCH-02  
**Tasks:** INTS-P2-006, INTS-P2-007, INTS-P2-008, INTS-P2-009, INTS-P2-010  
**Phase:** Phase 2 - Architecture Consolidation  
**Estimated Effort:** 8 hours  
**Priority:** HIGH  
**Dependencies:** INTS-BATCH-01 must be completed and merged.

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch consolidates the architecture across the whole Hrot ecosystem by creating a single shared bootstrapper (`HrotEnvironment`). It will eliminate duplicate initialization code and resolve headless simulation logic on combined startup.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs\design\TASK-DETAILS-Integration-Troubleshooting.md` - See detailed task specifications
3. **Design Document:** `docs\design\DESIGN-Integration-Troubleshooting.md` - Technical context
4. **Developer Guidance (Project Rules):** `.dev-workstream/guides/CODE-STANDARDS.md`
5. **Previous Review:** `.dev-workstream/reviews/INTS-BATCH-01-REVIEW.md` - Make sure tests actually verify system properties and behaviors.

### Source Code Location
- **Primary Work Areas:** 
  - `Hrot.Map.Common`
  - `Hrot.ClusterRunner`
  - `Hrot.IG`
  - `Hrot.SimHost`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/INTS-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/INTS-BATCH-02-QUESTIONS.md`

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

---

## Context

With communication channels verified during Phase 1, Phase 2 focuses on code consistency. We will provide a shared Bootstopper so that all platforms use identical TKB databases, WGS84 coordinates, and generic DDS environments without duplicating boilerplate code. Finally, because `Raylib` restricts graphic access context ownership, we must restrict SimHost to headless operation when IG is active.

**Related Tasks:**
- [INTS-P2-006](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-006--implement-hrotenvironment-bootstrapper) - Common Bootstrapper
- [INTS-P2-007](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-007--fix-subsystemorchestrator-headless-logic) - SubsystemOrchestrator headless mode fix
- [INTS-P2-008](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-008--refactor-igapplication-to-use-hrotenvironment) - Update IgApplication 
- [INTS-P2-009](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-009--refactor-simhostapp-to-use-hrotenvironment) - Update SimHostApp
- [INTS-P2-010](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-010--refactor-iossubsystem-to-use-hrotenvironment) - Update IosSubsystem

---

## 🎯 Batch Objectives
Ensure the Simulation, IG, and IOS components all leverage the `HrotEnvironment` bootstrapper. Fix combined execution restrictions to prevent ImGui and viewports from failing gracefully. Keep application configurations and setups unified.

---

## ✅ Tasks

### Task 1: Implement HrotEnvironment Bootstrapper (INTS-P2-006)
**Files:** `Hrot.Map.Common/HrotEnvironment.cs` (New)
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-006--implement-hrotenvironment-bootstrapper)

### Task 2: Fix SubsystemOrchestrator Headless Logic (INTS-P2-007)
**Files:** `Hrot.ClusterRunner/Services/SubsystemOrchestrator.cs`
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-007--fix-subsystemorchestrator-headless-logic)

### Task 3: Refactor IgApplication to Use HrotEnvironment (INTS-P2-008)
**Files:** `Hrot.IG/IgApplication.cs`
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-008--refactor-igapplication-to-use-hrotenvironment)

### Task 4: Refactor SimHostApp to Use HrotEnvironment (INTS-P2-009)
**Files:** `Hrot.SimHost/SimHostApp.cs`
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-009--refactor-simhostapp-to-use-hrotenvironment)

### Task 5: Refactor IosSubsystem to Use HrotEnvironment (INTS-P2-010)
**Files:** `Hrot.ClusterRunner/Services/IosSubsystem.cs`
**Task Definition:** See [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-010--refactor-iossubsystem-to-use-hrotenvironment)

---

## 🧪 Testing and Technical Requirements

**Guidelines Override:**
- **xUnit Framework:** All new unit tests MUST be xUnit (absolutely no MSTest or NUnit tests are permitted).
- **FdpLog Standard:** Debug prints and logging MUST utilize `FdpLog` from the FDP kernel. Using `Console.WriteLine` or standard logging frameworks is invalid.
- **Verification of Quality:** Unit tests MUST exercise the actual resulting state structure. Empty setup tests, simplistic mocked methods, or tests checking exclusively for compile success will be rejected. 

---

## 📊 Report Requirements

Provide a copy of this layout in your `.dev-workstream/reports/INTS-BATCH-02-REPORT.md` report, filling in details:

**Developer Insights**

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] HrotEnvironment successfully builds identical environments and participant bindings
- [ ] SubsystemOrchestrator enables automated headless mode gracefully when IG acts as Map owner
- [ ] Test scenarios defined within INTS-P2-006..INTS-P2-010 are actively verified passing
- [ ] No regression failures occur across SimHost, IOS, IG, and Runner suites
- [ ] Report submitted addressing developer feedback explicitly

---

## 📚 Reference Materials
- **Task Defs:** [TASK-DETAILS-Integration-Troubleshooting.md](../../docs/design/TASK-DETAILS-Integration-Troubleshooting.md)
- **Design:** [DESIGN-Integration-Troubleshooting.md](../../docs/design/DESIGN-Integration-Troubleshooting.md)
