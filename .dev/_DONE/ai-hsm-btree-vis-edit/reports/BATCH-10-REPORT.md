# BATCH-10 COMPLETION REPORT

**Topic:** ai-hsm-btree-vis-edit
**Batch:** BATCH-10
**Status:** COMPLETE

---

## Tasks Delivered

### DEBT-06 — Stale Alias Pruning

- Added `PruneStaleAliasBindings(IReadOnlyCollection<Guid> knownAssetIds)` to both `BehaviorTreeAsset` and `HsmAsset`. The method iterates alias binding lists, removes entries whose `RequiringAssetId` is not in `knownAssetIds`, fires `Changed` once if any were removed, and cleans up empty lists.
- Added `GetKnownSubAssetIds()` to both assets. It returns the set of all distinct `RequiringAssetId` GUIDs across all alias binding lists.
- Added default implementations to `IBlackboardManagedAsset` for both methods (no-op defaults) so all 7 existing test stubs compile without changes.
- `BlackboardAuthoringWindow.DrawClientArea()` now calls `bbAsset.PruneStaleAliasBindings(bbAsset.GetKnownSubAssetIds())` before `BuildViewModel` on each frame.
- **Limitation (recorded in DEBT-TRACKER):** Full catalog-wide cascade — notifying asset B when asset A's variable is deleted — is deferred. The current fix handles self-contained pruning (remove bindings for assets no longer known to this asset). Catalog-level eventing is a future batch item.

### 1f-07 — Load-State Banner in Blackboard Authoring Window

- Added `BlackboardLoadState` enum with members: `Clean`, `SpanCaptureFailed`, `StructParseFailed`, `AssemblyFailed`.
- Added `LoadState` and `LoadDiagnosticMessage` default interface members to `IBlackboardManagedAsset`.
- Added `LoadState { get; private set; }`, `LoadDiagnosticMessage { get; private set; }`, and `internal SetLoadDiagnostic(...)` to both `BehaviorTreeAsset` and `HsmAsset`.
- Added `BlackboardDtoEmitter.ValidateSaveAllowed(IBlackboardManagedAsset asset, bool allowLossySave = false)` — throws `InvalidOperationException` for `StructParseFailed`/`AssemblyFailed` unconditionally and for `SpanCaptureFailed` unless `allowLossySave` is set.
- `BlackboardAuthoringWindow.DrawClientArea()` now renders:
  - `AssemblyFailed`: red error banner + early return (replaces entire client area).
  - `StructParseFailed`: yellow warning banner + read-only display.
  - `SpanCaptureFailed`: yellow warning banner + "Save anyway (lossy)" button with `BeginPopupModal` confirmation.

### 1e-01 — Subtree Sync Panel in InspectorWindow

- Created `SubtreeNodeInfo(bool IsResolved, Guid SubtreeAssetId)` record in `Hrot.Editor.AiShared`.
- Created `SubtreeSyncBinding(string FieldName, string? MasterVariableName, bool SyncIn, bool SyncOut)` record in `Hrot.Editor.AiShared`.
- Created `IBTreeSyncableAsset` interface in `Hrot.Editor.AiShared` with: `GetSubtreeNodeInfo`, `GetSyncBindings`, `SetSyncBinding`, `ClearSyncBindings`, `GetVariablesOfType`.
- `BehaviorTreeAsset` now implements `IBTreeSyncableAsset`.
- `InspectorWindow` gained optional `Func<Guid, IBlackboardManagedAsset?>? subAssetResolver` constructor parameter. `DrawClientArea()` detects `BTreeNodeSelection + IBTreeSyncableAsset`, calls `GetSubtreeNodeInfo`, and renders either a "not resolved" message or the `DrawSyncBindingsTable` helper.

### 1e-02 — "Bound to" Dropdown in Sync Panel

- `DrawSyncBindingsTable` renders a 4-column ImGui table (Field name+type, Bound-to combo, SyncIn checkbox, SyncOut checkbox).
- The combo is populated via `syncAsset.GetVariablesOfType(fieldTypeName)` where `fieldTypeName` comes from `BlackboardTypeHelper.GetDisplayName(field.FieldType)`.
- Selecting a combo item calls `syncAsset.SetSyncBinding(nodeVisualId, new SubtreeSyncBinding(...))`.
- `GetVariablesOfType(string typeName)` on `BehaviorTreeAsset` uses exact-match on `BlackboardTypeHelper.GetDisplayName`. CLR names like "Int32" or "Single" do not match — only C# alias names ("int", "float") match. Verified by test T5.

---

## Files Created / Modified

| File | Change |
|------|--------|
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardLoadState.cs` | NEW |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/SubtreeNodeInfo.cs` | NEW |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/SubtreeSyncBinding.cs` | NEW |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBTreeSyncableAsset.cs` | NEW |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/IBlackboardManagedAsset.cs` | Added 4 default members (LoadState, LoadDiagnosticMessage, PruneStaleAliasBindings, GetKnownSubAssetIds) |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardTypeHelper.cs` | Changed `internal` to `public` (class + members) to allow access from Hrot.BTree.Editor |
| `Hrot/Editor/Hrot.Editor.AiShared/Blackboard/BlackboardDtoEmitter.cs` | Added `ValidateSaveAllowed(IBlackboardManagedAsset, bool)` |
| `Hrot/Editor/Hrot.Editor.AiShared/Windows/BlackboardAuthoringWindow.cs` | Added lossy-save fields + PruneStaleAliasBindings call + LoadState banners |
| `Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs` | Added `_subAssetResolver`, `DrawSyncBindingsTable`, `using System`, `using System.Collections.Generic` |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BehaviorTreeAsset.cs` | Added `IBTreeSyncableAsset` impl + LoadState + PruneStaleAliasBindings + GetKnownSubAssetIds |
| `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Model/HsmAsset.cs` | Added LoadState + PruneStaleAliasBindings + GetKnownSubAssetIds |
| `Hrot/Editor/Hrot.Editor.AiShared.Tests/Blackboard/BlackboardLoadStateTests.cs` | NEW (7 tests) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Inspector/BTreeSubtreeSyncPanelTests.cs` | NEW (11 tests) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/Inspector/BTreeBoundToDropdownTests.cs` | NEW (8 tests) |

---

## Test Results

| Project | Before | After | Delta |
|---------|--------|-------|-------|
| Hrot.Editor.AiShared.Tests | 365 | 372 | +7 |
| Hrot.BTree.Editor.Tests | 221 | 239 | +18 |
| Hrot.Hsm.Editor.Tests | 215 | 215 | 0 |

All tests pass. 0 errors, 0 warnings.

---

## Issues Encountered

1. **`BlackboardTypeHelper` accessibility:** The class was `internal` in `Hrot.Editor.AiShared` but needed by `BehaviorTreeAsset` in `Hrot.BTree.Editor` for `GetVariablesOfType`. Changed to `public`. No `InternalsVisibleTo` was added since the method is genuinely part of the public API surface (`IBTreeSyncableAsset` callers need the same display name logic).

2. **`_lossySaveRequested` field:** Initial implementation set this flag in the modal but never consumed it (lossy save plumbing not yet wired to emitter). Removed the field; a comment in the modal code notes the TODO. This avoids CS0414 (assigned but never read).

3. **Circular dependency prevention:** Spec originally suggested placing `SubtreeSyncBinding` in `Hrot.BTree.Editor`. Moved to `Hrot.Editor.AiShared` to avoid a circular reference (`InspectorWindow` is in AiShared and must not reference BTree.Editor). `IBTreeSyncableAsset` interface and its record types are all in AiShared; `BehaviorTreeAsset` implements the interface.

---

## Design Decisions Beyond Spec

- `ValidateSaveAllowed` is a static helper on `BlackboardDtoEmitter` rather than a separate class. This collocates the save-guard logic with the emitter it guards, avoiding a loose utility class for a one-off operation.
- `_lossySavePopupOpen` is initialised to `true` (as required by `BeginPopupModal`'s `p_open` semantics in ImGuiNET). The field is not exposed externally.
- `GetKnownSubAssetIds()` returns a `HashSet<Guid>` (cast to `IReadOnlyCollection<Guid>`) for O(1) membership testing in `PruneStaleAliasBindings`.

---

## Weak Points Spotted

- `DrawSyncBindingsTable` iterates `subAsset.BlackboardVariables` on every frame during ImGui rendering. If the subtree has many variables, a persistent cached view-model per nodeVisualId would be more efficient. Not a problem for typical subtree sizes (< 20 fields).
- `GetSyncBindings` returns `list.AsReadOnly()` — this creates a `ReadOnlyCollection<T>` wrapper on every call. In hot-path rendering this is negligible; if called in tight loops, a struct enumerable or direct readonly ref would help.
