# DTE-BATCH-13: Integration Troubleshooting Phase 2

**Batch Number:** DTE-BATCH-13  
**Tasks:** INTS-P2-006, INTS-P2-007, INTS-P2-008, INTS-P2-009, INTS-P2-010  
**Phase:** Integration Troubleshooting P2  
**Estimated Effort:** 16–20 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-12 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch consolidates shared initialization and headless behavior across SimHost, IG, and IOS using `BagiraEnvironment`, and fixes headless orchestration when IG is present.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md` (INTS-P2-006 ? INTS-P2-010)
3. **Design Document:** `docs/design/DESIGN-Integration-Troubleshooting.md`
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-12-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Bagira.Map.Common/`, `Bagira.Runner/`, `Bagira.IG/`, `Bagira.SimHost/`
- **Test Projects:** `Bagira.Map.Common.Tests/`, `Bagira.IG.Tests/`, `Bagira.SimHost.Tests/`, `Bagira.Runner.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-13-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-13-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: INTS-P2-006 — Implement `BagiraEnvironment` bootstrapper
**File:** `Bagira.Map.Common/BagiraEnvironment.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-006--implement-bagiraenvironment-bootstrapper`

---

### Task 2: INTS-P2-007 — Fix SubsystemOrchestrator headless logic
**File:** `Bagira.Runner/Services/SubsystemOrchestrator.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-007--fix-subsystemorchestrator-headless-logic`

---

### Task 3: INTS-P2-008 — Refactor `IgApplication` to use `BagiraEnvironment`
**File:** `Bagira.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-008--refactor-igapplication-to-use-bagiraenvironment`

---

### Task 4: INTS-P2-009 — Refactor `SimHostApp` to use `BagiraEnvironment`
**File:** `Bagira.SimHost/SimHostApp.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-009--refactor-simhostapp-to-use-bagiraenvironment`

---

### Task 5: INTS-P2-010 — Refactor `IosSubsystem` to use `BagiraEnvironment`
**File:** `Bagira.Runner/Services/IosSubsystem.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p2-010--refactor-iossubsystem-to-use-bagiraenvironment`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Bagira.Map.Common.Tests/Bagira.Map.Common.Tests.csproj`
  - `dotnet test Bagira.IG.Tests/Bagira.IG.Tests.csproj`
  - `dotnet test Bagira.SimHost.Tests/Bagira.SimHost.Tests.csproj`
  - `dotnet test Bagira.Runner.Tests/Bagira.Runner.Tests.csproj`

---

## ?? Quality Standards

**? TEST QUALITY EXPECTATIONS**
- Tests must verify behavior (no string-only checks).
- Include all scenarios specified in `TASK-DETAILS-Integration-Troubleshooting.md` for these tasks.

**? REPORT QUALITY EXPECTATIONS**
- Include test output summary.
- List any design deviations and why they were required.

---

## ?? Report Requirements

## Developer Insights

**Q1:** What issues did you encounter while consolidating initialization via `BagiraEnvironment`? How did you resolve them?

**Q2:** Did you spot any weak points in headless orchestration? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## ?? Success Criteria

This batch is DONE when:
- [ ] INTS-P2-006–P2-010 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-13-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not instantiate multiple DDS participants inside the same app initialization path.
- Ensure `BagiraEnvironment.CreateTkb()` registers `BdcTkbCatalog` before returning.
- When IG is present, SimHost must be forced headless in the orchestrator to avoid input conflicts.

---

## ?? Reference Materials
- **Task Details:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md`
- **Design:** `docs/design/DESIGN-Integration-Troubleshooting.md`
