# BS-1-BATCH-01: Phase 1 Foundations + First CQRS Cut (WeaponFireIntent)

**Batch Number:** BS-1-BATCH-01  
**Tasks:** BS1-T001, BS1-T002, BS1-T003, BS1-T004  
**Phase:** Phase 1 (Contracts) + start of Phase 2 (Weapon Fire CQRS)  
**Estimated Effort:** 10–11 hours  
**Priority:** HIGH  
**Dependencies:** None

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch establishes the **POC data contracts** (ECS event structs + simplified DDS message structs) and fixes the first correctness violation in the split topology (authority guard in `DamageSystem`). It then performs the **first Brain-tier refactor** to emit `WeaponFireIntent` (no translators yet).

### Required Reading (IN ORDER)
1. **Developer Workflow:** `.dev-workstream/guides/DEV-GUIDE.md`
2. **BS-1 Onboarding:** `docs/brain-split/BS-1-ONBOARDING.md`
3. **BS-1 Design:** `docs/brain-split/BS-1-DESIGN.md` (focus: §4.1–§4.3, §5.1)
4. **BS-1 Task Detail:** `docs/brain-split/BS-1-TASK-DETAIL.md` (BS1-T001..BS1-T004)
5. **BS-1 Tracker (context only):** `docs/brain-split/BS-1-TASK-TRACKER.md`

### Source Code Location
- **Primary work areas:**
  - `FDP/Toolkits/FDP.Toolkit.Combat/Events/`
  - `FDP/Toolkits/FDP.Toolkit.Combat/Executors/`
  - `FDP/Toolkits/FDP.Toolkit.Combat/Systems/`
  - `Bagira.DDS.DataModel/`
- **Test project (use this unless you have a strong reason not to):**
  - `FDP/Toolkits/FDP.Toolkit.Combat.Tests/`

### Build & Test Commands (repo root)
- **Build:** `dotnet build IOS-IG-SimHost.sln`
- **Run focused tests:** `dotnet test FDP/Toolkits/FDP.Toolkit.Combat.Tests/FDP.Toolkit.Combat.Tests.csproj`
- **Run everything (before report):** `dotnet test IOS-IG-SimHost.sln`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BS-1-BATCH-01-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BS-1-BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Context

This workstream enforces strict Brain/Muscle separation via a CQRS chain for combat and fixes distributed-topology correctness issues.

**Related task specs (do not duplicate logic here):**
- `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t001--define-weaponfire-pipeline-ecs-event-structs`
- `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t002--define-detonation--damage-pipeline-ecs-event-structs`
- `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t003--add-hasauthority-guard-to-damagesystem`
- `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t004--refactor-aimandfire-executor-to-publish-weaponfireintent`

---

## 🎯 Batch Objectives
- Add the POC contract types required by the later translators and systems (inert additions, compile-safe).
- Enforce **authority-gated health mutation** in `DamageSystem` (single source of truth).
- Start decoupling Brain combat from local physics by making `AimAndFireExecutor` publish `WeaponFireIntent`.

---

## ✅ Tasks

### Task 1: Add WeaponFire contract structs (BS1-T001) (~2–2.5h)

**Task Definition:** `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t001--define-weaponfire-pipeline-ecs-event-structs`  
**Design Reference:** `docs/brain-split/BS-1-DESIGN.md#41-weaponfire-pipeline-contracts`

**Files**
- **NEW:** `FDP/Toolkits/FDP.Toolkit.Combat/Events/WeaponFireEvents.cs`
- **UPDATE:** `Bagira.DDS.DataModel/FireInteractionMessages.cs`

**Requirements**
- Follow the struct field minimums and topic names exactly (POC simplified).
- Keep ECS event structs unmanaged (no managed references, no allocations).

**Tests Required (in `FDP/Toolkits/FDP.Toolkit.Combat.Tests/`)**
- Add tests that match the “Success Conditions” in the task detail (layout + DDS attribute).
- Tests must validate **actual struct size** and **topic attribute value**, not string presence in generated code.

---

### Task 2: Add Detonation/Damage contract structs (BS1-T002) (~2–2.5h)

**Task Definition:** `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t002--define-detonation--damage-pipeline-ecs-event-structs`  
**Design Reference:** `docs/brain-split/BS-1-DESIGN.md#42-detonation--damage-pipeline-contracts`

**Files**
- **NEW:** `FDP/Toolkits/FDP.Toolkit.Combat/Events/DetonationEvents.cs`
- **UPDATE:** `Bagira.DDS.DataModel/FireInteractionMessages.cs`

**Requirements**
- Topic names must match exactly: `MunitionDetonation`, `EntityHitDamage`.
- `DetonationNotification` must carry hit position XYZ as `float`.

**Tests Required (in `FDP/Toolkits/FDP.Toolkit.Combat.Tests/`)**
- Add tests per task detail success conditions (layout + DDS attribute).

---

### Task 3: Add authority guard in DamageSystem (BS1-T003) (~3–3.5h)

**Task Definition:** `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t003--add-hasauthority-guard-to-damagesystem`  
**Design Reference:** `docs/brain-split/BS-1-DESIGN.md#43-damagesystem-authority-guard`

**Files**
- **UPDATE:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageSystem.cs`

**Requirements**
- Use the existing authority API already used in the codebase (per task detail).
- Skip silently when not authoritative (no logs, hot path).

**Tests Required**
- Add/extend tests to cover:
  - non-owner does not decrement health
  - owner decrements health
- Prefer adding tests in `FDP/Toolkits/FDP.Toolkit.Combat.Tests/`. If existing tests for `DamageSystem` live elsewhere, keep them where they are and add new tests alongside them.

---

### Task 4: Refactor AimAndFireExecutor to publish WeaponFireIntent (BS1-T004) (~3–3.5h)

**Task Definition:** `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t004--refactor-aimandfire-executor-to-publish-weaponfireintent`  
**Design Reference:** `docs/brain-split/BS-1-DESIGN.md#51-aimandfire-executor--weaponfireintent`

**Files**
- **UPDATE:** `FDP/Toolkits/FDP.Toolkit.Combat/Executors/AimAndFireExecutor.cs`
- **UPDATE/DELETE if unused after change:** `FDP/Toolkits/FDP.Toolkit.Combat/Events/FireRequestEvent.cs`

**Requirements**
- Ensure entity IDs are published as `long` network IDs (per task detail; use `EntityMap` conversion path referenced there).
- Do not move cooldown/ammo logic out of the executor.

**Tests Required**
- Add/extend tests matching the task detail success conditions:
  - publishes exactly one `WeaponFireIntent` with correct IDs
  - does not publish `FireRequestEvent` (or it no longer exists)
  - ammo/cooldown behavior preserved
  - no-ammo path returns Failure and publishes no intent

---

## 🧪 Testing Requirements
- **Minimum bar:** All tests passing for `FDP.Toolkit.Combat.Tests` after each task, not only at the end.
- **❗ Test quality:** No shallow “object exists” tests; validate meaningful behavior (authority gating, event bus content, struct layout).

---

## 📊 Report Requirements

Write your report in `.dev-workstream/reports/BS-1-BATCH-01-REPORT.md`.

## Developer Insights
- **Q1:** What issues did you encounter during implementation? How did you resolve them?
- **Q2:** Did you spot any weak points in the existing codebase relevant to Brain/Muscle separation? What would you improve?
- **Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?
- **Q4:** What edge cases did you discover that weren't mentioned in the spec?
- **Q5:** Any performance or allocation concerns noticed on hot paths?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] BS1-T001 complete (types + tests)
- [ ] BS1-T002 complete (types + tests)
- [ ] BS1-T003 complete (authority guard + tests)
- [ ] BS1-T004 complete (executor publishes `WeaponFireIntent` + tests)
- [ ] `dotnet test IOS-IG-SimHost.sln` passes
- [ ] Report submitted at `.dev-workstream/reports/BS-1-BATCH-01-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid
- Mixing ECS `Entity` handles into DDS messages (DDS must use `long` IDs).
- Adding logs to authority failure paths (high-frequency).
- Writing tests that only check that code compiles or strings exist; tests must validate behavior and sizes/attributes.

---

## 📚 Reference Materials
- `docs/brain-split/BS-1-ONBOARDING.md`
- `docs/brain-split/BS-1-DESIGN.md`
- `docs/brain-split/BS-1-TASK-DETAIL.md`
- `docs/brain-split/BS-1-TASK-TRACKER.md`

