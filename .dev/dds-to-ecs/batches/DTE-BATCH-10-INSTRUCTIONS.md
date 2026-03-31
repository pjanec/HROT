# DTE-BATCH-10: Mission Director Migration

**Batch Number:** DTE-BATCH-10  
**Tasks:** DDS2ECS-S16T3, DDS2ECS-S16T4, DDS2ECS-S16T5  
**Phase:** Phase 16  
**Estimated Effort:** 14�18 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-09 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch completes the UrbanCombat-aligned mission pipeline by removing the legacy `MissionAdapterSystem`, registering `MissionDirectorSystem`, compiling real BTree interpreters, and wiring parameter parsing for doctrine definitions.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S16T3 ? DDS2ECS-S16T5)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (�10)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-09-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/`, `FDP.Toolkit.Behavior/`
- **Test Projects:** `Hrot.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-10-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-10-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: DDS2ECS-S16T3 � Delete `MissionAdapterSystem`, register `MissionDirectorSystem`
**Files:**
- `Hrot.SimHost/Systems/MissionAdapterSystem.cs` (DELETE or retire)
- `Hrot.SimHost/Modules/SimulationLogicModule.cs` (UPDATE)
- `Hrot.SimHost.Tests/MissionAdapterSystemTests.cs` (UPDATE or replace)

**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s16t3--delete-missionadaptersystem-register-missiondirectorsystem`

---

### Task 2: DDS2ECS-S16T4 � Compile real BTree interpreters for all doctrines
**Files:**
- `Hrot.SimHost/DoctrineIds.cs` and doctrine registry setup
- Any required FDP behavior toolkit classes

**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s16t4--compile-real-btree-interpreters-for-all-doctrines`

---

### Task 3: DDS2ECS-S16T5 � Wire `ParseParams` delegates
**Files:**
- `Hrot.SimHost/DoctrineIds.cs` or doctrine registry setup
- `Hrot.SimHost.Tests/*` for coverage

**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s16t5--wire-parseparams-delegates-for-param-carrying-doctrines`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`

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

**Q1:** What issues did you encounter during MissionDirectorSystem integration? How did you resolve them?

**Q2:** Did you spot any weak points in the Behavior toolkit integration? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## ?? Success Criteria

This batch is DONE when:
- [ ] DDS2ECS-S16T3�S16T5 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-10-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not keep `MissionAdapterSystem` registered once `MissionDirectorSystem` is in place.
- Do not skip doctrine interpreter compilation; partial doctrine coverage is a failure.
- Do not leave `ParseParams` unassigned for param-carrying behaviors.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (�10)
