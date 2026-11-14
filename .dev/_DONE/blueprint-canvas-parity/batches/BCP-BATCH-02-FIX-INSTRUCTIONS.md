# BCP-BATCH-02-FIX: picker not drawn (stuck canvas) + hotkeys + window title + variable node
User testing surfaced several breaks. Root causes confirmed by code reading.

## Onboarding
1. `.dev/.guides/DEV-GUIDE_claude.md`; `.dev/_DONE/blueprint-canvas-parity/DESIGN.md` (projection-only still binds).
2. Specimen: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/DemoShell.cs` (per-frame loop), `HotkeyDispatcher.cs`, `FakeBlueprint/*`.
Use codebase-memory MCP; not search_code. GizmoMap.Contracts 0.2.2; don't touch Hrot.IG/DDS. Headless tests gated behind `ImGui.GetCurrentContext() != Zero`.

## CONFIRMED ROOT CAUSE (unifies most symptoms): the picker overlay is never drawn
`DemoShell.cs:149-150` calls `_host.PickerRegistry_.DrawFrame()` EVERY frame. The integration NEVER calls `DrawFrame()`. So when a picker is opened — via TAB (`CanvasInput.cs:99-118` sets `Mode = PickerOpen` + `Pickers.Open("nodes.all", …)`) or wire-drop-to-empty (`CanvasInput.cs:1121-1215`) — the picker window is never rendered and never closes. The interaction `Mode` stays `PickerOpen`/`PendingWire`, so `HandleIdle` (which owns RMB-pan, wire-drag-start, TAB, context-menu, delete) never runs again → the canvas goes unresponsive. This is why:
- TAB "does nothing" (it opens an invisible picker, then sticks).
- wire-drop-to-empty shows no picker.
- SampleWiredDemo (you can drag from its pins) goes unresponsive after a wire attempt opens the invisible picker; empty-link recipes don't hit it as readily so they still pan.
Pan is **RMB-drag**, context menu is **RMB**, wire-drag is **LMB-on-pin** — all in `HandleIdle`, all dead once Mode is stuck.

## Tasks (in order)

### Task 1 — Draw the picker every frame (THE key fix) + pump hotkeys
In `Hrot/Editor/Hrot.Editor.AiShared/Windows/AiGraphCanvasWindow.cs` `DrawClientArea` (after `_renderer.Render(...)`, gated behind ImGui-available):
- Call the picker registry's `DrawFrame()` once per frame. `PickerRegistry.DrawFrame()` is concrete (`NodeEditor.UI/Picker/PickerRegistry.cs:83`); verify whether `IPickerRegistry` exposes it — if not, either add `DrawFrame()` to `IPickerRegistry` (cleanest) or have `AiEditorAdapterBundle` hold the concrete `PickerRegistry` and expose a `DrawPickerFrame()` hook. The picker registry instance is the one in `AiEditorAdapterBundle.PickerRegistry` (shared) — the window must be able to reach it (inject it into `AiGraphCanvasWindow`, or carry a `DrawPicker` delegate on `AiCanvasContext`).
- **Hotkeys:** the demo's `HotkeyDispatcher` (input + IEditorCommands → invoke on key chord) lives in the *demo* project. Add an equivalent in `Hrot.Editor.AiShared` (small: iterate the `IEditorCommands` registrations, check each command's key binding against the host `IInputSource`, invoke when pressed) OR if `NodeEditor.UI` already has a dispatcher, use it. Pump it each frame with `ActiveContext.Commands` + the host `IInputSource`. This makes Ctrl+F (find) and other command shortcuts fire. (TAB is handled inside `CanvasInput`, so Task 1's DrawFrame already makes the TAB picker visible.)
Apply to all three perspectives (the window is shared logic).
**Tests:** headless — a fake `IPickerRegistry`/spy asserts `DrawFrame` is called once per `DrawClientArea` when ImGui is available; hotkey dispatcher invokes the bound command when the input source reports the chord (e.g. Ctrl+F → find command). Gate ImGui.

### Task 2 — Canvas window title shows the active asset name (S5)
`ManagedWindow.Title` is settable and the internal name uses `"{Title}###{Id}"` (stable id). In `AiGraphCanvasWindow.DrawClientArea`, when the active document changes, set `Title = $"{assetName} — {_assetKind}"` (asset name from `ActiveDocument.Name` / `ActiveContext`). Keep the stable `###id` so docking is preserved. Empty state when no doc.
**Test:** after activating a doc, the window Title contains the asset name; id unchanged.

### Task 3 — Variable Get/Set node create-path + value pin (S3, S3b)
The My-Blueprint variable-drag create-path currently yields a node with exec in+out and no data pin (a Get should be PURE: data-out "Value" only; Set: exec in/out + data "Value"). Find the drag-create handler (BlueprintMyBlueprintModel/Window or an EditorSubsystem drop handler) and fix it to create a real `GetVariableNode`/`SetVariableNode` (set `VariableId`) so `NodePinSchema` projects the correct pins. Verify `NodePinSchema` Get = data-out only (no exec), Set = exec in/out + typed data "Value"; the Value type comes from the variable. Also implement the My Blueprint **`+` create-variable** command (currently a no-op from BATCH-13's faked create) so a new `VariableDecl` is added to the asset and appears in the panel.
**Tests:** drag-create a Get for a `System.Single` var → node has a single `Value` data-output pin of type `System.Single`, NO exec pins; Set → exec in/out + `Value` data-input of the var type. `+` create-variable adds a `VariableDecl` to the asset (assert it appears in `BlueprintMyBlueprintModel` Variables section).

## Out of scope (separate engine batch) — DO NOT attempt here
- **Non-zoomable/ugly fonts (S4):** the engine ImGui atlas bakes a single font size; the canvas needs several sizes for `IEditorTheme.GetFontForSize` to scale with zoom. This is an engine font-atlas change (FDP/Engine ImGui setup) — leave for a dedicated batch. Note it in the report.

## Success Criteria
- [ ] TAB opens a VISIBLE add-node picker; wire-drop-to-empty opens the by-pin picker; canvas stays responsive (RMB pan, RMB context menu, LMB wire-drag) in wired assets incl. SampleWiredDemo; Ctrl+F opens find.
- [ ] Canvas window title shows the active asset name.
- [ ] Variable drag creates a correct Get/Set node with its typed value pin; My Blueprint `+` creates a variable.
- [ ] Byte-stability + compiler golden unchanged. Build 0 errors / 0 warnings; GizmoMap.Contracts 0.2.2.
- [ ] Green: `Hrot.Blueprints.Tests` (no new failures beyond the 10 DEBT-006; flaky sub-80ns perf re-run isolated), `Hrot.Editor.AiShared.Tests`, `Hrot.BTree.Editor.Tests`, `Hrot.Hsm.Editor.Tests`, `EditorSubsystemBoot` filter.
- [ ] Report at `.dev/_DONE/blueprint-canvas-parity/reports/BCP-BATCH-02-FIX-REPORT.md`.

## Execution rules
- Task 1 first (it unsticks the canvas and is the highest-impact). Run suites yourself; assert real behavior (DrawFrame called; command invoked on chord; node pin shape/type; title contains name). Never fake a pass.
- Reuse `PickerRegistry.DrawFrame`, the existing commands/FindBar from BATCH-02, `NodePinSchema`, `ManagedWindow.Title`. Verify member signatures first.
- Projection-only stays mandatory. Keep changes additive + symmetric across the three perspectives where shared.

## Report
Document: where DrawFrame + the hotkey pump are called and how the picker registry is reached from the window; the title mechanism; the variable create-path + `+` handler fixes; confirmation the canvas is no longer stuck; actual test counts; build 0/0; byte-stability + golden status; suggested commit message. No comprehension questions.
