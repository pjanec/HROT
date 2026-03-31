# DTE-BATCH-02: SimHost DescriptorMapper + EntityMaster Egress Translator

**Batch Number:** DTE-BATCH-02  
**Tasks:** DDS2ECS-S2T1, DDS2ECS-S2T2, DDS2ECS-S2T3, DDS2ECS-S2T4, DDS2ECS-S3T1, DDS2ECS-S3T2, DDS2ECS-S3T3, DDS2ECS-S3T4  
**Phase:** Phase 2 + Phase 3  
**Estimated Effort:** 10�12 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-01 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch fixes SimHost descriptor mapping and replaces the `EntityMaster` auto-translator with a dedicated egress translator. Follow the rules in the design doc and task detail sections precisely.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S2T1 ? DDS2ECS-S3T4)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (�3.2, �3.3)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-01-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/`
- **Test Project:** `Hrot.SimHost.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-02-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement ? Write tests ? **ALL tests pass** ?
2. **Task 2:** Implement ? Write tests ? **ALL tests pass** ?
3. **Task 3:** Implement ? Write tests ? **ALL tests pass** ?
4. **Task 4:** Implement ? Write tests ? **ALL tests pass** ?
5. **Task 5:** Implement ? Write tests ? **ALL tests pass** ?
6. **Task 6:** Implement ? Write tests ? **ALL tests pass** ?
7. **Task 7:** Implement ? Write tests ? **ALL tests pass** ?
8. **Task 8:** Implement ? Write tests ? **ALL tests pass** ?

**DO NOT** move to the next task until:
- ? Current task implementation complete
- ? Current task tests written
- ? **ALL tests passing** (including previous task tests)

---

## Context
Phase 2 removes raw DDS DTOs from `DescriptorMapper.MapToComponents`. Phase 3 removes the `AutoCycloneTranslator<EntityMaster>` anti-pattern and replaces it with an explicit egress translator.

**Related Tasks:**
- Phase 2: [DDS2ECS-S2T1�S2T4](../docs/dds-to-ecs/TASK-DETAIL.md#phase-2-simhost--fix-descriptormapper)
- Phase 3: [DDS2ECS-S3T1�S3T4](../docs/dds-to-ecs/TASK-DETAIL.md#phase-3-simhost--replace-autocyclonetranslatorentitymaster)

---

## ?? Batch Objectives
- Ensure `DescriptorMapper` emits only ECS components (no DDS DTOs).
- Add a dedicated `EntityMasterEgressTranslator` and wire it into SimHost.

---

## ? Tasks

### Task 1: DDS2ECS-S2T1 � `dtEntityMaster` produces nothing
**File:** `Hrot.SimHost/Util/DescriptorMapper.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s2t1--dtentitymaster-case-produces-nothing`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests in `Hrot.SimHost.Tests/` per the success conditions.

---

### Task 2: DDS2ECS-S2T2 � `dtEntityInfo` produces nothing
**File:** `Hrot.SimHost/Util/DescriptorMapper.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s2t2--dtentityinfo-case-produces-nothing`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests in `Hrot.SimHost.Tests/` per the success conditions.

---

### Task 3: DDS2ECS-S2T3 � `dtWorldPos` adds `GeoTransform`, no raw DTO
**File:** `Hrot.SimHost/Util/DescriptorMapper.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s2t3--dtgeospatial-case-remove-raw-dto-add-geotransform`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests in `Hrot.SimHost.Tests/` per the success conditions.

---

### Task 4: DDS2ECS-S2T4 � `dtWorldPos` ? `GeoVelocity`
**File:** `Hrot.SimHost/Util/DescriptorMapper.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s2t4--dtgeospatialdr-case-translate-to-geovelosity`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests in `Hrot.SimHost.Tests/` per the success conditions.

---

### Task 5: DDS2ECS-S3T1 � Create `EntityMasterEgressTranslator`
**File:** `Hrot.SimHost/Translators/EntityMasterEgressTranslator.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s3t1--create-entitymastereresstranslator`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests in `Hrot.SimHost.Tests/` per the success conditions.

---

### Task 6: DDS2ECS-S3T2 � SimHostApp replaces `AutoCycloneTranslator<EntityMaster>`
**File:** `Hrot.SimHost/SimHostApp.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s3t2--simhostapp-replace-autocyclonetranslatorentitymaster`

**Requirements:** Implement exactly as specified in the task detail. Update tests as required by the success conditions.

---

### Task 7: DDS2ECS-S3T3 � Remove `RegisterComponent<EntityMaster>`
**File:** `Hrot.SimHost/SimHostApp.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s3t3--simhostapp-remove-registercomponententitymaster`

**Requirements:** Implement exactly as specified in the task detail. Add xUnit tests in `Hrot.SimHost.Tests/` per the success conditions.

---

### Task 8: DDS2ECS-S3T4 � Fix `onEntitySpawned` callback
**File:** `Hrot.SimHost/SimHostApp.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s3t4--simhostapp-fix-onentityspawned-callback`

**Requirements:** Implement exactly as specified in the task detail. Update tests as required by the success conditions.

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run: `dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`
- All tests must pass before reporting completion.

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
- [ ] DDS2ECS-S2T1�S2T4 complete with xUnit tests
- [ ] DDS2ECS-S3T1�S3T4 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-02-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not add DDS DTOs to `SpawnEntityCommand.InitialComponents`.
- Do not use `AutoCycloneTranslator<EntityMaster>` anywhere.
- Do not register `EntityMaster` as an ECS component.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (�3.2, �3.3)
