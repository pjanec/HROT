# DTE-BATCH-04: IG Damage + Map Symbol Translators (with DTE-BATCH-03 fixes)

**Batch Number:** DTE-BATCH-04  
**Tasks:** Corrective-0 (DTE-BATCH-03 fixes), DDS2ECS-S6T1, DDS2ECS-S6T2, DDS2ECS-S6T3, DDS2ECS-S6T4, DDS2ECS-S7T1, DDS2ECS-S7T2  
**Phase:** Phase 6 + Phase 7  
**Estimated Effort:** 8–10 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-03 fixes applied

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch finishes the IG damage pipeline and map symbol overrides, and closes the test gaps from DTE-BATCH-03.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md` (DDS2ECS-S6T1 ? DDS2ECS-S7T2)
3. **Design Document:** `docs/dds-to-ecs/DESIGN.md` (§3.6, §3.7, §3.8)
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-03-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Bagira.IG/`
- **Test Project:** `Bagira.IG.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-04-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-04-QUESTIONS.md`

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

**DO NOT** move to the next task until:
- ? Current task implementation complete
- ? Current task tests written
- ? **ALL tests passing** (including previous task tests)

---

## Context
Phase 6 introduces `IgHealthState` and `EntityDamageTranslator`. Phase 7 adds `MapEntitySymbolTranslator`. Corrective Task 0 closes the missing tests from DTE-BATCH-03 and locks in Phase 8 behavior with tests.

**Related Tasks:**
- Phase 6: [DDS2ECS-S6T1–S6T4](../docs/dds-to-ecs/TASK-DETAIL.md#phase-6-ig--create-ighealthstate-and-entitydamagetranslator)
- Phase 7: [DDS2ECS-S7T1–S7T2](../docs/dds-to-ecs/TASK-DETAIL.md#phase-7-ig--create-mapentitysymboltranslator)
- Phase 8 tests: [DDS2ECS-S8T1–S8T3](../docs/dds-to-ecs/TASK-DETAIL.md#phase-8-ig--fix-igapplication-registrations-and-queries)

---

## ?? Batch Objectives
- Close DTE-BATCH-03 test gaps (S5T4 + Phase 8 tests).
- Implement IG damage translation via `IgHealthState`.
- Implement map symbol override translation via `MapEntitySymbolTranslator`.

---

## ? Tasks

### Corrective Task 0: DTE-BATCH-03 test gaps

**Files:**  
- `Bagira.IG.Tests/` (UPDATE)

**Requirements:**
- Add the S5T4 test verifying `IgApplication.InitializeEmbedded(headless: true)` registers `IgEntityData`.
- Add Phase 8 tests per task detail for:
  - no `EntityMaster` registration,
  - render query using `NetworkIdentity`,
  - `DisTypeExtractor` uses `NetworkSpawnRequest`.

---

### Task 1: DDS2ECS-S6T1 — Create `IgHealthState`
**File:** `Bagira.IG/Components/IgHealthState.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s6t1--create-ighealthstate-component`

---

### Task 2: DDS2ECS-S6T2 — Create `EntityDamageTranslator`
**File:** `Bagira.IG/Translators/EntityDamageTranslator.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s6t2--create-entitydamagetranslator`

---

### Task 3: DDS2ECS-S6T3 — `IgApplication` registers `EntityDamageTranslator`
**File:** `Bagira.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s6t3--igapplication-register-entitydamagetranslator`

---

### Task 4: DDS2ECS-S6T4 — `IgApplication` registers `IgHealthState`
**File:** `Bagira.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s6t4--igapplication-register-ighealthstate`

---

### Task 5: DDS2ECS-S7T1 — Create `MapEntitySymbolTranslator`
**File:** `Bagira.IG/Translators/MapEntitySymbolTranslator.cs` (NEW)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s7t1--create-mapentitysymboltranslator`

---

### Task 6: DDS2ECS-S7T2 — `IgApplication` registers `MapEntitySymbolTranslator`
**File:** `Bagira.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** `docs/dds-to-ecs/TASK-DETAIL.md#dds2ecs-s7t2--igapplication-register-mapentitysymboltranslator`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Bagira.IG.Tests/Bagira.IG.Tests.csproj`

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
- [ ] Corrective-0 tests added
- [ ] DDS2ECS-S6T1–S6T4 complete with xUnit tests
- [ ] DDS2ECS-S7T1–S7T2 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-04-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not add DDS DTOs to `SpawnEntityCommand.InitialComponents`.
- Do not register DDS DTOs as ECS components.

---

## ?? Reference Materials
- **Task Details:** `docs/dds-to-ecs/TASK-DETAIL.md`
- **Design:** `docs/dds-to-ecs/DESIGN.md` (§3.6, §3.7, §3.8)
