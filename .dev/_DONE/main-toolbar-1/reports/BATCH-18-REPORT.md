# BATCH-18 Report

## Implementation Summary
**MTB-P6-T3 (INewAssetService for BTree, HSM, Scenario) + MTB-P6-T4 (FolderTreePicker pick mode).**

### T3 — BTree/Hsm/Scenario `INewAssetService` impls (§18.3)

**BTree (`BTreeNewAssetService`, `Hrot.BTree.Editor`):**
- Implements `INewAssetService`; `Kind => AssetKind.BTree`.
- **Empty path:** synthesizes a minimal `BehaviorTreeAssetDto` in code (no disk read), mints a fresh `AssetId`, serializes via `BTreeJsonServices.Serialize`, pretty-prints via `JsonAestheticFormatter.FlattenNumericArrays`, and writes atomically via `AtomicFileWriter.Write` under `<assetsRoot>/<relPath>/<name>.btree.json`.
- **From recipe:** round-trips the recipe DTO (serialize→deserialize), assigns fresh `AssetId`+`Name`, and writes to the same location pattern.
- Returns `BTreeEditableAssetAdapter` (wraps DTO + written file path) implementing `IEditableAsset`.
- `AvailableRecipes()` includes synthetic "Empty" only (recipe discovery TODO: requires future `IRecipeDiscovery` service).
- Constructor accepts optional `string? assetRootPath` (defaults to `AssetRoots.AssetsFor(BTree)`) for testability with temp directories.

**HSM (`HsmNewAssetService`, `Hrot.Hsm.Editor`):**
- Identical pattern to BTree via `HsmJsonServices`.
- Minimal `HsmAssetDto` with empty states/transitions/events/regions; file extension `.hsm.json`.
- Returns `HsmEditableAssetAdapter`.

**Scenario (`ScenarioNewAssetService`, `Hrot.Editor`):**
- Implements `INewAssetService`; `Kind => AssetKind.Scenario`.
- Routes to `IScenarioCreationSession` (narrow testable seam over `IEditorLogic`):
  - **Empty** → `NewScenario()` (clear world) then `SaveScenarioAs("<relPath>/<name>")`.
  - **FromSeed** → `LoadScenarioByName(seedName)` then `SaveScenarioAs("<relPath>/<name>")`.
- `AvailableRecipes()`: synthetic "Empty" + seed scenario names (passed via constructor).
- Returns `ScenarioEditableAssetAdapter`; scenario assets have no file-based DTO.

### T4 — FolderTreePicker pick mode (§18.1)

**`FolderPickerState`** (added to `FolderTreePicker.cs`, `Hrot.Editor.AiShared.Browser`):
- Pure in-memory model (no ImGui dependency) for pick mode.
- **`string SelectedRelPath`** — currently selected folder relative to root (`""` = root, `/`-normalized).
- **`AddFolder(parentRelPath, name) → string`** — creates a new folder node in the model, returns its root-relative path, sets `SelectedRelPath` to the new folder.
- **Root-bounding:** rejects `..` (anywhere in name/path), `/`-leading paths, drive letters (`C:`), path separators within segment names. Static helpers `SanitizeFolderName`, `SanitizeRelPath`, `IsBounded`.
- Constructor imports existing folder paths (sanitized; unsafe paths silently filtered).
- `ContainsFolder(relPath)` and `FolderPaths` for UI enumeration.

## Design Decisions

1. **DTO-first minting for BTree/HSM:** Rather than constructing the heavy editor models (`BehaviorTreeAsset` / `HsmAsset` — which require `BehaviorTreeBlob`/`HsmDefinitionBlob`/`MachineMetadata` placeholders and complex internal wiring), the services build minimal DTOs directly and write JSON. The dialog (MTB-P6-T5) will load them from disk via the existing mappers. This avoids coupling the NewAssetService to the full editor model graph and keeps constructors simple.

2. **Narrow `IScenarioCreationSession` seam over `IEditorLogic`:** The full `IEditorLogic` interface has 20+ methods. Exposing only the 3 needed for scenario creation (`NewScenario`, `SaveScenarioAs`, `LoadScenarioByName`) keeps the service testable with a 3-method fake rather than requiring a mock of the entire interface.

3. **`SanitizeRelPath` rejects leading `/`:** The spec says "bounded to root" and "reject absolute." Unix-style absolute paths (`/a/b`) are rejected. On Windows, `Path.IsPathRooted` is checked as a fallback for platform-specific edge cases.

4. **`AssetRoots` path override via constructor:** Both `BTreeNewAssetService` and `HsmNewAssetService` accept `string? assetRootPath` — when null, they default to `AssetRoots.AssetsFor(kind)`. Tests pass a temp directory. This is a minimal seam (no DI interface for path resolution) since the full path-resolution strategy is deferred to the dialog layer.

## Deviations
None — all tasks implemented exactly as specified in the instructions, DEC-12 decision, and test requirements.

## Test Results

### New tests (unfiltered — all pass)

| Suite | Tests | Passed | Failed |
|---|---|---|---|
| BTreeNewAssetTests (BTree.Editor.Tests) | 7 | 7 | 0 |
| HsmNewAssetTests (Hsm.Editor.Tests) | 6 | 6 | 0 |
| ScenarioNewAssetTests (Editor.Tests) | 6 | 6 | 0 |
| FolderTreePickerPickTests (AiShared.Tests) | 17 | 17 | 0 |
| **Total** | **36** | **36** | **0** |

### Required suites (Stability filter: `Stability!=Flaky&Stability!=Environment&Stability!=Broken`)

| Suite | Passed | Failed | Skipped | Notes |
|---|---|---|---|---|
| Hrot.Editor.AiShared.Tests | 968 | 1* | 0 | *Pre-existing flaky: `Batch14RefactorTests.RefactorService_Rename_PartialMatch_OnlyWritesMatchingFiles` — order-dependent, passes in isolation |
| Hrot.BTree.Editor.Tests | 406 | 0 | 0 | |
| Hrot.Hsm.Editor.Tests | 358 | 0 | 0 | |
| Hrot.Editor.Tests | 156 | 0 | 0 | |
| Fdp.Toolkits.Tests | 1856 | 0 | 0 | |
| Hrot.SimHost.Tests | 585 | 0 | 3 | 3 pre-existing skips (headless subsystem init) |

`dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings.

## Developer Insights

### Issues encountered and resolved
1. **`Path.IsPathRooted` platform variance:** On Windows, `Path.IsPathRooted("/a/b")` can return `true` depending on .NET version (forward-slash is `AltDirectorySeparatorChar`). Added an explicit `IsAbsolutePath()` helper that checks both separator characters and drive letters before falling back to `Path.IsPathRooted`.
2. **xUnit2013 analyzer warnings:** `Assert.Equal(1, list.Count)` triggers xUnit analyzers asking for `Assert.Single`. Fixed in `ScenarioNewAssetTests.cs`. The BTree/Hsm test projects don't have xUnit analyzers enabled, so those weren't flagged.
3. **Pre-existing flaky test:** `Batch14RefactorTests.RefactorService_Rename_PartialMatch_OnlyWritesMatchingFiles` fails in full-suite parallel run but passes in isolation — order-dependent, not caused by this batch.

### Edge cases discovered
- Folder names with path separators (`/`, `\`) within a single segment are rejected by `SanitizeFolderName` (e.g. `"a/b"` as a single name is invalid — callers should use `AddFolder("a", "b")` recursively).
- Drive-letter patterns like `"C:"` at any segment boundary are rejected — covers both `"C:"` and `"C:stuff"`.
- Double-dot not just at segment level but anywhere in the string (`"foo..bar"`) is rejected by `SanitizeFolderName`.

### Weak points / improvement opportunities
- BTree/HSM recipe discovery from disk is not wired — `AvailableRecipes()` returns only the in-code "Empty" entry. An `IRecipeDiscoveryService` would be the natural next step when recipe files land on disk.
- The `BTreeEditableAssetAdapter`/`HsmEditableAssetAdapter` carry DTOs but have no real mutation support (`IsDirty = false`, no `Changed` event wiring). Sufficient for the mint+open flow; the dialog (T5) will need them to support opening via `AiDocumentManager`.

## Known Issues
- Scenario `IScenarioCreationSession` has no production wiring in this batch. The `IEditorLogic` adapter will be wired in MTB-P6-T5 (New Asset dialog) or MTB-P7-T1 (scenario menu commands).

## Suggested Commit Message
```
feat(main-toolbar): BTree/HSM/Scenario INewAssetService + FolderTreePicker pick mode (MTB-P6-T3, T4)
```
