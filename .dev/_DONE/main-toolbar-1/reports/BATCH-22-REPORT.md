# BATCH-22 Report

## Implementation Summary

### MTB-P7-T2: Delete `ScenarioBrowserPanel`

**Type deleted:**
- `Hrot/Subsystems/Hrot.Editor/UI/ScenarioBrowserPanel.cs`

**Test deleted:**
- `Hrot/Subsystems/Hrot.Editor.Tests/ScenarioBrowserPanelTests.cs`

**References removed:**
| File | Reference | Action |
|------|-----------|--------|
| `EditorSubsystem.cs:209` | `private ScenarioBrowserPanel? _browserPanel;` field | Removed |
| `EditorSubsystem.cs:1470` | `_browserPanel = new ScenarioBrowserPanel();` instantiation | Removed |
| `EditorSubsystem.cs:2916` | `windowManager.RegisterWindow(new EditorBrowserWindow(…))` registration | Removed |
| `EditorSubsystem.cs:63` | `using Hrot.Editor.UI;` | Removed then restored (other types in `Hrot.Editor.UI` still needed: `EditorToolbarPanel`, `EditorOrbatPanel`, `JsonEntityContextMenuHandler`) |
| `EditorWindows.cs:35-60` | `EditorBrowserWindow` class (field + ctor + DrawClientArea) | Removed entire class |
| `EditorWindows.cs` | `using Hrot.Editor.Migration;` | Removed (only used by deleted `EditorBrowserWindow`) |
| `EditorFileOpsIntegrationTests.cs:91-92` | `new ScenarioBrowserPanel().HandleNewClick(app)` | Replaced with `app.NewScenario()` direct call |
| `EditorFileOpsIntegrationTests.cs:9` | `using Hrot.Editor.UI;` | Removed |
| `EditorApplication.cs:71-72` | `<see cref="EditorBrowserWindow"/>` doc comment | Updated to remove dead cref |

**Not removed (scenario logic, not panel wiring):**
- `IEditorLogic` members (NewScenario, SaveCurrentScenario, LoadScenarioByName, etc.)
- `EditorApplication.NewScenario()`, `MigrationAlertManager`, `AlertManager` property
- All scenario shell commands added in BATCH-21

---

### MTB-P7-T4: Retire AiShared `AssetBrowserWindow`; register `AssetBrowserDockedWindow`

**Type deleted:**
- `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs`

**Test deleted:**
- `Hrot/Editor/Hrot.Editor.AiShared.Tests/Windows/AssetBrowserWindowTests.cs`

**References removed/replaced:**
| File | Reference | Action |
|------|-----------|--------|
| `SharedAiEditorServiceCollectionExtensions.cs:57` | `services.AddSingleton<AssetBrowserWindow>();` | Replaced with factory `AddSingleton<AssetBrowserDockedWindow>` |
| `SharedAiWindowRegistrar.cs:13/22-23/44` | `AssetBrowserWindow` field + ctor param + `RegisterWindow` | Replaced with `AssetBrowserDockedWindow` |
| `EditorSubsystem.cs:296` | `private AssetBrowserWindow? _aiAssetBrowser;` field | Changed to `AssetBrowserDockedWindow?` |
| `EditorSubsystem.cs:2050-2056` | `new AssetBrowserWindow(store, catalog, refactorService, findResults, liveProvider, documentManager)` | Replaced with `new AssetBrowserDockedWindow(catalog, icons, options, onAssetActivated)` |

**Docked-host registration (DBT-2 wiring):**
- `AssetBrowserDockedWindow` registered with `ExpectedId="AssetBrowser"`, `WindowScope.Global` — same identity the old window used.
- Activation callback wired to `_aiDocumentManager?.Open(asset)` — file-kind assets (Blueprint, BTree, Hsm) open as documents; the callback is null-safe (no-op if `_aiDocumentManager` is not yet set).
- `IIconProvider` default (`NoOpIconProvider`) registered via `TryAddSingleton` so the DI container resolves without external dependencies; the production host (`EditorSubsystem`) provides `SilkIconProvider` from `windowManager.Atlas`.
- `AssetBrowserDockedWindow.CustomToolbarDraw` property added to preserve the recipe modal toolbar injection point that the old `AssetBrowserWindow` supported.

**Tests updated:**
| Test | Change |
|------|--------|
| `AddSharedAiEditor_Resolves_AssetBrowserWindow_WithCorrectId` | Replaced with `AddSharedAiEditor_Resolves_AssetBrowserDockedWindow_WithExpectedId` (asserts `Id == AssetBrowserDockedWindow.ExpectedId`) |
| New: `AddSharedAiEditor_WithActivationCallback_OpensDocumentViaManager` | Registers with `docManager.Open` callback, activates asset via panel, asserts document opened in `AiDocumentManager` |
| New: `AddSharedAiEditor_WithNullCallback_DoesNotThrowOnActivation` | Registers with null callback, activates asset, asserts no throw |

## Design Decisions

- **`NoOpIconProvider` as DI default:** `IIconProvider` is required by `AssetBrowserDockedWindow` but was not previously registered in the AiShared DI. A no-op default is registered via `TryAddSingleton` so the DI resolves without hosts providing it; production hosts override with `SilkIconProvider`.

- **`AddSharedAiEditor` optional parameter:** The `onAssetActivated` callback is an optional parameter (defaults to `null` → no-op) so existing callers (tests, subsystem registrations) do not break. The editor host passes `_aiDocumentManager?.Open` when calling `AddSharedAiEditor` — but since the host creates the `AssetBrowserDockedWindow` directly (not via DI), the DI parameter isn't used in production. It exists for testability and potential future DI-driven hosts.

- **`CustomToolbarDraw` retained on docked window:** The old `AssetBrowserWindow` exposed a `CustomToolbarDraw` property used to inject a "+ New from Recipe..." button. This property was added to `AssetBrowserDockedWindow` to preserve the existing recipe modal integration.

- **SilkIconProvider created early:** The `_aiAssetBrowser` now requires an `IIconProvider` at construction time. A `SilkIconProvider` is created from `windowManager.Atlas` at the asset browser creation point; the later `AiEditorAdapterBundle` creates its own instance (both share the same atlas — lightweight, no GPU calls).

## Deviations

None — all changes follow the batch specification exactly. The only "addition" is `CustomToolbarDraw` on `AssetBrowserDockedWindow`, which is required to preserve existing host behavior (recipe modal toolbar). This is a direct consequence of replacing `AssetBrowserWindow` with the docked host and does not constitute a spec deviation.

## Test Results

All suites run with Stability filter (`Stability!=Flaky&Stability!=Environment&Stability!=Broken`), WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`:

| Suite | Passed | Failed | Skipped | Notes |
|-------|--------|--------|---------|-------|
| `Hrot.Editor.AiShared.Tests` | 1014 | 0 | 0 | `AtomicMultiFileWriterTests.Write_to_invalid_path` flaked on first run (temp file race); passed on re-run |
| `Hrot.Editor.Tests` | 176 | 0 | 0 | — |
| `Fdp.Toolkits.Tests` | 1856 | 0 | 0 | — |
| `Hrot.SimHost.Tests` | 585 | 0 | 3 | `TeardownReplay_PreservesEntityRepositoryState` flaked on first run; passed on re-run |

**New/updated tests (unfiltered):**

| Test class | Passed | Failed | Notes |
|------------|--------|--------|-------|
| `SharedAiEditorDiTests` | 13 | 0 | Includes 2 new callback-wiring tests |
| `EditorFileOpsIntegrationTests` | 7 | 0 | `NewScenario_EmptiesRepo_AndResetsGlobalTime` now calls `app.NewScenario()` directly |

**Build:** 0 errors, 0 new warnings (pre-existing xUnit2013 and CS0618 warnings only).

## Developer Insights

- **Issues encountered:** The `using Hrot.Editor.UI;` was initially removed from both `EditorWindows.cs` and `EditorSubsystem.cs` because the only visible user was `ScenarioBrowserPanel`. However, `EditorToolbarPanel`, `EditorOrbatPanel`, and `JsonEntityContextMenuHandler` also live in that namespace. The build failure was caught and the using was restored (minus `using Hrot.Editor.Migration;` which genuinely had no remaining users). The lesson: always do a full grep for namespace usage before removing a using.

- **Pre-existing flakes:** Two tests flaked on first run and passed on re-run — `AtomicMultiFileWriterTests.Write_to_invalid_path_does_not_leave_temp_files_behind` (temp file cleanup race) and `LiveFromReplayTests.TeardownReplay_PreservesEntityRepositoryState`. Neither is in TEST-HEALTH.md; both are unrelated to this batch.

- **Docked-host callback wiring:** The `AssetBrowserDockedWindow` is now fully wired in the DI and registrar, but the production host (`EditorSubsystem`) creates it directly rather than resolving from DI (because `IIconProvider` requires `windowManager.Atlas` which is not available in DI). The DI registration is primarily for testability and subsystem-level resolution. A future improvement could move the icon provider into DI so the docked window can be fully DI-resolved.

## Known Issues

- The `AlertManager` property on `EditorApplication` is now unused (its only consumer was `EditorBrowserWindow`). It was intentionally left in place per the batch instruction "do NOT remove scenario LOGIC". If `MigrationAlertManager` functionality is re-added to a future docked window or menu, this property can be wired up.
- `AssetBrowserDockedWindow` is not resolved from DI in production — it's created directly in `EditorSubsystem`. The DI registration exists for testability but the production path bypasses it. This is a known pattern (same as the old `AssetBrowserWindow`), not a regression.

## Suggested Commit Message

```
feat(main-toolbar): retire ScenarioBrowserPanel + AiShared AssetBrowserWindow; register docked host (MTB-P7-T2, T4)

Delete ScenarioBrowserPanel and all UI/panel references (actions live in Scenario menu).
Delete Hrot.Editor.AiShared AssetBrowserWindow; register AssetBrowserDockedWindow 
(ExpectedId="AssetBrowser", Global) with activation callback wired to AiDocumentManager.Open.
Update DI/registrar/tests. DBT-2 docked-host wiring complete.
```

Co-Authored-By: Claude <noreply@anthropic.com>
