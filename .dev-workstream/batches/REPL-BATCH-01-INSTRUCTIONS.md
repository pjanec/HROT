# BATCH-01: Phase 0, 1 & 2 - Replication Systems Modernisation & ECS-as-Staging

**Batch Number:** REPL-BATCH-01
**Tasks:** REPL-P0-T1, REPL-P1-T1, REPL-P1-T2, REPL-P1-T3, REPL-P1-T4, REPL-P1-T5, REPL-P1-T6, REPL-P1-T7, REPL-P1-T8, REPL-P2-T1, REPL-P2-T2, REPL-P2-T3, REPL-P2-T4, REPL-P2-T5
**Phase:** Phase 0, 1 & 2
**Estimated Effort:** ~18 hours (2.2 days)
**Priority:** HIGH
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to the Replication Fixes workstream. This batch focuses on fixing the simulation phase bug by modernising the replication systems to implement `IModuleSystem`, removing the `SimWrapper` class, and pivoting to an ECS-as-Staging architecture.

You are expected to finish the batch without stopping and asking if it is ok to do obvious things like running the tests and fixing the root cause until all ok. No laziness allowed. Do it all until all ok and then write the report. No useless asking for permission allowed.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-LEAD-GUIDE.md` - How to work with batches
2. **Architectural Rules:** `FDP/Docs/architectural-rules.md` - Core design rules and guidelines (MANDATORY)
3. **Task Tracker:** `docs/replication-fixes/REPL-TASK-TRACKER.md` - Overall context
4. **Task Details:** `docs/replication-fixes/REPL-TASK-DETAIL.md` - Specific task instructions
5. **Design Document:** `docs/replication-fixes/REPL-DESIGN.md` - Technical specifications

### Source Code Location
- **Primary Work Area:** `FDP/Toolkits/FDP.Toolkit.Replication/`, `Bagira.IG/`, `FDP/ModuleHost/ModuleHost.Network.Cyclone/`
- **Test Project:** `Bagira.Runner.Integration.Tests/` (To be updated in Phase 4)

### Report Submission
**When done, submit your report to:**
`.dev-workstream/reports/REPL-BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev-workstream/questions/REPL-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests (where applicable):**

1. **Task 1:** Implement → Compile → **Verify** ✅
2. **Task 2:** Implement → Compile → **Verify** ✅
3. **Task 3:** Implement → Compile → **Verify** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task compiles successfully
- ✅ Any existing tests are passing

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context

The batch addresses the root cause of the silent simulation phase bug and the zombie entity memory leak. You will replace `ComponentSystem` inheritance with `IModuleSystem` across seven replication systems, remove `SimWrapper<T>`, and rewire Ghost entity creation (ECS-as-Staging).

**Related Tasks Details:**
Refer to `docs/replication-fixes/REPL-TASK-DETAIL.md` for full implementation logic, steps, and target states. This batch delegates the exact specifics to the task definition file to avoid duplication. Reference the design doc directly and follow the explicit steps properly.

---

## 🎯 Batch Objectives
- Verify `EntityLifecycle.Ghost` state presence.
- Modernise seven replication systems to properly integrate with `ModuleHostKernel`.
- Refactor `ReplicationLogicModule` to remove `ISerializationRegistry` and inject `NetworkEntityMap` and `ITkbDatabase`.
- Update translators in IG and Cyclone to utilise to the new ECS-as-Staging pattern.

---

## ✅ Tasks

### Phase 0: Kernel Prerequisite Verification
**Task:** REPL-P0-T1
**File:** `FDP/Kernel/Fdp.Kernel/EntityLifecycleState.cs`
**Reference:** `docs/replication-fixes/REPL-TASK-DETAIL.md#repl-p0-t1-verify-entitylifecycleghost-exists`
**Action:** Simply verify the existence of `Ghost = 4`.

### Phase 1: Modernise Replication Systems
**Tasks:** REPL-P1-T1 to REPL-P1-T8
**Files:** 
- `FDP/Toolkits/FDP.Toolkit.Replication/Systems/*.cs`
- `FDP/Toolkits/FDP.Toolkit.Replication/ReplicationLogicModule.cs`
**Reference:** `docs/replication-fixes/REPL-TASK-DETAIL.md#repl-p1-t1-modernise-disposalmonitoringsystem` (and subsequent tasks T2-T8)
**Action:** Follow the target states provided in the task details. Remove `SimWrapper`. Change all systems to `IModuleSystem` with exact phase bindings. Make sure paths are relative to repo root as specified.

### Phase 2: ECS-as-Staging Architecture
**Tasks:** REPL-P2-T1 to REPL-P2-T5
**Files:**
- `Bagira.IG/Translators/EntityMasterTranslator.cs`
- `Bagira.IG/Translators/*.cs` (GeoSpatial, Info, Damage, etc.)
- `FDP/ModuleHost/ModuleHost.Network.Cyclone/Translators/EntityMasterTranslator.cs`
**Reference:** `docs/replication-fixes/REPL-TASK-DETAIL.md#repl-p2-t1-update-ghostcreationsystem--ecs-as-staging-part-a` (and T2-T5)
**Action:** Follow the task details to switch translators from skipping missing entities to creating Ghosts immediately.

*(Note: Phase 3 wiring and Phase 4 tests will be covered in the next batch to balance the workload).*

---

## ⚠️ Quality Standards

**❗ CODE QUALITY EXPECTATIONS**
- **REQUIRED:** Strictly adhere to the architecture in the design doc. Do not try to bypass the Ghost pipeline.
- **REQUIRED:** Avoid quick and dirty fixes. You must correctly inject dependencies instead of relying on `World` singletons.
- **REQUIRED:** Code must strictly abide by the rules in `FDP/Docs/architectural-rules.md`, specifically the Three Domains Rule, Generational Entity Safety, and Zero-Allocation logic on Hot Paths.

**❗ REPORT QUALITY EXPECTATIONS**
- **REQUIRED:** Document issues encountered and how you resolved them.
- **REQUIRED:** Document design decisions YOU made beyond the spec.
- **REQUIRED:** Share insights on code quality and improvement opportunities.
- **REQUIRED:** Note any edge cases or scenarios discovered during implementation.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks.**

Write your report inside `.dev-workstream/reports/REPL-BATCH-01-REPORT.md`.

**Answer the following questions:**
1. What issues did you encounter during implementation? How did you resolve them?
2. Did you spot any weak points in the existing codebase? What would you improve?
3. What design decisions did you make beyond the instructions? What alternatives did you consider?
4. What edge cases did you discover that weren't mentioned in the spec?
5. Are there any performance concerns or optimization opportunities you noticed in the systems we modernised?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] REPL-P0-T1 verified
- [ ] REPL-P1-T1 to REPL-P1-T8 completed precisely per task details
- [ ] REPL-P2-T1 to REPL-P2-T5 completed precisely per task details
- [ ] All code compiles successfully
- [ ] Report submitted with insights and feedback
