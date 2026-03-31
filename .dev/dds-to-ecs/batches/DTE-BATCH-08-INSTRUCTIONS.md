# DTE-BATCH-08: IOS Mission Editor UI + Runner Test Harness

**Batch Number:** DTE-BATCH-08  
**Tasks:** DDS2ECS-S14T1, DDS2ECS-S14T2, DDS2ECS-S14T3, DDS2ECS-S15T1, DDS2ECS-S15T2, DDS2ECS-S15T3  
**Phase:** Phase 14 + Phase 15  
**Estimated Effort:** 18�22 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-07 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch builds a full mission editor UI in IOS and starts the end-to-end Runner harness for automated integration tests.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S14T1 ? DDS2ECS-S15T3)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (�8.5, �9)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-07-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Hrot.ExCon/`, `Hrot.ClusterRunner/`, `Hrot.IG/`
- **Test Projects:** `Hrot.ExCon.Tests/`, `Hrot.ClusterRunner.Integration.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-08-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-08-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: DDS2ECS-S14T1 � Mission task list editing
**File:** `Hrot.ExCon/Panels/MissionPanel.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s14t1--task-list-editing-add--insert--delete`

---

### Task 2: DDS2ECS-S14T2 � BehaviorId dropdown + params editor
**File:** `Hrot.ExCon/Panels/MissionPanel.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s14t2--behaviorid-dropdown-and-behaviorparams-json-editor`

---

### Task 3: DDS2ECS-S14T3 � Commit button wired to `CommitMissionAsync`
**File:** `Hrot.ExCon/Panels/MissionPanel.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s14t3--commit-button-wired-to-commitmissionasync`

---

### Task 4: DDS2ECS-S15T1 � Internal test hooks
**Files:**
- `Hrot.ClusterRunner/Services/IgSubsystem.cs` (UPDATE)
- `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` (UPDATE)
- `Hrot.ClusterRunner/Services/IosSubsystem.cs` (UPDATE)
- `Hrot.IG/IgApplication.cs` (UPDATE)

**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s15t1--add-internal-test-hook-propertiesmethods`

---

### Task 5: DDS2ECS-S15T2 � Create `HrotRunnerHarness`
**File:** `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s15t2--create-hrotrunnerharness`

---

### Task 6: DDS2ECS-S15T3 � Map placement integration test
**File:** `Hrot.ClusterRunner.Integration.Tests/MapPlacementIntegrationTests.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s15t3--map-placement-integration-test`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Hrot.ExCon.Tests/Hrot.ExCon.Tests.csproj`
  - `dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj`

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
- [ ] DDS2ECS-S14T1�S14T3 complete with xUnit tests
- [ ] DDS2ECS-S15T1�S15T3 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-08-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not commit mission edits without the draft-plan guard.
- Do not skip `InternalsVisibleTo` wiring for harness access.
- Do not leave tests failing; fix the root cause.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (�8.5, �9)
