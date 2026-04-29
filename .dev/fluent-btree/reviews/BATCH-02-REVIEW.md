# BATCH-02 Review

**Status: APPROVED**

**Date:** 2026-04-29

---

## Summary

BATCH-02 is approved. 108/108 tests pass, build is clean, FBT-003 and FBT-005 are fully implemented.

---

## Code Review

### FBT-003 — Expression-Based Blackboard Parameter Binding
✅ `ReusableDelegates.cs` — `ReusableConditionDelegate<TValue, TContext>` and `ReusableActionDelegate<TValue, TContext>` — correctly parallels `NodeLogicDelegate<TBlackboard, TContext>`.
✅ `ExtractFieldInfo<TValue>` helper centralizes lambda-walk + offset computation — clean design.
✅ `Marshal.OffsetOf` called once at build time, stored as `nint`.
✅ Curried closure uses `Unsafe.As + Unsafe.AddByteOffset` — no `unsafe` blocks.
✅ Registry key includes byte offset (`@{offset}`) for stable deduplication across different fields.
✅ `UnaryExpression` unwrapping handled — expression body may be a boxing Convert.

### FBT-005 — Graph Data Structures
✅ `Fbt.Compiler.Graph` namespace with 5 classes matching the spec.
✅ `ToGraph()` correctly round-trips `VisualId` from `BuilderEntry.Meta`.
✅ `Parent` references set correctly.
✅ Expression-bound leaves populate `TargetFieldName` and `TargetDtoType` on `LogicNode`.
✅ `BuilderEntry` cleanly extended with nullable `TargetFieldName`/`TargetDtoType`.

---

## Test Quality Review

✅ FBT-003 tests verify **actual byte offset correctness** by setting FieldA to -999 and FieldB to 5.0f — if the closure reads from the wrong offset, the test fails.
✅ Mutation test verifies actual field value after ticking (AmmoCount 5→4).
✅ Two-tick decrement test (AmmoCount 5→3) verifies state persistence.
✅ `InvalidExpression_ThrowsArgumentException` — tries `bb => 42f` (constant), verifies exception.
✅ FBT-005 tests check node subclass types, child counts, parent references, VisualId uniqueness/non-empty.

---

## Design Decisions Accepted

1. **`TargetDtoType = typeof(TBlackboard).FullName`** — accepted. Full blackboard type identifies which DTO the delegate belongs to; `TargetFieldName` identifies the projected field.

2. **`[StructLayout(LayoutKind.Sequential)]` on test blackboards** — accepted. Production requirement documented in developer insights; future Fbt.SourceGen could enforce this via diagnostic.

---

## Technical Debt Recorded

| Priority | Description |
|----------|-------------|
| P3 | No compile-time enforcement that `TBlackboard` has `[StructLayout(LayoutKind.Sequential)]` for `Marshal.OffsetOf` reliability. Future `Fbt.SourceGen` / `Fbt.Attributes` could add a Roslyn analyzer diagnostic. |

---

## Decision: APPROVED — Proceed to BATCH-03
