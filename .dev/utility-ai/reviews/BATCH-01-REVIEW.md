# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-05-29  
**Status:** APPROVED with corrective task (P1 → Corrective Task 0 in BATCH-02)

---

## Summary

All 7 Phase-0 tasks implemented correctly. 45 P0 tests pass (0 failures). Implementation code is solid. One test coverage gap found for SC-P0-01-2 (translator path for MaxAmmo) — becomes Corrective Task 0 in BATCH-02.

---

## Issues Found

### Issue 1: SC-P0-01-2 not covered via translator path

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Combat/WeaponStateTests.cs`  
**Problem:** `WeaponState_MaxAmmo_EqualsInitialAmmunition` creates `WeaponState` directly:
```csharp
var state = new WeaponState { Ammo = initialAmmunition, MaxAmmo = initialAmmunition, ... };
Assert.Equal(initialAmmunition, state.MaxAmmo);
```
SC-P0-01-2 says "A TKB spawn with `InitialAmmunition = 30` produces `WeaponState { Ammo = 30, MaxAmmo = 30, … }`." No test goes through `CombatTkbTranslator` and asserts `ws.MaxAmmo == initialAmmunition`. If someone removed `MaxAmmo = primary.InitialAmmunition` from the translator, all current tests would still pass.

**Fix:** In `WeaponMountTests.cs`, add assertion `Assert.Equal(30, _repo.GetComponentRO<WeaponState>(owner).MaxAmmo)` after translator injection in an existing test (e.g., `ThreeMountDefinition_SpawnsThreeWeaponStateComponents`), or add a dedicated test in `WeaponStateTests.cs` that uses `CombatTkbTranslator` and checks `ws.MaxAmmo`.

---

## Test Quality Assessment

Tests are generally good quality — verify actual values, not just string presence. Struct size test, eviction tests (fills full 16 slots), aliasing test (mutual struct projection), and PartMetadata back-link tests all validate actual behavior.

One P2 note: `Fnv1a32_CoverQuery_ProducesStableNonZeroValue` has the pinned value (0x9317A97B) in a comment but the `Assert.Equal` is commented out. Activating that assertion would catch algorithm regressions. Added to debt tracker.

---

## Verdict

**Status: APPROVED**

Corrective task carried into BATCH-02 as Task 0 (mandatory, before Phase-1 work):
- Add translator-path test for `WeaponState.MaxAmmo` (SC-P0-01-2)

---

## Commit Message

```
feat(utility-ai): Phase 0 prerequisites (BATCH-01)

Completes TASK-UAI-P0-01 through TASK-UAI-P0-07.

- WeaponState.MaxAmmo field + spawn initialisation (P0.01)
- WeaponMountInfo component, WeaponMountQuery.EnumerateMounts, multi-mount translator (P0.02)
- PerceptionConstants.MaxTrackedTargets raised to 16 (P0.03)
- UnitRoster.Add / IndexOf static helpers (P0.04)
- Blackboard1024.Project<T> write-through projection (P0.05)
- UtilityTestWorld helper + 7 spawn/seed helpers (P0.06)
- Phase0_Bundle_Integration gate test (P0.07)
- Fix AimAndFireExecutor cooldown drain (missing dt decrement)

Tests: 45 P0 tests pass; 62 pre-existing suite failures unchanged.
```

---

**Next Batch:** BATCH-02 (Corrective Task 0 + TASK-UAI-P1-01 through P1-03)
