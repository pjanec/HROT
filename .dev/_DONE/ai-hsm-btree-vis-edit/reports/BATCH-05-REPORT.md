# BATCH-05-REPORT: Add/Remove Variables, Rename, DEBT-02 Fix

**Batch:** BATCH-05
**Status:** COMPLETE
**Build:** PASS (0 errors, 0 new warnings)
**Tests:** PASS

---

## Test Summary

| Project | Tests Passed | Tests Added |
|---------|-------------|-------------|
| Hrot.Editor.AiShared.Tests | 320 | 18 |
| Hrot.BTree.Editor.Tests | 201 | 8 |
| **Total new tests** | | **26** |

---

## Tasks Completed

### DEBT-02 Fix — C# type aliases in variable display

**Files changed:**
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` — changed `private static readonly Dictionary<Type, string> TypeAliases` to `internal static readonly`
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardTypeHelper.cs` — NEW: `GetDisplayName(Type)` and `GetPrimitiveType(string)` helpers; `DefaultKnownTypeNames` list
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — `BuildViewModel` now calls `BlackboardTypeHelper.GetDisplayName(v.FieldType)` instead of `v.FieldType.Name`
- `Hrot/Editor/Hrot.Editor.AiShared/Tests/Windows/BlackboardAuthoringWindowTests.cs` — Updated assertion from `"Single"` to `"float"`

**Outcome:** Variable type names now display as C# aliases (`float`, `int`, `bool`, etc.) instead of CLR names (`Single`, `Int32`, `Boolean`).

---

### TASK-BB-1b-02 — Add Variable + Remove Variable workflows

**Files changed:**
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` — added 6 new mutation method signatures: `AddVariable`, `RemoveVariable`, `UpdateVariableComment`, `MoveVariable`, `RenameVariable`, `CountNodesReferencingVariable`
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardNameValidator.cs` — NEW: validates variable names (null/empty, first char, chars, duplicate, C# keyword)
- `Hrot/Editor/Hrot.Editor.AiShared/Hrot.Editor.AiShared.csproj` — added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (required for ImGui drag-drop payload pointer reads, matching the pattern used in Hrot.UI.Common)
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — REWRITTEN: 4-column table (Name/Type/Bytes/[x]); `[+] Add variable...` popup; inline rename on double-click; `[x]` remove button with dangling-reference confirmation modal; drag-drop reorder via ImGui `BeginDragDropSource`/`BeginDragDropTarget` with `"BB_VAR_DRAG"` payload; `_refactorService` field injected via constructor; `CommitRename` calls `PreviewRename`/`ApplyRename`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` — implemented all 6 new interface methods; `CountNodesReferencingVariable` checks `Action?.ExpressionTargetField` and `Condition?.ExpressionTargetField`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — implemented all 6 new interface methods; `CountNodesReferencingVariable` returns 0 (HSM does not have expression bindings in this slice)
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Windows/BlackboardAuthoringWindowTests.cs` — added `StubRefactorServiceBbWin`; updated `StubBlackboardAsset` with 6 new method stubs; updated 4 constructor calls to pass the stub refactor service
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardVariableWiringTests.cs` — added 6 missing method stubs to `StubManagedAsset`
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardAddRemoveTests.cs` — NEW: 17 tests covering model mutations, validator, type helper, and view-model population

---

### TASK-BB-1b-03 — Variable rename via the refactor service

**Files changed:**
- `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` — `RenameVariable(string oldName, string newName)` included in the 6 new methods above
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` — `RenameVariable` finds the matching entry by name, replaces it with a `with { Name = newName }` record copy, fires `Changed`
- `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` — same implementation
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` — inline rename mode: double-click enters rename, Enter/Tab commits via `CommitRename`, Escape cancels; `CommitRename` invokes `PreviewRename($"{asset.AssetId:D}::{oldName}", $"{asset.AssetId:D}::{newName}", new RefactorOptions())` then `ApplyRename` if no errors, then `bbAsset.RenameVariable(oldName, newName)`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeBlackboardVariableContributor.cs` — NEW: `IReferenceCatalogContributor` implementation; `EnumerateElements` yields one `BlackboardVariableSubElement` per variable with key `"{assetId:D}::{varName}"`; `EnumerateReferences` yields one `AssetReference` per node with non-null `ExpressionTargetField`
- `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BlackboardRenameTests.cs` — NEW: 8 tests covering `RenameVariable` model behavior, the `BTreeBlackboardVariableContributor` key format, element kind, empty-when-not-managed, references from action nodes, and skipping nodes without bindings

---

## New Files

| File | Purpose |
|------|---------|
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardTypeHelper.cs` | C# alias display + type resolution helpers |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardNameValidator.cs` | Variable name validation (UI input guard) |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardAddRemoveTests.cs` | 17 tests for model mutations, validator, type helper, view-model |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Catalog/BTreeBlackboardVariableContributor.cs` | Wires blackboard variable sub-elements and action-node references into the reference catalog |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BlackboardRenameTests.cs` | 8 tests for rename model + contributor |

---

## Decisions and Notes

- `AllowUnsafeBlocks` was added to `Hrot.Editor.AiShared.csproj` to support ImGui drag-drop payload pointer reads (`SetDragDropPayload` / `AcceptDragDropPayload` require an `int*`). This matches the established pattern in `Hrot.UI.Common`.
- `HsmAsset.CountNodesReferencingVariable` returns 0 because HSM nodes do not have `ExpressionTargetField` bindings in this slice. A follow-up task will implement the HSM equivalent when expression bindings are added.
- Per TASK-BB-1b-03 open-question #4 resolution: rename applies silently; errors surface as ImGui TextColored toast (no preview pane).
- `BlackboardVariableSubElement` is `internal` to `Hrot.BTree.Editor` (not `public`) since it is consumed only by `BTreeBlackboardVariableContributor` and the catalog machinery.
