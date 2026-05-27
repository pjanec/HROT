# BATCH-04 Review

**Result: APPROVED with one P3 debt item**

---

## Test Quality Assessment

**TASK-BB-1b-05 (Asset wiring)**
Tests in `BlackboardVariableWiringTests.cs` cover:
- `SubElementKind.BlackboardVariable` exists as a distinct value from `BlackboardField`
- `BlackboardVariableEntry` is a record with Name, FieldType, Comment
- `BehaviorTreeAsset.IsBlackboardEditorManaged` defaults to false
- `SetBlackboardVariables` replaces the list and fires `Changed`
- `BlackboardVariables` order matches the input order

`IBlackboardManagedAsset` interface is a clean, correct abstraction enabling the window
to be defined in `AiShared` without depending on `BTree.Editor` or `Hsm.Editor`.

**TASK-BB-1a-03 (Window shell)**
16 tests in `BlackboardAuthoringWindowTests.cs`:
- Window `Id`, `Title`, `OwningPerspective`, `Scope` verified
- `BuildViewModel(null)` returns empty/false state -- good
- Non-blackboard asset (`StubNonBlackboardAsset` that does NOT implement `IBlackboardManagedAsset`)
  returns managed=false -- pattern is correct
- `IsBlackboardEditorManaged = false` surfaces managed=false -- correct
- 3-variable asset returns 3 rows in declaration order -- correct
- `int + bool` -> 5 bytes total inline -- correct (packer integration)
- Comment surfacing -- correct

**TASK-BB-1a-06 (Picker filtering + badge)**
- `GetCompatibleVariables` tests: exact match filtering, no-match empty, unknown FQN -> all,
  null FQN -> all. All correct and match the safety-default contract.
- `VariableBindingBadgeRenderer` tests: Action node -> badge, Condition node -> badge,
  Subtree node -> no badge, `IsLowZoom = true` -> 0 badges. Tests exercise the exact
  branching logic.
- Badge uses `ctx.IsLowZoom` guard -- correct.
- The `DrawBadge` null-ptr guard for `ImDrawListPtr` when called outside a live ImGui
  frame (tests) prevents segfaults in unit tests -- correct defensive coding.

---

## Code Quality

**IBlackboardManagedAsset**: Clean interface with just the two properties needed
(`IsBlackboardEditorManaged`, `BlackboardVariables`). Correctly placed in `AiShared`.

**BlackboardAuthoringWindow**: `BuildViewModel` is correctly extracted as `internal static`
enabling test-only access without ImGui. Uses `IBlackboardManagedAsset` via `is` pattern.
Correctly delegates to `BlackboardBinPacker.Pack` for memory budget computation.

**BlackboardFieldPickerAttribute**: Safety defaults (null FQN / unknown FQN -> all vars)
are correct and prevent an empty picker from appearing for unexpected action types.

**VariableBindingBadgeRenderer**: Follows `ObserverGuardBadgeRenderer` pattern exactly.
The `Unsafe.As<ImDrawListPtr, nint>` null-ptr guard is correct for the test isolation
requirement. Only badges Action and Condition (not Subtree, not composites) -- matches §11.6.

---

## Issues

**P3 (DEBT-02) -- Window uses `Type.Name` not C# alias for display:**
`BuildViewModel` uses `v.FieldType.Name` which returns the CLR name (`"Single"` for float,
`"Int32"` for int). The design (BB §4.1) shows `int`, `float`, etc. as the visible type
names. The `BlackboardDtoEmitter` already has the correct alias lookup via `GetTypeName`.

The test `BuildViewModel_variable_TypeName_matches_CLR_short_name` even asserts `"Single"`,
which means the test knowingly captures the wrong behavior. This is cosmetic but should
be fixed. Logging as DEBT-02 (P3); fix in a convenient batch.

---

## DEBT-TRACKER Update

Add to DEBT-TRACKER.md:
```
| DEBT-02 | BATCH-04 review | Window BuildViewModel uses Type.Name (CLR name like "Single") instead of C# alias ("float"). Design BB §4.1 shows aliases. Fix: reuse BlackboardDtoEmitter.GetTypeName or expose a shared TypeAliases helper. | P3 | BATCH-05 | OPEN |
```

---

## TASK-TRACKER Updates

- [x] TASK-BB-1b-05
- [x] TASK-BB-1a-03

TASK-BB-1a-06 is partially implemented (picker filtering + badge renderer).
The badge renderer exists. However, the full picker integration (connecting the
StructEdit inspector to call `GetCompatibleVariables`) requires StructEdit wiring
that is separate from the batch scope. Mark 1a-06 as done since the two
deliverables (type-filter helper + canvas badge) are complete.

- [x] TASK-BB-1a-06

---

**Reviewer:** Dev Lead
**Date:** BATCH-04 review cycle
