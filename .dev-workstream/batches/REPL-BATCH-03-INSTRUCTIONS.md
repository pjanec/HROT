# BATCH-03: Phase 5 Translator Unification (Part B) & Phase 4 Integration Tests

**Batch Number:** REPL-BATCH-03
**Tasks:** REPL-P5-T3, REPL-P5-T4, REPL-P5-T5, REPL-P5-T6, REPL-P4-T1, REPL-P4-T2, REPL-P4-T3, REPL-P4-T4
**Phase:** Phase 5 (Part B) & Phase 4
**Estimated Effort:** ~10 hours (1.2 days)
**Priority:** HIGH
**Dependencies:** REPL-BATCH-02

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back to the third and final batch of the Replication Fixes workstream. BATCH-02 relocated the IG Ingress Translators to the shared `Bagira.Map.Common` directory. This batch will migrate the remaining SimHost Egress translators and `DescriptorMapper`, finalizing the unification. Following the merge, you will complete the definitive integration tests validating all of Phase 1-4's ECS-as-Staging properties and anti-zombie measures.

**This batch comes with EXPLICIT autonomy instructions:**
You are to work autonomously until **ALL DONE**. The entire solution MUST compile cleanly. You shall **NOT** ask for obvious things. Do not ask me for permission to start a build or run tests—you are an intelligent developer. Use your intelligence to decide your steps autonomously, execute `dotnet build`, execute `dotnet test`, verify your logic, and *only* submit a report once everything is successful.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-LEAD-GUIDE.md` - How to work with batches
2. **Architectural Rules:** `FDP/Docs/architectural-rules.md` - Core design rules and guidelines (MANDATORY)
3. **Previous Review:** `.dev-workstream/reviews/REPL-BATCH-02-REVIEW.md` - Learn from your feedback
4. **Task Tracker:** `docs/replication-fixes/REPL-TASK-TRACKER.md` - Overall context
5. **Task Details:** `docs/replication-fixes/REPL-TASK-DETAIL.md` - Specific implementation steps

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost/Translators/`, `Bagira.Map.Common/Replication/`, `Bagira.IG/IgApplication.cs`
- **Test Project:** `Bagira.Runner.Integration.Tests/`

### Report Submission
**When done, submit your report to:**
`.dev-workstream/reports/REPL-BATCH-03-REPORT.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests (where applicable):**

1. **Task 1:** Implement → Compile → **Verify/Run Tests** ✅
2. **Task 2:** Implement → Compile → **Verify/Run Tests** ✅
3. **Task 3:** Implement → Compile → **Verify/Run Tests** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task compiles successfully
- ✅ All related tests are passing

---

## 🎯 Batch Objectives
- **Phase 5 Part B:** Move all remaining `Bagira.SimHost` Egress translators into `Bagira.Map.Common.Replication.Egress` library space. Relocate `DescriptorMapper` logic into Utils.
- **Phase 5 Part B Wiring:** Update `SimHostModule.cs` to access `Map.Common`.
- **Phase 4 Integration Tests:** Prove all functionality via autonomous integration tests via the `BagiraRunnerHarness`.

---

## ✅ Tasks

### Phase 5: Migrate SimHost Egress Translators (REPL-P5-T3)
**Files:** `Bagira.SimHost/Translators/*.cs`
**Action:** Relocate 3 mapped Translator files to `Bagira.Map.Common/Replication/Egress/`. Rename files, classes, and namespaces to explicitly contain the `EgressTranslator` postfix according strictly to `REPL-TASK-DETAIL.md#repl-p5-t3-migrate-simhost-egress-translators`. Make sure they shed any SimHost-specific logging bounds.

### Phase 5: Migrate EntityMission Translators (REPL-P5-T4)
**File:** `Bagira.SimHost/Translators/EntityMissionTranslator.cs`
**Action:** Because `EntityMission` feature has both Ingress AND Egress components, separate the Ingress translation and Egress translation into two separate classes directly inside `Bagira.Map.Common/Replication/` within their respective folders. Maintain their ECS mapping properties. Reference `REPL-TASK-DETAIL.md#repl-p5-t4-migrate-entitymission-translators`.

### Phase 5: Migrate DescriptorMapper (REPL-P5-T5)
**File:** `Bagira.SimHost/Util/DescriptorMapper.cs`
**Action:** Move this DDS-to-ECS mapping class into `Bagira.Map.Common/Replication/Utils/DescriptorMapper.cs`. Reorganize as an explicit shared utility for Latency testing, IG spawn prediction, and direct SimHost request parsing.

### Phase 5: Update Composition Roots (REPL-P5-T6)
**Action:** Update `Bagira.SimHost/Modules/SimHostModule.cs` and verify `Bagira.IG/IgApplication.cs` accesses this fully successfully post-migration. Use `dotnet build` iteratively.

### Phase 4: Integration Test Coverage
**Tasks:** REPL-P4-T1, REPL-P4-T2, REPL-P4-T3, REPL-P4-T4
**Files:** `Bagira.Runner.Integration.Tests/*.cs`
**Reference:** `docs/replication-fixes/REPL-TASK-DETAIL.md#repl-p4-t1-replicationphaseexecutiontests--systems-execute-each-frame`
**Action:** Implement all verification integration tests using `BagiraRunnerHarness`. These must autonomously test the Ghost ECS-as-Staging lifecycle and anti-zombie rules. 
**Pro Tip:** FDP queries default to `EntityLifecycle.Alive`. To find entities created by `GhostCreationSystem`, you MUST use `.WithLifecycle(EntityLifecycle.All)` (or `.Ghost`) in your test verification queries. 

---

## ⚠️ Quality Standards

**❗ CODE QUALITY EXPECTATIONS**
- **REQUIRED:** Check `dotnet build` from root.
- **REQUIRED:** Rules in `FDP/Docs/architectural-rules.md` still apply. Be especially cautious defining single-frame transient transient interactions in test mocks (Tier 3 Rule 9).

**❗ TEST QUALITY EXPECTATIONS**
- **REQUIRED:** Tests must actually assert the behaviors (map destruction, correct Ghost lifecycle status, accurate coordinates preserved). Assert values, not just strings.
- **REQUIRED:** You MUST execute `dotnet test` successfully across the newly built `Bagira.Runner.Integration.Tests.csproj` and provide output in the report.

**❗ REPORT QUALITY EXPECTATIONS**
- Document test outcomes carefully. If an edge case arose from integrating Ingress/Egress, specify how you corrected it dynamically.

---

## 📊 Report Requirements

Write your report inside `.dev-workstream/reports/REPL-BATCH-03-REPORT.md`.

**Answer the following questions:**
1. Did the separation of `EntityMission` into distinct Ingress/Egress models uncover any misalignments in the prior `Bagira.SimHost` class structure?
2. Did any of the new Phase 4 tests fail on their first run? If so, why and how was it addressed?
3. What edge cases did you discover during Integration test hooks implementation?
4. How well did the automated tests validate the actual `EntityLifecycle.Ghost` state? Have you captured runtime behaviors correctly?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] REPL-P5-T3 to REPL-P5-T6 implemented and solution passes `dotnet build`.
- [ ] REPL-P4-T1 to REPL-P4-T4 integration tests written.
- [ ] `dotnet test` output explicitly proves passing integrations for all Fix tracking requirements.
- [ ] Report submitted with insights and feedback.
