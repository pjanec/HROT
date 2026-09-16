# BCP-BATCH-02-FIX2 Review — wire-from-connected-pin + full palette + variable title/modal + title char
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Verification (ran myself)
- **`dotnet build IOS-IG-SimHost.sln` → 0 Warnings / 0 Errors** (coder claimed "18 warnings in untouched test files" — false again; clean build).
- `Hrot.Blueprints.Tests` **1097 / 11 / 8** → 10 = DEBT-006, 11th = flaky `WhenNodePerfTests.ReadEqsResultNode_Under80ns` (passes isolated; touches no changed code). No new failures; golden + byte-stability unchanged.
- `NodeEditor.UI.Tests` **41 / 0** (incl. new HitTester pin-over-wire test). `Hrot.Editor.AiShared.Tests` **761 / 0**. `Hrot.BTree.Editor.Tests` **382 / 0**. `Hrot.Hsm.Editor.Tests` **333 / 0**. `EditorSubsystemBoot` **10 / 0**.

## Code read
1. **HitTester.cs:206** — pin hit now submitted at `ZLayerPin` (100) instead of `ZLayerNodeElement` (40). Exactly the documented Z-order (`Wire < Pin`). One line; matches the confirmed root cause (wire was beating pin at connected pins). Regression test added in `HitTesterZOrderTests`.
2. **BlueprintNodePaletteEntries.All()** (new) — 24 typed kinds via `Make<TNode>` across 10 categories (FlowControl, Variables, Function, Events, Array, Latent, Channel, Decision, Squad, Utility); `CreateInstance` returns the typed node, pins projected by `NodePinSchema` (projection-only). Registered in `BlueprintEditorBootstrap.CreatePaletteRegistry` alongside the 3 When/EQS → 27 total. Real, not stubbed.
3. **BlueprintNodeModel** — asset threaded in; `BuildTitle` resolves the variable name from `BlueprintAsset.Variables` (strips `var:`) → "Get <name>"/"Set <name>", id fallback.
4. **AiGraphCanvasWindow.UpdateTitle** — ASCII separator (no more "?").
5. **Variable modal** — `BlueprintDocumentFactory.CreateVariable(name,type)` (headless-testable) + `BlueprintTypeSystem.SelectableTypeIds` + ImGui-gated `VariableCreateModal`, wired to the My Blueprint `+` command.

## Test quality
HitTester test asserts `HoverKind.Pin` wins when coincident with a wire; palette test asserts ≥25 kinds + categories; title test asserts "Get Health"; ASCII-title test; create-variable test asserts the `VariableDecl` (name+type) is added. Real assertions.

## Issues
- Only NodeEdit-core edit is the one-line HitTester z-layer (matches documented intent; UI tests green). Low risk.
- Variable modal: UI is ImGui-gated; the create path is tested headlessly (modal interaction itself verified by user in-editor).

## Verdict
APPROVED. The serious wire-from-connected-pin bug is fixed, the picker now offers the full node vocabulary, variable nodes show names, the title is ASCII, and variable creation has a name/type modal. Hand back for in-editor re-test. Remaining: fonts (S4, engine batch); BATCH-03 (mini-editors, comments/reroutes).

## Commit Message
```
fix(editor): pin wins hit-test over wire + full node palette + variable title/modal + ASCII title (BCP-BATCH-02-FIX2)

HitTester.cs:206: submit pin hits at ZLayerPin (100), not ZLayerNodeElement (40), so a pin coincident
with a wire wins (matches the file's documented Wire<Pin order). Fixes: dragging a new wire from an
already-connected pin (was selecting the wire instead). + HitTester regression test.

BlueprintNodePaletteEntries.All(): 24 typed node kinds across 10 categories registered into
CreatePaletteRegistry (+ When/EQS = 27), so the TAB / wire-drop picker offers the full blueprint
vocabulary. Pins projected by NodePinSchema (projection-only).

BlueprintNodeModel.BuildTitle resolves Get/Set variable names ("Get Health", not the guid).
AiGraphCanvasWindow title uses an ASCII separator (no more "?"). Variable '+' opens a name/type modal
(VariableCreateModal + BlueprintDocumentFactory.CreateVariable + BlueprintTypeSystem.SelectableTypeIds).

Build 0/0. NodeEditor.UI 41/0, Blueprints 1097/10 (DEBT-006), AiShared 761/0, BTree 382/0, Hsm 333/0,
Boot 10/0. Projection-only intact (byte-stability + compiler golden unchanged).
```
