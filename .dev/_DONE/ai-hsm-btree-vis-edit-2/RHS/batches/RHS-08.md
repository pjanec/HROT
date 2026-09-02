# RHS-08 — Make HSM transition wires render (pins positioned but glyph-less)

**Workstream:** RHS (../RHS-PLAN.md). **Layer:** NodeEditor core + Hrot.Hsm.Editor model. **Depends:** none (independent of RHS-01..06). **Priority: blocker** — user reports "no transitions, no visual relation between nodes."

## Root cause (confirmed in code)

HSM state hidden pins (`Model/HsmPinModel.cs`) are `IsAdvanced => true`, and `StateNode.ShowAdvancedPins => false`. Both the layout builder (`FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasLayout.cs:94`) and the pin renderer (`PinRenderer.cs:39`) skip a pin when `pin.IsAdvanced && !node.ShowAdvancedPins`. So HSM pins are **never laid out** → absent from `PinScreenPositions` → `WireRenderer.DrawAll` hits `if (!pinPositions.TryGetValue(link.FromPin, out a)) continue;` (`WireRenderer.cs:49-50`) and **draws no wire**. (Transition labels still appear because RHS-03 falls back to node-rect centers when pin lookup fails.)

The design (HSM host design §7.1) intends: each state has a hidden output + input pin that **are positioned so wires route**, but **no pin glyph is drawn**. The current code can't express "positioned but glyph-less" — `IsAdvanced` removes the pin from layout entirely. Fix that.

## Fix

### Part A — NodeEditor: a glyph-less pin shape

1. `FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/Enums.cs`: add `None` to the `PinShape` enum (additive; e.g. as the last member, or first — your call, but check no code relies on numeric values).
2. `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/PinRenderer.cs`: in `DrawNodePins`, after the `pinPositions.TryGetValue` line resolves `screenPos`, add `if (pin.Shape == PinShape.None) continue;` — so a `None`-shaped pin draws **no glyph and no label**, but (critically) is still laid out and positioned by `CanvasLayout` (which does NOT consult `Shape`). This keeps the pin available for wire routing while invisible.
3. Search for any other exhaustive `switch` on `PinShape` (e.g. hit-testing, hover) and make sure adding `None` doesn't break them (default arm is fine; just don't draw a glyph for `None`).

### Part B — HSM pins: positioned, glyph-less

`Model/HsmPinModel.cs`:
- `IsAdvanced => false` (so the pin is laid out and gets a screen position).
- `Shape => PinShape.None` (so no glyph/label is drawn).
- `Label => ""` (empty; belt-and-suspenders — PinRenderer skips label for `None` anyway).
- Keep `Kind => PinKind.Data` and `Type => null`. (This yields the default mid-blue data-wire color, which is clearly visible on the dark canvas. Do NOT switch to `Exec` — exec pins can carry single-input cardinality semantics that would break states with multiple incoming transitions.)

### Result
Transition wires render as visible mid-blue beziers from each source state's output pin (right edge, Horizontal orientation) to each target's input pin (left edge). No stray pin glyphs or "out/in" labels. Node bodies grow by ~1 pin row — acceptable.

## Constraints / watch-outs

- This makes HSM pins real layout participants. Confirm drag-to-create-transition and the existing HSM wiring/link-validator tests still pass (they should — `HsmLinkValidator` resolves states from pins regardless of layout).
- Additive `PinShape.None` must not change BTree/Blueprint rendering (their pins keep their existing shapes). Verify those editors build and their renderer tests pass.
- Do NOT touch the showcase JSON (the user is hand-laying-out positions in the editor) or any RHS-01..06 work.
- Arrowheads (direction) are intentionally OUT of scope here — get wires visible first; a follow-up (RHS-09) adds direction if the screenshot shows it's needed.

## Verification (run + paste raw output)

1. `dotnet build FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/NodeEditor.UI.csproj -c Debug -v q -nologo` → 0 errors.
2. `dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj -c Debug -v q -nologo` → 0 errors.
3. `dotnet build Hrot/Subsystems/AI/Hrot.BTree.Editor/Hrot.BTree.Editor.csproj -c Debug -v q -nologo` and the Blueprint editor host → 0 errors.
4. `dotnet test FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/NodeEditor.UI.Tests.csproj -c Debug --nologo -v q` → report counts (must stay green).
5. `dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj -c Debug --nologo -v q` → ≥478 passing, 0 failing.
6. Add a focused test: a `HsmPinModel` returns `IsAdvanced==false` and `Shape==PinShape.None`; and (if a layout harness exists) that a two-state + one-transition HSM graph yields non-empty `PinScreenPositions` for both pins after layout so the wire would route. State which you added.

## Report back

Diff summary; any other `PinShape` switches you had to touch; whether drag-create/link tests were affected; raw build + test output for all projects above. Do NOT commit — lead reviews & commits.
