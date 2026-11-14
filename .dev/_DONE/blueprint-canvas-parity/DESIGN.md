# Blueprint Canvas — Full NodeEdit Demo Parity (DESIGN)

**Goal:** the Hrot editor's Blueprint canvas must look and behave exactly like the NodeEdit demo (scenarios S01..S36) — a complete, non-minimalistic blueprint editing experience: rich pins + wires, movable nodes, the demo color scheme, inline mini-editors, full pickers, find/commands, comments, reroutes, containers, bookmarks.

**Specs (authoritative):** `docs/blueprints/NodeEdit/` (A-canvas-interactions, B-mini-editors, C-picker, D0-action-api, D1-to-D4-flows, D6-my-blueprint, D7-details, D8-comments-reroutes, D9-bookmarks, D10-hot-reload) and `.dev/blueprints-1/Blueprint_Subsystem_Editor_Detailed_Design.md`. Behavioral specimen: `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/` (DemoShell.cs, FakeBlueprint/*, Scenarios/S01..S36).

## Confirmed root causes (current integration)
1. **Pins & wires completely missing.** Loaded `.bp.json` serialize `"Pins": []`; `BlueprintNodeModel` projects `node.Pins` → empty → no pins; links carry `FromPinId/ToPinId` but `FindPin` returns null → no wires. No pin-hydration step on load. (`MoveToAndFire.bp.json`, `simple_node.bp.json`; `BlueprintNodeModel.cs:37-39`.)
2. **Nodes won't move.** `BlueprintCommandSink.ApplyMoveNodes` calls `_model.RebuildAndNotify()` every drag-frame, recreating all node models; `BlueprintNodeModel.Position` is a ctor snapshot. BTree/demo mutate position in place, no rebuild. (`BlueprintCommandSink.cs:277-292`.)
3. **Yellow marquee / wrong colors.** `EngineEditorTheme` forwards all colors to engine `DefaultTheme`. Demo uses `FakeEditorTheme` (blue selection 0.21/0.52/0.89, per-category headers, 4px corners, exec=3/data=2 wires).
4. **No inline editors / pickers / find.** `BlueprintPinModel.Default => null` + `NullPinDefaultValueEditorRegistry`; `Render(view, null)` passes no FindBar/IEditorCommands; no picker sources registered.

## Core decision — PINS ARE PROJECTION-ONLY (no JSON schema change for pins)
The asset `Node`/`Pin`/`Link` types are **shared with the compiler** (`Hrot.Blueprints.Compiler/Assets/`). The compiler *deliberately* skips pin-existence checks, type resolution and implicit-cast insertion when `Node.Pins` is empty (`Stage2_Validate.cs:209-246`, `Stage3_Normalize.cs:61-66`, `Stage4_TypeResolve.cs:30-55,176`). Persisting pins to disk would flip the compiler into those paths → new diagnostics + golden-snapshot regen → a semantic change masquerading as a UI fix.

**Therefore:**
- Hydrate pins in the editor projection from a per-kind **canonical pin schema** (registry descriptor `CreateInstance().Pins` + a built-in fallback table). Bind each connected pin's GUID **from the incident link's `FromPinId`/`ToPinId`** so wires resolve; unconnected pins get a deterministic `SynthesizedGuid(nodeId, name, dir)`. Persist nothing.
- Editor-only extras (pin default values, comments, reroutes, containers) live in the **already-editor-only** `NodeMetadata` / `GraphMetadata` (compiler ignores them) with **ignore-null serialization** so existing fixtures stay byte-identical.
- **Guardrail:** a byte-stability test (load → serialize → assert identical) over all fixtures + the unchanged compiler golden suite prove every change is non-semantic.

## Phases (impact order, deps honored: C → B → A → D → E → F → G → H → I)
- **C — Demo theme, all 3 perspectives.** Rewrite `EngineEditorTheme` to the demo scheme. (No schema. Shared → BTree/HSM also get it, per user.)
- **B — In-place node movement.** Mutable `BlueprintNodeModel.Position` + `SetPosition`; `ApplyMoveNodes` mutates in place + lightweight move-notify, no full rebuild. Mirror BTree/`FakeCommandSink`.
- **A — Pin/wire hydration (#1 fix).** `NodePinSchema` resolver; two-pass `BlueprintGraphModel.Rebuild` (resolve pin GUIDs from links → build pins → build links). Projection-only.
- **D — Mini-editors / inline pin defaults.** `PinDefaultValueEditorRegistry.CreateWithBuiltins()`; `BlueprintPinModel.Default`; `SetPinDefault` → `NodeMetadata.PinDefaults` (ignore-null).
- **E — Pickers fully wired (S07–S12).** Register blueprint-backed `IPickerSource`s: node-type/add-node, wire-drop by-pin, variable, type, asset-grid, flags/enum.
- **F — FindBar + IEditorCommands** into the canvas `Render` for all 3 perspectives (S28–S30); carry on `AiCanvasContext`.
- **G — Comments + reroutes** (D8; S06/S26/S27) — editor-only `GraphMetadata`.
- **H — Containers** (S35) — editor-only grouping metadata, **explicitly non-compiled**.
- **I — Bookmarks (D9), Details (D7) + My Blueprint (D6) parity audit.**

## Test strategy
Headless-first at model/sink layer (projection correctness, move identity, property round-trips, picker source queries, find/command dispatch). **Byte-stability gate** over all `.bp.json` fixtures after every schema-adjacent change. Re-run compiler golden suite unchanged (any drift = design violation). ImGui-dependent rendering gated behind `ImGui.GetCurrentContext() != Zero`, verified via demo + manual smoke.

## Out of scope (would need compiler coordination)
Persisting authored `Node.Pins` to disk; compiling pin-defaults via the pin model (vs `LiteralNode`s); compiling container/macro grouping.
