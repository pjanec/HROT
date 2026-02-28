# DTE-BATCH-07: Fire Interaction Events + Mission Control

**Batch Number:** DTE-BATCH-07  
**Tasks:** DDS2ECS-S12T1, DDS2ECS-S12T2, DDS2ECS-S12T3, DDS2ECS-S13T1, DDS2ECS-S13T2  
**Phase:** Phase 12 + Phase 13  
**Estimated Effort:** 14–18 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-06 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch completes transient combat event replication and adds mission-control request handling in SimHost.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S12T1 ? DDS2ECS-S13T2)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (§6.2, §8.4)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-06-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Bagira.IG/`, `Bagira.SimHost/`
- **Test Projects:** `Bagira.IG.Tests/`, `Bagira.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-07-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-07-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: DDS2ECS-S12T1 — Create `FireInteractionEventTranslator`
**Files:**
- `Bagira.IG/Translators/FireInteractionEventTranslator.cs` (NEW, ingress)
- `Bagira.SimHost/Translators/FireInteractionEventTranslator.cs` (NEW, egress)

**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s12t1--create-fireinteractioneventtranslator`

---

### Task 2: DDS2ECS-S12T2 — Register SimHost egress translator
**Files:** `Bagira.SimHost/SimHostApp.cs`, `Bagira.Runner/Services/SimHostSubsystem.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s12t2--simhostapp--simhostsubsystem-register-egress-translator`

---

### Task 3: DDS2ECS-S12T3 — Register IG ingress translator
**File:** `Bagira.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s12t3--igapplication-register-ingress-translator`

---

### Task 4: DDS2ECS-S13T1 — Create `MissionControlRequestSystem`
**File:** `Bagira.SimHost/Systems/MissionControlRequestSystem.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s13t1--create-missioncontrolrequestsystem`

---

### Task 5: DDS2ECS-S13T2 — Register mission control system
**Files:** `Bagira.SimHost/SimHostApp.cs`, `Bagira.Runner/Services/SimHostSubsystem.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s13t2--register-missioncontrolrequestsystem`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Bagira.IG.Tests/Bagira.IG.Tests.csproj`
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
- [ ] DDS2ECS-S12T1–S12T3 complete with xUnit tests
- [ ] DDS2ECS-S13T1–S13T2 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-07-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not publish fire interaction events from IG (ingress only).
- Do not skip DDS ack creation for mission control requests.
- Do not leave tests failing; fix the root cause.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (§6.2, §8.4)
