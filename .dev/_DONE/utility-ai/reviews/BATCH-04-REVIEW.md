# BATCH-04 Review

**Batch:** BATCH-04
**Reviewer:** Development Lead
**Date:** 2026-05-29
**Status:** APPROVED

---

## Summary

All deliverables complete: D-03 doc fix applied, `SeedContact` / `AssignmentFor` fixed, `ThreatMatrixAssignmentState` struct defined, `[UtilityInput]` attribute stub created, all 17 standard input readers implemented in `StandardInputs.cs`. 30 new tests pass; total utility tests: 100. Pre-existing 83 failures in unrelated test classes are unchanged.

---

## Issues Found

No P1 or P2 issues.

### P3 — `EnemyStrengthRatio` denominator heuristic

`EnemyStrengthRatio` normalizes threat sum against `selfHealthFraction * MaxTrackedTargets`. This means a healthy agent sees lower ratios than a wounded one facing the same threat — arguably correct (wounded agents feel more threatened) but the design doc says "sum of `TargetMemory.ThreatScores` vs. own strength." If "own strength" is interpreted as a fixed pool capacity rather than health-adjusted, the formula would differ. Given the design doc is ambiguous at Phase 1 and this is a derived reader with clear documentation in the code, this is P3 only. Target: BATCH-05 (double-check at starter-pack integration test time).

### P3 — `TryFindEqsChild` / `TryFindMountChild` call `Build()` per read

Both helpers call `repo.Query()...Build()` on every invocation (hot-path). This is allocation-free (struct enumerator) and correct for Phase 1. The design doc §6.6 notes "cache the resolved child handle in the per-entity `UtilityResultBuffer.SensorChildCache`" for Phase 2. Track as debt.

---

## Test Quality Assessment

- **SC-P1-06-1** (AmmoFraction + WeaponHasAmmo): 6 tests covering MaxAmmo=0, 15/30, overload clamp, no component, zero ammo, positive ammo. All specific numeric assertions. ✅
- **SC-P1-06-2** (HasLineOfSight): 4 tests covering Visual set, Acoustic-only, not in memory, and a raw-modality assertion confirming `SensorModality.Acoustic` is set and `Visual` is NOT set for hasLos=false. All 4 combinations spec'd by SC-P1-06-2. ✅
- **SC-P1-06-3** (EqsTopScore): 3 tests covering ready+matching, wrong BlueprintId, and `LastUpdateTick == 0` (not ready). ✅
- **SC-P1-06-4** (DistanceToContext): 4 tests pinning the interpolation at 0m → 1.0, MaxRange → 0.0, MaxRange/2 → 0.5, beyond MaxRange → 0.0. ✅
- **SC-P1-06-5** (IsAssignedTarget): 3 tests covering match, different target, and entity with no `UnitSubordinate`. ✅
- **Hash pin test**: all 17 `StandardInputIds` constants verified inline against runtime `Fnv1a32` computation. A hash algorithm regression or const typo would break this. ✅
- **ThreatMatrixAssignmentState layout**: `sizeof(ThreatMatrixAssignmentState) == 1024` is implicitly guaranteed by `AssignmentSlot` being `Size=64` and `[InlineArray(16)]` = 1024. No explicit layout test was written; acceptable given the `[StructLayout(Sequential)]` contract is deterministic. A size assertion can be added if needed (P3 candidate).

---

## Verdict

**Status: APPROVED**

All Phase 1 standard input readers implemented and tested. Ready for BATCH-05 (ThreatMatrixAssignmentSystem + starter-pack decisions).

---

## Commit Message

```
feat(utility-ai): Standard input readers catalog (BATCH-04)

Resolves D-03, Corrective-0 (SeedContact fix), TASK-UAI-P1-06.

- D-03: doc remarks on Quadratic/InverseQuadratic Exponent field
- Corrective-0: SeedContact now passes modality (Visual/Acoustic based on
  hasLos), adds Health to contact entity, adds Position to contact entity;
  AssignmentFor uses real ThreatMatrixAssignmentState
- ThreatMatrixAssignmentState: 1024B struct (16x64B slots), GetSlot/GetAssignedTarget
- [UtilityInput] attribute stub (Phase 2 hook)
- StandardInputs: 17 readers (AmmoFraction, WeaponHasAmmo, WeaponReadiness,
  HealthFraction, ContactHealthFraction, DistanceToContext, ContactThreatLevel,
  HasLineOfSight, HaveLiveTarget, EnemyStrengthRatio, EqsTopScore, EqsResultCount,
  IsAssignedTarget, AllyAdvancingNearby, Constant, WeaponRangeBandFit,
  WeaponEffectivenessVsTarget) + StandardInputIds hash constants + RegisterAll()

Tests: 30 new tests; 100 utility tests pass total.
```

---

**Next Batch:** BATCH-05 (TASK-UAI-P1-07 ThreatMatrixAssignmentSystem + TASK-UAI-P1-08 starter-pack decisions + integration tests)
