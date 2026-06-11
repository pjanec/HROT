# BATCH-10 Report

## Implementation Summary
**Tasks MTB-P4-T1, MTB-P4-T2** — Implemented the pure read-mode `FolderTreePicker` tree-builder, the `BaseFolder` seam on `IAssetCatalogContributor`, and the `AssetRelPath` relative-path helper.

### Task 1 — `FolderTreePicker` read-mode tree-builder (MTB-P4-T1)
**File:** `Hrot/Editor/Hrot.Editor.AiShared/Browser/FolderTreePicker.cs`

- **`FolderTreeNode`** — sealed class: `Name` (segment name), `FullPath` (accumulated relative path using `/`), `IsLeaf` (true for terminal asset paths), `Children` (sorted readonly list).
- **`FolderTreePicker.Build(IEnumerable<string>? relativePaths)`** — pure, ImGui-free tree builder. Splits each path on `/`, creates intermediate folder nodes, marks final segment as leaf. Handles null input (returns empty root), skips null/empty entries, handles root-level leaves (no `/`), and supports nodes that are both folders and leaves (e.g. `"shared"` + `"shared/x"`).
- **Sort rule (stable/deterministic):** folders first (IsLeaf=false → group 0), then leaves (IsLeaf=true → group 1), each group alphabetical by `Name` using `StringComparer.Ordinal`. Same input → same tree regardless of input order.
- **No ImGui, no I/O, no pick mode** — pick mode is MTB-P6-T4.

### Task 2 — `BaseFolder` seam + `AssetRelPath` helper (MTB-P4-T2)

**Interface change:** `IAssetCatalogContributor` gains `string? BaseFolder { get; }` as a **default interface member** (`=> null;`). Every existing implementor stays backward-compatible — only the three file-backed contributors override it.

**Contributors with non-null `BaseFolder`:**
| Contributor | Kind | Why file-backed |
|---|---|---|
| `BlueprintAssetContributor` | Blueprint | Scans `*.bp.json` under `Assets/Blueprints`; assets have `SourceFilePath` with `IsEditorOwned=false` |
| `BTreeJsonAssetContributor` | BTree | Scans `*.btree.json` under `Assets/BTrees`; assets have `SourceFilePath` with `IsEditorOwned=true` |
| `HsmJsonAssetContributor` | Hsm | Scans `*.hsm.json` under `Assets/HSMs`; assets have `SourceFilePath` with `IsEditorOwned=true` |

**Assembly-backed contributors keep default `null`:**
- `BTreeAssetContributor` — uses `[BTreeDefinition]` reflection; assets have `SourceFilePath = string.Empty`
- `HsmAssetContributor` — uses `[HsmDefinition]` reflection; assets have `SourceFilePath = string.Empty`

All three overrides return `AssetRoots.AssetsFor(Kind)`, which resolves to the absolute `Assets/<Kind>` directory.

**Relative-path helper:** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetRelPath.cs`
- `static string RelPath(IEditableAsset asset, string? baseFolder)`
- **File asset** (non-empty `SourceFilePath` and non-null/non-empty `baseFolder`): `Path.GetRelativePath(baseFolder, SourceFilePath)` → `\` replaced with `/`, leading `/` trimmed.
- **Non-file asset** (empty `SourceFilePath` or null/empty `baseFolder`): returns `asset.Name` verbatim.
- Throws `ArgumentNullException` for null `asset`.

## Design Decisions

### 1. `FolderTreePicker` returns a root node (not a list)
The `Build()` method returns a `FolderTreeNode` root (with empty `Name`/`FullPath`, `IsLeaf=false`) whose `Children` are the top-level entries. This makes the empty-input case uniform (root with no children) and lets callers treat all levels identically. An alternative of returning `IReadOnlyList<FolderTreeNode>` would require special-casing the root.

### 2. HashSet-based trie for duplicate resilience
The build algorithm uses `Dictionary<string, HashSet<string>>` for parent→children relationships, which naturally deduplicates. If the same path appears multiple times in input, it silently collapses to one node.

### 3. `string.IsNullOrEmpty(baseFolder)` in `AssetRelPath`
The check uses `string.IsNullOrEmpty` rather than just `== null` because `Path.GetRelativePath` throws `ArgumentException` for an empty `relativeTo`. This also aligns with the intent: an empty base folder is semantically equivalent to "no base folder."

### 4. Json contributors (not assembly) get `BaseFolder`
The `BTreeAssetContributor` and `HsmAssetContributor` are **assembly-reflection** contributors — their assets have `SourceFilePath = string.Empty`. Only the JSON file contributors produce assets with real file paths under `Assets/<Kind>`, so only those override `BaseFolder`. This is documented in the table above.

## Deviations
None. All implementation follows the batch spec exactly.

## Test Results

### New tests (both classes, unfiltered — 14 total, all pass)

**`FolderTreePickerTests` (5 tests):**
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 11 ms
```
- `Build_NestedPaths_ProducesCorrectHierarchy` — `["a/b/x", "a/b/y", "a/z"]` → root→a→(b→x,y, z); asserts FullPath + IsLeaf at every level
- `Build_EmptyAndRootLevelLeaves_Handled` — empty → empty children; null → empty children; null/empty entries skipped; root-level leaf `"x"` is a leaf child; mixed root-level + nested
- `Build_IsStable_Sorted` — 3 different input orders produce identical trees; verifies sort rule: folders-first, leaves-second, alphabetical
- `Build_SingleDeepPath_CreatesChain` — `"a/b/c/d"` → 4-level chain, only deepest is leaf
- `Build_FolderThatIsAlsoLeaf_IsLeafTrue` — `"shared/x"` + `"shared"` → shared is both IsLeaf=true and has child

**`AssetRelPathTests` (9 tests):**
```
Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 11 ms
```
- `FileAsset_RelPath_IsSourceMinusBase` — base `…/Assets/Blueprints`, source `…/Assets/Blueprints/combat/Guard.bp.json` → `"combat/Guard.bp.json"`; no backslashes, no leading `/`
- `FileAsset_RelPath_HandlesWindowsBackslash` — `C:\…` paths → `\` normalized to `/`
- `FileAsset_RelPath_NestedDeeply` — BTree path, nested 3 levels deep
- `ScenarioAsset_RelPath_IsName` — empty SourceFilePath → returns Name verbatim
- `ScenarioAsset_RelPath_NullBaseFolder_ReturnsName` — non-null SourceFilePath but null base → Name
- `ScenarioAsset_RelPath_EmptyBaseFolder_ReturnsName` — empty `""` base → Name (null-or-empty guard)
- `Contributor_BaseFolder_MatchesAssetRoot` — 4 assertions: Blueprint/BTree/Hsm BaseFolder == AssetsFor(Kind) and != null; default fake → null
- `Contributor_BaseFolder_DefaultIsNull` — `IAssetCatalogContributor` typed variable → null
- `RelPath_NullAsset_ThrowsArgumentNullException` — throws with param name "asset"

### Required suites with Stability filter (all 0-failed)

```
Hrot.Editor.AiShared.Tests:  Passed!  - Failed: 0, Passed:  904, Skipped: 0, Total:  904, Duration:  4 s
Hrot.BTree.Editor.Tests:    Passed!  - Failed: 0, Passed:  399, Skipped: 0, Total:  399, Duration: <1 s
Hrot.Hsm.Editor.Tests:      Passed!  - Failed: 0, Passed:  352, Skipped: 0, Total:  352, Duration: <1 s
Fdp.Toolkits.Tests:         Passed!  - Failed: 0, Passed: 1856, Skipped: 0, Total: 1856, Duration: 26 s
Hrot.SimHost.Tests:         Passed!  - Failed: 0, Passed:  585, Skipped: 3, Total:  588, Duration: 12 s
```

**`Hrot.Blueprints.Tests`:** No new tests were added to this project, so it was not run per batch instructions (PRE-1 pre-existing failures, not in scope).

### Full solution build
```
Build succeeded.  0 Error(s), 13 Warning(s) — all pre-existing
```

No EQS flake appeared during this run.

## Developer Insights

1. **Default interface members work seamlessly across assembly boundaries.** The `BaseFolder` addition required zero changes to `BTreeAssetContributor`, `HsmAssetContributor`, `FakeContributor` (in tests), or any other implementor — they all compiled without modification. The C# 8 default interface member feature is well-supported in the net8.0 target.

2. **The tree builder's root-node design simplifies consumption.** Returning a `FolderTreeNode` root (rather than `IReadOnlyList<FolderTreeNode>`) means the browser panel can recursively render from a single node without special-casing the top level. The `IsLeaf=false, Name="", FullPath=""` sentinel at root is a clean "folder of everything."

3. **`Path.GetRelativePath` edge case with empty string.** `Path.GetRelativePath("", anyPath)` throws `ArgumentException` ("The path is empty"). The helper guards with `string.IsNullOrEmpty(baseFolder)` rather than just `== null`. This also correctly handles the case where a contributor's `BaseFolder` property is accidentally empty.

4. **The BTree/HSM assembly-backed contributors are the "odd ones out."** Their assets have `SourceFilePath = string.Empty`, making `RelPath` fall back to `Name`. This is correct — those contributors expose behavior trees/HSMs discovered via `[BTreeDefinition]`/`[HsmDefinition]` reflection, not files on disk. The JSON contributors (`BTreeJsonAssetContributor`, `HsmJsonAssetContributor`) are the file-backed counterparts and are the ones that override `BaseFolder`.

5. **No performance concerns at this scale.** The tree builder uses O(paths × segments) time and O(nodes) memory with HashSet-based deduplication. Asset catalogs typically have hundreds to low thousands of entries — well within acceptable range.

## Known Issues
None. All batch requirements are met.

## Suggested Commit Message
```
feat(main-toolbar): FolderTreePicker read-mode + BaseFolder seam + AssetRelPath helper (MTB-P4-T1, T2)

- FolderTreePicker.Build() pure tree-builder from relative paths
- IAssetCatalogContributor.BaseFolder as default interface member
- BaseFolder = AssetRoots.AssetsFor(Kind) on file-backed contributors
  (BlueprintAssetContributor, BTreeJsonAssetContributor, HsmJsonAssetContributor)
- AssetRelPath.RelPath() computes asset relpath from base
- 14 new tests (FolderTreePickerTests + AssetRelPathTests)
- All hot suites 0-failed with Stability filter
```
