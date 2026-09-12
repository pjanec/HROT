# BCP-BATCH-02-FIX Review — picker not drawn (stuck canvas) + hotkeys + title + variable node
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
Fixes the user-reported canvas breakages. Root cause (confirmed in code): the integration never called `PickerRegistry.DrawFrame()` (demo does so every frame at `DemoShell.cs:149`), so an opened picker (TAB add-node / wire-drop-to-empty) was invisible and left the interaction `Mode` stuck → RMB-pan, RMB-context-menu, LMB-wire-drag (all in `HandleIdle`) died. This is why wired graphs (SampleWiredDemo) went unresponsive and TAB "did nothing".

- **Task 1:** `AiGraphCanvasWindow` now calls `_pickers.DrawFrame()` once per frame (ImGui-gated) + pumps a new `EditorHotkeyDispatcher` (with `ActiveContext.Commands` + host `IInputSource`), suppressed while `ImGui.GetIO().WantTextInput` so it won't steal typing. `IPickerRegistry.DrawFrame()` added; `EditorSubsystem` injects `adapterBundle.PickerRegistry`/`InputSource` into all three canvas windows. → unsticks canvas, makes TAB/wire-drop pickers visible, makes Ctrl+F fire.
- **Task 2:** Window title shows `"{asset name} — {kind}"` via `ManagedWindow.Title` with the stable `###id` preserved (docking intact).
- **Task 3:** `BlueprintCommandSink.CreateAssetNode` maps `Util.GetVar`/`Util.SetVar` (+ aliases) to real `GetVariableNode`/`SetVariableNode` (was falling through to `FunctionCallNode` → exec pins, no data). `NodePinSchema` projects Get = pure data-out `Value`, Set = exec in/out + typed `Value`. `BlueprintDocumentFactory.RegisterCreateVariableCommand` implements `editor.create-variable`; `EditorSubsystem` now passes the document's real `ctx.Commands` to the My Blueprint window (was an empty instance).

## Verification (ran myself)
- **`dotnet build IOS-IG-SimHost.sln` → 0 Warnings / 0 Errors** (coder claimed "4 pre-existing warnings" — false; clean. Consistent miscount across this project — always re-checked).
- `Hrot.Blueprints.Tests` **1089 / 10 / 8** — 10 = DEBT-006; golden suite unchanged (projection-only held); a perf benchmark flakes under load (`WhenNode_*_perTick`/`ReadEqsResultNode_Under80ns`) — passes isolated. `Hrot.Editor.AiShared.Tests` **760 / 0**. `Hrot.BTree.Editor.Tests` **382 / 0**. `Hrot.Hsm.Editor.Tests` **333 / 0**. `EditorSubsystemBoot` **10 / 0**.

## Code read
- `AiGraphCanvasWindow`: DrawFrame + hotkey pump after Render, ImGui-gated, `WantTextInput` suppression; dynamic title only rebuilt on active-doc change. Headless `SimulatePickerAndHotkeyFrame` seam for tests. Clean.
- `EditorHotkeyDispatcher` (new, AiShared): host equivalent of the demo's HotkeyDispatcher (the demo's lives in the Demo project, unreferenceable).
- Variable create-path verified — real Get/Set nodes; value-pin type resolved from the variable.

## Issues / notes
- `IPickerRegistry.DrawFrame()` added as a **non-default** interface method (all in-solution implementers updated/compile). A default method would have been marginally safer for external implementers; non-blocking.
- **S4 (non-zoomable/ugly fonts) intentionally deferred** — needs the engine ImGui atlas to bake multiple font sizes so `GetFontForSize` can scale with zoom. Separate engine batch.
- 4 existing test files updated for the `AiGraphCanvasWindow` ctor change — expected.

## Verdict
APPROVED. The canvas-stuck root cause is fixed; TAB/wire-drop pickers, RMB pan/context-menu, Ctrl+F, window title, and variable Get/Set nodes addressed. Hand back for the user's in-editor re-test; font/zoom remains.

## Commit Message
```
fix(editor): draw picker overlay every frame (unstick canvas) + hotkeys + window title + variable nodes (BCP-BATCH-02-FIX)

Root cause: the integration never called PickerRegistry.DrawFrame() (demo does each frame), so an
opened picker (TAB add-node / wire-drop-to-empty) was invisible and left InteractionMode stuck,
killing RMB-pan, context-menu and wire-drag. AiGraphCanvasWindow now calls DrawFrame() + pumps an
EditorHotkeyDispatcher (Ctrl+F etc.) each frame, ImGui-gated, suppressed during text input;
IPickerRegistry gains DrawFrame(); EditorSubsystem injects the shared picker registry + input source.

Window title now shows the active asset name (ManagedWindow.Title + stable ###id).

Variable Get/Set: CreateAssetNode maps Util.GetVar/SetVar to real GetVariableNode/SetVariableNode
(was FunctionCallNode → exec pins, no data); NodePinSchema gives Get pure data-out Value, Set exec
in/out + typed Value. My Blueprint '+' create-variable implemented; real ctx.Commands passed to the
My Blueprint window.

Font/zoom (multi-size atlas) deferred to an engine batch. Projection-only intact (byte-stability +
compiler golden unchanged). Build 0/0. Blueprints 1089/10 (DEBT-006), AiShared 760/0, BTree 382/0,
Hsm 333/0, Boot 10/0.
```
