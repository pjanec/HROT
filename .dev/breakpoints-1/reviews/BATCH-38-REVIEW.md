# BATCH-38 Review

**Batch:** BATCH-38
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

Both tasks implemented correctly. The `IActiveViewProvider` intermediate interface is an
elegant solution to the layering constraint (Fdp.Toolkits cannot reference
Hrot.Diagnostics.Breakpoints). All 34 breakpoints tests pass; full solution builds clean.
The P3T1 test verifies all three state transitions (before pause / during pause / after resume)
with reference-equality assertions on the view. The NameSubstring lifecycle test exercises
`ReadEntityName`, `ReadStringField`, and the `NameSubstring` code path with a proper decoy entity.

---

## Positive Observations

**`IActiveViewProvider` design (correct):**
Putting the thin `IActiveViewProvider` interface in `Fdp.Toolkit.Diagnostics.Gizmos` and having
`DataBreakpointManager` implement both `IDataBreakpointManager` and `IActiveViewProvider` avoids
a circular project reference. The gizmo systems need only `IActiveViewProvider`; the full
manager contract stays in `Hrot.Diagnostics.Breakpoints`. Clean separation.

**Corrective Task 0 (correct):**
`LifecyclePredicate_FiresOnBirth_AndOnDeath_ByNameSubstring` uses a proper managed `EntityLabel`
class component, registers it via `RegisterManagedComponent`, and includes a decoy entity
("AllyTank") that must NOT trigger the breakpoint. Birth and death paths both verified.

**`preTick.SyncFrom(liveRepo)` insight (correct):**
The test correctly simulates the pre-tick snapshot being taken before the tick by syncing `preTick`
from `liveRepo` after entity creation. Without this, `OnHit`'s rewind would wipe the entity from
`liveRepo` and the gizmo would not receive the `UpdateAndDraw` call. The fix is minimal and correct.

**UpdateAndDraw signature sweep (complete):**
Solution builds with 0 warnings, 0 errors. All `IEntityStatefulGizmo` implementations were found
and updated (compiler-enforced guarantee).

**`ActiveView` property (correct):**
`ActiveView => _isPaused ? (ISimulationView)_preTickSnapshot : _liveRepo` returns the correct
view for rendering. The explicit cast is correct (`EntityRepository` implements `ISimulationView`).

**`PausedTick` (correct):**
Set in `OnHit` to `_preTickSnapshot.GlobalVersion`; cleared in `RequestStep`/`RequestContinue`.
Correctly returns 0 when not paused.

---

## Minor Observations (non-blocking)

**TargetValue in NameSubstring test:**
The test uses `TargetValue = "Enemy"` (matches "EnemyTank" via `Contains`).
TASK-DETAIL says `"EnemyTank"`. Both are correct because the logic uses `Contains`
(case-insensitive substring), so `"Enemy"` is a valid substring of `"EnemyTank"`.
This is acceptable for testing purposes. The decoy `"AllyTank"` correctly does not
contain `"Enemy"`. No action required.

---

## Verdict: APPROVED

All P3T1 and Corrective Task 0 requirements are met. Commit this batch.

**After commit, continue with BATCH-39 (UBP-P3T2 + UBP-P3T3): inspector view repointing and temporal status banner.**
