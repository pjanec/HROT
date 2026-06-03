# BCP-BATCH-02-FIX Report — picker not drawn (stuck canvas) + hotkeys + window title + variable node

## Implementation Summary

### Task 1 — Draw the picker every frame (THE key fix) + pump hotkeys
**Root cause confirmed and fixed:** the integration never called `PickerRegistry.DrawFrame()`, so an opened picker (TAB add-node via `CanvasInput`, or wire-drop-to-empty) rendered invisibly and the interaction `Mode` stuck at `PickerOpen`/`PendingWire`, which dead-locked `HandleIdle` (RMB-pan, RMB-context-menu, LMB-wire-drag, TAB).

- **`IPickerRegistry.DrawFrame()` added to the interface** (`FDP/.../NodeEditor.Core/Interfaces/IPickerRegistry.cs`). `PickerRegistry.DrawFrame()` was already concrete and public, so it now satisfies the interface with no impl change. Four test stubs that implement `IPickerRegistry` directly got a `DrawFrame() {}` no-op (Blueprint/HSM/BTree host-services + breakpoint-wiring test stubs).
- **`AiGraphCanvasWindow.DrawClientArea`** (`Hrot/Editor/Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs`): after `_renderer.Render(...)`, gated behind `ImGui.GetCurrentContext() != Zero`, it now calls `_pickers.DrawFrame()` once per frame and pumps the hotkey dispatcher. The picker registry + host input source are **injected into the window ctor** (new optional `IPickerRegistry? pickers`, `IInputSource? input` params). Production passes `adapterBundle.PickerRegistry` and `adapterBundle.InputSource` from `EditorSubsystem.cs` (all three perspectives — BTree/HSM/Blueprint — since the window is shared logic).
- **Hotkey pump:** new `EditorHotkeyDispatcher` in `Hrot.Editor.AiShared/Windows/EditorHotkeyDispatcher.cs` — mirrors the demo's `HotkeyDispatcher` (iterate `IEditorCommands.All`, match each command's `KeyBinding` against the host `IInputSource`, invoke on chord). The window pumps it each frame with `ActiveContext.Commands`, suppressed while `ImGui.GetIO().WantTextInput` is true so it does not steal keystrokes from text fields. Ctrl+F (`CommandCatalog.FindInGraph`, bound Ctrl+F in `CanvasCommands`) now fires.

**Where it's wired:** `EditorSubsystem.cs` ~line 1744-1771 (the three `new AiGraphCanvasWindow(...)` calls now pass `pickers:` and `input:`). The picker registry instance is the shared `AiEditorAdapterBundle.PickerRegistry`.

### Task 2 — Window title = active asset name
`AiGraphCanvasWindow.UpdateTitle(doc)` runs each `DrawClientArea` and, only when the active document reference changes, sets `ManagedWindow.Title` to `"{ActiveDocument.Asset.Name} — {assetKind}"`. Empty state restores the base `"{assetKind} Canvas"` title. `ManagedWindow` forms the ImGui name as `"{Title}###{Id}"`; the stable `Id` (`ai_canvas_{kind}`) is untouched, so docking identity is preserved across title changes.

### Task 3 — Variable Get/Set create-path + value pin + My Blueprint `+`
- **Create-path fix** (`BlueprintCommandSink.CreateAssetNode`): the My-Blueprint variable-drag path (`CanvasRenderer.PlaceVariableNode`) emits kind ids `"Util.GetVar"`/`"Util.SetVar"`. These are not in the Blueprint palette registry, so they previously fell through to a generic `FunctionCallNode` (exec in/out, no data pin). `CreateAssetNode` now recognizes these kind ids (plus `Variable.Get/Set`, `Blueprint.GetVariable/SetVariable` aliases) and creates a real `GetVariableNode`/`SetVariableNode`, applying the `VariableId` property. `NodePinSchema` (already correct) then projects: **Get = pure, single data-out `Value`, no exec; Set = exec in/out + typed data `Value`** (type resolved from the declared variable via the asset).
- **My Blueprint `+` create-variable** (`BlueprintDocumentFactory`): added `RegisterCreateVariableCommand` which registers `editor.create-variable` (`CommandCatalog.CreateVariable`) to append a new `VariableDecl` (unique name, `System.Boolean` default type) to `BlueprintAsset.Variables` and mark the doc dirty. It's registered in `Build()`. **Critical wiring fix:** `EditorSubsystem`'s `ActiveChanged` handler previously passed a *fresh empty* `EditorCommandsImpl()` to `BlueprintMyBlueprintWindow.Retarget`, so the panel's "+ Variable" hit nothing — now it passes the document's real `ctx.Commands`.

## Design Decisions
- **Picker `DrawFrame` on the interface** (the spec's "cleanest" option) rather than exposing the concrete registry — keeps `Hrot.Editor.AiShared` free of a `NodeEditor.UI` dependency.
- **New ctor params are optional/nullable** so the existing `AiGraphCanvasWindow` tests (which don't exercise pickers) keep compiling unchanged; production always supplies them.
- **`EditorHotkeyDispatcher` is pure** (input + commands only); the ImGui `WantTextInput` gate lives in the window. This makes the dispatcher headlessly testable.
- **`SimulateDrawClientArea` internal test hook** runs the non-ImGui portion of the frame (title update + picker DrawFrame + hotkey pump) so the behavior is verifiable headlessly, mirroring the existing `SimulateFocus` pattern.
- **Create-variable default type `System.Boolean`** — a sensible neutral default; user retypes in the Variables panel (matches the demo's modal-driven create which also picks a type afterward).

## Deviations
- None of substance. The only structural choice beyond the literal spec: I added the hotkey dispatcher as a standalone class (`EditorHotkeyDispatcher`) instead of inlining it into the window, for testability. WHAT: separate class; WHY: pure/headless-testable; BENEFIT: real behavioral test of chord→invoke; RISK: none (window owns the only instance).

## Test Results
- **`Hrot.Editor.AiShared.Tests`**: 760 passed, 0 failed (includes 10 new `BcpBatch02FixCanvasTests`: picker DrawFrame counted once per frame; no pump without active doc; hotkey invokes on matching chord; not on differing modifiers; null-commands no-op; Ctrl+F find fires through the window; suppressed while typing; title reflects asset name; stable id preserved; empty-state title).
- **`Hrot.Blueprints.Tests`**: 1081 passed, **10 failed**, 8 skipped (excl. perf class). The 10 failures are the **pre-existing DEBT-006 golden/snapshot/allocation failures** (InstanceEmitGolden ×3, AiPrimitiveEmitGolden ×2, LibraryEmitGolden, ConditionSummaryAttachment, AllocationFree, LibraryMathDemo, MoveToAndFireDemo snapshots) — identical to the set documented in `BCP-BATCH-02-REPORT.md`. **0 new failures.** Includes 4 new Task-3 tests (Get→pure value-out; Set→exec+typed value-in; `+`→VariableDecl appears in `BlueprintMyBlueprintModel`; `+`×2→unique names).
- **Byte-stability + JSON round-trip**: 79 passed, 0 failed (compiler golden / `.bp.json` serialization unchanged).
- **Flaky perf** `WhenNodePerfTests.ReadEqsResultNode_Under80ns` (isolated): passed.
- **`Hrot.BTree.Editor.Tests`**: 382 passed, 0 failed.
- **`Hrot.Hsm.Editor.Tests`**: 333 passed, 0 failed.
- **`Hrot.ClusterRunner.Integration.Tests --filter EditorSubsystemBoot`**: 10 passed, 0 failed (confirms the production canvas-window wiring boots).
- **`dotnet build IOS-IG-SimHost.sln`**: **0 errors.** 4 warnings, all in two projects I did not touch (`Hrot.Utility.Editor.Tests` xUnit2013 style, `Hrot.Diagnostics.Breakpoints.Tests` CS0618 obsolete-API) — pre-existing, unrelated to this batch. Every project I modified (`Hrot.Editor.AiShared`, `Hrot.Blueprints.Editor`, `NodeEditor.Core`, `NodeEditor.UI`, `Hrot.Editor`, and the touched test projects) builds at 0 warnings.

## Developer Insights
- The canvas-stuck symptom was a single missing per-frame call; the demo's `DemoShell.Frame` proved it (`_host.PickerRegistry_.DrawFrame()` at line 150). The fix is small and high-leverage.
- `NodePinSchema` already had correct Get/Set projection logic and tests — the real defect was purely in the *create-path* (kind-id → node-type mapping) and in the My-Blueprint command being handed an empty command set. Two narrow, additive fixes.
- The `EditorSubsystem.ActiveChanged` handler instantiating a throwaway `EditorCommandsImpl()` for the My Blueprint window was a latent bug that would have silently broken *every* My-Blueprint `+`/context command, not just create-variable.

## Known Issues
- **S4 (font/zoom) NOT addressed** — out of scope per instructions. The engine ImGui atlas bakes a single font size; `IEditorTheme.GetFontForSize` needs several baked sizes to scale with zoom. This is a dedicated FDP/Engine font-atlas batch.
- Create-variable uses a fixed `System.Boolean` default type (no inline type picker in the headless/managed path yet); the Variables panel can retype it.

## Suggested Commit Message
fix(blueprint-canvas): draw picker + pump hotkeys per frame, asset-name window title, real variable Get/Set create-path + My Blueprint create-variable (BCP-BATCH-02-FIX)
