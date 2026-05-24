# BATCH-40 Review

**Verdict: APPROVED**

---

## Build
- Solution: 0 errors, 5 pre-existing CS0618 warnings (IBlueprintTimeController) in
  Hrot.Blueprints.Tests and one in DataBreakpointManagerTests.cs (pre-existing from BATCH-35).
  BATCH-40 introduced zero new warnings.

## Tests
- 45 passed, 0 failed, 0 skipped (40 existing + 5 new)

---

## Test Quality vs DESIGN (P4T1 §8.1, P4T3 §8.3-§8.4)

**Stage_UnmanagedStruct_StoresSizeAndClassification** — Inspects queue via `PendingMutationsQueue.Peek()`.
Verifies `IsManaged==false`, `SizeBytes==Marshal.SizeOf<TestHealth>()`, and `ComponentTypeId` matches
registry. Directly tests DESIGN §8.1 envelope fields.

**Stage_ManagedRef_StoresClassificationOnly** — Uses `EntityLabel` (existing managed class, not redeclared).
Verifies `IsManaged==true`, `SizeBytes==0`. Tests the IsValueType classification branch in StageMutation.

**Drain_UnmanagedPayload_PinnedAndCopiedToECB** — Full end-to-end: creates repo state, triggers pause via
`manager.OnHit`, stages mutation, captures ECB reference, calls `RequestStep()`, calls `ecb.Playback(liveRepo)`.
Asserts `IsPaused==false`, `PendingMutationsCount==0`, and the live component has the staged value (999).
Tests DESIGN §8.3 GCHandle pinning path.

**Drain_ManagedPayload_RoutedViaSetManagedRaw** — Tests the managed path through `SetManagedComponentRaw`.
Asserts `GetManagedComponentRO<EntityLabel>().Name == "staged"` after drain+playback. Tests DESIGN §8.3
managed drain branch.

**Drain_AppliesAtN_Plus_1_BoundaryNotN** — The most critical test. Verifies three checkpoints:
1. During pause: `liveRepo.Current == 0` (preTick value restored by OnHit rewind)
2. After StageMutation, still paused: `liveRepo.Current == 0` (mutation not yet applied)
3. After RequestStep, before ECB Playback: `liveRepo.Current == 50` (postTick restored by SyncFrom)
4. After ecb.Playback: `liveRepo.Current == 777` (N+1 boundary, ECB applied)
This is a textbook verification of DESIGN §8.4 constraint: mutations apply at N+1, not N.

---

## Implementation Quality

- `PendingDebugMutation` struct: all DESIGN §8.1 fields present; immutable (readonly)
- `StageMutation`: null guards, correct classification via `IsValueType`, `Marshal.SizeOf`
- `DrainPendingMutations`: GCHandle.Alloc/Free in try/finally (no GC handle leaks), correct unsafe cast,
  `TryDequeue` pattern leaves queue empty after drain
- `RequestStep` / `RequestContinue`: both call `SyncFrom(_postTickSnapshot)` THEN `DrainPendingMutations`
  THEN time-controller — matches DESIGN §8.4 sequence exactly
- `PendingMutationsQueue` test seam: internal, minimal, clean

---

## Issues None

No correctness issues. The ECB thread-local identity pattern is correctly exploited in tests (same thread =
same ECB instance = capture before step, playback after). The managed component drain relies on the ECB
`SetManagedComponentRaw` applying directly to the live repo's managed table, which works regardless of
SyncFrom's NoSnapshot policy for managed types.
