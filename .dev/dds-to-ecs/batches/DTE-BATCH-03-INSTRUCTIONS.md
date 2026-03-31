# DTE-BATCH-03: IG EntityMaster + EntityInfo Cleanup (with DTE-BATCH-02 fixes)

**Batch Number:** DTE-BATCH-03  
**Tasks:** Corrective-0 (DTE-BATCH-02 fixes), DDS2ECS-S4T1, DDS2ECS-S4T2, DDS2ECS-S4T3, DDS2ECS-S5T1, DDS2ECS-S5T2, DDS2ECS-S5T3, DDS2ECS-S5T4  
**Phase:** Phase 4 + Phase 5  
**Estimated Effort:** 10�12 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-02 fixes applied

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch applies DTE-BATCH-02 corrective fixes and then cleans IG�s EntityMaster/EntityInfo ingress to remove DDS DTOs from ECS, introducing `IgEntityData`.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S4T1 ? DDS2ECS-S5T4)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (�3.4, �3.5)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-02-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Hrot.IG/`, `Hrot.ClusterRunner/`, `Hrot.SimHost/`
- **Test Projects:** `Hrot.IG.Tests/`, `Hrot.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-03-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Corrective Task 0:** Implement ? Write tests (if needed) ? **ALL tests pass** ?
2. **Task 1:** Implement ? Write tests ? **ALL tests pass** ?
3. **Task 2:** Implement ? Write tests ? **ALL tests pass** ?
4. **Task 3:** Implement ? Write tests ? **ALL tests pass** ?
5. **Task 4:** Implement ? Write tests ? **ALL tests pass** ?
6. **Task 5:** Implement ? Write tests ? **ALL tests pass** ?
7. **Task 6:** Implement ? Write tests ? **ALL tests pass** ?
8. **Task 7:** Implement ? Write tests ? **ALL tests pass** ?
9. **Task 8:** Implement ? Write tests ? **ALL tests pass** ?

**DO NOT** move to the next task until:
- ? Current task implementation complete
- ? Current task tests written
- ? **ALL tests passing** (including previous task tests)

---

## Context
Phase 4 removes DDS DTO usage from IG�s `EntityMasterTranslator`. Phase 5 introduces `IgEntityData` and updates `EntityInfoTranslator` to publish IG-internal ECS data instead of DDS DTOs.

**Related Tasks:**
- Phase 4: [DDS2ECS-S4T1�S4T3](../docs/dds-to-ecs/TASK-DETAIL.md#phase-4-ig--fix-entitymastertranslator)
- Phase 5: [DDS2ECS-S5T1�S5T4](../docs/dds-to-ecs/TASK-DETAIL.md#phase-5-ig--create-igentitydata-and-fix-entityinfotranslator)

---

## ?? Batch Objectives
- Apply DTE-BATCH-02 corrective fixes.
- Ensure IG no longer writes DDS `EntityMaster`/`EntityInfo` into ECS.
- Introduce `IgEntityData` and register it correctly.

---

## ? Tasks

### Corrective Task 0: Fix DTE-BATCH-02 issues

**Files:**  
- `Hrot.SimHost/Translators/EntityMasterEgressTranslator.cs` (UPDATE)  
- `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` (UPDATE)

**Requirements:**
- Update ownership filtering to compare `PrimaryOwnerId` vs `LocalNodeId` (see DTE-BATCH-02 review).
- Remove `world.RegisterComponent<EntityMaster>()` from SimHostSubsystem component registration.

---

### Task 1: DDS2ECS-S4T1 � Spawn path: empty `InitialComponents`
**File:** `Hrot.IG/Translators/EntityMasterTranslator.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s4t1--spawn-path-empty-initialcomponents`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests per success conditions.

---

### Task 2: DDS2ECS-S4T2 � Update path: remove `SetComponent`
**File:** `Hrot.IG/Translators/EntityMasterTranslator.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s4t2--update-path-remove-cmdsetcomponentexisting-master`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests per success conditions.

---

### Task 3: DDS2ECS-S4T3 � `ApplyToEntity` becomes no-op
**File:** `Hrot.IG/Translators/EntityMasterTranslator.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s4t3--applytoentity-become-a-no-op`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests per success conditions.

---

### Task 4: DDS2ECS-S5T1 � Create `IgEntityData`
**File:** `Hrot.IG/Components/IgEntityData.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s5t1--create-igentitydata-component`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests per success conditions. Coordinate component ID allocation per task detail note.

---

### Task 5: DDS2ECS-S5T2 � `EntityInfoTranslator.PollIngress` ? `IgEntityData`
**File:** `Hrot.IG/Translators/EntityInfoTranslator.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s5t2--entityinfotranslator-translate-to-igentitydata`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests per success conditions.

---

### Task 6: DDS2ECS-S5T3 � `EntityInfoTranslator.ApplyToEntity` ? `IgEntityData`
**File:** `Hrot.IG/Translators/EntityInfoTranslator.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s5t3--entityinfotranslatorapplytoentity-use-igentitydata`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests per success conditions.

---

### Task 7: DDS2ECS-S5T4 � `IgApplication`: register `IgEntityData`
**File:** `Hrot.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s5t4--igapplication-register-igentitydata`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests per success conditions.

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj`
  - `dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj` (for corrective task)

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
- [ ] Corrective-0 fixes applied
- [ ] DDS2ECS-S4T1�S4T3 complete with xUnit tests
- [ ] DDS2ECS-S5T1�S5T4 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-03-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not add DDS DTOs to `SpawnEntityCommand.InitialComponents`.
- Do not register DDS DTOs as ECS components.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (�3.4, �3.5)
