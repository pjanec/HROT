# BATCH-10 Report: Editor smoke-test fixes (post-migration regressions)

## Implementation Summary

### Bug #1a — JSON contributors loaded via Refresh (EditorSubsystem.cs:601-607)

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` lines 601–607

Changed `btreeJsonContrib.Discover(rootDirectory: btreeJsonRootDir)` → `btreeJsonContrib.Refresh(rootDirectory: btreeJsonRootDir)` and same for `hsmJsonContrib`. `Refresh` calls `Discover + LoadAll + ContributorChanged`, so the JSON contributor's assets are actually populated on startup. Added `Directory.Exists` guards that print a `Console.WriteLine` warning (matching the logging style already used in the file) if the root path doesn't resolve — no silent no-op.

### Bug #1b — AssetCatalog.Rebuild deduped by AssetId, last-writer wins

**File:** `Hrot/Editor/Hrot.Editor.AiShared/Catalog/AssetCatalog.cs` lines 36–61

Rewrote `Rebuild()` in two passes:
1. First pass: build `_byId` with standard last-writer-wins semantics (iterates contributors in order; JSON contributors are added *after* assembly contributors by `AiAssetCatalogBuilder`, so JSON wins).
2. Second pass: build `_cache` as a *deduped* list — uses a `HashSet<Guid>` to track seen AssetIds; the first contributor to expose an id reserves the slot, but the value stored is `byId[asset.AssetId]` (the last-writer's instance).

Result: `All` returns exactly one entry per AssetId; `FindByAssetId`/`FindByName` both return the JSON (layout-bearing) instance. The dedup also fixes the browser showing `SampleScout` twice.

**Other `All` consumers checked:**
- `AssetBrowserWindow.cs` — lists `_catalog.All` for display (dedup correct)
- `AiAssetCatalogBuilderTests`, `SaveAllAndFlushTests` — use fresh catalogs without duplicates; unaffected
- Aggregation/comparison code uses `FindByAssetId`, not `All` iteration; correct

### Bug #2 — doc.MarkDirty() in the Asset.Changed handler

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` lines 2203–2210

Added `doc.MarkDirty();` as the first statement in the `doc.Asset.Changed += () => { ... }` lambda. Previously the handler only called `schedulerRef.Schedule(doc.Asset)` if `doc.Asset.IsDirty`, but never set `doc.IsDirty = true` — so `SaveAllAiDocumentsCommand.Execute` (`if (!doc.IsDirty) continue;`) always skipped every document.

The model→DTO position sync was already working: `BTreeCommandSink.ApplyNodeMoves` sets `node.Position = m.NewPosition; _asset.MarkDirty()` (asset-level dirty), and `BehaviorTreeAssetMapper.ToDto` reads `node.Position` → `EditorMetadata.X/Y`. So adding `doc.MarkDirty()` is the complete fix for Save-All.

### Bug #3 — Canonical AssetKind→perspective-name map

**New file:** `Hrot/Editor/Hrot.Editor.AiShared/Identity/AssetKindExtensions.cs`

Added `AssetKindExtensions.ToPerspectiveName(this AssetKind kind)` switch expression:
- `BTree` → `"BTree"`, `Hsm` → `"HSM"`, `Blueprint` → `"Blueprint"`, others → `kind.ToString()`

**Forward direction** (`AiDocumentManager.Activate`, line 162): changed `doc.Kind.ToString()` → `doc.Kind.ToPerspectiveName()`.

**Reverse direction** (`WindowManagerPerspectiveSwitcher.OnPerspectiveChanged`, line 70): changed `doc.Kind.ToString() == newPerspective` → `doc.Kind.ToPerspectiveName() == newPerspective`.

The "HSM" display name in the registrar (`EditorSubsystem.cs:1803`) and canvas window (`EditorSubsystem.cs:2108`) is unchanged — those are the correct registered names that the mapping now targets.

## Design Decisions

1. **`Refresh` over `LoadAll` separately:** `Refresh` is the public API that does the full three-step (Discover + LoadAll + ContributorChanged). Calling it directly means any future refactoring of the contributor lifecycle stays behind a single API.

2. **Dedup preserves first-occurrence slot order:** The `_cache` list uses a `HashSet` for the slot-reservation pass, but reads the value from `_byId` (last-writer). This means the list order reflects the order contributors first introduce an AssetId — assembly contributor's slot is preserved, but filled with the JSON instance. Alternative was to reverse-iterate (only JSON entries visible). The chosen approach is simpler and order-stable.

3. **Warning via `Console.WriteLine`:** The EditorSubsystem file already uses `Console.WriteLine` for all diagnostic output (not a structured logger). Using the same pattern keeps consistency and avoids introducing a new dependency.

4. **`ToPerspectiveName` as an extension method:** The spec said "put the helper somewhere both sites can share." Extension method on `AssetKind` in the same namespace (`Hrot.Editor.AiShared`) is accessible everywhere `AssetKind` is used without extra imports. No static class proliferation.

## Deviations

**None.** All three bugs fixed exactly as specified. The only forward change beyond the spec is updating two existing `AiDocumentManagerTests` that expected the old `"Hsm"` string — those tests were testing pre-bug behavior and needed to be corrected to match the canonical fix.

## Test Results

### New tests added

**`AssetCatalogTests.cs`** (+4 new tests):
- `All_Deduped_ByAssetId_LastWriterWins` — two contributors, same AssetId; asserts `All` has exactly 1 entry with PositionX=42 (JSON instance)
- `FindByAssetId_ReturnsJsonInstance_WhenDuplicate` — same setup; asserts `FindByAssetId` returns JSON instance
- `FindByName_ReturnsJsonInstance_WhenDuplicate` — same setup; asserts `FindByName` returns JSON instance
- `All_TotalCount_IsDeduped_NotRaw` — 2 same-id assets + 1 unrelated; asserts `All.Count == 2`

**`AssetKindExtensionsTests.cs`** (4 new tests):
- `[Theory]` with 3 cases: BTree→"BTree", Hsm→"HSM", Blueprint→"Blueprint"
- `Hsm_ToPerspectiveName_IsUppercaseHSM_NotEnumToString` — asserts `"HSM" != "Hsm"` (documents the exact bug)
- `BTree_ToPerspectiveName_MatchesRegisteredName`
- `Blueprint_ToPerspectiveName_MatchesRegisteredName`

**Existing tests updated** (+2):
- `AiDocumentManager_Activate_InvokesPerspectiveSwitchWithKind`: `"Hsm"` → `"HSM"`
- `AiDocumentManager_SwitchCallback_ReceivesKindName`: `"Hsm"` → `"HSM"` in expected array

### Gate results

| Gate | Result |
|------|--------|
| `dotnet build IOS-IG-SimHost.sln -c Debug --no-incremental` | **0 errors / 26 warnings** (all pre-existing) |
| `Hrot.Editor.AiShared.Tests` | **Passed 832 / 832** (was 820; +12 net: +8 new, -2 old+2 updated) |
| `Hrot.BTree.Editor.Tests` | **Passed 391 / 391** |
| `Hrot.Hsm.Editor.Tests` | **Passed 339 / 339** |
| `EditorSubsystemBoot` (integration filter) | **Passed 10 / 10** |
| `Hrot.Blueprints.Tests` | **Failed 7 / 1372** — all pre-existing DEBT-006 snapshot/golden tests; 0 new failures |

## Developer Insights

- **Bug #2 is only partially headless-testable.** The `doc.Asset.Changed` → `doc.MarkDirty()` wiring lives in `EditorSubsystem.Initialize` inside a lambda closure. That lambda is only created during full editor boot (tested by the 10/10 `EditorSubsystemBoot` tests). The `SaveAllAndFlushTests` prove that a dirty doc is saved correctly — together these give confidence. The canvas render path (node drag → `BTreeCommandSink.ApplyNodeMoves` → `asset.Changed` fires → `doc.MarkDirty`) requires a manual smoke test with the live editor.

- **Dedup ordering:** The `_cache` list preserves assembly-contributor insertion order for the *slot*, but substitutes the JSON instance as the *value*. This means `catalog.All[0]` for `SampleScout` is at the position corresponding to the assembly contributor's order — which is fine for all UI consumers (they display by name, not index).

- **AiAssetCatalogBuilder contributor ordering** (verified): JSON contributors (`bTreeJsonContributor`, `hsmJsonContributor`) are added via `catalog.AddContributor` after the assembly contributors. This means `_byId` last-writer-wins correctly resolves to the JSON instances.

- **Logging style:** EditorSubsystem does not use NLog or any structured logger (`FdpLog<T>` is generic and would require a type parameter). `Console.WriteLine` is the established pattern in this file and is appropriate for startup diagnostics.

## Known Issues

- **Bug #2 (Save-All) manual smoke required:** The `doc.MarkDirty()` wiring change is confirmed correct by code inspection (model→DTO position round-trip verified in BATCH-10-INSTRUCTIONS; `doc.MarkDirty()` is the only missing piece), but the end-to-end flow (drag node → save → restart → layout preserved) requires a live editor smoke test by the user.

- **Bug #1 manual smoke required:** The `Refresh()` fix is correct by inspection, but the actual rendering of migrated SampleScout/SampleGuard with correct layout after the fix requires the user to run the editor and open those assets.

- **Bug #3 manual smoke required:** `WindowManagerPerspectiveSwitcher` calls `SwitchPerspective` which invokes `WindowManager.SwitchPerspective` — the actual perspective tab transition is an ImGui/WindowManager concern not reachable in headless tests. The mapping fix is headless-tested; the visual tab switch must be verified live.

## Out of Scope (confirmed)

- **`[x]` close in OPEN section** (`AssetBrowserWindow.cs:164-165` is correctly wired to `mgr.Close(doc)`): not a code bug, likely an ImGui interaction/frame artifact. User should re-repro.
- **"Blueprint not registered" on Run**: pre-existing, requires "Compile / Reload Blueprint" step first (`BlueprintAttachService.cs:100-104`). Not our regression.
- **Asset-browser friendliness / type indicators**: Phase 7 (PU-701), out of scope.

## Suggested Commit Message

```
fix(editor): BATCH-10 — JSON contributors Refresh, catalog dedup, MarkDirty, HSM perspective map

- EditorSubsystem.Initialize: Discover→Refresh for BTree+HSM JSON contributors (+ missing-dir warning)
- AssetCatalog.Rebuild: dedup _cache by AssetId, last-writer (JSON) wins — browser shows one entry
- EditorSubsystem asset.Changed handler: add doc.MarkDirty() so Save-All includes edited docs
- AssetKindExtensions.ToPerspectiveName(): canonical Kind→perspective map (Hsm→"HSM")
- AiDocumentManager.Activate + WindowManagerPerspectiveSwitcher: use ToPerspectiveName() both directions
- Tests: AssetCatalog dedup (4), AssetKindExtensions mapping (4), update 2 stale test expectations
```
