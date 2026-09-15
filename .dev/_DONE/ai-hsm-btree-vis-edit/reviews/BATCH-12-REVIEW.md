# BATCH-12 REVIEW

**Reviewer:** Dev Lead
**Date:** 2026-05-28
**Verdict:** APPROVED with P2 notes

---

## Test Suite Results

| Project | Before | After | Delta |
|---------|--------|-------|-------|
| `Hrot.BTree.Editor.Tests` | 265 | 275 | +10 |
| `Hrot.Hsm.Editor.Tests` | 215 | 228 | +13 |
| `Hrot.Editor.AiShared.Tests` | 372 | 379 | +7 |
| **Total** | **852** | **882** | **+30** |

Build: 0 errors. 9 pre-existing warnings in `Hrot.Blueprints.Tests` (unrelated to this batch).

---

## Task 0: Corrective Tests (BATCH-10 P2)

### ✅ PruneStaleAliasBindings tests

`BTreePruneStaleBindingsTests` and `HsmPruneStaleBindingsTests` — 3 tests each. All three tests
are properly behavioral:
- Verify alias removal by checking `GetAliasesFor` returns exactly the expected count.
- Verify `Changed` fires on prune (and does NOT fire on no-op).
- Verify `GetKnownSubAssetIds` returns exactly the expected GUIDs.

Test construction uses real model objects (`BehaviorTreeAsset`, `HsmAsset`), not stubs.

### ✅ LoadState computation tests

`BTreeAssetLoadStateTests` and `HsmAssetLoadStateTests` — 4 tests each. Tests verify:
- Default state is `BlackboardLoadState.Clean` with null message.
- `SetLoadDiagnostic(Clean, null)` sets state and clears message.
- `SetLoadDiagnostic(StructParseFailed, "msg")` sets state and message correctly.
- `SetLoadDiagnostic(AssemblyFailed, "msg")` sets state and message correctly.

No reflection needed — `SetLoadDiagnostic` was already `public` from BATCH-10.

---

## TASK-BB-1f-01: Cross-region blackboard conflict validator

### ✅ Algorithm

`HsmValidator.CheckBlackboardRegionConflicts` correctly implements the design §9.2 algorithm:
- Builds state-by-StableId map for O(1) lookup.
- For each alias binding, checks `state.Parent.IsParallel` before adding to the region bucket.
- Reports one diagnostic per variable (not per pair) — appropriate noise reduction.
- Single `goto nextVariable` label per outer loop is clean and readable.

The `IBlackboardManagedAsset? blackboard = null` optional parameter is backward-compatible:
all 215 pre-existing HSM tests pass unchanged.

### ✅ Tests (HsmValidatorBlackboardConflictTests.cs)

6 tests covering:
- T1: no blackboard → no conflict diagnostic (correct null guard behavior)
- T2: two aliases in different regions → conflict, verifies `Code`, `Severity`, and `TargetStableIds`
- T3: different variables, one per region → no conflict (per-variable scoping correct)
- T4: two bindings in the same region → no conflict (boundary condition)
- T5: sequential (non-parallel) parent → no conflict (correct `Parent.IsParallel` guard)
- T6: single alias only → no conflict (two-alias minimum enforced)

T4's setup is correct but slightly unusual: uses the same `RequiringElementId` (`child0.StableId`)
with two different `RequiringAssetId`s rather than two distinct child states both at `RegionIndex = 0`.
This is a valid test of the same-region guard, but a test with two distinct states in the same
region would be more representative. This is a P3 observation only — the test is not wrong.

The `StubBlackboardAsset` is declared `file sealed class` inside the test file. Since it's only
used as local variables (not in method return type signatures), the `file` scope is valid here.

### ⚠️ P2: Diagnostic message content not verified

T2 verifies `d.Code`, `d.Severity`, and `d.TargetStableIds`, but does not verify the diagnostic
message text. The message contains the variable name and region indices, which are the key
human-readable content. A test asserting that the message contains the variable name and the
parallel composite name would be valuable for regression protection.

**Recommended addition** (next batch or corrective):
```csharp
Assert.Contains("speed", conflict.Message);          // variable name
Assert.Contains("Parallel", conflict.Message);       // composite name
```

---

## TASK-BB-1f-02: Drop-target validation

### ✅ Architecture: GetParallelRegionMap()

The `GetParallelRegionMap()` default-interface-method design is a good solution to the circular
reference problem. Rather than having the window import `HsmAsset` directly, the region map is
computed by the asset and surfaced via the shared interface. The default returns `null` (no parallel
regions), which is the correct behavior for BTree assets.

`HsmAsset.GetParallelRegionMap()` builds the map by iterating `AllStates` and filtering to
`s.Parent?.IsParallel == true`. This correctly covers direct children of parallel composites.

**Known scope limitation** (P3): For states nested multiple levels inside a parallel region
(e.g., a state inside a sequential composite inside a parallel region), `GetParallelRegionMap()`
would not include them. This is consistent with the `HsmValidator`'s check (which also looks only
at the direct parent). Deep nesting is deferred to a future task.

### ✅ BlackboardAliasDropValidator

Clean pure static function. The short-circuit ordering is correct:
1. `IsCrossRegionWriteAllowed` check first (designer override takes full precedence)
2. Null/empty region map (fast exit for BTrees)
3. New binding's element not in map (sequential states, not parallel children)
4. Existing alias loop with same-asset filter and different-region check

### ✅ Window integration

The drop refusal is correctly placed — `WouldCreateCrossRegionConflict` is evaluated before
`bbAsset.AddAlias`. The `// TODO TASK-BB-1f-02` comment marks the UX gap (no visual feedback).

### ✅ BTreeCrossRegionAllowedTests (3 tests)

Tests verify: set-true allows, set-false-after-true disallows, and `Changed` fires on set.

### ⚠️ P2: SetCrossRegionWriteAllowed always fires Changed

Both `BehaviorTreeAsset` and `HsmAsset` call `MarkDirty()` unconditionally in
`SetCrossRegionWriteAllowed`. Setting `true` when already `true` fires `Changed` unnecessarily.
Low impact (triggers an extra re-render), but inconsistent with the pattern used elsewhere
(e.g., `RenameVariable` guards against no-op renames).

**Recommended fix** (next corrective batch or inline):
```csharp
public void SetCrossRegionWriteAllowed(string variableName, bool allowed)
{
    bool wasAllowed = _crossRegionAllowedVariables.Contains(variableName);
    if (wasAllowed == allowed) return;
    if (allowed) _crossRegionAllowedVariables.Add(variableName);
    else         _crossRegionAllowedVariables.Remove(variableName);
    MarkDirty();
}
```

### ⚠️ P2: Build error in BlackboardAliasDropValidatorTests.cs (self-corrected)

The original `StubDropValidatorAsset` was declared `file sealed class`, which caused `CS9051`
(file-local type used in non-file-local member signatures `EmptyAsset()` / `AllowedAsset()`).
The developer recognized and fixed this by removing the `file` modifier. No tests were affected.

Good self-correction, but this highlights a pattern to watch: `file` scope is valid ONLY when
the type is never referenced outside the current file (e.g., only as local variables inside methods
within the same file — not as method return types, even in a private helper within the same type).

### P3: No UX feedback for refused drop

The window silently refuses cross-region alias drops. The spec noted this as a TODO. The
designer has no way to know why the drag failed. This should be addressed in a UI polish pass
(tooltip, status bar message, or brief red highlight).

---

## TASK-BB-1f-06: [BlackboardReadOnly]/[BlackboardReadWrite] filtering

Not implemented in this batch. The BATCH-12 instructions did not include TASK-BB-1f-06.
The conservative behavior (all aliased states treated as writers) is correct per §9.6.
The `IsCrossRegionWriteAllowed` per-variable override is the designer-facing control for now.

**TASK-BB-1f-06 should be scheduled in a near-future batch** (BATCH-13 or BATCH-14).
It requires: injecting `IActionSchemaExporter` into `HsmValidator`, adding an `IsWriter(fqn)`
helper that checks `entry.Access != BlackboardAccess.ReadOnly`, and calling it inside
`CheckBlackboardRegionConflicts` to skip read-only action bindings.

---

## Issues Summary

| ID | Severity | Description |
|----|----------|-------------|
| P2-1 | P2 | T2 in `HsmValidatorBlackboardConflictTests` does not assert diagnostic message content |
| P2-2 | P2 | `SetCrossRegionWriteAllowed` fires `Changed` unconditionally (no-op fire on same value) |
| P2-3 | P2 | `BlackboardAliasDropValidatorTests` had a build error (`file` type in signature) — self-corrected |
| P3-1 | P3 | T4 in conflict tests uses same-StableId workaround rather than two distinct same-region states |
| P3-2 | P3 | `GetParallelRegionMap` does not cover states nested > 1 level inside a parallel region |
| P3-3 | P3 | Drop refusal is silent — no UX feedback to the designer |

---

## Next Batch Recommendations

**BATCH-13** should cover:
1. **Corrective P2 tasks (from this review):**
   - Add T8-style test to `HsmValidatorBlackboardConflictTests`: assert message contains variable name and composite name.
   - Add no-op guard to `SetCrossRegionWriteAllowed` (both `BehaviorTreeAsset` and `HsmAsset`).
2. **TASK-BB-1f-05** — Suppression metadata persistence (`.SuppressBlackboardConflict` in layout method)
3. **TASK-BB-1f-06** — `[BlackboardReadOnly]/[BlackboardReadWrite]` handling: inject `IActionSchemaExporter` into `HsmValidator`, add `IsWriter(fqn)` helper, filter read-only actions from conflict writer set.

Tasks remaining in Phase 1.5f after BATCH-12:
- [ ] TASK-BB-1f-05 — Suppression metadata persistence
- [ ] TASK-BB-1f-06 — `[BlackboardReadOnly]/[BlackboardReadWrite]` handling

Phase 1.5g (TASK-BB-1g-01 through 1g-06) remains for subsequent batches.
