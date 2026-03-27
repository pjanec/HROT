# DTE-BATCH-01: Purify DDS Data Model (ComponentId Removal)

**Batch Number:** DTE-BATCH-01  
**Tasks:** DDS2ECS-S1T1, DDS2ECS-S1T2  
**Phase:** Phase 1 — Purify DDS Data Model  
**Estimated Effort:** 4–6 hours  
**Priority:** HIGH  
**Dependencies:** None (first batch)

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch removes ECS `[ComponentId]` attributes from DDS DTOs and adds reflection guard tests.
Follow the design rules in `docs/dds-to-ecs/DESIGN.md` — DDS types must never be ECS components.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S1T1, DDS2ECS-S1T2)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (§2.1, §3.1)
4. **Previous Review:** N/A (first batch)

### Source Code Location
- **Primary Work Area:** `Bagira.DDS.DataModel/`
- **Test Project:** `Bagira.DDS.DataModel.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-01-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement ? Write tests ? **ALL tests pass** ?
2. **Task 2:** Implement ? Write tests ? **ALL tests pass** ?

**DO NOT** move to the next task until:
- ? Current task implementation complete
- ? Current task tests written
- ? **ALL tests passing** (including previous task tests)

---

## Context
These tasks eliminate the ECS coupling on DDS DTOs. This unblocks later phases that replace
`AutoCycloneTranslator<EntityMaster>` and introduce explicit translators.

**Related Tasks:**
- [DDS2ECS-S1T1](../docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s1t1--remove-componentid-from-entitymaster)
- [DDS2ECS-S1T2](../docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s1t2--remove-componentid-from-entitydamage)

---

## ?? Batch Objectives
- Remove `[ComponentId]` attributes from `EntityMaster` and `EntityDamage` DDS DTOs.
- Add reflection guard tests to ensure these attributes never return.

---

## ? Tasks

### Task 1: Remove `[ComponentId]` from `EntityMaster` (DDS2ECS-S1T1)

**File:** `Bagira.DDS.DataModel/GenericDescriptors.cs` (UPDATE)  
**Task Definition:** See `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s1t1--remove-componentid-from-entitymaster`

**Description:**
Remove the `[ComponentId(GlobalComponentIds.EntityMaster)]` attribute from `EntityMaster`.
Keep all DDS attributes intact.

**Requirements:**
- Delete only the ECS attribute line.
- No other structural changes to `EntityMaster`.

**Design Reference:** `docs/dds-to-ecs/DESIGN.md` §2.1, §3.1

**Tests Required (xUnit):**
- Add xUnit test `EntityMaster_HasNo_ComponentIdAttribute` in `Bagira.DDS.DataModel.Tests/`:
  - Assert `typeof(EntityMaster).GetCustomAttributes(typeof(ComponentIdAttribute), false)` is empty.

---

### Task 2: Remove `[ComponentId]` from `EntityDamage` (DDS2ECS-S1T2)

**File:** `Bagira.DDS.DataModel/SimDescriptors.cs` (UPDATE)  
**Task Definition:** See `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s1t2--remove-componentid-from-entitydamage`

**Description:**
Remove the `[ComponentId(GlobalComponentIds.EntityDamage)]` attribute from `EntityDamage`.

**Requirements:**
- Delete only the ECS attribute line.
- Ensure no `Bagira.*` (non-FDP) code references `GlobalComponentIds.EntityDamage` after change.
  Use a solution-wide search to verify and include the result in your report.

**Design Reference:** `docs/dds-to-ecs/DESIGN.md` §2.1, §3.1

**Tests Required (xUnit):**
- Add xUnit test `EntityDamage_HasNo_ComponentIdAttribute` in `Bagira.DDS.DataModel.Tests/`:
  - Assert `typeof(EntityDamage).GetCustomAttributes(typeof(ComponentIdAttribute), false)` is empty.

---

## ?? Testing Requirements
- Update or add tests in `Bagira.DDS.DataModel.Tests/`.
- Run: `dotnet test Bagira.DDS.DataModel.Tests/Bagira.DDS.DataModel.Tests.csproj`
- All tests must pass before reporting completion.

---

## ?? Quality Standards

**? TEST QUALITY EXPECTATIONS**
- Tests must assert reflection results, not just instantiate objects.
- Use direct `GetCustomAttributes` checks as specified.

**? REPORT QUALITY EXPECTATIONS**
- Include the exact search query used to confirm no references to `GlobalComponentIds.EntityDamage`.
- Include test output summary and any issues encountered.

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
- [ ] DDS2ECS-S1T1 complete and tests added
- [ ] DDS2ECS-S1T2 complete and tests added
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-01-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not remove DDS attributes (`[DdsTopic]`, `[DdsIdlFile]`, `[DdsQos]`).
- Do not add new ECS attributes to DDS DTOs.
- Do not skip the reflection guard tests.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (§2.1, §3.1)
