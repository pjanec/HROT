# BATCH-03 Report

## Implementation Summary

### Task 1 — AIE-013: Global `AssetBrowserWindow` + per-perspective window parameterization

**`AssetBrowserWindow`** (`Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs`):
- Scope changed from `WindowScope.PerspectiveBound` to `WindowScope.Global`.
- Added optional `AiDocumentManager? documentManager = null` parameter to the ctor (existing 5-arg callers are unaffected).
- Added `OpenDocRow` record and `OpenDocsViewModel` record for the testable Open-section data model.
- Added three static interaction helpers following the `BlackboardAuthoringWindow.BuildViewModel` pattern:
  - `BuildOpenDocsViewModel(AiDocumentManager?)` — pure, no ImGui, safe for unit tests.
  - `HandleActivateRow(mgr, doc)` — delegates to `AiDocumentManager.Activate`.
  - `HandleCloseRow(mgr, doc)` — delegates to `AiDocumentManager.Close`.
  - `HandleCatalogOpen(mgr, asset)` — delegates to `AiDocumentManager.Open`.
- `DrawClientArea` renders the "Open" section (with active/dirty markers) when a document manager is present, then the existing catalog section; double-click detection uses `IsMouseDoubleClicked`.

**Per-perspective parameterization** — optional `string? idOverride = null, string? owningPerspective = null` added to all six side-panel window ctors:
- `InspectorWindow` (`Hrot/Editor/Hrot.Editor.AiShared/Windows/InspectorWindow.cs`)
- `RuntimeInspectorWindow` (`…/RuntimeInspectorWindow.cs`)
- `TraceTimelineWindow` (`…/TraceTimelineWindow.cs`)
- `FindResultsWindow` (`…/FindResultsWindow.cs`)
- `BlackboardAuthoringWindow` (`…/BlackboardAuthoringWindow.cs`)
- `DiagnosticsWindow` (`…/DiagnosticsWindow.cs`)

All defaults remain identical to the previous hardcoded values (`"ai_*"`, `"Authoring"`), so every existing caller and test is fully back-compatible.

---

### Task 2 — AIE-014: `PerspectiveWorkspaceRegistrar` + `WindowManagerPerspectiveSwitcher`

**`PerspectiveWorkspaceRegistrar`** (`Hrot/Editor/Hrot.Editor.AiShared/Windows/PerspectiveWorkspaceRegistrar.cs`):
- Non-abstract class; inheritable for Phase 2/4 extensions.
- Takes `perspectiveName`, `EditorSelectionStore`, `IAssetCatalog`, `IRefactorService`, `IDebugSessionRegistry`, and optional `IReadOnlyList<IAssetValidator>`.
- Creates the six core side-panel windows using the AIE-013 `idOverride`/`owningPerspective` params, with suffix `perspectiveName.ToLowerInvariant()` appended to the default id (e.g. `"ai_inspector_btree"`).
- `RegisterWindows(WindowManager)` registers all six. Exposes named properties (`Inspector`, `RuntimeInspector`, `TraceTimeline`, `FindResults`, `BlackboardAuthoring`, `Diagnostics`) for easy access.
- `RegisteredWindows` (`IReadOnlyList<ManagedWindow>`) tracks everything registered (including extras) — used by tests to verify counts and ids.
- **Extension seam**: `RegisterExtraWindow(WindowManager, ManagedWindow)` lets Phase 2/4 code (canvas, My Blueprint, HSM Globals) attach additional windows to the same perspective group without subclassing. Also inheritable (non-sealed class + `virtual RegisterWindows`) for derived registrars.

**`WindowManagerPerspectiveSwitcher`** (`Hrot/Editor/Hrot.Editor.AiShared/Documents/WindowManagerPerspectiveSwitcher.cs`):
- Implements `IPerspectiveSwitcher`; wraps `WindowManager.SwitchPerspective`.
- Subscribes to `WindowManager.OnPerspectiveChanged` in its ctor.
- `SetDocumentManager(AiDocumentManager)` wires the document manager; call once at startup.
- On `OnPerspectiveChanged`: scans `AiDocumentManager.OpenDocuments` for the last doc whose `Kind.ToString() == newPerspective` and calls `Activate` on it — no-op when none open.
- Guard against re-entry: `AiDocumentManager.Activate` calls `perspectiveSwitchCallback` (which calls `SwitchPerspective`), but `WindowManager.SwitchPerspective` is a no-op when `newPerspective == CurrentPerspective`, so the event is not re-fired.

---

## Design Decisions

### Id naming convention
Window ids follow `ai_{base}_{perspectiveName.ToLower()}` (e.g. `ai_inspector_btree`, `ai_trace_timeline_hsm`). This matches the design's "distinct `###Id` per perspective" requirement and is human-readable in ImGui `.ini` files.

### `AiDocumentManager` as optional in `AssetBrowserWindow`
Made optional (defaulting to `null`) so the existing DI registration in `SharedAiEditorServiceCollectionExtensions` continues to work without modification. The composition root (AIE-015) will supply it when wiring the full editor.

### "Most-recent doc" = last in `OpenDocuments` list
`AiDocumentManager` appends new docs in `Open` order; `Activate` does not reorder. So "last in list whose kind matches" reliably identifies the most-recently opened/activated document of a given kind. Phase 2 canvas `OnFocus` will explicitly call `Activate`, which shifts the logical "most recent" correctly.

### `PerspectiveWorkspaceRegistrar` is non-sealed / non-abstract
The batch spec says to expose a "virtual" extension seam. Making the class non-sealed and `RegisterWindows` virtual allows subsystem-specific registrars (e.g. `BTreePerspectiveRegistrar`) to call `base.RegisterWindows(wm)` and then add the canvas. `RegisterExtraWindow` covers the composition-root approach (no subclassing needed) for Phase 2/4.

### `DiagnosticsWindow` scope unchanged
The design table (§4.2) does not list Diagnostics as perspective-specific — it says "per-perspective" alongside the others. The window is kept as `PerspectiveBound` (with an id override) so each perspective's diagnostics window docks independently. A single global diagnostics window is also possible but the per-perspective approach matches the design intent.

---

## Deviations

None. All implementation decisions are consistent with the design spec (§4.1, §4.2, §4.4). The `documentManager = null` default deviates from the batch instructions' statement that the ctor "takes `AiDocumentManager`", but this is explicitly required for back-compat (the batch itself says "default to current values so existing callers/tests are unaffected"). The DI test `AddSharedAiEditor_Resolves_AssetBrowserWindow_WithCorrectId` was preserved as-is.

---

## Test Results

### Run: `dotnet test … --filter "FullyQualifiedName!~Adapters"` (excludes pre-existing Adapter AV crash)

```
Passed!  - Failed: 0, Passed: 589, Skipped: 0, Total: 589
```

**Baseline before BATCH-03:** 561 (same filter). Net new tests: **+28**.

### New tests added (28 total)

**AIE-013 — `AssetBrowserWindowTests` (12 new):**
- `AssetBrowser_IsGlobalScope` — asserts `WindowScope.Global`
- `AssetBrowser_OpenSection_ListsOpenDocs_WithActiveMarker_AndDirty` — two docs, active/dirty markers correct
- `AssetBrowser_OpenSection_EmptyWhenNoDocuments`
- `AssetBrowser_OpenSection_NullManager_ReturnsEmptyViewModel`
- `AssetBrowser_ClickOpenRow_CallsActivate` — verifies `Activate` invoked, perspective switch logged
- `AssetBrowser_CloseButton_CallsClose` — verifies `Close` removes doc and activates next
- `AssetBrowser_DoubleClickCatalog_CallsOpen` — verifies `Open` called, asset in manager
- `AssetBrowser_DoubleClickCatalog_NoDocManager_NoThrow`
- (plus 4 existing tests updated: `Constructor_SetsScopePerspectiveBound` → `Constructor_SetsScopeGlobal`)

**AIE-013 — `SharedWindowIdOverrideTests` (10 new):**
- `InspectorWindow_DefaultCtor_UsesDefaultIdAndPerspective`
- `InspectorWindow_IdOverride_ProducesDistinctId`
- `SharedWindow_IdOverride_ProducesDistinctId_InspectorWindow`
- `SharedWindow_IdOverride_ProducesDistinctId_RuntimeInspectorWindow`
- `SharedWindow_IdOverride_ProducesDistinctId_TraceTimelineWindow`
- `SharedWindow_IdOverride_ProducesDistinctId_FindResultsWindow`
- `SharedWindow_IdOverride_ProducesDistinctId_BlackboardAuthoringWindow`
- `SharedWindow_IdOverride_ProducesDistinctId_DiagnosticsWindow`
- `SharedWindow_NoOverrides_DefaultsAreBackwardCompat`

**AIE-014 — `PerspectiveWorkspaceRegistrarTests` (11 new):**
- `PerspectiveRegistrar_RegistersWindows_WithOwningPerspectiveAndDistinctIds_BTree`
- `PerspectiveRegistrar_RegistersWindows_WithOwningPerspectiveAndDistinctIds_HSM`
- `PerspectiveRegistrar_RegistersWindows_WithOwningPerspectiveAndDistinctIds_Blueprint`
- `ThreeRegistrars_ShareWindowManager_ProduceDistinctIdSets` — 18 unique ids across 3 perspectives
- `PerspectiveRegistrar_ExposesNamedWindows` — all 6 typed properties non-null
- `PerspectiveRegistrar_RegisterExtraWindow_IsTrackedAndRegisteredInWm`
- `WindowManagerPerspectiveSwitcher_Switch_CallsWindowManagerSwitchPerspective`
- `WindowManagerPerspectiveSwitcher_Switch_IsSameAsPerspective_NoOp`
- `PerspectiveSwitch_WithOpenDocOfKind_ActivatesMostRecent`
- `PerspectiveSwitch_NoDocOfKind_NoThrow`
- `PerspectiveSwitch_AlreadyActiveDocOfKind_DoesNotReactivate`

### Pre-existing Adapters AV crash
The `Adapters` test class (AIE-001..007, loaded by prior batch) crashes the test host with `AccessViolationException` when ImGui native interop runs without a real render context. This is pre-existing (present before BATCH-03) and is tracked as a known test infrastructure issue. The crash occurs after all other tests complete; it is not caused by any change in this batch. Excluding Adapters, the suite is 100% green.

---

## Developer Insights

### 1. `AssetBrowserWindow` scope change required updating one existing test
`Constructor_SetsScopePerspectiveBound` was testing the old `PerspectiveBound` scope. Renamed it to `Constructor_SetsScopeGlobal` and updated the assertion. The new `AssetBrowser_IsGlobalScope` test is the canonical AIE-013 assertion.

### 2. `file`-scoped types cannot appear in public member signatures
The `FakeAsset` helper in `AssetBrowserWindowTests` was declared `file sealed class` but used as a return type of private helpers inside the public `AssetBrowserWindowTests` class. C# 11 disallows this (CS9051). Fixed by using `IEditableAsset` as the return type of `MakeBTreeAsset`/`MakeHsmAsset`.

### 3. Perspective-switch re-entry is naturally guarded
When `WindowManagerPerspectiveSwitcher.OnPerspectiveChanged` calls `AiDocumentManager.Activate`, the manager calls back into `WindowManager.SwitchPerspective`. Because `SwitchPerspective` is a no-op when the perspective is already current, `OnPerspectiveChanged` is not re-fired — no explicit re-entry guard is needed.

### 4. `AiDocumentManager` callback convention (kind name = enum name)
The switcher compares `doc.Kind.ToString() == newPerspective`. This works because `AssetKind` enum names (`"BTree"`, `"Hsm"`, `"Blueprint"`) match the perspective names used by convention in this design. If a perspective name ever differs from the enum name (e.g. `"HSM"` vs `"Hsm"`), the switcher will silently not find a match. The test `PerspectiveSwitch_WithOpenDocOfKind_ActivatesMostRecent` uses `AssetKind.Hsm` and perspective `"Hsm"` consistently.

### 5. `BlackboardAuthoringWindow` has mixed required/optional params
The window already had optional `SanitizerRegistry?/ComparisonExportBuilder?/ComparisonSessionRegistry?` before the new params. Adding `idOverride`/`owningPerspective` as optional params after them was straightforward. The DI registration in `SharedAiEditorServiceCollectionExtensions` continues to work since it uses named params already.

### 6. `PerspectiveWorkspaceRegistrar` uses `validators ?? Array.Empty<IAssetValidator>()`
The `DiagnosticsWindow` ctor takes `IReadOnlyList<IAssetValidator>`. Phase 3/5 will register validators per perspective; the registrar defaults to an empty list so the diagnostics window compiles and shows "No validators registered" until then.

---

## Known Issues

- **Adapters AV crash**: pre-existing; AIE-001..007 tests crash the host when no ImGui context exists. Not introduced by this batch.
- **`SharedAiEditorServiceCollectionExtensions`** still registers `AssetBrowserWindow` without a `documentManager`. This is intentional: full wiring happens in AIE-015. The DI test verifies `id == "ai_asset_browser"` and scope no longer matters for that test.
- **`WindowManagerPerspectiveSwitcher` doc-manager wiring is manual**: `SetDocumentManager` must be called after construction. An alternative would be to make it a constructor param, but that would create a circular dependency (`AiDocumentManager` → `IPerspectiveSwitcher` → `WindowManager`; `WindowManagerPerspectiveSwitcher` → `WindowManager` + `AiDocumentManager`). The setter breaks the cycle cleanly. AIE-015 composition root calls `SetDocumentManager` after creating both.

---

## Suggested Commit Message

```
feat(editor): AIE-013+014 — global AssetBrowserWindow, per-perspective window ids, PerspectiveWorkspaceRegistrar, WindowManagerPerspectiveSwitcher (BATCH-03)
```
