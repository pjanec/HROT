# DTE-BATCH-11: SimHost Combat Readiness

**Batch Number:** DTE-BATCH-11  
**Tasks:** DDS2ECS-S17T1, DDS2ECS-S17T2, DDS2ECS-S17T3, DDS2ECS-S17T4, DDS2ECS-S17T5  
**Phase:** Phase 17  
**Estimated Effort:** 18–22 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-10 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch upgrades SimHost for combat readiness by wiring perception/combat toolkits and updating TKB combat descriptors as per Phase 17.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S17T1 ? DDS2ECS-S17T5)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (§11)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-10-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost/`, `Bagira.Map.Definitions/`
- **Test Projects:** `Bagira.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-11-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-11-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: DDS2ECS-S17T1 — Add Perception + Combat project references
**File:** `Bagira.SimHost/Bagira.SimHost.csproj` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s17t1--add-perception-and-combat-project-references`

---

### Task 2: DDS2ECS-S17T2 — Register Perception/Combat/Physics/HSM components
**File:** `Bagira.SimHost/SimHostApp.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s17t2--register-perception-combat-physics-and-hsm-components`

---

### Task 3: DDS2ECS-S17T3 — Initialize `PhysicsToolkitModule`
**File:** `Bagira.SimHost/SimHostApp.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s17t3--initialize-physicstoolkitmodule-in-simhostapponload`

---

### Task 4: DDS2ECS-S17T4 — Expand `SimulationLogicModule` with combat systems
**File:** `Bagira.SimHost/Modules/SimulationLogicModule.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s17t4--expand-simulationlogicmodule-with-combat-systems`

---

### Task 5: DDS2ECS-S17T5 — Rewrite `BdcTkbBuilder.WithCombat()` to real ECS components
**File:** `Bagira.Map.Definitions/Tkb/BdcTkbBuilder.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s17t5--rewrite-bdctkbbuilderwithcombat-to-attach-real-ecs-components`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
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

**Q1:** What issues did you encounter while wiring Perception/Combat modules? How did you resolve them?

**Q2:** Did you spot any weak points in the combat toolkits? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## ?? Success Criteria

This batch is DONE when:
- [ ] DDS2ECS-S17T1–S17T5 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-11-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not skip `PhysicsToolkitModule` initialization; the physics systems depend on its singletons.
- Do not register combat systems without the required components.
- Do not leave `BdcTkbBuilder.WithCombat()` attaching obsolete DTOs.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (§11)
