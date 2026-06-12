# BATCH-29: Wire the editor Open-Asset picker through NodeEdit's Tree picker (retire AssetPickerModal path)

**Batch Number:** BATCH-29
**Tasks:** MTB-P8-T3 (Wire the editor Open-Asset picker through `IPickerRegistry`)
**Phase:** Phase 8 — Asset Picker UX via NodeEdit's picker (Tree layout)
**Estimated Effort:** ~7h
**Priority:** HIGH (final Phase-8 task)
**Dependencies:** BATCH-27 (MTB-P8-T1), BATCH-28 (MTB-P8-T2 — `AssetPickerSource.BuildEntries`) — both merged.

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Engineering rules (MUST follow):** `.dev/.guides/DEV-GUIDE.md`
2. **Design doc:** `.dev/main-toolbar-1/ASSET-PICKER-UX-DESIGN.md` — "Editor integration", **Decisions D3/D4**.
3. **Task definition:** `.dev/main-toolbar-1/TASK-DETAIL.md` → **`MTB-P8-T3`** (acceptance bar).
4. **Decision context:** `.dev/main-toolbar-1/DEBT-TRACKER.md` → **DEC-15** (why we open via the entry-driven
   `PickerRegistry.OpenPicker(PickerRequest)` path, NOT `registry.Open(sourceKey)`).

### Source Code Location (all paths relative to repo root)
- **NEW launcher (testable glue):** `Hrot/Subsystems/Hrot.Editor/Browser/AssetPickerLauncher.cs`
- **Router (reuse):** `Hrot/Subsystems/Hrot.Editor/Browser/AssetPickActionRouter.cs`
- **Asset source (reuse — `BuildEntries`/`ToEntry`):** `Hrot/Editor/Hrot.Editor.AiShared/Browser/AssetPickerSource.cs`
- **Entry-driven picker API:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Picker/PickerRegistry.cs`
  (`OpenPicker(PickerRequest, Action<PickerResult>)`, `DrawFrame()`, `SetServices(icons, theme)`),
  `PickerRequest.cs`, `PickerResult.cs`.
- **Editor wiring (the big file):** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
  - `_assetPickerModal` field (~L360), construction (~L2455), `DrawModal()` call (~L1834),
    `shell.openAsset` command handler (~L2943–2966), `ScenarioMenuCommands.Register(openPicker: …)`
    (~L2467–2482), toolbar/menu registration (~L2975, ~L3016).
  - `adapterBundle` (`AiEditorAdapterBundle`, ~L2447) exposes `.PickerRegistry` (shared with canvas
    windows — **do NOT reuse it for the global picker**, see Task 3.2), `.IconProvider`, `.EditorTheme`.
  - `_assetPickRouter` (`AssetPickActionRouter`, ~L1976) — reuse as the pick action.
- **ScenarioMenuCommands (do not change its signature):** `Hrot/Subsystems/Hrot.Editor/ScenarioMenuCommands.cs`
  — `openPicker` is `Action<AssetKindFilter, Action<IEditableAsset?>>`.
- **Test projects:** `Hrot/Subsystems/Hrot.Editor.Tests/` (router tests:
  `Browser/AssetPickActionRouterTests.cs`; toolbar guardrail: `EditorSubsystem_RegisterWindows_PopulatesMainToolbar`).

### Report Submission
When done, write `.dev/main-toolbar-1/reports/BATCH-29-REPORT.md`.

---

## Context

T1 gave NodeEdit's Tree picker icons/highlight/folders; T2 gave `AssetPickerSource` (catalog → `PickerEntry`
via `BuildEntries`). This batch routes the editor's Open-Asset entry points through NodeEdit's **entry-driven**
picker (`PickerRegistry.OpenPicker` — Tree layout) and retires the `AssetPickerModal` production path. The
**docked `AssetBrowserPanel` browser is left untouched**.

**Entry points (4 surfaces, 2 logical openers):**
1. Toolbar "Open Asset" button (`shell.openAsset`, leftmost) — Kinds=All
2. File → Open Asset… menu item (`shell.openAsset`) — Kinds=All
3. Ctrl+O hotkey (`shell.openAsset` `DefaultKey`) — Kinds=All
4. Scenario → Load (`ScenarioMenuCommands` `openPicker`) — Kinds=Scenario

All pick → `AssetPickActionRouter.Route` (file → `AiDocumentManager.Open`, scenario →
`IEditorLogic.LoadScenarioByName`).

---

## ✅ Tasks

### Task 3.1 — `AssetPickerLauncher` (testable wiring glue)

**File (NEW):** `Hrot/Subsystems/Hrot.Editor/Browser/AssetPickerLauncher.cs`
**Namespace:** `Hrot.Editor` (match `AssetPickActionRouter`)

Encapsulates "build an `AssetPickerSource` for the given kinds → build a Tree `PickerRequest` from
`source.BuildEntries` → open it → route the picked asset". The **open** is an injected delegate seam so the
launcher is unit-testable without ImGui / a live registry.

```csharp
public sealed class AssetPickerLauncher
{
    // openPicker seam == PickerRegistry.OpenPicker (production); a fake in tests.
    public AssetPickerLauncher(
        Action<PickerRequest, Action<PickerResult>> openPicker,
        IAssetCatalog catalog,
        AssetPickActionRouter router,
        Func<AssetKind, string?>? baseFolderResolver = null,   // null → AssetBrowserPanel.BaseFolderFor
        Func<IEditableAsset, string?>? describe = null);        // recipe metadata for preview; null ok

    /// Open the Tree picker for the given kinds. When the user confirms, the picked asset is
    /// routed: onPicked if supplied, else router.Route. Cancel → nothing.
    public void Open(AssetKindFilter kinds, Action<IEditableAsset?>? onPicked = null);
}
```

`Open` must:
- Construct `new AssetPickerSource(catalog, kinds, baseFolderResolver, describe)`.
- Build a `PickerRequest`:
  - `ContextKey = $"assets.open.{kinds}"`, `Title = "Open Asset"`, `Layout = PickerLayout.Tree`,
    `SelectionMode = PickerSelectionMode.Single`,
    `ItemsProvider = () => source.BuildEntries("", null)`.
- Call `openPicker(request, result => { ... })` where the result handler:
  - if `result.Cancelled` → do nothing;
  - else if `result.First?.Tag is IEditableAsset asset` →
    `(onPicked ?? router.Route)(asset)`  (i.e. when `onPicked` is null, call `router.Route(asset)`).
- Be null-safe (throw `ArgumentNullException` on null `openPicker`/`catalog`/`router` in the ctor).

### Task 3.2 — Wire the entry points in `EditorSubsystem` (dedicated shell registry; retire modal path)

**File:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` (UPDATE)

1. **Dedicated shell picker registry (avoids double-`DrawFrame`).** The `adapterBundle.PickerRegistry` is
   already `DrawFrame()`-ed once per frame by each `AiGraphCanvasWindow` (in-canvas node/var/type pickers).
   Do NOT reuse it for the global Open-Asset picker. Instead add a field
   `private NodeEditor.UI.Picker.PickerRegistry? _shellPickers;` and in `RegisterWindows` (right after
   `adapterBundle` is built):
   ```csharp
   _shellPickers = new NodeEditor.UI.Picker.PickerRegistry();
   _shellPickers.SetServices(adapterBundle.IconProvider, adapterBundle.EditorTheme);
   ```
2. **Construct the launcher** (after `_assetPickRouter` is set; reuse `catalog` + `_assetPickRouter`):
   ```csharp
   var assetPickerLauncher = new Hrot.Editor.AssetPickerLauncher(
       openPicker: _shellPickers.OpenPicker,
       catalog:    catalog,
       router:     _assetPickRouter);
   ```
   (Guard for null `_assetPickRouter` as the surrounding code does; keep all wiring null-safe so a bare
   `new EditorSubsystem()` `RegisterWindows` does not throw.)
3. **`shell.openAsset` handler** (~L2953): replace the `_assetPickerModal?.Open(…)` body with
   `assetPickerLauncher.Open(AssetKindFilter.All);` (router.Route is the default pick action).
4. **Scenario → Load** (`ScenarioMenuCommands.Register` `openPicker:` lambda, ~L2472): replace the
   `_assetPickerModal?.Open(…)` body with `assetPickerLauncher.Open(kinds, callback);` — the
   `ScenarioMenuCommands` callback (`Action<IEditableAsset?>`) is passed as `onPicked` so the existing
   scenario-load contract is preserved (it routes scenario → load). Do NOT change `ScenarioMenuCommands`'s
   signature.
5. **Per-frame draw** (~L1834): replace `_assetPickerModal?.DrawModal();` with `_shellPickers?.DrawFrame();`
   at the same top-level DrawUI location.
6. **Retire the `AssetPickerModal` production path:** remove the `_assetPickerModal` field (~L360) and its
   construction (~L2455), now that nothing uses it. **Do NOT delete** the `AssetPickerModal.cs` class file,
   `AssetBrowserPanel`, the docked `AssetBrowserDockedWindow`/`_aiAssetBrowser`, or any of their tests
   (ORCH §5 — no deletions outside Phase-7's named items; retiring the *path* = removing the wiring only).

**Untouched:** the docked `AssetBrowserPanel` browser (`_aiAssetBrowser`, registered ~L2443), the in-canvas
pickers (`adapterBundle.PickerRegistry` + `AiGraphCanvasWindow.DrawFrame`), toolbar/menu/Ctrl+O registration
(they already point at `shell.openAsset`), Save-As / New-Asset dialogs.

---

## 🔄 MANDATORY WORKFLOW
1. Task 3.1 → build `Hrot.Editor` → write + run `AssetPickerLauncherTests` → all pass ✅
2. Task 3.2 → build the editor library → existing `AssetPickActionRouterTests` + the toolbar guardrail still
   pass ✅
3. Run the relevant suites green (see Definition of done). WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS`.

No asking permission for obvious steps; finish until green; no stubbing.

---

## 🧪 Tests Required (exact names — acceptance bar)

**File (NEW):** `Hrot/Subsystems/Hrot.Editor.Tests/Browser/AssetPickerLauncherTests.cs`
Use a fake `IAssetCatalog` + fake `IEditableAsset` (copy the `StubAsset` pattern from
`AssetPickActionRouterTests.cs`) and a **fake openPicker** delegate that captures the `PickerRequest` and
lets the test invoke its `Action<PickerResult>` with a crafted result.

- `Open_BuildsTreeLayoutRequest_FromAssetSource` — calls `launcher.Open(AssetKindFilter.All)`; assert the
  captured `PickerRequest.Layout == PickerLayout.Tree`, `SelectionMode == Single`, and
  `ItemsProvider()` yields one `PickerEntry` per catalog asset with `Tag` == the asset and a non-null
  `IconKey` (the source projection ran).
- `Open_Confirm_RoutesPickedAssetTag_ViaRouter` — craft a `PickerResult` containing a `PickerEntry` whose
  `Tag` is a file-kind asset; invoke the captured handler; assert the router's `openDocument` fired with
  **that asset** (use a router built with spy delegates). `loadScenario` not called.
- `Open_Cancel_RoutesNothing` — invoke the handler with a cancelled `PickerResult` (empty selection);
  assert neither `openDocument` nor `loadScenario` fired.
- `Open_WithOnPickedCallback_InvokesCallback_NotRouter` — pass an `onPicked` spy to `Open`; confirm →
  assert `onPicked` got the asset and the router was NOT called (the scenario-load contract path).
- `Open_ScenarioKinds_RequestQueriesOnlyScenarios` — catalog has mixed kinds; `Open(AssetKindFilter.Scenario)`;
  assert `ItemsProvider()` yields only `Scenario`-kind entries (the source was built with the scenario filter).

> Assert **actual values** (request fields, entry Tag identity, which router delegate fired). No `[Skip]`,
> no asserting only that a mock was configured.

**Keep green (do not weaken):** `AssetPickActionRouterTests` (all), and the toolbar guardrail
`EditorSubsystem_RegisterWindows_PopulatesMainToolbar` (the rewire must not break main-toolbar population or
throw on a bare `EditorSubsystem`).

---

## 🎯 Success Criteria (from TASK-DETAIL MTB-P8-T3)
- [ ] `AssetPickerLauncher` opens a **Tree-layout** `PickerRequest` from `AssetPickerSource.BuildEntries`;
      pick → `AssetPickActionRouter.Route` (or the supplied `onPicked`); cancel → nothing.
- [ ] All four entry points (toolbar / File menu / Ctrl+O = All; Scenario→Load = Scenario) go through the
      launcher; `shell.openAsset` + toolbar + menu + Ctrl+O still registered (BATCH-26 wiring preserved).
- [ ] A **dedicated** `_shellPickers` registry is `DrawFrame()`-ed once per frame at the top-level DrawUI
      (replacing the modal's `DrawModal`); the in-canvas `adapterBundle.PickerRegistry` is untouched.
- [ ] The `AssetPickerModal` **production path is removed** (field + construction + DrawModal call); the
      class file, `AssetBrowserPanel`, docked browser, and their tests remain (no deletions).
- [ ] Build green; `AssetPickerLauncherTests` + `AssetPickActionRouterTests` + toolbar guardrail pass;
      `Hrot.Blueprints.Tests` (Stability filter) stays at exactly the established 9 PRE-1 failures (no new
      breakage).

---

## ⚠️ Hard Constraints
- **NodeEdit untouched** (T1 already provided everything). Do NOT modify any `NodeEditor.*` file or
  `AssetPickerSource`/`AssetBrowserPanel`/`AssetPickerModal`.
- **No deletions** beyond removing the `_assetPickerModal` field + its construction + its DrawModal call in
  `EditorSubsystem` (these are recently-added wiring, not legacy). The `AssetPickerModal` class + tests +
  docked browser stay.
- **No scope creep** — only `AssetPickerLauncher.cs`, the named `EditorSubsystem.cs` edits, and the test
  file. Keep `ScenarioMenuCommands` signature unchanged. Keep all wiring null-safe (bare `EditorSubsystem`
  `RegisterWindows` must not throw).
- Zero new warnings; no `TODO`/`NotImplementedException` on covered paths; no test weakening.

---

## 📊 Report Requirements (`reports/BATCH-29-REPORT.md`)
- The `AssetPickerLauncher` shape + the injected `openPicker` seam (why — testability + avoids the
  `registry.Open` Category/icon loss, DEC-15).
- Why a **dedicated** `_shellPickers` registry (double-`DrawFrame` avoidance vs canvas windows).
- Exactly what was removed for the modal retirement (and confirmation the class/tests/docked browser remain).
- Test-run summaries: `AssetPickerLauncherTests`, `AssetPickActionRouterTests`, toolbar guardrail,
  `Hrot.Blueprints.Tests` (Stability) at 9 PRE-1.
- Note any environmental build issues (e.g. running-editor file locks) explicitly and re-run.
- Suggested commit message.

If something cannot be done as specified, **stop and report why** rather than stubbing it.
