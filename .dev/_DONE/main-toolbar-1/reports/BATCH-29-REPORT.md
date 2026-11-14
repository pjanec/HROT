# BATCH-29 REPORT: Wire editor Open-Asset picker through NodeEdit's Tree picker (retire AssetPickerModal path)

**Batch:** BATCH-29  
**Tasks:** MTB-P8-T3  
**Date:** 2026-06-12  
**Status:** ✅ COMPLETE  

---

## Summary

Implemented `AssetPickerLauncher` (testable wiring glue in `Hrot.Editor/Browser`), wired all 4 Open-Asset entry points through a dedicated `_shellPickers` `PickerRegistry` via `OpenPicker` (Tree layout) → `AssetPickActionRouter.Route`, and retired the `AssetPickerModal` production path. 5 exact named tests pass; all router + guardrail tests green; Hrot.Blueprints.Tests at 7 PRE-1 failures (down from 9 — 2 intermittent debug tests passed, zero new breakage).

---

## Task 3.1 — `AssetPickerLauncher` (new file)

**File:** `Hrot/Subsystems/Hrot.Editor/Browser/AssetPickerLauncher.cs`  
**Namespace:** `Hrot.Editor`

The launcher encapsulates "build `AssetPickerSource` → build Tree `PickerRequest` → open → route". Key design:

- **Injected `openPicker` seam** (`Action<PickerRequest, Action<PickerResult>>`) — in production, wired to `PickerRegistry.OpenPicker`; in tests, a fake that captures the request and lets the test simulate a result. This avoids ImGui/live-registry dependencies in unit tests. Matches **DEC-15**: uses the entry-driven `OpenPicker` path (not `registry.Open(sourceKey)`, which would discard `Category`/`IconKey`).

- **Constructor null-guards:** `openPicker`, `catalog`, `router` all throw `ArgumentNullException` on null.

- **`baseFolderResolver` defaults to null** → `AssetPickerSource` handles the default (`AssetBrowserPanel.BaseFolderFor`) internally. This avoids an `InternalsVisibleTo` dependency.

- **`Open(AssetKindFilter, Action<IEditableAsset?>?)`:**
  1. Constructs `new AssetPickerSource(catalog, kinds, baseFolderResolver, describe)`
  2. Builds `PickerRequest` with `ContextKey = $"assets.open.{kinds}"`, `Title = "Open Asset"`, `Layout = Tree`, `SelectionMode = Single`, `ItemsProvider = () => source.BuildEntries("", null)`
  3. Calls `openPicker(request, result => ...)`
  4. On cancel → nothing; on confirm → `onPicked ?? router.Route`

## Task 3.2 — EditorSubsystem wiring (edits only)

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

### Changes made:

| # | Location | Change |
|---|----------|--------|
| 1 | ~L360 (field) | Removed `_assetPickerModal` field; added `_shellPickers` (`PickerRegistry?`) |
| 2 | ~L1834 (DrawUI) | Replaced `_assetPickerModal?.DrawModal()` with `_shellPickers?.DrawFrame()` |
| 3 | ~L2450-2460 (RegisterWindows) | Replaced `_assetPickerModal = new AssetPickerModal(...)` with `_shellPickers = new PickerRegistry()` + `.SetServices(icons, theme)` + null-safe `assetPickerLauncher` construction |
| 4 | ~L2473-2482 (ScenarioMenuCommands openPicker) | Replaced `_assetPickerModal?.Open(options, callback)` with `assetPickerLauncher?.Open(kinds, callback)` — preserves `ScenarioMenuCommands` signature |
| 5 | ~L2955-2966 (shell.openAsset handler) | Replaced `_assetPickerModal?.Open(options, picked => router.Route(picked))` with `assetPickerLauncher?.Open(AssetKindFilter.All)` |

### Dedicated `_shellPickers` rationale

The `adapterBundle.PickerRegistry` is already `DrawFrame()`-ed once per frame by each `AiGraphCanvasWindow` (in-canvas node/var/type pickers). Reusing it for the global Open-Asset picker would cause **double-`DrawFrame`** on the same registry instance — each canvas window calls `DrawFrame()` in its own render scope, and a top-level `DrawFrame()` on the same registry would conflict. A dedicated `_shellPickers` registry avoids this entirely.

### Null-safety

All wiring is null-safe: `_shellPickers?`, `assetPickerLauncher?.Open(...)`, and `_assetPickRouter != null` guard. A bare `new EditorSubsystem()` + `RegisterWindows()` does not throw (confirmed by toolbar guardrail).

### What was retired (and what was NOT)

**Removed:**
- `_assetPickerModal` field (was `AssetPickerModal?`)
- `_assetPickerModal = new AssetPickerModal(catalog, adapterBundle.IconProvider)` construction
- `_assetPickerModal?.DrawModal()` call

**NOT removed (per hard constraints):**
- `AssetPickerModal.cs` class file — untouched
- `AssetBrowserPanel` — untouched
- `AssetBrowserDockedWindow` / `_aiAssetBrowser` — untouched
- All existing tests — untouched
- `NodeEditor.*` files — untouched
- `AssetPickerSource.cs` — untouched
- `ScenarioMenuCommands.cs` — signature unchanged

---

## Test Results

### `AssetPickerLauncherTests` (5 new tests — all pass)
```
Passed: 5, Failed: 0, Skipped: 0
```
- `Open_BuildsTreeLayoutRequest_FromAssetSource` ✅
- `Open_Confirm_RoutesPickedAssetTag_ViaRouter` ✅
- `Open_Cancel_RoutesNothing` ✅
- `Open_WithOnPickedCallback_InvokesCallback_NotRouter` ✅
- `Open_ScenarioKinds_RequestQueriesOnlyScenarios` ✅

### `AssetPickActionRouterTests` (existing — all pass)
```
Passed: 9, Failed: 0, Skipped: 0
```

### `EditorSubsystem_RegisterWindows_*` guardrails (existing — all pass)
```
Passed: 12, Failed: 0, Skipped: 0
```
Including:
- `EditorSubsystem_RegisterWindows_PopulatesMainToolbar` (BATCH-24 guardrail)
- `EditorSubsystem_RegisterWindows_RegistersOpenAssetCommand` (BATCH-26 guardrail)
- `EditorSubsystem_RegisterWindows_OpenAssetMenuItem_UnderFile` (BATCH-26 guardrail)
- `EditorSubsystem_RegisterWindows_OpenAssetToolbarEntry_Exists` (BATCH-26 guardrail)

### `Hrot.Blueprints.Tests` (full suite, no filter)
```
Total: 1874, Passed: 1859, Failed: 7, Skipped: 8
```
7 pre-existing failures (down from 9 in BATCH-26 — the 2 debug E2E tests passed intermittently this run):
1. `AiPrimitive_EmitMatchesGoldenSource("MoveToAndFire")` — compiler golden mismatch (pre-existing)
2. `AiPrimitive_EmitMatchesGoldenSource("HasVisibleTarget")` — compiler golden mismatch (pre-existing)
3. `Stage8_PdbContainsEmbeddedSource` — PDB test (pre-existing)
4. `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb` — Roslyn compiler test (pre-existing)
5. `TickFrame_1000Frames_AllocatesZeroBytes` — allocation benchmark (pre-existing)
6. `MoveToAndFire_GeneratedSource_Snapshot` — snapshot mismatch (pre-existing)
7. `WhenNode_ZeroAllocOnHotPath` — allocation benchmark (pre-existing)

**Zero new failures.** The 2 tests that passed this run (`CF2_EndToEnd_DelayBreakpointPauses`, `SetBreakpoint_TriggersAutoInstrument_ThenPauses`) are known intermittent debug E2E tests unrelated to this batch.

---

## Build

- `Hrot.Editor` — 0 warnings, 0 errors
- `Hrot.Editor.Tests` — 0 warnings, 0 errors
- Run WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`

---

## Suggested Commit Message

```
feat(main-toolbar): AssetPickerLauncher + retire AssetPickerModal path (MTB-P8-T3)

- Add AssetPickerLauncher (Hrot.Editor/Browser) — testable glue that builds
  Tree-layout PickerRequest from AssetPickerSource.BuildEntries and routes
  picked assets through AssetPickActionRouter (or onPicked callback)
- Wire all 4 Open-Asset entry points (toolbar, File→Open Asset…, Ctrl+O,
  Scenario→Load) through dedicated _shellPickers PickerRegistry
- Retire AssetPickerModal production path (field + construction + DrawModal)
  — class file, AssetBrowserPanel, docked browser, tests remain
- 5 AssetPickerLauncherTests + 9 router tests + 12 guardrail tests green
- 7 PRE-1 Blueprints failures unchanged (2 intermittent debug tests passed)
```

Co-Authored-By: Claude <noreply@anthropic.com>
