# BATCH-06 Report

## Implementation Summary
**Task:** MTB-P2-T4 — Save / Save-As / Save-All commands + Ctrl+S fix (Phase 2).

### 1. ShellSaveCommands registrar (`Hrot/Editor/Hrot.Editor.AiShared/Documents/ShellSaveCommands.cs`)
A static registrar class that registers three shell-level commands into any `Action<EditorCommandDescriptor, Action<EditorCommandContext>>` registration delegate:

- **`shell.save`** (Ctrl+S, `IsEnabled = Active != null`): When `Active.Asset.SourceFilePath` is empty, routes to `requestSaveAs(doc)`. Otherwise dispatches by `AssetKind` (Blueprint/BTree/Hsm) to the matching per-kind save delegate with the source path, then calls `doc.MarkClean()`. Unsupported kinds report a warning and skip (never throw).
- **`shell.saveAs`** (no default key, `IsEnabled = Active != null`): Invokes `requestSaveAs(manager.Active)`.
- **`shell.saveAll`** (Ctrl+Shift+S, `IsEnabled = any dirty open doc`): Delegates to `SaveAllAiDocumentsCommand.Execute(manager, saveBlueprint, saveBTree, saveHsm, report)`.

All decision logic is pure and testable with a fake `AiDocumentManager` + recording delegates.

### 2. Wiring into the composition root (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`)
- **Shell command registration** (~L2244–2266, inside `RegisterWindows`): `ShellSaveCommands.Register` is called with `windowManager.ShellCommands.Register` as the registration delegate, the existing per-kind save delegates (`saveBlueprintDelegate`, `saveBTreeDelegate`, `saveHsmDelegate` from PU-603), a `requestSaveAs` seam that reports "Save As not yet available" (DEC-9), and a status reporter.
- **Perspective-level hotkey pump** (~L1593–1608, at the top of `DrawUI`): An `EditorHotkeyDispatcher` backed by `ImGuiInputSource` pumps `ProcessThisFrame(WindowManager.ShellCommands)` once per frame, gated by `!ImGui.GetIO().WantTextInput` so hotkeys don't fire while typing in a text field. Fields `_shellInputSource` and `_shellHotkeyDispatcher` (~L340) hold the dispatcher.
- The existing document-scoped pump in `AiGraphCanvasWindow` is entirely undisturbed.

### 3. Inline Ctrl+S/Ctrl+Shift+S removal (`EditorSubsystem.cs`)
- **Removed** the focus-gated hotkey conditions at ~L1620–1624 (locals `isWindowFocused`, `ctrlDown`, `shiftDown`, `sPressed`) and the inline key-polling conditions in the "Save Blueprint" (~L1644) and "Save All" (~L1680) sections.
- The **Buttons remain** — Save Blueprint / Save All still fire on button click via `_blueprintSaveCallback` / `_saveAllCallback`.
- Now-unused locals were removed to avoid dead-code warnings (TreatWarningsAsErrors).

## Design Decisions
- **Registration delegate pattern**: `ShellSaveCommands.Register` takes an `Action<EditorCommandDescriptor, Action<EditorCommandContext>>` instead of `IEditorCommands`. This makes it trivially testable — tests supply a recording lambda that captures descriptors + handlers, production passes `windowManager.ShellCommands.Register`. This avoids the `CommandRegistration(EditorCommandsImpl)` constructor which doesn't accept `IEditorCommands`.
- **Perspective-level pump in `DrawUI`, not `Program.cs`**: The batch specified "likely in EditorSubsystem". `DrawUI` is called within the ImGui frame (via `orchestrator.DrawUIAll()` → `EditorSubsystem.DrawUI()`) and has access to both `_wm.ShellCommands` and `ImGui.GetIO().WantTextInput`. This keeps the editor-level concern self-contained.
- **Scenario save delegate = null**: `AssetKind.Scenario` does not yet exist in the codebase. The parameter is passed as `null` with a comment indicating Phase 6 wiring.
- **requestSaveAs reports via `_saveAllStatus`**: The seam writes to the existing status string field so it appears in the Blueprint Tools panel, giving the operator immediate feedback.

## Deviations
None intentional. The single `CommandRegistration(EditorCommandsImpl)` constraint was resolved by switching to a delegate-based registration pattern, which is more testable and follows the same DI principle used throughout the codebase (cf. `AiDocumentManager` accepting `Action<string>` instead of `IPerspectiveSwitcher`).

## Test Results

### New tests: `SaveCommandsTests` (5 tests, all pass unfiltered)
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 15 ms
```

| Test | What it verifies |
|------|-----------------|
| `Save_WithSourcePath_WritesActiveDocument` | Active Blueprint doc with source path → `saveBlueprint` called with path, `MarkClean()` invoked, `requestSaveAs` NOT called |
| `Save_EmptySourcePath_RoutesToSaveAs` | Active BTree doc with empty path → `requestSaveAs` called with that doc, per-kind delegate NOT called, doc stays dirty |
| `SaveAll_SavesEveryDirtyDocument` | 3 docs (2 dirty, 1 clean) → only dirty docs' delegates called, clean doc untouched, all dirty docs now clean |
| `Hotkey_CtrlS_InvokesSave_RegardlessOfFocusedWindow` | Ctrl+S → dispatcher invokes `shell.save` (per-kind delegate called, no report). Ctrl+Shift+S → dispatcher invokes `shell.saveAll` (per-kind delegate AND report called). Mirrors `BcpBatch02FixCanvasTests` fake-input pattern |
| `Save_NoActiveDocument_IsNoOp` | No open doc → Save/SaveAs handlers don't crash; `IsEnabled` returns false for Save/SaveAll |

### Hrot.Editor.AiShared.Tests (Stability filter)
```
Passed!  - Failed:     0, Passed:   890, Skipped:     0, Total:   890, Duration: 5 s
```

### Fdp.Toolkits.Tests (Stability filter)
```
Passed!  - Failed:     0, Passed:  1856, Skipped:     0, Total:  1856, Duration: 29 s
```

### Hrot.SimHost.Tests (Stability filter)
```
Failed!  - Failed:     1, Passed:   584, Skipped:     3, Total:   588, Duration: 12 s
```
**1 pre-existing, non-deterministic failure**: `CognitiveSpatialModule_ResolvesAreaQuery_ThroughSharedSnapshotProvider_ConvoyWithNavigationSolver` fails with `EditablePolyline not registered` — a test-suite-ordering issue in the SimHost test fixture (not caused by this batch). The same test passes in isolation. Root cause: the `EqsModuleTests` class constructor does not call `RegisterManagedComponent<EditablePolyline>()`, and a preceding test in the alphabetically-sorted run unregisters it. This affects multiple tests in the SimHost suite (see TEST-HEALTH.md lines 106, 122–133).

### Full solution build
```
Build succeeded.
    20 Warning(s)
    0 Error(s)
```
Zero new warnings from changed files. All 20 warnings are pre-existing (mostly in unrelated projects).

## Developer Insights
- **Issues encountered**: `CommandRegistration` requires `EditorCommandsImpl` not `IEditorCommands`, making the batch's "takes the command set" signature ambiguous. Resolved by accepting a registration delegate instead — this is actually cleaner for testing.
- **Weak points**: The `SaveAllAiDocumentsCommand.Execute` uses collision guards for BTree/HSM but the `shell.save` handler doesn't — single-doc saves bypass the collision check. This is by design (the original `SaveActiveBlueprintCommand` also doesn't collision-check), but worth flagging.
- **Edge cases**: When `Active` is null, both `shell.save` and `shell.saveAs` handlers are safe no-ops. `IsEnabled` returning `false` prevents UI invocation, but even direct `Invoke` won't crash.
- **Performance**: The hotkey dispatcher iterates all shell commands each frame (currently 3 save commands + whatever Phase 3+ adds). This is trivially fast — a few pointer comparisons per command.

## Known Issues
- **Save-As dialog not implemented** (DEC-9: Phase 6). The `requestSaveAs` seam reports "not yet available" to the status bar. No crash, no data loss.
- **Scenario save delegate = null**. When `AssetKind.Scenario` is added (Phase 5/6), the scenario save delegate will need wiring.
- **SimHost test suite has 1 pre-existing, non-deterministic failure** unrelated to this batch.

## Suggested Commit Message
```
feat(main-toolbar): ShellSaveCommands + perspective-level Ctrl+S/Ctrl+Shift+S hotkey dispatch (MTB-P2-T4)
```

Co-Authored-By: Claude <noreply@anthropic.com>
