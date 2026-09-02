# BATCH-05 Report

## Implementation Summary

**Batch scope:** MTB-P2-T1, MTB-P2-T2, MTB-P2-T3 — Shell command set + menu/toolbar binding adapters.

### T1 — Shell `EditorCommandsImpl` holder (MTB-P2-T1)
Created `ShellEditorCommands` — a thin holder class in `Fdp.Presentation.WindowManager` that wraps an `EditorCommandsImpl` instance. It implements `IEditorCommands` and exposes `Register(descriptor, action)` so subsystems can register shell-level commands at startup. Exposed via `WindowManager.ShellCommands` property alongside `MainToolbar`, `StatusBar`, and `GlobalMenu`.

**Files:**
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ShellEditorCommands.cs` (NEW)
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` (+ ShellCommands property)
- `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/ShellCommandsTests.cs` (NEW, 5 tests)

### T2 — Menu-binding adapter (MTB-P2-T2)
Created `MenuCommandAdapter` — a static adapter that bridges `IEditorCommands` → `GlobalMenuRegistry`. Given a command id and menu path, it registers the appropriate menu item type (plain `RegisterItem` for non-checkable commands, `RegisterCheckableItem` for checkable ones). Added optional `Shortcut` and `GetEnabled` fields to `MenuItemNode` for shortcut text display and enabled/disabled greying — backward-compatible (null by default). Updated `RenderGlobalMenu` to use these fields.

**Files:**
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/MenuCommandAdapter.cs` (NEW)
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/GlobalMenuRegistry.cs` (+ Shortcut, GetEnabled on MenuItemNode)
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/WindowManager.cs` (RenderGlobalMenu updated)
- `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/MenuCommandAdapterTests.cs` (NEW, 6 tests)

### T3 — Toolbar-binding adapter (MTB-P2-T3)
Created `ToolbarCommandAdapter` — a static adapter that bridges `IEditorCommands` → `MainToolbarManager`. Registers a toolbar entry whose render delegate resolves the command's `IconKey` via `IIconProvider`, draws via `IconWidgets` (using the IconHandle-based overloads from BATCH-03), sets tooltip from `DisplayName`/`Description`/shortcut, and invokes the command on click. Falls back to text button when icon is missing (no throw). Extracted `GetState()` → `ToolbarCommandState` as the headless-testable seam (pure computation of `IsEnabled`, `IsToggled`, `OnClick`).

**Files:**
- `FDP/Engine/Fdp.Presentation/ImGui/WindowManager/ToolbarCommandAdapter.cs` (NEW)
- `FDP/Engine/Fdp.Presentation.Tests/ImGui/WindowManager/ToolbarCommandAdapterTests.cs` (NEW, 7 tests)

## Design Decisions

### Shell command set placement
**Chosen:** Thin holder in `Fdp.Presentation.WindowManager` (engine layer), exposed as `WindowManager.ShellCommands`.

**Why:** `WindowManager` already hosts all shell-level UI services (`GlobalMenu`, `StatusBar`, `MainToolbar`). It's the natural home — subsystems that register toolbar entries and menu items also register shell commands through the same `WindowManager` instance. `Fdp.Presentation` already references `NodeEditor.Core` (where `EditorCommandsImpl` lives), so there's no new dependency. This is simpler than threading a separate service through the editor composition root, and keeps all the §6 "Editor Action System Integration" infrastructure in one place.

**Alternative considered:** Placing in `EditorSubsystem`/`EditorApplication` (Hrot.Editor). Rejected because it would require the engine-layer adapters (`Fdp.Presentation`) to reach up into the editor layer, inverting the dependency direction.

### MenuItemNode additions (Shortcut, GetEnabled)
Added two nullable fields to `MenuItemNode`: `string? Shortcut` and `Func<bool>? GetEnabled`. Both default to `null`, so all existing code and tests are unaffected. The menu rendering (`RenderGlobalMenu`) was updated to pass these to `Gui.MenuItem()` overloads with `shortcut` and `enabled` parameters. This is the minimal change needed for shortcut display and disabled-item greying.

### Toolbar adapter headless seam
Extracted `ToolbarCommandAdapter.GetState(IEditorCommands, string)` → `ToolbarCommandState` as a pure function with no ImGui dependency. This enables headless testing of the click→invoke wiring, enabled/toggled state tracking, and disabled-noop behavior without needing an ImGui frame. The render delegate (which does use ImGui) calls into the same underlying descriptor delegates re-read each frame.

## Deviations

### T1: Added `NotifyAvailabilityChanged` and `AvailabilityChanged` event passthrough
`ShellEditorCommands` passes through `AvailabilityChanged` events and exposes `NotifyAvailabilityChanged`. This is not explicitly called out in the batch task description but is present on `EditorCommandsImpl` and is part of the `IEditorCommands` contract (§6). Subsystems may need to signal state changes; omitting this would be a regression vs. the underlying impl.

### T3: Text-button fallback for missing icon
The batch mentions `MissingIcon_FallsBackToText_NoThrow` as a test condition. Implemented a fallback path: when `IIconProvider.TryGet` returns false, the render delegate draws a `Gui.Button` with the `DisplayName` text instead. For checkable commands, uses `Gui.MenuItem` with the toggle state. This ensures the toolbar entry is always visible even when icon assets are missing.

### T3: `ToolbarCommandState` uses `Action?` for disabled
When a command is disabled (`IsEnabled() == false`), `OnClick` is `null` rather than a no-op action. This makes the disabled state more explicit in tests and avoids accidental invocations.

## Test Results

### New tests (all pass unfiltered, 0 failures)

**ShellCommandsTests** (5 tests):
- `RegisteredCommand_IsReturnedByGetAndAll` — asserts `Get(id)` returns the descriptor and `All` contains it
- `Invoke_CallsHandler_WhenEnabled` — handler runs, `result.Success` is true
- `Invoke_NoOp_WhenDisabled` — handler does NOT run, `result.Success` is false
- `Invoke_UnknownCommand_ReturnsFailure` — non-existent id → `result.Success` false
- `AvailabilityChanged_Fires_OnNotify` — event fires with correct id

**MenuCommandAdapterTests** (6 tests):
- `RegistersItem_AtPath_OnClickInvokesCommand` — `leaf.OnClick` invokes the recording fake command
- `Checkable_ReflectsIsChecked` — `leaf.GetCheckedState()` tracks live `IsChecked` changes
- `Disabled_ItemNotInvoked_WhenIsEnabledFalse` — `OnClick` does NOT invoke when disabled; works when re-enabled
- `Shortcut_IsSet_FromDefaultKey` — "Ctrl+S" shortcut stored from `KeyBinding`
- `GetEnabled_Tracks_IsEnabled` — `leaf.GetEnabled()` reflects live `IsEnabled` changes
- `Register_UnknownCommand_ThrowsInvalidOperationException` — correct exception

**ToolbarCommandAdapterTests** (7 tests):
- `Click_InvokesCommand` — `state.OnClick()` calls the recording fake
- `Enabled_And_Toggled_TrackDescriptor` — live state changes (enabled→disabled→enabled, toggled) reflected in `ToolbarCommandState`
- `Disabled_StateHasNoClickAction` — `OnClick` is null and command not invoked when disabled
- `MissingIcon_FallsBackToText_NoThrow` — registration succeeds with missing icon provider; entry visible in plan
- `Register_CreatesVisibleEntry` — entry appears for matching perspective, hidden for non-matching
- `Register_UnknownCommand_ThrowsInvalidOperationException` — correct exception
- `State_WithNoChecked_HasIsToggledFalse` — `IsChecked == null` → `IsToggled == false`

**Total: 18 new tests, 0 failures.**

### Existing suite results

| Suite | Tests | Failed | Filter |
|---|---|---|---|
| NodeEditor.Core.Tests | 181 | 0 | Stability filter |
| Hrot.Editor.AiShared.Tests | 885 | 0 | Stability filter |
| Fdp.Presentation.Tests (WindowManager) | 114 | 0 | Namespace + Stability filter |
| Fdp.Presentation.Tests (full) | ~19 pre-existing failures + Vis2D deadlock | N/A | Known issue — NOT caused by this batch |

**Note on Fdp.Presentation.Tests full suite:** The full `Fdp.Presentation.Tests` suite has ~19 pre-existing failures in Vis2D, EntityInspector, and EventBrowser tests, and can DEADLOCK when Vis2D NRE tests run together (as documented in the batch instructions). This batch does NOT touch these failures. The WindowManager namespace tests (114 total including all new BATCH-05 tests) pass cleanly.

## Developer Insights

### Issues encountered and resolved
1. **`IconHandle` is a readonly struct** — initial test code used property-initializer syntax (`new() { TextureId = ... }`) which fails. Fixed by using the constructor: `new IconHandle(textureId, width, height, uv0, uv1)`.
2. **`KeyModifiers` is in `NodeEditor.Primitives`** — the test needed an additional `using NodeEditor.Primitives;` to reference `KeyModifiers.Ctrl`.
3. **`Assert.Null` doesn't accept a message parameter** in this Xunit version. Removed message strings.
4. **C# `out var` in short-circuit `&&`** — the compiler doesn't track definite assignment through `&&`. Fixed by pre-declaring `IconHandle iconHandle = default;` and using `out iconHandle`.

### Weak points / improvement opportunities
- `MenuItemNode` could benefit from being made immutable or at least having a builder pattern — the mutable trie nodes make it easy to accidentally leave inconsistent state.
- The `RenderGlobalMenu` method could be moved out of `WindowManager` into the `GlobalMenuRegistry` itself for better testability.
- The `ToolbarCommandAdapter.RenderEntry` method has an ImGui dependency that can't be fully unit-tested headlessly — only the state computation is tested. The full render path requires the `ImGuiTestFixture`.

### Edge cases discovered
- When a command has `IsChecked` but no icon, the toolbar falls back to a `Gui.MenuItem` checkable (text-based). This is correct but visually different from the icon-based toggle.
- When `IsChecked` is null (non-checkable command) and there's no icon, the disabled state uses a simple `Gui.Button` with click suppression — the button still renders visually normal, just ignores clicks. A better approach would be to push a disabled style color, but that's an ImGui detail beyond the scope.

### Performance observations
- State is re-read every frame (immediate mode) — no caching. This is per-design (§6.2). The delegates (`IsEnabled`, `IsChecked`) are expected to be trivial (field reads or simple conditions), so the overhead is negligible.
- The `ToolbarCommandAdapter` calls `IIconProvider.TryGet` every frame even when the icon is unchanged. For commands with many toolbar entries this could be a micro-optimization candidate, but `TryGet` is a dictionary lookup and is cheap.

## Known Issues
- The toolbar render delegate's text-button fallback for disabled non-checkable commands renders a normal-looking button that ignores clicks. Ideally ImGui would grey it out. This is cosmetic and can be addressed in a follow-up.
- The `Fdp.Presentation.Tests` suite has ~19 pre-existing Vis2D/EntityInspector/EventBrowser failures — not touched by this batch.

## Suggested Commit Message
```
feat(main-toolbar): shell command set + menu/toolbar binding adapters (MTB-P2-T1,T2,T3)

- Add ShellEditorCommands holder (Fdp.Presentation) exposed via WindowManager
- Add MenuCommandAdapter: IEditorCommands → GlobalMenuRegistry
- Add ToolbarCommandAdapter: IEditorCommands → MainToolbarManager
- Extend MenuItemNode with Shortcut/GetEnabled (backward-compatible)
- Update RenderGlobalMenu to use shortcut+enabled fields
- 18 new tests passing
```
