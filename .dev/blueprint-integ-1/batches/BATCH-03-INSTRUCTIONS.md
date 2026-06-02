# BATCH-03: Global Asset Browser + perspective scaffolding
**Tasks:** AIE-013, AIE-014   **Phase:** 1   **Est:** ~11h
**Dependencies:** BATCH-02 (`AiDocumentManager`, `AiAssetCatalogBuilder`).

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE_claude.md` — working contract.
2. `.dev/blueprint-integ-1/DESIGN.md` §4.1, §4.2, §4.4 — perspectives, per-perspective window instances, the global Asset Browser with Open-docs.
3. `.dev/blueprint-integ-1/TASK-DETAIL.md` AIE-013, AIE-014 — authoritative success conditions.
4. `.dev/blueprint-integ-1/reviews/BATCH-02-REVIEW.md` — context.

Use the **codebase-memory MCP** first (project `D-Work-IOS-IG-SimHost-FDP-2`); not `search_code`.

**Scope guard:** Do **NOT** modify `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — the composition rewrite that *uses* these is AIE-015 / BATCH-04. Build these as standalone, headlessly-testable pieces. Do NOT build the graph canvas window or kind-specific panels (My Blueprint, HSM Globals) — those are Phase 2/4.

## Key facts (verified)
- `WindowManager` (`FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs`): perspectives are emergent from `ManagedWindow.OwningPerspective` (grouped for the switcher); `SwitchPerspective(name)`, `OnPerspectiveChanged(old,new)`, `FocusWindow(id)`, `RegisterWindow(win)`. Visibility: `Global || IsPinned || OwningPerspective==current`. Dock layout is per `###Id` (so per-perspective docking needs **distinct ids per perspective**).
- `ManagedWindow` (`.../ManagedWindow.cs`): ctor `(id, title, owningPerspective, scope)`; `WindowScope.{Global,PerspectiveBound}`; `IsVolatile` auto-unregisters on close.
- AiShared windows hardcode id + `"Authoring"`: `AssetBrowserWindow` (`"ai_asset_browser"`, ctor `(EditorSelectionStore, IAssetCatalog, IRefactorService, FindResultsWindow, ILiveSessionProvider)`), `InspectorWindow` (`"ai_inspector"`), `RuntimeInspectorWindow`, `TraceTimelineWindow`, `FindResultsWindow`, `BlackboardAuthoringWindow`, `DiagnosticsWindow` — all in `Hrot/Editor/Hrot.Editor.AiShared/Windows/`. `SharedAiWindowRegistrar` registers one set under `"Authoring"`.
- `AiDocumentManager` / `IPerspectiveSwitcher` (`Hrot/Editor/Hrot.Editor.AiShared/Documents/`, from BATCH-02): `Open/Activate/Close`, `ActiveChanged`, injected perspective switch.
- Tests: `Hrot/Editor/Hrot.Editor.AiShared.Tests/`. Many existing window tests construct windows headlessly — keep them green.

## Tasks (in order)

### Task 1: Parameterize shared windows for per-perspective instances + global Asset Browser with Open-docs (AIE-013) — files: `Hrot/Editor/Hrot.Editor.AiShared/Windows/AssetBrowserWindow.cs` (UPDATE) + the other AiShared windows that must exist per-perspective (UPDATE ctors)
1. **AssetBrowserWindow → Global + Open-docs.** Change its scope to `WindowScope.Global` (single shared instance per DESIGN decision #3). Add an `AiDocumentManager` dependency. Render an **"Open" section** above the catalog: one row per open document (display name, kind tag, `*` if dirty, `[×]` close) with the active row marked; clicking a row → `AiDocumentManager.Activate(doc)`; `[×]` → `Close`. Catalog section: double-click → `AiDocumentManager.Open(asset)` (or focus if already open). Keep the existing catalog/refactor/find behavior. Keep it headlessly testable (gate ImGui calls; expose the interaction logic so tests can drive Open/Activate/Close without a UI — follow the pattern in existing `AssetBrowserWindowTests`).
2. **Per-perspective parameterization.** For the windows that must appear once **per perspective** (Inspector, RuntimeInspector, TraceTimeline, FindResults, BlackboardAuthoring, Diagnostics), add **optional ctor parameters** `string? idOverride = null, string? owningPerspective = null` (default to the current hardcoded id + `"Authoring"` so existing callers/tests are unaffected). The id override must produce a distinct `###Id` per perspective (e.g. `ai_inspector_btree`). This enables AIE-014 to create one instance per perspective with independent docking.
**Tests required:** `AssetBrowser_IsGlobalScope`; `AssetBrowser_OpenSection_ListsOpenDocs_WithActiveMarker_AndDirty` (over a fake/real `AiDocumentManager`); `AssetBrowser_DoubleClickCatalog_CallsOpen`; `AssetBrowser_ClickOpenRow_CallsActivate`; `AssetBrowser_CloseButton_CallsClose`; `SharedWindow_IdOverride_ProducesDistinctId` (e.g. two `InspectorWindow`s with different overrides have different `Id`/`OwningPerspective`). Existing `AssetBrowserWindowTests`/`InspectorWindowTests`/etc. stay green.

### Task 2: PerspectiveWorkspaceRegistrar + active-asset→perspective (AIE-014) — files: `Hrot/Editor/Hrot.Editor.AiShared/Windows/PerspectiveWorkspaceRegistrar.cs` (NEW) + `Hrot/Editor/Hrot.Editor.AiShared/Documents/WindowManagerPerspectiveSwitcher.cs` (NEW)
1. **`PerspectiveWorkspaceRegistrar`**: given a perspective name (`"BTree"`/`"HSM"`/`"Blueprint"`) + the shared services (selection store for that perspective, catalog, refactor, find, debug-session accessors, adapter bundle) + a `WindowManager`, construct and `RegisterWindow` that perspective's **side-panel** instances (Inspector, RuntimeInspector, TraceTimeline, FindResults, BlackboardAuthoring, Diagnostics) using the AIE-013 id/perspective overrides so each gets a distinct `###Id` bound to `OwningPerspective`. **Do not** register a canvas or kind-specific panels yet — expose a documented extension seam (e.g. a `RegisterExtraWindow(ManagedWindow)` hook or virtual) for Phase 2/4 to add the canvas / My Blueprint / Globals. Provide a way to enumerate the windows it registered (for tests).
2. **`WindowManagerPerspectiveSwitcher : IPerspectiveSwitcher`**: wraps `WindowManager.SwitchPerspective(kind)` (+ optional focus). Wire `WindowManager.OnPerspectiveChanged` so that switching to a perspective with open docs of that kind focuses/activates the most-recent one via `AiDocumentManager` (and is a no-op-safe empty state when none open).
**Tests required:** `PerspectiveRegistrar_RegistersWindows_WithOwningPerspectiveAndDistinctIds` (all registered windows have `OwningPerspective==kind` and unique ids; three registrars over a shared `WindowManager` → 3× distinct id sets); `WindowManagerPerspectiveSwitcher_Switch_CallsWindowManagerSwitchPerspective`; `PerspectiveSwitch_WithOpenDocOfKind_ActivatesMostRecent` (use a real `AiDocumentManager` + a fake/real `WindowManager`); `PerspectiveSwitch_NoDocOfKind_NoThrow`.

## Success Criteria
- [ ] AIE-013, AIE-014 implemented per TASK-DETAIL success conditions.
- [ ] `Hrot.Editor.AiShared.Tests` fully green (new + existing); `EditorSubsystem.cs` unchanged.
- [ ] No warnings; public APIs documented; no leftover TODO/debug code.
- [ ] Report at `.dev/blueprint-integ-1/reports/BATCH-03-REPORT.md`.

## Execution rules
- Tasks in sequence; don't start Task 2 until Task 1 impl + tests are done and ALL tests pass.
- Run `dotnet test Hrot/Editor/Hrot.Editor.AiShared.Tests/...` yourself; fix root causes; never fake a pass. Tests assert real behavior (Open/Activate/Close invoked, distinct ids, perspective switch invoked), not existence.

## Report Requirements
In `reports/BATCH-03-REPORT.md`: issues & fixes; the id/perspective parameterization approach + any window ctors changed; the extension-seam shape for Phase 2/4 canvas registration; edge cases; actual test counts; suggested commit message. No comprehension questions.
