# BATCH-10 — BTree vertical pin orientation (tree layout) **[VISUAL GATE]**

**Task:** TASK-BT-10 (REVIEW-BT F3). **One objective.** Decision **D-06** (`.dev/_DONE/ai-hsm-btree-vis-edit-2/DECISIONS.md`).

## 🔒 Working agreement (MANDATORY)
One task; **NO cheating**; finish without asking until build clean + `Failed: 0`; tests assert real values; litter-free; report = diffs.
**[VISUAL GATE]:** implement + headless tests that assert pin *positions* (which edge); the pixel/wire look is confirmed by the lead in the running editor (REVIEW-BT-2).

## 📋 Onboarding / context
- Report → `.dev/_DONE/ai-hsm-btree-vis-edit-2/reports/BATCH-10-REPORT.md`.
- NodeEditor renders pins Blueprint-style: **input on the LEFT, output on the RIGHT**, so wires run horizontally. Pin positions are computed in `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasLayout.cs` (the `CanvasLayoutBuilder.Build` pin-position loops, ~lines 171-183: input pins at `X = NodeHorizPadGu`, output pins at `X = nodeWGu - NodeHorizPadGu`, both stepping in Y by row). **Wire routing is position-agnostic** (`WireRenderer` just draws between pin positions) — so moving pins to top/bottom makes wires follow automatically.
- For a BTree the tree must read **root at top → leaves at bottom**, wires vertical. BTree uses a **reversed-pin convention** (a node's **Output** pin links UP to its parent; its **Input** pin receives from its children — see `BTreeGraphModel`/`BTreeLinkValidator`). BTree auto-layout puts the root at low Y and children at higher Y.
- Therefore, for the vertical BTree layout: a node's **Output pin goes on the TOP edge** (links up to the parent above it) and its **Input pin goes on the BOTTOM edge** (links down to children below). Wires then run parent.bottom → child.top. (Do not "fix" the reversed convention — D-06: keep it; BTree pins render no labels, only the picture matters.)

## 🎯 Objective
Add an opt-in **vertical pin orientation** to NodeEditor and have the BTree canvas use it; Blueprint/HSM stay horizontal (unchanged).

## Implementation (verify exact names against the real code; do not invent members)
1. **Orientation flag (NodeEditor.Core/Primitives):** add a way for a graph to declare vertical pin layout. Preferred: a property on `GraphKindDescriptor` (e.g. `PinOrientation Orientation` with enum `{ Horizontal, Vertical }`, default `Horizontal`) — `GraphKindDescriptor` is what `IGraphModel.Kind` returns. (If a cleaner seam exists — e.g. a flag on `IGraphModel` or the view — use it, but default MUST be Horizontal so Blueprint/HSM are unchanged.)
2. **`CanvasLayout.cs` pin positioning:** when the graph's orientation is `Vertical`, compute pin screen positions on the horizontal edges instead of the vertical edges:
   - **Output** pins: along the node's **TOP** edge (`Y = graphPos.Y` / header top), spread across X (center if single).
   - **Input** pins: along the node's **BOTTOM** edge (`Y = graphPos.Y + nodeHeight`), spread across X (center if single).
   - Keep the existing horizontal math for `Horizontal`. Extract a small helper or branch on orientation.
3. **BTree opts in:** set the BTree graph kind's orientation to `Vertical` (in `BTreeGraphModel.Kind` / `BTreeGraphKinds`). Leave Blueprint + HSM at `Horizontal`.
4. Pin *label* and *inline-editor* positioning (`PinRenderer`/`NodeRenderer`) assume horizontal — BTree pins have empty labels and no inline editors, so you may leave those as-is, BUT ensure no crash/misplacement for vertical (guard if needed). Do not restyle Blueprint/HSM.

## 🧪 Tests (NodeEditor + host; headless — assert positions/orientation, not pixels)
In NodeEditor's test project (find where `CanvasLayout`/`CanvasLayoutBuilder` is tested; mirror it):
- `Layout_VerticalOrientation_OutputPinOnTopEdge_InputOnBottom`: a node in a `Vertical` graph → its output pin's computed Y ≈ node top, input pin's Y ≈ node bottom (assert Y relationship: output.Y < input.Y, and X within node bounds), whereas in `Horizontal` the X differs (output.X > input.X) — assert the orientation actually changes the axis.
- `Layout_HorizontalOrientation_Unchanged`: default graph → input left / output right as before (regression).
- Host test (`Hrot.BTree.Editor.Tests`): `BTreeGraphModel.Kind.Orientation == Vertical` (or whichever flag) — BTree opts in; (optionally) a Blueprint/HSM graph kind stays Horizontal.

## ✅ Success criteria
- [ ] `dotnet build IOS-IG-SimHost.sln` — 0 errors, 0 new warnings.
- [ ] `Failed: 0` in the NodeEditor test project + `Hrot.BTree.Editor.Tests`; **Blueprint + HSM canvases unchanged** (their graph kinds stay Horizontal — verify no Blueprint/HSM test regresses).
- [ ] BTree graph declares Vertical; layout puts output-pin top / input-pin bottom; wires follow (position-agnostic).
- [ ] Report written. (Pixel/wire-look confirmation → REVIEW-BT-2.)

## Notes
- Default orientation MUST be Horizontal so Blueprint/HSM are byte-for-byte unchanged.
- Reversed convention stays (output=top=up-to-parent, input=bottom=down-to-children) per D-06.
- This is a shared-NodeEditor change — keep the change minimal and additive; do NOT refactor unrelated canvas code.
