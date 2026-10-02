# BATCH-11 Report

## Implementation Summary

Created `AssetBrowserPanel` (MTB-P4-T3) — a generic, reusable Asset Browser content panel with per-kind tabs, folder trees, kind icons, and selection/activation events. The logic model is fully separated from ImGui draw, making it testable headlessly.

**New files:**
- `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetBrowserPanel.cs` — all types in one file (210 lines of logic + 100 lines of DrawContent):
  - `AssetKindFilter` [Flags] enum (None=0, Scenario=1, Blueprint=2, BTree=4, Hsm=8, Blackboard=16, Utility=32, All=~0)
  - `AssetKindFilterMapping` static helper (FromKind, PermittedKinds)
  - `AssetBrowserPanelOptions` (Kinds wired; ShowAllTab/InitialKind/InitialFullPath stored, deferred to MTB-P4-T4/T5)
  - `AssetBrowserPanel` — constructor(IAssetCatalog, IIconProvider, AssetBrowserPanelOptions), logic model + DrawContent
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Browser/AssetBrowserPanelTests.cs` — 4 tests against fakes

## Design Decisions

1. **Tabs are filter-driven, not data-driven.** `Tabs` returns all permitted kinds from `options.Kinds` (via `PermittedKinds`), regardless of whether the catalog has any assets of that kind. A kind with zero assets appears as a tab with an empty tree ("No assets"). This keeps tabs predictable and stable across catalog changes. The spec allowed either choice; filter-driven was chosen because it honors the caller's intent ("show me Blueprints and HSMs") even when one kind is temporarily empty.

2. **Logic/draw separation.** The model exposes `Tabs`, `TreeFor(kind)`, `AssetForLeaf(kind, leaf)`, `RowIconKey(asset)`, `Selection`, `SelectAsset(asset)`, `ActivateAsset(asset)`, and the `AssetActivated` event. `DrawContent()` calls these model methods but contains no logic. The draw side is intentionally simple — it renders tabs via `ImGui.BeginTabBar`/`BeginTabItem`, trees via `TreeNodeEx`, and resolves icons through the `IIconProvider`. No effort was spent on visual polish since the host (modal/docked window) is not yet implemented.

3. **BaseFolderFor wraps AOORE → null.** For file kinds (Blueprint/BTree/Hsm), delegates to `AssetRoots.AssetsFor(kind)`. For rootless kinds (Blackboard/Utility), catches `ArgumentOutOfRangeException` and returns `null`, per the batch spec's "wrap the AOORE" instruction. This keeps the panel agnostic about which kinds have Assets roots.

4. **Per-kind rebuild on Changed.** The panel subscribes to `IAssetCatalog.Changed` and eagerly rebuilds all per-kind trees. Since the panel is immediate-mode (re-rendered every frame), this is cheap and correct.

5. **Scenario flag reserved.** `AssetKindFilter.Scenario = 1` is defined but never returned by `PermittedKinds` — it's reserved for MTB-P5-T2 when `AssetKind.Scenario` exists. The `AssetKindFilterMapping.PermittedKinds` method explicitly omits it with a comment.

## Deviations

None. The implementation follows the batch instructions exactly:
- Only `Kinds` is wired; `ShowAllTab`/`InitialKind`/`InitialFullPath` are stored but deferred.
- No "All" tab, chips, or incremental filter (MTB-P4-T4).
- No auto-expand/last-opened (MTB-P4-T5).
- No `AssetKind.Scenario` (MTB-P5-T2).
- Panel performs no side effects.
- No legacy code modified or deleted.

## Test Results

### New tests (unfiltered) — 4 passed, 0 failed

```
dotnet test Hrot.Editor.AiShared.Tests --filter "FullyQualifiedName~AssetBrowserPanelTests"
Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 16 ms
```

| Test | What it verifies |
|------|-----------------|
| `Tabs_ReflectKindFilter` | `Kinds = Blueprint \| Hsm` → `Tabs` = `[Blueprint, Hsm]`; BTree/Blackboard/Utility excluded |
| `PerKindTree_GroupsAssetsByRelPath` | Assets under `Assets/Blueprints/combat/Guard.bp.json` and `Assets/Blueprints/Patrol.bp.json` produce correct tree (folder "combat" → leaf "Guard.bp.json", root leaf "Patrol.bp.json"); leaf→asset mapping via `AssetForLeaf` returns correct assets; non-leaf node returns null; cross-kind exclusion verified |
| `Row_CarriesKindIconKey` | `RowIconKey(bpAsset) == "asset/blueprint"`, `RowIconKey(btreeAsset) == "asset/btree"`, `RowIconKey(hsmAsset) == "asset/hsm"` |
| `DoubleClick_RaisesAssetActivated_WithAsset` | `ActivateAsset` raises `AssetActivated` with exact asset; `SelectAsset` sets `Selection`; clear-by-null works; recording fake catalog confirms no Load/OpenDocument calls |

### Hrot.Editor.AiShared.Tests (Stability-filtered) — 908 passed, 0 failed

```
dotnet test Hrot.Editor.AiShared.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
Passed! - Failed: 0, Passed: 908, Skipped: 0, Total: 908, Duration: 5 s
```

### Fdp.Toolkits.Tests (Stability-filtered) — 1856 passed, 0 failed

```
dotnet test Fdp.Toolkits.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
Passed! - Failed: 0, Passed: 1856, Skipped: 0, Total: 1856, Duration: 30 s
```

### Hrot.SimHost.Tests (Stability-filtered) — 585 passed, 0 failed, 3 skipped

```
dotnet test Hrot.SimHost.Tests --filter "Stability!=Flaky&Stability!=Environment&Stability!=Broken"
Passed! - Failed: 0, Passed: 585, Skipped: 3, Total: 588, Duration: 12 s
```

The 3 skipped tests are pre-existing (`SimHostSubsystem_InitializeHeadless_DoesNotThrow`, `CgfSubsystem_InitializeHeadless_DoesNotThrow`, `OnLoad_RegistersFireInteractionEventTranslator`). No EqsModuleTests flake appeared in this run.

### Full solution build — 0 errors, 0 new warnings

```
dotnet build IOS-IG-SimHost.sln
20 Warning(s) — all pre-existing in other projects (xUnit2013, CS0618 obsolete, CS8601/CS8602 nullable)
0 Error(s)
```

## Developer Insights

- **FolderTreePicker + AssetRelPath integration worked cleanly.** The trie-based `Build` from BATCH-10 handles forward-slash relpaths perfectly. The `AssetRelPath.RelPath` normalization (backslash→forward slash, trim leading `/`) produces exactly the format `FolderTreePicker` expects.
- **FakeAsset pattern already established.** The `FakeAsset` class in `AssetCatalogTests.cs` provided the template — the test fakes mirror it exactly.
- **No DI registration needed.** Unlike previous batches that added services, this panel is a plain class created by its host — no DI changes required.
- **DrawContent is intentionally minimal.** It renders functional tabs and trees but uses text labels for icons rather than `IconWidgets` overloads. When `IconWidgets.IconButton(IconHandle, ...)` matures (MTB-P1-T2), the draw-side resolution can be upgraded without touching the logic model.
- **Edge case: duplicate relpaths.** If two assets somehow produce the same relpath (shouldn't happen with well-formed catalogs), the per-kind `Dictionary<string, IEditableAsset>` uses last-writer-wins. This matches the `AssetCatalog` dedup-by-AssetId pattern from BATCH-10.

## Known Issues

None. The implementation is self-contained and complete for MTB-P4-T3 scope.

## Suggested Commit Message

```
feat(main-toolbar): AssetBrowserPanel — tabs, per-kind tree, row icons, selection/activation (MTB-P4-T3)
```
