# BATCH-12 REPORT

**Batch:** BATCH-12
**Developer:** AI Developer (Claude Sonnet 4.6)
**Status:** COMPLETE
**Date:** 2026-05-28

---

## Summary

Completed all four tasks in BATCH-12: corrective tests from BATCH-10 (Task 0), cross-region
blackboard conflict validator (TASK-BB-1f-01), and drop-target validation (TASK-BB-1f-02).
TASK-BB-1f-06 (`[BlackboardReadOnly]/[BlackboardReadWrite]` read-only filtering) is addressed in
the design via `IsCrossRegionWriteAllowed`, but the schema-exporter integration is deferred as
noted below.

---

## Task 0: Corrective tests from BATCH-10 (P2)

### Files created

- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Blackboard/BTreePruneStaleBindingsTests.cs` (3 tests)
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Blackboard/BTreeAssetLoadStateTests.cs` (4 tests)
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Blackboard/HsmPruneStaleBindingsTests.cs` (3 tests)
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Blackboard/HsmAssetLoadStateTests.cs` (4 tests)

### Notes

`SetLoadDiagnostic` is already accessible without reflection — it was left `public` on both
`BehaviorTreeAsset` and `HsmAsset` during the BATCH-10 implementation. No `[InternalsVisibleTo]`
changes were needed.

---

## TASK-BB-1f-01: Cross-region blackboard conflict validator

### Files modified

- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmDiagnosticCode.cs`
  - Added `CrossRegionBlackboardConflict` after `OutputLaneConflict`.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmValidator.cs`
  - Added `IBlackboardManagedAsset? blackboard = null` optional parameter to `Validate`.
  - Added `CheckBlackboardRegionConflicts` (Rule 8): scans `blackboard.GetAliasesFor` for each
    variable, maps `RequiringElementId` to states via `AllStates` dictionary, checks for
    distinct-region pairs under a `Parent.IsParallel == true` composite.
  - One diagnostic per variable (first conflicting pair only).
  - Approach B sync-out deferred with a TODO comment.
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Validation/HsmAssetValidator.cs`
  - Passes `hsmAsset as IBlackboardManagedAsset` to `Validate`.
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDiagnosticCode.cs`
  - Added `CrossRegionConflict` for shared panel use.

### Files created

- `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Validation/HsmValidatorBlackboardConflictTests.cs`
  (6 tests)

### Design decisions

1. **Algorithm scope:** Only Approach A alias bindings (via `GetAliasesFor`) are checked. The
   `RequiringElementId` in those bindings corresponds to `StateNode.StableId`, established by
   the `HsmBlackboardAggregatorStrategy` which uses `state.StableId` as the element ID.
2. **Single diagnostic per variable:** Emits one `CrossRegionBlackboardConflict` per variable
   rather than one per conflicting pair. This avoids diagnostic noise when a variable is written
   by many parallel regions simultaneously.
3. **No `[BlackboardReadOnly]` filtering in this task:** TASK-BB-1f-06's schema-exporter
   integration is tracked separately. Without it, any aliased state is treated as a writer
   (conservative, per §9.6). The per-variable `IsCrossRegionWriteAllowed` flag provides a
   designer-controlled override.

---

## TASK-BB-1f-02: Drop-target validation + override flag

### Files modified

- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs`
  - Added default interface methods:
    - `bool IsCrossRegionWriteAllowed(string variableName) => false`
    - `void SetCrossRegionWriteAllowed(string variableName, bool allowed) {}`
    - `IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => null`
  - The `GetParallelRegionMap` helper avoids a circular project reference from
    `Hrot.Editor.AiShared` to `Hrot.Hsm.Editor`: the HSM asset builds its own region map
    and exposes it via the shared interface.
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs`
  - Added `_crossRegionAllowedVariables: HashSet<string>`.
  - Concrete `IsCrossRegionWriteAllowed` / `SetCrossRegionWriteAllowed`. Fires `Changed`.
  - `GetParallelRegionMap()` not overridden (returns `null` via default — BTrees have no parallel regions).
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs`
  - Same `_crossRegionAllowedVariables` pattern.
  - Concrete `GetParallelRegionMap()` builds `StateId -> RegionIndex` for direct children of
    parallel composites.
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs`
  - In the `BB_UNBOUND_DRAG` accept block: calls
    `BlackboardAliasDropValidator.WouldCreateCrossRegionConflict(bbAsset, row.Name, newBinding, regionMap)`.
  - Drop is refused silently when conflict detected and override not set.
  - TODO comment at refusal site for future user-visible rejection notice (red flash / tooltip).

### Files created

- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardAliasDropValidator.cs`
  - Pure static function, no ImGui dependency.
  - Short-circuits on null/empty region map, element not in map, no existing aliases.
  - Returns `false` when `IsCrossRegionWriteAllowed` is set (override takes precedence).
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardAliasDropValidatorTests.cs`
  (7 tests)
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Blackboard/BTreeCrossRegionAllowedTests.cs`
  (3 tests)

### Known issue fixed during implementation

`BlackboardAliasDropValidatorTests.cs` originally declared the stub class as `file sealed class`,
which caused `CS9051` because the type appeared in non-file-local method signatures (`EmptyAsset()`
and `AllowedAsset()` return types). Fixed by removing the `file` modifier (making it `internal`).

---

## Questions for Dev Lead

**Q1: Should `SetCrossRegionWriteAllowed` call `Changed` when the value doesn't change?**

Current implementation calls `MarkDirty()` unconditionally. Setting `true` when already `true`
fires `Changed` unnecessarily. This is low-impact (it just triggers an extra re-render) but could
be guarded cheaply:
```csharp
if (allowed == _crossRegionAllowedVariables.Contains(variableName)) return;
```
I left it as-is to match the pattern used in `PruneStaleAliasBindings`. Flagging for awareness.

**Q2: Rejection UX — currently silent**

The drop is silently refused when a cross-region conflict is detected and the override is not
set. The instructions noted this with a TODO. A visible rejection notice (tooltip or brief flash)
would improve designer discoverability. Deferring to the UI polish pass or a future batch if the
team wants it sooner.

---

## Build & Test Results

```
dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4
  0 Errors
  9 Warnings  (pre-existing in Hrot.Blueprints.Tests — IBlueprintTimeController CS0618)
  Build succeeded.
```

| Project | Baseline | Final | Delta |
|---------|----------|-------|-------|
| `Hrot.BTree.Editor.Tests` | 265 | 275 | +10 |
| `Hrot.Hsm.Editor.Tests` | 215 | 228 | +13 |
| `Hrot.Editor.AiShared.Tests` | 372 | 379 | +7 |
| **Total** | **852** | **882** | **+30** |

All 882 tests pass. 0 failures.
