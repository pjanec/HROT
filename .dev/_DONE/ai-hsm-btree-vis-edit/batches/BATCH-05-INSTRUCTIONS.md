# BATCH-05: Add/Remove Variables, Rename, DEBT-02 Fix

**Batch Number:** BATCH-05
**Tasks:** DEBT-02 fix (P3), TASK-BB-1b-02, TASK-BB-1b-03
**Phase:** Phase 1.5b — edit operations in the Variables panel
**Estimated Effort:** 12-16 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 through BATCH-04 all committed

---

## Onboarding & Workflow

### Developer Instructions

This batch delivers:
1. **DEBT-02 fix** — expose C# type aliases from `BlackboardDtoEmitter` so the window shows `float` instead of `Single`
2. **TASK-BB-1b-02** — Add Variable popup + Remove Variable with dangling-reference report; drag-reorder canonical order; promote-from-picker affordance
3. **TASK-BB-1b-03** — Variable rename via `IRefactorService`

Do tasks in the order listed.

### Required Reading (IN ORDER)

1. **Workflow:** `.dev/.guides/DEV-GUIDE.md`
2. **Onboarding:** `.dev/_DONE/ai-hsm-btree-vis-edit/ONBOARDING.md`
3. **Task Details:** `.dev/_DONE/ai-hsm-btree-vis-edit/TASK-DETAIL.md` — TASK-BB-1b-02, TASK-BB-1b-03
4. **Design:**
   - §4.3 (comments as first-class feature)
   - §4.4 (Add Variable workflow)
   - §4.5 (variable row interactions: single-click, drag, menu)
   - §11.3 (promote-from-picker)
   - §11.4 (rename)
5. **Existing code to read before coding:**
   - `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — existing window shell
   - `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` — type alias dict (for DEBT-02)
   - `Hrot/Editor/Hrot.Editor.AiShared/Refactor/IRefactorService.cs` — refactor service interface
   - `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` — rename modal pattern
   - `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` — asset model
   - `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` — from Batch 04
   - `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardVariableEntry.cs` — from Batch 04
   - `.dev/_DONE/ai-hsm-btree-vis-edit/reviews/BATCH-04-REVIEW.md` — DEBT-02 description

### Report Submission

**When done, submit your report to:**
`.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-05-REPORT.md`

---

## Tasks

### DEBT-02 Fix — C# type aliases in variable display

**Problem:** `BlackboardAuthoringWindow.BuildViewModel` uses `v.FieldType.Name` which returns
CLR names like `"Single"` for `float`, `"Int32"` for `int`. The design (BB §4.1) shows C#
aliases. The test `BuildViewModel_variable_TypeName_matches_CLR_short_name` currently asserts
`"Single"` — this must be updated to assert `"float"` after the fix.

**Fix:**

1. In `BlackboardDtoEmitter.cs`, make the `TypeAliases` dictionary `internal static` (it is
   currently `private static`). No other changes to `BlackboardDtoEmitter`.

2. Add a helper in a new file `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardTypeHelper.cs`:
```csharp
namespace Hrot.Editor.AiShared.Blackboard;

// Type name helpers shared between the emitter (for emit) and the window (for display).
internal static class BlackboardTypeHelper
{
    // Returns the C# alias name for known primitives; otherwise Type.Name.
    // Examples: typeof(float) -> "float", typeof(int) -> "int", typeof(Vector3) -> "Vector3"
    internal static string GetDisplayName(Type t)
        => BlackboardDtoEmitter.TypeAliases.TryGetValue(t, out string? alias)
            ? alias
            : t.Name;
}
```

3. Update `BlackboardAuthoringWindow.BuildViewModel` to use:
```csharp
rows.Add(new VariableViewModel(v.Name, BlackboardTypeHelper.GetDisplayName(v.FieldType), byteSize, v.Comment));
```

4. Update the test `BuildViewModel_variable_TypeName_matches_CLR_short_name` to assert
   `"float"` (not `"Single"`).

---

### TASK-BB-1b-02 — Add Variable + Remove Variable workflows

**Spec:** TASK-DETAIL.md §TASK-BB-1b-02; design BB §4.3, §4.4, §4.5, §11.3.

This task adds edit operations to the existing `BlackboardAuthoringWindow`.

#### a) Extend `IBlackboardManagedAsset`

In `IBlackboardManagedAsset`, add two mutation methods:
```csharp
// Appends a new variable at the end of the canonical order. Fires Changed.
void AddVariable(BlackboardVariableEntry entry);

// Removes the variable by name. No-op if not found. Fires Changed.
void RemoveVariable(string name);

// Replaces the comment on an existing variable. No-op if not found. Fires Changed.
void UpdateVariableComment(string name, string? comment);

// Moves a variable from sourceIndex to destIndex in canonical order. Fires Changed.
void MoveVariable(int sourceIndex, int destIndex);
```

Implement these on `BehaviorTreeAsset` and the HSM asset (find it with file_search).

#### b) Name validator

Add `BlackboardNameValidator.cs` in `Hrot.Editor.AiShared/Blackboard/`:
```csharp
public static class BlackboardNameValidator
{
    // Returns null if valid; an error message string if invalid.
    public static string? Validate(string name, IReadOnlyList<BlackboardVariableEntry> existingVars);
}
```

Rules:
- Not null or empty
- First character: letter or underscore
- All characters: letter, digit, or underscore
- Not a duplicate of an existing variable's name (case-sensitive)
- Not a C# keyword

For the C# keyword check, a minimal set suffices (the user is unlikely to type keywords):
`bool`, `byte`, `char`, `decimal`, `double`, `float`, `int`, `long`, `object`, `sbyte`, `short`, `string`, `uint`, `ulong`, `ushort`, `void`, `class`, `struct`, `enum`, `interface`, `delegate`, `event`, `base`, `this`, `new`, `return`, `if`, `else`, `while`, `for`, `foreach`, `switch`, `case`, `break`, `continue`, `true`, `false`, `null`, `namespace`, `using`, `static`, `public`, `private`, `protected`, `internal`, `sealed`, `abstract`, `readonly`, `const`, `var`, `ref`, `out`, `in`.

#### c) Extend `BlackboardWindowViewModel` and `BuildViewModel`

The view-model must carry the list of known type names for the Add Variable dropdown:
```csharp
IReadOnlyList<string> KnownTypeNames   // sorted list of type display names to show in dropdown
```

For the initial slice, `KnownTypeNames` is hardcoded to the primitive C# type aliases + vector types:
```
bool, byte, sbyte, short, ushort, int, uint, long, ulong, float, double, Vector2, Vector3, Vector4, Quaternion
```

`BuildViewModel` now takes an optional `IReadOnlyList<string>? knownTypeNames` parameter (default null = use the hardcoded list).

#### d) `[+] Add variable...` popup in `DrawClientArea`

When the user clicks `[+] Add variable...`:
- Open an ImGui popup with:
  - Name text input (512-byte buffer, validation on each character)
  - Type dropdown (from `vm.KnownTypeNames`)
  - Comment text input (512-byte buffer, optional)
  - `[Add]` button (disabled while name is invalid; tooltip shows the error)
  - `[Cancel]` button
- On confirm: build a `BlackboardVariableEntry` with the appropriate `FieldType` (resolve the type name to a `Type` via a lookup), call `bbAsset.AddVariable(entry)`

For type resolution (name -> Type), add `BlackboardTypeHelper.GetPrimitiveType(string name) -> Type?`.

#### e) Remove via `⋮` menu

In each variable row, add a small button `[x]` or `[...]` (you can use ImGui's `##ctx_{name}` popup) with a "Remove" menu item.

Before removing, check if any `ExpressionTargetField` in the active asset's nodes references this variable name. Show a dangling-reference count in the confirmation: "Remove 'speed'? Referenced by 2 nodes. Remove anyway?". If the user confirms, call `bbAsset.RemoveVariable(name)`.

The dangling-reference check for the remove confirmation: iterate `BehaviorTreeAsset.Nodes` (or the HSM equivalent) and count nodes whose `ExpressionTargetField == name`. Display the count in the confirmation modal.

#### f) Drag-reorder

Use ImGui's `BeginDragDropSource` / `BeginDragDropTarget` on each variable row. On a successful drop, call `bbAsset.MoveVariable(sourceIndex, destIndex)`. This is silent for editor-managed fields (no warning modal in this slice — the warning for read-only fields is deferred to when read-only fields are added in later phases).

**Tests** (`BlackboardAddRemoveTests.cs` in `Hrot.Editor.AiShared.Tests`):
- `AddVariable` appends to the list and fires `Changed`
- `AddVariable` with duplicate name still works at the model level (validation is the window's responsibility)
- `RemoveVariable` removes the correct entry and fires `Changed`
- `RemoveVariable` with unknown name is a no-op (no exception)
- `MoveVariable(0, 2)` correctly reorders 3-item list
- `BlackboardNameValidator.Validate(null)` returns non-null error
- `BlackboardNameValidator.Validate("")` returns non-null error
- `BlackboardNameValidator.Validate("123bad")` returns non-null error (starts with digit)
- `BlackboardNameValidator.Validate("my_var", [])` returns null (valid)
- `BlackboardNameValidator.Validate("speed", [new("speed", ...)])` returns non-null (duplicate)
- `BlackboardNameValidator.Validate("float")` returns non-null (C# keyword)
- `BlackboardNameValidator.Validate("_private")` returns null (underscore prefix valid)
- `BuildViewModel` with `KnownTypeNames` provided populates `vm.KnownTypeNames`
- `BlackboardTypeHelper.GetPrimitiveType("int") == typeof(int)`
- `BlackboardTypeHelper.GetPrimitiveType("Vector3") == typeof(System.Numerics.Vector3)`
- `BlackboardTypeHelper.GetPrimitiveType("unknown") == null`

---

### TASK-BB-1b-03 — Variable rename via the refactor service

**Spec:** TASK-DETAIL.md §TASK-BB-1b-03; design BB §11.4.

Variable rename routes through `IRefactorService` using the `BlackboardVariable` sub-element kind established in TASK-BB-1b-05.

#### a) Rename key convention

When renaming variable `"speed"` in asset with ID `{assetId}`, the keys are:
- `fromKey = "{assetId:D}::speed"`
- `toKey = "{assetId:D}::newSpeed"`

The refactor service finds all `ExpressionTargetField` references registered under the `fromKey`, rewrites them to the new name, and re-emits affected assets.

#### b) Rename entry point in `BlackboardAuthoringWindow`

Add an inline rename affordance to each variable row:
- Double-click the variable name → enter inline rename mode (show a text input with the current name pre-filled)
- On Enter / Tab: validate the new name with `BlackboardNameValidator.Validate`; if valid, invoke rename
- On Escape: cancel

The rename invocation:
```csharp
var fromKey = $"{bbAsset.AssetId:D}::{oldName}";
var toKey   = $"{bbAsset.AssetId:D}::{newName}";
var preview = _refactorService.PreviewRename(fromKey, toKey, new RefactorOptions());
if (!preview.Issues.Any(i => i.Severity == RefactorIssueSeverity.Error))
    _refactorService.ApplyRename(preview);
```

Per design BB §11.4 (open-question #4 resolution): silent apply, no preview pane. Errors surface as ImGui TextColored toast (3-second duration).

Also update `bbAsset.RenameVariable(oldName, newName)` (add this method to `IBlackboardManagedAsset`):
- Replaces the `Name` in the `BlackboardVariableEntry` record at the matching index
- Fires `Changed`
- This updates the editor model; the refactor service separately updates all `ExpressionTargetField` references in nodes

#### c) Register `BlackboardVariable` references in the catalog

The rename only works if references are registered. When an action/condition node's `ExpressionTargetField` is non-null, register it with the reference catalog using `SubElementKind.BlackboardVariable`. Find where `BlackboardField` references are registered and add a parallel `BlackboardVariable` registration there.

If `ExpressionTargetField` references are not yet wired into the catalog, add the wiring. The key is `"{assetId}::{fieldName}"`.

**Tests** (`BlackboardRenameTests.cs` in the relevant test project):
- `IBlackboardManagedAsset.RenameVariable("old", "new")` updates the name in the list and fires `Changed`
- `RenameVariable` on unknown name is a no-op (no exception)
- Key formatting: `"{guid}::name"` (double colon delimiter, no spaces)
- Preview rename for a variable key produces edits targeting `ExpressionTargetField` lines (integration test if feasible; otherwise a unit test that verifies the key format)

---

## Mandatory Workflow

For each task in order:
1. Read the spec and design sections
2. Read all referenced existing code
3. Write tests first (failing), then implement
4. Run `dotnet build IOS-IG-SimHost.sln -v minimal` after each task
5. Run `dotnet test` on affected test projects
6. Move to next task only when tests pass

---

## Testing Requirements

- `BlackboardAddRemoveTests.cs` — TASK-BB-1b-02
- `BlackboardRenameTests.cs` — TASK-BB-1b-03
- Update `BlackboardAuthoringWindowTests.cs` for DEBT-02 fix
- Minimum 25 new tests total
- Tests for `BlackboardNameValidator` must cover each individual failure rule separately

---

## Notes

- `IBlackboardManagedAsset` must be extended; both `BehaviorTreeAsset` and `HsmAsset` need the new methods
- The `TypeAliases` dictionary in `BlackboardDtoEmitter` should be made `internal static` (not `public static` — it's an implementation detail)
- For the "remove dangling reference" check, look at how the `InspectorWindow` creates dangling-reference reports via `IRefactorService.FindReferences` (search the existing code)
- Do NOT change how existing `ExpressionTargetField` is stored in `BTreeActionPayload` / `BTreeConditionPayload` — it stays as a plain `string?`

---

## Report Requirements

Submit `.dev/_DONE/ai-hsm-btree-vis-edit/reports/BATCH-05-REPORT.md`:

```markdown
# BATCH-05 Report

## Tasks Completed
- [ ] DEBT-02 fix (C# type aliases)
- [ ] TASK-BB-1b-02 (Add/Remove Variable workflows)
- [ ] TASK-BB-1b-03 (Variable rename via refactor service)

## Test Results
[dotnet test summary]

## Files Changed / Created

## Developer Insights

**Q1:** What issues did you encounter? How did you resolve them?
**Q2:** How did you handle the catalog wiring for BlackboardVariable references?
**Q3:** Anything surprising about the ImGui drag-drop approach?
**Q4:** Suggested git commit message?
```

---

## Success Criteria

- [ ] DEBT-02: `BuildViewModel` uses C# aliases; test updated to assert `"float"` not `"Single"`
- [ ] `IBlackboardManagedAsset` has AddVariable, RemoveVariable, UpdateVariableComment, MoveVariable, RenameVariable
- [ ] Both `BehaviorTreeAsset` and `HsmAsset` implement all new methods
- [ ] `BlackboardNameValidator.Validate` enforces all rules with individual tests
- [ ] `BlackboardTypeHelper.GetPrimitiveType` resolves name -> Type for known types
- [ ] Window shows `[+] Add variable...` popup with name, type, comment, validation
- [ ] Window shows remove option per row with dangling-reference count confirmation
- [ ] Rename routes through `IRefactorService` with the `"{assetId}::{name}"` key convention
- [ ] `dotnet build IOS-IG-SimHost.sln` succeeds
- [ ] All tests pass
- [ ] Report submitted
