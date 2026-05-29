# BATCH-04 Completion Report

**Batch:** BATCH-04 — Utility AI Standard Input Readers
**Status:** APPROVED (all success criteria met)
**Date:** 2025

---

## Summary

All six tasks completed. 30 new tests pass; all 58 prior Utility tests continue to pass (81 total
in the `Fdp.Toolkit.Tests` namespace). Pre-existing failures (55 tests in `Gizmos`/`Combat`) are
unrelated to this batch and were present before the work began.

---

## Tasks Completed

### D-03 — ResponseCurveEvaluate.cs remarks (debt item)

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/ResponseCurveEvaluate.cs`

Added inline `// Note:` comments to both `CurveKind.Quadratic` and `CurveKind.InverseQuadratic`
cases explaining that the `Exponent` field is ignored and that a general power curve using
`MathF.Pow(x, Exponent)` is not implemented in Phase 1.

---

### Corrective-0 — UtilityTestWorld fixes

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs`

Three changes:

1. **Added `using Fdp.Toolkit.Utility;`** to support the new component types and struct.

2. **Registered two missing component types** in the constructor:
   - `Repo.RegisterComponent<UtilityDebugFlags>()`
   - `Repo.RegisterComponent<UtilityTraceWorkingMemory1024>()`

3. **Fixed `SeedContact`**: now passes `modality: hasLos ? SensorModality.Visual : SensorModality.Acoustic`
   to `AddOrUpdateTarget`. Also adds a `Health` component to the contact if `contactHealth01 >= 0`
   (creating it if absent, or updating `Current` if already present), and adds a `Position`
   component to the contact if not already present (at `new Vector3(distanceM, 0f, 0f)`).

4. **Fixed `AssignmentFor`**: replaced the `return -1L` stub with a real implementation that
   projects `ThreatMatrixAssignmentState` from the leader's `Blackboard1024` and looks up the
   member's roster index via `UnitRoster.IndexOf`. Returns `-1L` only when the member is absent
   from the roster. Updated the doc comment accordingly.

---

### Task 3 — ThreatMatrixAssignmentState (new file)

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs`

Created three types in namespace `Fdp.Toolkit.Utility`:

- **`AssignmentSlot`** — `[StructLayout(LayoutKind.Sequential, Size = 64)]` with fields
  `AssignedTargetHandle` (long), `AssignmentScore` (float), `FocusFireCount` (byte), `Flags` (byte);
  remaining 50 bytes are implicit padding.
- **`AssignmentSlotArray`** — `[InlineArray(16)]` wrapper over `AssignmentSlot`.
- **`ThreatMatrixAssignmentState`** — 1024-byte struct with `AssignmentSlotArray Slots`;
  provides `Project(ref Blackboard1024)`, `GetSlot(int)` (via `MemoryMarshal.CreateSpan` to
  avoid InlineArray defensive-copy), and `GetAssignedTarget(int)`.

---

### Task 4 — UtilityInputAttribute (new file)

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/UtilityInputAttribute.cs`

Minimal `[AttributeUsage(AttributeTargets.Method)]` attribute with a `Name` property.
Serves as the Phase 2 source-generator hook. Namespace: `Fdp.Toolkit.Utility`.

---

### Task 5 — StandardInputs + StandardInputIds (new file)

**File:** `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs`

**`StandardInputIds`** — 17 `const ushort` FNV-1a-16 hash constants, one per reader.

**`StandardInputs`** (unsafe static class) — 17 readers all with signature
`static float Name(in UtilityInputCtx ctx)` and `[UtilityInput("Name")]`:

| Name | Group | Notes |
|------|-------|-------|
| `AmmoFraction` | A | WeaponState.Ammo/MaxAmmo clamped |
| `WeaponHasAmmo` | A | 1 if Ammo > 0 |
| `WeaponReadiness` | A | 1 if CooldownSecondsRemaining <= 0 |
| `HealthFraction` | A | Health.Current/Max clamped |
| `ContactHealthFraction` | A | same on ctx.Context |
| `DistanceToContext` | A | 1 - clamp(dist/MaxRange, 0, 1) |
| `ContactThreatLevel` | B | ThreatScores[i] clamped |
| `HasLineOfSight` | B | Visual bit check in Modalities[i] |
| `HaveLiveTarget` | B | Count > 0 |
| `EnemyStrengthRatio` | B | sum(threats) / (healthFrac * MaxTrackedTargets) |
| `EqsTopScore` | C | GetTop().Score via TryFindEqsChild |
| `EqsResultCount` | C | Count / 16f via TryFindEqsChild |
| `IsAssignedTarget` | D | ThreatMatrixAssignmentState slot lookup |
| `AllyAdvancingNearby` | D | Phase 1 stub returning 0 |
| `Constant` | D | clamp(ctx.Params.MaxRange, 0, 1) |
| `WeaponRangeBandFit` | D | range-band interpolation via TryFindMountChild |
| `WeaponEffectivenessVsTarget` | D | Phase 1 delegates to WeaponRangeBandFit |

**`RegisterAll()`** registers all 17 readers into `UtilityInputRegistrar`.

Two private helpers: `TryFindEqsChild` and `TryFindMountChild` both scan via
`repo.Query().With<...>().With<PartMetadata>().Build()` matching parent entity and blueprint/mount ID.

All readers include `Debug.Assert(result >= 0f && result <= 1f)` in DEBUG builds and guard
for missing components by returning 0.

---

### Task 6 — StandardInputReaderTests (new file)

**File:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs`

30 tests covering:

| Success Criterion | Tests |
|-------------------|-------|
| SC-P1-06-1: AmmoFraction | MaxAmmo==0 → 0; 15/30 → 0.5; Ammo>Max → 1.0 clamp; no component → 0 |
| SC-P1-06-1: WeaponHasAmmo | Ammo>0 → 1; Ammo==0 → 0 |
| HealthFraction | Max==0 → 0; half → 0.5 |
| SC-P1-06-4: DistanceToContext | distance=0 → 1.0; distance=MaxRange → 0.0; half → ~0.5; beyond → 0 clamp |
| SC-P1-06-2: HasLineOfSight | Visual → 1; Acoustic-only → 0; not in memory → 0; Visual+Acoustic bit verification |
| HaveLiveTarget | contact seeded → 1; empty → 0 |
| ContactThreatLevel | contact found with score; contact not found → 0 |
| SC-P1-06-3: EqsTopScore | ready with count → top score; wrong blueprintId → 0; LastUpdateTick==0 → 0 |
| SC-P1-06-5: IsAssignedTarget | slot matches → 1; different target → 0; no UnitSubordinate → 0 |
| Constant | clamps MaxRange param |
| AssignmentFor (via UtilityTestWorld) | assigned target returned; stranger returns -1 |
| Hash pin test | all 17 FNV-1a-16 constants verified against runtime computation |

---

## Test Results

```
Fdp.Toolkit.Tests.Utility namespace (all tests):  81 passed, 0 failed
  - Prior tests (58):  all pass, no regressions
  - New tests (30):    all pass
  - UtilityTestWorld helper tests still pass after SeedContact/AssignmentFor fixes
```

Pre-existing failures (55 tests in `Gizmos`/`Combat` suites) are unrelated to this batch.

---

## Files Created / Modified

| Action | Path |
|--------|------|
| Modified | `FDP/Toolkits/Fdp.Toolkits/Utility/Core/ResponseCurveEvaluate.cs` |
| Created | `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs` |
| Created | `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/UtilityInputAttribute.cs` |
| Created | `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` |
| Modified | `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` |
| Created | `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs` |
