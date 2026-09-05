# BCP-BATCH-02 Review — pickers + find/commands + variable value-pin
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
- **BCP-F:** `AiCanvasContext` carries optional `FindBar`/`IEditorCommands`; built per-document in all three factories via `EditorCommandsImpl` + `FindBar`/`FindEngine` + `BuiltinCommandHandlers.RegisterAll`; `AiGraphCanvasWindow` now calls `Render(view, ctx.FindBar, ctx.Commands)`. `ICanvasRenderSeam` gained a backward-compatible 3-arg overload (default interface method). Ctrl+F + command dispatch now available in all three perspectives.
- **BCP-E:** `BlueprintPickerSources` registers `nodes.all`, `nodes.by-pin`, `variables.all`, `types.all`, `assets.by-type`, `enum.values`.
- **Variable value-pin:** `NodePinSchema.GetCanonicalPins` now takes the asset and types Get/Set Value pins from the declared variable type (`ResolveVariableTypeId`, handles raw-Guid and `var:<guid>` ids).

## Verification (ran myself)
- **`dotnet build IOS-IG-SimHost.sln` → 0 Warnings / 0 Errors.** (Coder claimed "26 pre-existing warnings" — false; clean build. Third coder warning-miscount this project — always re-checked.)
- `Hrot.Blueprints.Tests` **1084 / 10 / 8** — 10 = DEBT-006; the 11th in the combined run was the flaky sub-80ns `WhenNodePerfTests.ReadEqsResultNode_Under80ns` which **passes 8/8 isolated** (load flake, not a regression — BATCH-02 touches no runtime path). Golden suite unchanged; byte-stability green.
- `Hrot.Editor.AiShared.Tests` **750 / 0**. `Hrot.BTree.Editor.Tests` **382 / 0**. `Hrot.Hsm.Editor.Tests` **333 / 0**. `EditorSubsystemBoot` **10 / 0**.

## Code read
- Seam/context changes additive + symmetric across Blueprint/BTree/HSM. `DelegatingCanvasRenderSeam` takes an optional find-bar-aware delegate; `EditorSubsystem` supplies it to all three.
- `BlueprintNodePickerSource` is **real** — `nodes.all` → `BlueprintNodeCatalog.Query`; `nodes.by-pin` → `QueryForPinContext(pinId, dir, kind, type, text)` from the picker context. `BlueprintVariablePickerSource` is **real** (asset.Variables + name filter). All ImGui render methods gated behind `GetCurrentContext()`.
- `NodePinSchema` variable typing verified — Value pin TypeRef resolved from the variable.

## Issues / debt (flagged to user)
- **DEBT-BCP-003 (P2):** `assets.by-type` and `enum.values` picker sources are **placeholders** (return empty). The user asked for full pickers; node-type/wire-drop/variable are real, but asset-grid and enum/flags are not yet backed. `types.all` exposes 9 common System types rather than the full `BlueprintTypeSystem` registry. Fill in a later batch.
- **DEBT-BCP-002 (carried):** HSM nested-region reparent lightly tested.
- TAB/wire-drop open the pickers via `BuiltinCommandHandlers` wiring — verified wired; full interactive behavior needs the in-editor smoke test the user is doing.

## Verdict
APPROVED. Core node-creation + search experience is in (real node/variable/type pickers, find, commands, typed variable pins). Placeholders logged. Pausing for user's in-editor check before BATCH-03.

## Commit Message
```
feat(blueprint-editor): pickers + find-bar/commands + typed variable value pins (BCP-BATCH-02)

BCP-F: AiCanvasContext carries FindBar + IEditorCommands, built per-document in all three factories
(EditorCommandsImpl + FindBar/FindEngine + BuiltinCommandHandlers.RegisterAll); AiGraphCanvasWindow
renders Render(view, findBar, commands). Backward-compatible seam overload. Ctrl+F + commands in all
three perspectives.

BCP-E: BlueprintPickerSources registers nodes.all / nodes.by-pin (BlueprintNodeCatalog), variables.all
(asset variables), types.all; asset-grid + enum sources are placeholders (DEBT-BCP-003).

Variable Get/Set value pins now typed from the declared variable type (NodePinSchema takes the asset).

Projection-only intact (byte-stability + compiler golden unchanged). Build 0/0.
Blueprints 1084/10 (DEBT-006), AiShared 750/0, BTree 382/0, Hsm 333/0, Boot 10/0.
Adds SampleWiredDemo.bp.json recipe (content-only) so a wired+laid-out graph is openable in the editor.
```
