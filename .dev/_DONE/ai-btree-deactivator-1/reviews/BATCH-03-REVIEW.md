# BATCH-03 Review

**Reviewed by:** Dev Lead
**Date:** 2025-05-24
**Status:** APPROVED WITH DEV-LEAD CORRECTION APPLIED

---

## Verdict

BATCH-03 is **functionally correct and approved**. All four deactivator methods are
implemented correctly. All 14 tests pass. EQL-006/007/008 are fully wired via the
generator. One runtime-wiring gap in EQL-005 (UrbanCombat, manual registration) was
corrected directly by the dev lead during review (one-line fix).

---

## Scope Check

| Task | Status | Notes |
|------|--------|-------|
| TASK-EQL-005 — `InsurgentNodes.Deactivate_AimAndFire` | DONE | Method correct. Generator does not apply (no `[BTreeAction]` in assembly). Manual registration added by dev lead in review. |
| TASK-EQL-006 — `HillAttackTankNodes.Deactivate_CreepToAndBeyondSlot` | DONE | Correct. Generator emits `RegisterDeactivator` with `@0` key. |
| TASK-EQL-007 — `HillAttackTankNodes.Deactivate_AimAndFireSpecific` | DONE | Correct. Generator emits `RegisterDeactivator` with `@0` key. |
| TASK-EQL-008 — `HillAttackCommanderNodes.Deactivate_RequestAreaQuery` | DONE | Correct. Generator emits `RegisterDeactivator` with `@0` key. |

---

## Implementation Quality

### EQL-005 — InsurgentNodes.Deactivate_AimAndFire

Correct logic:
1. Guard on `HasComponent<WeaponChannel>` — prevents NPE for partially-constructed entities. ✅
2. Reads `GetComponentRW<WeaponChannel>` by ref — correct (avoids copy). ✅
3. Guard on `channel.ActiveAction != CombatConstants.ActionIdAimAndFire` — prevents clearing a channel owned by a different action. ✅
4. Sets `channel.ActiveAction = 0` — correct. ✅
5. `unchecked { channel.ActionInstanceId++; }` — correct, signals re-dispatch on next tick. ✅

**Runtime wiring gap (corrected by dev lead):**
`InsurgentNodes` methods have no `[BTreeAction]` attribute. The generator returned early
before emitting `RegisterDeactivator`. The `ambushReg` in `HeadlessDemoApp.RegisterBehaviors()`
was registering actions manually but not the deactivator. The dev lead added:
```csharp
ambushReg.RegisterDeactivator("Fdp.Examples.UrbanCombat.Brains.InsurgentNodes.Action_AimAndFire", InsurgentNodes.Deactivate_AimAndFire);
```
This is NOT a production AiBehaviorFactory change — it is the demo app's own registry setup.
Instructions gap: BATCH-03 instructions incorrectly said "do not write manual registration
code", which applied to generator-covered assemblies but not to manually-wired examples.
Added as D-09 in DEBT-TRACKER.

### EQL-006 — HillAttackTankNodes.Deactivate_CreepToAndBeyondSlot

Correct logic:
1. Guard on `HasComponent<LocomotionChannel>`. ✅
2. Guard on `loco.ActiveAction != NavigationConstants.ActionIdMoveTo`. ✅
3. Clears `ActiveAction = 0`, increments `ActionInstanceId` (unchecked). ✅
4. The existing in-body `ActiveAction = 0` clear on the Failure path is preserved — belt-and-suspenders as intended. ✅
5. First parameter is `ref BrainBlackboard` — matches the single group in `Hrot.AI.Behaviors`. ✅

### EQL-007 — HillAttackTankNodes.Deactivate_AimAndFireSpecific

Correct logic:
1. Guard on `HasComponent<WeaponChannel>`. ✅
2. Guard on `weapon.ActiveAction != CombatConstants.ActionIdAimAndFire`. ✅
3. Clears `ActiveAction = 0`, increments `ActionInstanceId` (unchecked). ✅
4. The `ClearWeaponActionIfActive` call on the MaxRounds path in `Action_AimAndFireSpecific`
   is preserved — deactivator covers the abort path only. ✅

### EQL-008 — HillAttackCommanderNodes.Deactivate_RequestAreaQuery

Correct logic:
1. Guard on `HasComponent<Blackboard1024>`. ✅
2. `GetComponentRW<Blackboard1024>` by ref — no copy of the 1024-byte struct. ✅
3. `Unsafe.As<Blackboard1024, HillAttackMutableState>` — matches the pattern used throughout the file. ✅
4. Sets only `s.CachedEqsRequestId = -1` — no other fields touched. ✅

---

## Test Quality Assessment

All tests follow the minimal `EntityRepository` setup pattern (no HeadlessDemoApp):
`new EntityRepository()` → `RegisterComponent<T>()` → `CreateEntity()` → `AddComponent()` → invoke deactivator → assert.

### InsurgentNodesDeactivatorTests (4 tests)

- **T1** (channel match → cleared + incremented): `Assert.Equal((ushort)0, ch.ActiveAction)` and `Assert.Equal((uint)1, ch.ActionInstanceId)` — precise. ✅
- **T2** (no channel → no exception): Direct invocation without component; passes if no exception. ✅
- **T3** (ActiveAction == 0 → ActionInstanceId unchanged): Starts at 5, asserts still 5. Correct guard check. ✅
- **T4** (different ActiveAction → channel unchanged): Asserts both `ActiveAction` and `ActionInstanceId` unchanged. Correct. ✅

### HillAttackTankNodesDeactivatorTests (7 tests — EQL-006: 3, EQL-007: 4)

**EQL-006 (CreepDeactivator):**
- T1, T2, T3 — same pattern as InsurgentNodes. Correct. ✅

**EQL-007 (AimAndFireSpecificDeactivator):**
- T1 (cleared + incremented), T2 (no component), T3 (ActiveAction == 0 → no increment), T4 (different action → unchanged). Full coverage of TASK-DETAIL.md T1–T4. ✅

### HillAttackCommanderNodesDeactivatorTests (3 tests — EQL-008)

- **T1** (CachedEqsRequestId = 42 → resets to -1): Uses `Unsafe.As` in test setup to project
  `Blackboard1024` to `HillAttackMutableState` and seeds the field. After deactivation, reads
  via same projection and asserts `== -1L`. Correct and mirrors the production code pattern. ✅
- **T2** (no Blackboard1024 → no exception): Correct. ✅
- **T3** (CachedEqsRequestId already -1 → still -1): Idempotency check. Correct. ✅

---

## Generator Verification (EQL-006/007/008)

Generated `FbtActionRegistrar.g.cs` for `Hrot.AI.Behaviors` after BATCH-03 contains:
```csharp
registry.RegisterDeactivator("Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Action_RequestAreaQuery@0", global::Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Deactivate_RequestAreaQuery);
registry.RegisterDeactivator("Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_CreepToAndBeyondSlot@0", global::Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Deactivate_CreepToAndBeyondSlot);
registry.RegisterDeactivator("Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_AimAndFireSpecific@0", global::Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Deactivate_AimAndFireSpecific);
```
All three use `@0` compound keys — correct.

---

## Test Baseline

### Fdp.Examples.UrbanCombat.Tests

**Before BATCH-03:** 25 passing (baseline before deactivator tests)
**After BATCH-03:** 29 passing (4 new InsurgentNodesDeactivatorTests)
No failures. ✅

### Hrot.IG.Tests

**After BATCH-03:** 330 passing, 69 pre-existing failures (IgApplication bootstrap,
GizmoRegistrar infrastructure — unrelated to BATCH-03).
10 new Deactivator tests all pass. ✅

---

## Issues Found

### D-09 (P3) — Generator gap: manually-wired action registries are not covered

`InsurgentNodes` in `Fdp.Examples.UrbanCombat` does not use `[BTreeAction]`/`[BTreeCondition]`
attributes — actions are manually wired via `ActionRegistry.Register(...)` calls. The generator
returns early and emits nothing. Any deactivator added to this assembly requires a corresponding
manual `RegisterDeactivator(...)` call in the same place where the action is registered. Dev
lead added the missing call in `HeadlessDemoApp.RegisterBehaviors()` during review.

Future: Document this constraint in `ONBOARDING.md`. The generator only covers annotated
assemblies; manually-wired assemblies remain manually-wired for deactivators too.

---

## Commit Message

```
feat(engine): Phase 3 deactivators — WeaponChannel, LocomotionChannel, EqsRequestId (BATCH-03)

EQL-005: InsurgentNodes.Deactivate_AimAndFire — clears WeaponChannel on abort.
         Manual registration added in HeadlessDemoApp.RegisterBehaviors().
EQL-006: HillAttackTankNodes.Deactivate_CreepToAndBeyondSlot — clears LocomotionChannel.
EQL-007: HillAttackTankNodes.Deactivate_AimAndFireSpecific — clears WeaponChannel.
EQL-008: HillAttackCommanderNodes.Deactivate_RequestAreaQuery — resets CachedEqsRequestId.

EQL-006/007/008: generator emits RegisterDeactivator with @0 compound keys.
Tests: 14 new unit tests (4+3+4+3), all pass.
Baselines: UrbanCombat.Tests 29/0, Hrot.IG.Tests 330/69 (pre-existing).
```
