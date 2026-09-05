# BATCH-28 Report

**Batch:** BATCH-28  
**Developer:** pjanec (Claude)  
**Date:** 2026-06-12  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| MTB-P8-T2 (AssetPickerSource) | ✅ | Full implementation with public ToEntry/BuildEntries seam |
| MTB-P8-T2 (DBT-1 icon distinctness) | ✅ | All 8 cells confirmed pairwise-distinct — no reassignments needed |
| AssetPickerSourceTests | ✅ | 6 tests covering Category, IconKey, Tag, Scenario filter, GetItemKey stability, Description, SingleKind |
| AssetKindIconsRegistrationTests | ✅ | 2 tests: asset-kind distinctness + folder distinctness + cross-set check |
| Full Hrot.Editor.AiShared.Tests | ✅ | 1025 passed, 0 failed, 0 skipped |

---

## 🧪 Testing Results

**Unit Tests Passed:** 1025 / 1025  
**Integration Tests Passed:** N/A (no integration tests for this batch)  
**Skipped:** 0

**Key Test Scenarios Verified:**
- ✅ `Entries_HaveKindGroupedCategory_AndPerKindIcon_AndAssetTag` — All-kinds source projects Blueprint assets: subfolder `AI` → `Category == "Blueprint/AI"`, root → `Category == "Blueprint"`, both `IconKey == "asset/blueprint"`, both `Tag` identity to input asset
- ✅ `ScenarioVariant_YieldsOnlyScenarios` — `AssetKindFilter.Scenario` source returns only Scenario-kind items from a mixed catalog; both `Query` and `BuildEntries`
- ✅ `GetItemKey_StableAcrossQueries` — same asset returns same key (`asset.AssetId.ToString()`) across queries
- ✅ `Description_FromRecipeMetadata_WhenPresent` — targeted asset gets `"Recipe desc"` via injectable `describe`; another asset gets `null`
- ✅ `SingleKindVariant_OmitsKindPrefixInCategory` — `AssetKindFilter.Blueprint` source: subfolder → `Category == "AI"` (no prefix), root → `Category == null`
- ✅ `BuildEntries_ReturnsEntryPerQueryResult` — full Query→projection roundtrip; text filter works
- ✅ `EachAssetKind_ResolvesToDistinctIcon_NoSharedCell` — all 6 `AssetKind` values have resolvable `TryGet`, 6 cells pairwise distinct
- ✅ `FolderIcons_ResolveAndAreDistinct` — `"folder"` and `"folder_open"` resolve, cells differ, and neither collides with any asset-kind cell

**Full suite run (without `BLUEPRINT_REGENERATE_SNAPSHOTS`):**
```
Passed!  - Failed:     0, Passed:  1025, Skipped:     0, Total:  1025, Duration: 5 s
```

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

No blocking issues. The interfaces and existing patterns were well-documented. Key observations:
- `AssetBrowserPanel.BaseFolderFor` is `internal static` in the same assembly (`Hrot.Editor.AiShared`), so it's directly accessible from `AssetPickerSource` without any access modifier changes.
- `AssetRelPath.RelPath` correctly handles both file-based assets (Blueprint/BTree/Hsm) and non-file assets (Scenario/Blackboard/Utility) — for the latter, it falls back to `asset.Name` when `SourceFilePath` is empty or `baseFolder` is null. This means Blackboard and Utility assets (which have no `Assets/` root) still get reasonable relpaths via their name.
- The `AssetKindFilterMapping.PermittedKinds` helper returns kinds in enum-declaration order, which is deterministic.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `AssetBrowserPanel.BaseFolderFor` catches `ArgumentOutOfRangeException` from `AssetRoots.AssetsFor` and returns null. This pattern (exception-as-control-flow) is a mild smell but is an established internal convention in this assembly — not something to change in this batch.
- The `IIconProvider.TryGet` interface uses `out IconHandle` rather than a nullable return, which makes test assertions slightly more verbose but is consistent with the rest of the codebase.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **Additional test `BuildEntries_ReturnsEntryPerQueryResult`:** While not explicitly required by the acceptance bar, this test validates the full `Query → Select(ToEntry)` roundtrip including text filtering, which is the exact seam T3 will call as `PickerRequest.ItemsProvider`. This provides confidence that `BuildEntries` produces a complete, well-shaped result.
- **Cross-set distinctness in `FolderIcons_ResolveAndAreDistinct`:** The spec only required folder cells to differ from each other, but DBT-1's full intent is that all 8 keys (6 asset-kind + folder + folder_open) are pairwise distinct. Added `Assert.DoesNotContain` checks ensuring folder cells don't collide with any asset-kind cell.
- **No `AssetPickerSources.Register` helper added:** The spec allows a thin factory/registration helper but doesn't require one. Since T3 (next batch) is responsible for wiring, keeping registration in T3 avoids pre-committing to a registration pattern that might not match the final integration needs.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **Non-file assets (Blackboard, Utility):** These kinds have no `Assets/` root, so `BaseFolderFor` returns `null`. `AssetRelPath.RelPath` then uses `asset.Name` as the relpath. This means a Blackboard asset named `"CombatBB"` would have `subfolder = null` (no `/` in the name). This is correct behavior — Blackboard/Utility assets sit at the root of their kind group without subfolder hierarchy.
- **Scenario assets with structured names:** Scenarios may have names like `"combat/Patrol"` (path-encoded). Since `AssetRelPath.RelPath` falls back to `Name` for non-file contributors, the `/` in the name creates a subfolder. This is intentional per design doc §19.
- **Empty catalog:** If `catalog.All` is empty, `BuildEntries` returns an empty list. The `EmptyResultText = "No assets found."` property is available for the picker UI.
- **`RenderItem`/`RenderPreview` in headless tests:** These are guarded with `ImGui.GetCurrentContext() != IntPtr.Zero`, so they are safe to call in test environments without an ImGui context — they simply no-op.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `BuildEntries` calls `Query` then `Select(ToEntry).ToList()`, which iterates the catalog twice. For the current catalog size (dozens to low hundreds of assets), this is negligible. If the catalog grows to thousands, the `ToList()` in `Query` could be eliminated by making `BuildEntries` do the filtering and projection in a single pass. Not a concern for this batch.
- `_permittedKinds.Contains(a.Kind)` in the `Query` LINQ filter is O(k) per asset where k ≤ 6 — acceptable.

---

## 🔍 Icon Cell Analysis (DBT-1)

### Asset-Kind Cells (6)

| AssetKind | IconKey | Cell |
|-----------|---------|------|
| Blueprint | `asset/blueprint` | `b2` |
| BTree | `asset/btree` | `c10` |
| Hsm | `asset/hsm` | `c11` |
| Blackboard | `asset/blackboard` | `c12` |
| Utility | `asset/utility` | `b8` |
| Scenario | `asset/scenario` | `b1` |

### Folder Cells (2)

| IconKey | Cell |
|---------|------|
| `folder` | `c8` |
| `folder_open` | `a1` |

### Distinctness Result

**All 8 cells pairwise distinct:** `{a1, b1, b2, b8, c8, c10, c11, c12}` — no collisions.

**Cross-context reuse** (acceptable per spec — only the asset-kind + folder set must be internally distinct):
- `asset/btree` = `c10` also used by `bt/selector`
- `asset/hsm` = `c11` also used by `bt/observer_selector`
- `asset/blackboard` = `c12` also used by `bt/parallel`
- `folder` = `c8` also used by `bt/composite`, `browser/open`
- `folder_open` = `a1` also used by `bt/root`, `perspective/editor`

**No cells were reassigned** — the existing default map already satisfies the distinctness requirement.

---

## ⚠️ Outstanding Issues / Next Steps

- ✅ **T3 wiring:** The `BuildEntries`/`ToEntry` seam is exposed and ready. Next batch should:
  - Register `AssetPickerSource` in the picker registry (likely via `IPickerRegistry.Register`)
  - Wire an "Open Asset…" entry point that calls `registry.OpenPicker(PickerRequest{ ItemsProvider = source.BuildEntries, Layout = Tree })`
  - Route `PickerEntry.Tag` (the `IEditableAsset`) to the appropriate editor/document opener
- No known issues or limitations from this batch.

---

## 💾 Suggested Commit Message

```
feat(main-toolbar): AssetPickerSource + per-kind/folder icon distinctness (MTB-P8-T2)

- New AssetPickerSource : IPickerSource<IEditableAsset> in Hrot.Editor.AiShared.Browser
  projects IAssetCatalog → PickerEntry with kind-grouped Category, per-kind IconKey,
  Tag=IEditableAsset, and recipe Description
- Public ToEntry/BuildEntries seam for T3's PickerRequest.ItemsProvider
- Supports All/Single-kind Category derivation, Scenario-only filtering, text search
- Verified all 6 asset-kind cells + folder/folder_open are pairwise distinct (DBT-1)
- 6 AssetPickerSourceTests + 2 AssetKindIconsRegistrationTests
- Full Hrot.Editor.AiShared.Tests: 1025 passed, 0 failed
```

Co-Authored-By: Claude <noreply@anthropic.com>
