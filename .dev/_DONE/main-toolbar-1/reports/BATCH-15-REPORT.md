# BATCH-15 Report

## Implementation Summary

Built two generic, side-effect-free hosts for the `AssetBrowserPanel` as specified in §10.3:

**MTB-P5-T3 — `AssetPickerModal`** (`Hrot.Editor.AiShared/Browser/AssetPickerModal.cs`):
A modal popup that hosts an `AssetBrowserPanel` and returns the user's pick via a callback.
- `Open(options, callback)` creates the panel, subscribes to `AssetActivated`.
- `HandleActivated(asset)` → close the modal + invoke `callback(asset)` (exactly once per open).
- `HandleCancel()` → close the modal + invoke `callback(null)` (exactly once per open).
- `DrawModal()` renders via ImGui `BeginPopupModal`; handles Esc → cancel and X-button → cancel.
- `Close()` discards the pending callback without invoking it.
- Double-callback guard: `_callbackInvoked` flag prevents the callback from firing more than once per `Open`.
- Re-opening replaces the previous callback without invoking it.
- Open/activate/cancel logic is separated from ImGui draw → fully testable headlessly.

**MTB-P5-T4 — `AssetBrowserDockedWindow`** (`Hrot.Editor.AiShared/Browser/AssetBrowserDockedWindow.cs`):
A `ManagedWindow` subclass hosting the same `AssetBrowserPanel` in a permanent docked window.
- Extends `ManagedWindow(id, title, perspective, scope)`.
- Creates the panel in the constructor; `DrawClientArea()` delegates to `panel.DrawContent()`.
- On `AssetActivated`, invokes the registrant's callback; the window **stays open**.
- Public constant `ExpectedId = "AssetBrowser"` — the stable, documented window identity.
- Default `WindowScope.Global` — the browser is a shared tool, not perspective-specific. Documented in XML doc.
- Default `Title = "Asset Browser"`, perspective `"Authoring"`.
- Constructor accepts optional `id`/`title` overrides for edge-cases (e.g. a second browser instance).

## Design Decisions

1. **`AssetPickerModal.Open` replaces callback on re-open.** If `Open` is called while already open, the old panel is disposed and the new callback takes effect. The old callback is discarded without invocation. This avoids stale-callback bugs and is the natural UI expectation (opening a new picker means you want a new result).

2. **`Close()` vs `HandleCancel()`.** `Close()` is a programmatic close — it discards the callback. `HandleCancel()` is the user-facing Esc/X action — it invokes `callback(null)`. This separation gives callers the option to dismiss without side effects vs. report a null pick.

3. **`AssetBrowserDockedWindow` uses `WindowScope.Global`**. The browser is a shared workspace tool, not tied to any one perspective. `Global` scope means it persists across perspective switches and appears under the "Global" group in the Windows menu — the same pattern as the existing `AssetBrowserWindow`.

4. **`_callbackInvoked` guard on the modal.** Without this, a double-click that fires two `AssetActivated` events in a single frame could invoke the callback twice, leading to undefined behavior (e.g. two documents opened from one double-click). The flag is set before invoking the callback → `ClosePanel()` → safe.

5. **Test reflection for `AssetBrowserDockedWindow._panel`.** The panel is a private implementation detail, but tests need to simulate `AssetActivated`. The test assembly has `InternalsVisibleTo` from `Hrot.Editor.AiShared`. Reflection is used only because `_panel` is `private readonly`; this is the established pattern (same as how `ManagedWindowTests` tests `FocusRequested`).

## Deviations

None — implemented exactly to spec.

## Test Results

### New tests — unfiltered (must pass zero-failed)

```
Hrot.Editor.AiShared.Tests:
  AssetPickerModalTests:        14 passed, 0 failed
  AssetBrowserDockedWindowTests: 8 passed, 0 failed
  Total new:                    22 passed, 0 failed
```

**AssetPickerModalTests (14 tests):**
- `Constructor_NullCatalog_ThrowsArgumentNullException`
- `Constructor_NullIcons_ThrowsArgumentNullException`
- `Open_NullCallback_ThrowsArgumentNullException`
- `Open_SetsIsOpen_ToTrue`
- `Activate_ClosesAndInvokesCallback_WithAsset` — activates with asset → `IsOpen` false, callback received the exact asset, invoked exactly once
- `Escape_InvokesCallback_WithNull` — cancel → `IsOpen` false, callback received `null`, invoked exactly once
- `Callback_IsInvokedAtMostOnce_PerOpen` — activate twice + cancel without re-open → callback fires only once
- `Reopen_ReplacesCallback` — second `Open` replaces callback; first callback never invoked again; each callback fires exactly once
- `Close_DiscardsCallback_WithoutInvocation` — `Close()` does not invoke the callback
- `AfterClose_HandleMethods_AreNoOps` — after `Close()`, `HandleActivated`/`HandleCancel` are no-ops
- `NeverCalls_DocumentManager_Or_Load` — recording doc-manager fake never called through open/activate/cancel paths; proves zero side effects
- `Activate_ScenarioAsset_InvokesCallback_NoSideEffects` — scenario asset activates cleanly without side effects

**AssetBrowserDockedWindowTests (8 tests):**
- `Constructor_SetsExpectedId` — `Id == "AssetBrowser"`
- `Constructor_SetsExpectedTitle` — `Title == "Asset Browser"`
- `Constructor_SetsGlobalScope` — `Scope == WindowScope.Global`
- `Constructor_SetsOwningPerspective` — configurable perspective
- `Constructor_AllowsCustomIdAndTitle` — overridable id/title
- `Constructor_NullCallback_ThrowsArgumentNullException`
- `Registered_WithExpectedId_AndScope` — `WindowManager.RegisterWindow` then `TryGetWindow("AssetBrowser")` returns it with correct `Id`/`Scope`/`Title`
- `Activate_InvokesCallback_WindowStaysOpen` — simulate `AssetActivated` via panel → callback receives asset, `IsOpen` remains `true`
- `Activate_MultipleAssets_StaysOpenEachTime` — two consecutive activations both invoke callback, window never closes
- `Activate_AfterReopen_StillInvokesCallback` — close/re-open the window, activation still works

### Full suite — Stability-filtered (0-failed required)

```
Hrot.Editor.AiShared.Tests:  0 failed,  947 passed   (Stability!=Flaky&Stability!=Environment&Stability!=Broken)
Fdp.Toolkits.Tests:           0 failed, 1856 passed   (same filter)
Hrot.SimHost.Tests:           0 failed,  585 passed   (same filter, 3 skipped)
```

No `BLUEPRINT_REGENERATE_SNAPSHOTS` set. No new warnings (0 errors in full solution build). No flakes encountered (EqsModuleTests flake did not appear).

## Developer Insights

- **Fixed a bug during testing:** The initial `Close()` implementation called `ClosePanel()` but didn't clear `_callback`/`_callbackInvoked`, so `HandleActivated`/`HandleCancel` after `Close()` would still fire the callback. The `AfterClose_HandleMethods_AreNoOps` test caught this immediately. Fixed by also clearing the callback state in `Close()`.

- **No test infrastructure changes needed.** The `FakeCatalog`/`FakeIconProvider`/`FakeAsset` patterns from `AssetBrowserPanelTests` were reused verbatim. The `RecordingDocManager` is a novelty only for this batch — it exists purely to prove the modal has zero side-effect dependencies.

- **Headless testability works as designed.** The `HandleActivated`/`HandleCancel` internal seams make it trivial to test the modal's core logic without an ImGui context. This pattern (separate headless test seams from ImGui draw) should be the standard for all new UI components.

- **WindowManager construction for headless tests** uses `new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f))` — zero-config, works in any test.

## Known Issues

- The `AssetBrowserDockedWindow._panel` field is accessed via reflection in tests. If the field is renamed, tests will break. A future improvement could expose the panel via `internal` property or a test-specific accessor interface. This is low-priority since the field is unlikely to change.

- `WindowScope.Global` for the docked window means it appears under "Global" in the Windows menu, not under its owning perspective. This is intentional (browser is a shared tool), but the owning perspective `"Authoring"` is effectively unused. It's kept for window-setting persistence compatibility and to support a hypothetical future where different perspectives want independent browser instances.

## Suggested Commit Message

```
feat(main-toolbar): AssetPickerModal + AssetBrowserDockedWindow hosts (MTB-P5-T3, T4)

Add two generic, side-effect-free hosts for AssetBrowserPanel:
- AssetPickerModal: popup with Action<IEditableAsset?> callback,
  activate→close+callback(asset), Esc→callback(null), double-callback guard.
- AssetBrowserDockedWindow: ManagedWindow subclass, Id="AssetBrowser",
  WindowScope.Global, activate→callback, stays open.
Both headless-testable; 22 new tests; 0 failures across full suites.
```
