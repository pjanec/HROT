# BS-1-BATCH-03: WeaponFire to IG + DetonationNotifications

**Batch Number:** BS-1-BATCH-03  
**Tasks:** TD-4, TD-5, BS1-T008, BS1-T009, BS1-T010  
**Phase:** Phase 2 continuation (Weapon Fire egress/IG) + Phase 3 start (detonation)  
**Estimated Effort:** 10–12 hours  
**Priority:** HIGH  
**Dependencies:** BS-1-BATCH-02  

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch completes the **WeaponFire → IG muzzle-flash** slice and starts the **bullet impact → detonation notification** slice.

It also addresses two tech-debt items discovered during Batch 02:
1) making “skip silently” paths truly silent, and  
2) strengthening test coverage around T007’s event ordering constraint.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-LEAD-GUIDE.md`
2. **BS-1 Onboarding:** `docs/brain-split/BS-1-ONBOARDING.md`
3. **BS-1 Design:** `docs/brain-split/BS-1-DESIGN.md` (focus: Phase 2–3)
4. **BS-1 Task Detail:** `docs/brain-split/BS-1-TASK-DETAIL.md` (BS1-T008..BS1-T010)
5. **Context Tracker:** `docs/brain-split/BS-1-TASK-TRACKER.md`
6. **Previous Review:** `.dev-workstream/reviews/BS-1-BATCH-02-REVIEW.md`

### Source Code Location
- Translators (SimHost egress/ingress + IG):
  - `Bagira.SimHost/Network/Egress/`
  - `Bagira.SimHost/Network/Ingress/`
  - `Bagira.IG/Translators/`
- Fire / hit resolution systems:
  - `FDP/Toolkits/FDP.Toolkit.Combat/Systems/`
- IG event definitions:
  - `Bagira.IG/IgEvents.cs`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BS-1-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BS-1-BATCH-03-QUESTIONS.md`

---

## 🔧 Tech Debt Items (Address Before Implementing BS1-T008..BS1-T010)

---

### TD-5: Strengthen T007 test coverage around notification-after-bullet constraint
**File:** `FDP/Toolkits/FDP.Toolkit.Combat.Tests/FireProcessingSystemTests.cs`  
**Problem:** Current tests validate notification payload and bullet existence after `FireProcessingSystem` runs, but do not explicitly fail if notification emission were moved before bullet creation within the same frame.
**Success criteria (choose the best feasible approach):**
- Add a stronger assertion that would detect incorrect ordering, or
- If true ordering can’t be directly observed with the current bus model, document the limitation and implement the closest behavioral proxy that still detects regressions.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **TD-4 / TD-5:** Implement → tests → **ALL tests pass** ✅
2. **Task 1 (BS1-T008):** Implement → tests → **ALL tests pass** ✅  
3. **Task 2 (BS1-T009):** Implement → tests → **ALL tests pass** ✅  
4. **Task 3 (BS1-T010):** Implement → tests → **ALL tests pass** ✅  

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

After Batch 02:
- Brain emits `WeaponFireIntent` on the local bus.
- SimHost can translate it to DDS `WeaponFireRequest` and consume it back into `WeaponFireIntent` on the Muscle side.
- `FireProcessingSystem` consumes `WeaponFireIntent` and publishes `WeaponFireNotification` after spawning bullets.

This batch:
- adds DDS egress for `WeaponFireNotification` (to drive IG muzzle flashes),
- adds IG ingress to convert DDS `WeaponFire` into local `IgWeaponFireEvent`,
- and starts detonation notification emission from `HitResolutionSystem`.

---

## 🎯 Batch Objectives
- Implement `WeaponFireNotificationEgressTranslator` (SimHost → DDS `WeaponFire`).
- Implement `WeaponFireIngressTranslator` for IG (DDS `WeaponFire` → `IgWeaponFireEvent`).
- Implement detonation notification emission (`HitEvent` → `DetonationNotification`).

---

## ✅ Tasks

### Task 1: WeaponFireNotificationEgressTranslator (BS1-T008)
**File:** `Bagira.SimHost/Network/Egress/WeaponFireNotificationEgressTranslator.cs` (NEW)  
**Task Definition:** `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t008--create-weaponfirenotificationegress-translator`

**Description:** Consume `WeaponFireNotification` ECS events and publish DDS `WeaponFire` messages for IG.

**Requirements (from task detail):**
- Do not require an authority check (notifications are only emitted by authoritative Muscle execution path).
- Publish one DDS message per notification event.

**Tests Required:**
- ✅ DDS `Write` called with matching `WeaponFire` payload.
- ✅ Multiple notifications result in multiple DDS writes.

---

### Task 2: WeaponFireIngressTranslator for IG (BS1-T009)
**Files:**
- `Bagira.IG/Translators/WeaponFireIngressTranslator.cs` (NEW)
- `Bagira.IG/IgEvents.cs` (UPDATE / add `IgWeaponFireEvent`)
**Task Definition:** `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t009--create-weaponfire-ingress-translator-for-ig`

**Description:** Poll DDS `WeaponFire` on the IG and publish `IgWeaponFireEvent` to IG local bus.

**Requirements:**
- Unknown entity IDs must not break translation; still publish the IG event.

**Tests Required:**
- ✅ DDS message → one IG event with correct payload.
- ✅ Unknown IG entity → event still published (no exception).

---

### Task 3: Refactor HitResolutionSystem to emit DetonationNotification (BS1-T010)
**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HitResolutionSystem.cs` (UPDATE)  
**Task Definition:** `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t010--refactor-hitresolutionsystem-to-emit-detonationnotification`

**Description:** In addition to publishing `HitEvent`, publish `DetonationNotification` for bullet impacts (and not for LOS-check rays).

**Requirements:**
- `HitEvent` must remain published.
- `DetonationNotification` uses world-space hit XYZ coordinates.
- Identify bullet impacts vs LOS-check rays correctly.

**Tests Required:**
- ✅ Impact produces both `HitEvent` and `DetonationNotification`.
- ✅ LOS-check rays produce no `DetonationNotification`.
- ✅ Existing HitResolution tests pass unchanged.

---

## 🧪 Testing Requirements
- Minimum bar: all tests pass after each task.
- Recommended run:
  - `dotnet test FDP/Toolkits/FDP.Toolkit.Combat.Tests/FDP.Toolkit.Combat.Tests.csproj`
  - `dotnet test Bagira.SimHost.Tests/Bagira.SimHost.Tests.csproj`
  - `dotnet test IOS-IG-SimHost.sln`

---

## 📊 Report Requirements
Focus on Developer Insights, Not Understanding Checks.

Answer:
1. Issues encountered + how you resolved them
2. Weak points you noticed in authority/ID mapping or event flow
3. Design decisions beyond the specs
4. Edge cases discovered
5. Performance/allocation concerns on hot paths

---

## 🎯 Success Criteria
This batch is DONE when:
- [ ] TD-5 complete (T007 test coverage strengthened with rationale)
- [ ] BS1-T008 complete (translator + tests)
- [ ] BS1-T009 complete (translator + IG event + tests)
- [ ] BS1-T010 complete (system + tests)
- [ ] `dotnet test IOS-IG-SimHost.sln` passes
- [ ] Report submitted to `.dev-workstream/reports/BS-1-BATCH-03-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid
- Logging in “skip silently” paths (high-frequency event noise).
- Mixing ECS `Entity` handles with DDS `long` IDs.
- Publishing IG events without tolerating unknown entities.
- Breaking existing HitResolution test behavior.

---

## 📚 Reference Materials
- `docs/brain-split/BS-1-ONBOARDING.md`
- `docs/brain-split/BS-1-DESIGN.md`
- `docs/brain-split/BS-1-TASK-DETAIL.md`
- `.dev-workstream/reviews/BS-1-BATCH-02-REVIEW.md`

