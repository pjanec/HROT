# DTE-BATCH-05: SimHost Network Cleanup System

**Batch Number:** DTE-BATCH-05  
**Tasks:** DDS2ECS-S9T1, DDS2ECS-S9T2  
**Phase:** Phase 9  
**Estimated Effort:** 4–6 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-04 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch eliminates zombie entities by registering `CycloneNetworkCleanupSystem` in both SimHost entry points.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S9T1 ? DDS2ECS-S9T2)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (§6.1)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-04-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost/`, `Bagira.Runner/`
- **Test Projects:** `Bagira.SimHost.Tests/`, `Bagira.Runner.Tests/` (or a new test project if required)

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-05-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-05-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: DDS2ECS-S9T1 — `SimHostApp`: register `CycloneNetworkCleanupSystem`
**File:** `Bagira.SimHost/SimHostApp.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s9t1--simhostapp-register-cyclonenetworkcleanupsystem`

**Requirements:**
- Register `CycloneNetworkCleanupSystem` after constructing `EntityMasterEgressTranslator`.
- Add unit test per task detail verifying registration in headless init.

---

### Task 2: DDS2ECS-S9T2 — `SimHostSubsystem`: same registration
**File:** `Bagira.Runner/Services/SimHostSubsystem.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s9t2--simhostsubsystem-same-registration`

**Requirements:**
- Mirror the same cleanup-system registration in the subsystem initialize path.
- Add unit test per task detail verifying registration.

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Bagira.SimHost.Tests/Bagira.SimHost.Tests.csproj`
  - `dotnet test Bagira.Runner.Tests/Bagira.Runner.Tests.csproj` (if tests added there)

---

## ?? Quality Standards

**? TEST QUALITY EXPECTATIONS**
- Tests must verify behavior (no string-only checks).
- Include all scenarios specified in `TASK-DETAIL.md` for these tasks.

**? REPORT QUALITY EXPECTATIONS**
- Include test output summary.
- List any design deviations and why they were required.

---

## ?? Report Requirements

## Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## ?? Success Criteria

This batch is DONE when:
- [ ] DDS2ECS-S9T1 complete with xUnit tests
- [ ] DDS2ECS-S9T2 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-05-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not skip cleanup-system registration in either entry point.
- Do not leave tests failing; fix the root cause.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (§6.1)
