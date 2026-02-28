# DTE-BATCH-09: Runner Integration Tests + Mission Plan Queue Migration

**Batch Number:** DTE-BATCH-09  
**Tasks:** DDS2ECS-S15T4, DDS2ECS-S15T5, DDS2ECS-S15T6, DDS2ECS-S16T1, DDS2ECS-S16T2  
**Phase:** Phase 15 + Phase 16  
**Estimated Effort:** 16–20 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-08 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
Complete the remaining Runner integration tests and begin the SimHost mission pipeline migration by replacing `EntityMissionHolder` with `MissionPlanQueue` and updating the translator.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S15T4 ? DDS2ECS-S16T2)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (§9, §10)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-08-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Bagira.Runner.Integration.Tests/`, `Bagira.SimHost/`, `Bagira.SimHost.Tests/`
- **Related IG/IOS:** `Bagira.IG/`, `Bagira.IOS/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-09-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-09-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: DDS2ECS-S15T4 — Context Menu Push integration test
**File:** `Bagira.Runner.Integration.Tests/ContextMenuIntegrationTests.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s15t4--context-menu-push-integration-test`

---

### Task 2: DDS2ECS-S15T5 — Entity Destroy integration test
**File:** `Bagira.Runner.Integration.Tests/EntityDestroyIntegrationTests.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s15t5--entity-destroy-integration-test`

---

### Task 3: DDS2ECS-S15T6 — Mission Control integration test
**File:** `Bagira.Runner.Integration.Tests/MissionControlIntegrationTests.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s15t6--mission-control-integration-test`

---

### Task 4: DDS2ECS-S16T1 — Delete `EntityMissionHolder`, register `MissionPlanQueue`
**Files:**
- `Bagira.SimHost/Components/EntityMissionHolder.cs` (DELETE)
- `Bagira.SimHost/SimHostApp.cs` (UPDATE)
- `Bagira.SimHost.Tests/EntityMissionTranslatorTests.cs` (UPDATE)

**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s16t1--delete-entitymissionholder`

---

### Task 5: DDS2ECS-S16T2 — Rewrite `EntityMissionTranslator` to `MissionPlanQueue`
**Files:**
- `Bagira.SimHost/Translators/EntityMissionTranslator.cs` (UPDATE)
- `Bagira.SimHost.Tests/EntityMissionTranslatorTests.cs` (UPDATE)

**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s16t2--rewrite-entitymissiontranslator-to-write-missionplanqueue`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Bagira.Runner.Integration.Tests/Bagira.Runner.Integration.Tests.csproj`
  - `dotnet test Bagira.SimHost.Tests/Bagira.SimHost.Tests.csproj`

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
- [ ] DDS2ECS-S15T4–S15T6 complete with xUnit tests
- [ ] DDS2ECS-S16T1–S16T2 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-09-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not use `EntityMissionHolder` anywhere after the migration.
- Do not bypass mission trigger mapping rules in `MissionPlanQueue` translation.
- Do not leave integration tests flaky; use harness `PumpUntil` and avoid raw sleeps.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (§9, §10)
