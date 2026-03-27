# BS-1-BATCH-02: WeaponFire CQRS Completion (Translators + FireProcessing)

**Batch Number:** BS-1-BATCH-02
**Tasks:** BS1-T005, BS1-T006, BS1-T007
**Phase:** Phase 2 (Weapon Fire CQRS Pipeline)
**Estimated Effort:** 10–12 hours
**Priority:** HIGH
**Dependencies:** BS-1-BATCH-01

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch continues the BS-1 combat CQRS chain by wiring the first cross-node pieces:
publish `WeaponFireRequest` from Brain and consume it on Muscle, then refactor `FireProcessingSystem` to consume `WeaponFireIntent` (the event contract now emitted by the Brain executor in Batch 1).

This batch also starts clearing newly discovered tech debt that blocks meaningful headless/distributed validation.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/guides/DEV-LEAD-GUIDE.md`
2. **BS-1 Onboarding:** `docs/brain-split/BS-1-ONBOARDING.md`
3. **BS-1 Design:** `docs/brain-split/BS-1-DESIGN.md` (focus: Phase 2: §5.1–§5.4)
4. **BS-1 Task Detail:** `docs/brain-split/BS-1-TASK-DETAIL.md` (BS1-T005..BS1-T007)
5. **Context Tracker:** `docs/brain-split/BS-1-TASK-TRACKER.md`
6. **Previous Review:** `.dev-workstream/reviews/BS-1-BATCH-01-REVIEW.md`

### Source Code Location
- `Bagira.SimHost/Network/Egress/` (WeaponFire intent egress translator)
- `Bagira.SimHost/Network/Ingress/` (WeaponFire request ingress translator)
- `FDP/Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs` (consume intent, emit notification)
- `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` (tech debt)
- `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/UrbanAmbushIntegrationTests.cs` (tech debt: restore milestones)

### Report Submission
**When done, submit your report to:**
`.dev-workstream/reports/BS-1-BATCH-02-REPORT.md`

**If you have questions, create:**
`.dev-workstream/questions/BS-1-BATCH-02-QUESTIONS.md`

---

## 🔧 Tech Debt Items (Address Before/While Implementing BS1-T005..T007)

### TD-1: Fix headless `NetworkEntityMap` registration (HeadlessDemoApp)
**File:** `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`
**Problem:** `AimAndFireExecutor` publishes `WeaponFireIntent` with Shooter/Target IDs derived from `NetworkEntityMap`; this headless demo currently does not register entities into `_entityMap`, so headless firing emits `0/0` IDs.
**Success criteria:**
- Headless demo setup registers the relevant entities in `_entityMap` such that published intents contain non-zero shooter/target net IDs during the scenario.
- Add/adjust a lightweight assertion (unit test or an existing scenario log assertion) so the failure mode is caught.

### TD-2: Restore UrbanAmbush milestones once bullet chain is fixed
**File:** `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/UrbanAmbushIntegrationTests.cs`
**Problem:** Batch 1 narrowed bullet-dependent milestones because bullets were deferred until T007. This batch must restore those milestones once `FireProcessingSystem` consumes `WeaponFireIntent`.
**Success criteria:**
- `UrbanAmbush_SimulationRunsToCompletion_WithExpectedMilestones` once again asserts bullet-dependent milestones (HIT, CAPABILITY LOST, HSM TRANSITION, INTERACTION, FLEE), matching the intended pipeline after T007.

### TD-3: Clarify/fix authority guard contract to avoid misleading guards
**File(s):** `FDP/Toolkits/FDP.Toolkit.Replication/Extensions/AuthorityExtensions.cs` (and tests)
**Problem:** The authority helper contract can be misleading for absent/unknown entities; Batch 1 worked around this by using `NetworkAuthority` component checks.
**Success criteria:**
- Implement either: (a) a safe documented helper that “assumes local authority when `NetworkAuthority` is missing”, or (b) adjust the existing helper + add unit tests that pin the behavior.
- The change must be covered by `FDP.Toolkit.Replication.Tests`.

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

After Batch 1, Brain emits `WeaponFireIntent` internally. This batch wires Brain->Muscle transport for fire intents (`WeaponFireRequest`) and refactors `FireProcessingSystem` so the Muscle-side execution consumes `WeaponFireIntent` and emits `WeaponFireNotification` (muzzle-flash intent for IG).

---

## 🎯 Batch Objectives
- Implement Brain egress translator: `WeaponFireIntent` -> DDS `WeaponFireRequest`.
- Implement Muscle ingress translator: DDS `WeaponFireRequest` -> local `WeaponFireIntent`.
- Refactor `FireProcessingSystem` to consume `WeaponFireIntent` and publish `WeaponFireNotification`.
- Restore integration milestone assertions and fix headless demo validation gaps.

---

## ✅ Tasks

### Task 1: WeaponFireIntentEgressTranslator (BS1-T005)

**File:** `Bagira.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs` (NEW)
**Task Definition:** See `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t005--create-weaponfireintentegress-translator`
**Description:** Create translator that reads `WeaponFireIntent` from the local event bus and publishes DDS `WeaponFireRequest`.
**Requirements (must follow task detail):**
- Publish only if local node has authority for the shooter entity (`view.HasAuthority(shooterEntity)` equivalent as described in the task detail).
- Create DDS writer once in constructor; do not allocate per frame.
**Tests Required:**
- Add unit tests as described in the task detail success conditions (mock DDS writer, authority gating, empty bus no-op).

---

### Task 2: WeaponFireRequestIngressTranslator (BS1-T006)

**File:** `Bagira.SimHost/Network/Ingress/WeaponFireRequestIngressTranslator.cs` (NEW)
**Task Definition:** See `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t006--create-weaponfirerequest-ingress-translator`
**Description:** Muscle-side translator that polls DDS `WeaponFireRequest` and republishes local `WeaponFireIntent`.
**Requirements:**
- Use standard ingress pattern (`PollIngress`/`Decode`).
- Convert `long` IDs via `NetworkEntityMap` to local `Entity`; skip silently if entity missing.
**Tests Required:**
- Mock DDS reader decode path and validate publish/no-op cases from task detail.

---

### Task 3: FireProcessingSystem refactor (BS1-T007)

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/FireProcessingSystem.cs` (UPDATE)
**Task Definition:** See `docs/brain-split/BS-1-TASK-DETAIL.md#bs1-t007--refactor-fireprocessingsystem-to-consume-weaponfireintent-and-emit-weaponfirenotification`
**Description:** Consume `WeaponFireIntent` instead of `FireRequestEvent` and publish `WeaponFireNotification` after spawning bullets.

**Requirements:**
- Resolve local ECS entities via `EntityMap` from IDs in `WeaponFireIntent`; skip event if mapping fails.
- Bullet creation logic must stay unchanged.
- Publish `WeaponFireNotification` only after bullet entity exists.

**Tests Required:**
- Cover the “intent spawns bullet + emits notification” success condition and the “unknown entity -> skip gracefully” condition.

---

## 🧪 Testing Requirements
- Required minimum: all existing tests pass plus the new/updated unit tests for BS1-T005..T007.
- Specifically run:
  - `dotnet test FDP/Toolkits/FDP.Toolkit.Combat.Tests/FDP.Toolkit.Combat.Tests.csproj`
  - `dotnet test Bagira.SimHost.Tests/Bagira.SimHost.Tests.csproj` (or the closest relevant host tests that exercise translators)
  - `dotnet test IOS-IG-SimHost.sln` before submission if runtime allows.

---

## 📊 Report Requirements
Focus on Developer Insights, Not Understanding Checks.

In your report, answer:
1. What issues did you encounter during implementation? How did you resolve them?
2. Did you spot weak points in authority/ID mapping or event flow? What would you improve?
3. What design decisions did you make beyond the task specs?
4. What edge cases did you discover?
5. Any performance/allocation concerns noticed on translator hot paths?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] BS1-T005 complete (translator + tests)
- [ ] BS1-T006 complete (translator + tests)
- [ ] BS1-T007 complete (FireProcessingSystem + tests)
- [ ] TD-1 headless demo emits non-zero intent IDs
- [ ] TD-2 UrbanAmbush bullet-dependent milestones restored and pass
- [ ] TD-3 authority helper contract clarified/fixed with tests
- [ ] All relevant tests pass
- [ ] Report submitted to `.dev-workstream/reports/BS-1-BATCH-02-REPORT.md`

---

## ⚠️ Common Pitfalls to Avoid
- Treating DDS messages as ECS `Entity` handles (DDS uses `long` IDs).
- Publishing per-frame allocations in translators (writer creation must be once).
- Mutating `NavState` or other Muscle-owned kinematics from Brain during this batch (out of scope).
- Leaving bullet-dependent integration assertions narrowed after BS1-T007.

---

## 📚 Reference Materials
- `docs/brain-split/BS-1-ONBOARDING.md`
- `docs/brain-split/BS-1-DESIGN.md` (Phase 2: §5)
- `docs/brain-split/BS-1-TASK-DETAIL.md` (BS1-T005..BS1-T007)
- `.dev-workstream/reviews/BS-1-BATCH-01-REVIEW.md`
