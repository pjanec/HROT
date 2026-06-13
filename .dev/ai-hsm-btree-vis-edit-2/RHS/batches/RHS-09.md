# RHS-09 — Arrowheads on HSM transition wires (direction)

**Workstream:** RHS (../RHS-PLAN.md). **Layer:** Hrot.Hsm.Editor renderer. **Depends:** RHS-08 (wires now render).

## Goal

HSM transition wires render (RHS-08) but are directionless beziers — NodeEditor's `WireRenderer` only draws a midpoint arrowhead for `Exec`-kind pins, and HSM pins are `Data`. Add a **target-end arrowhead** so each transition's direction (source → target) is unambiguous.

## Approach

Do it HSM-side in `Renderers/HsmTransitionLabelRenderer.cs` (the `hsm.transition_labels` renderer; `AfterWires` pass — runs after wires, so the arrowhead sits on top). It already resolves the source-output and target-input pin screen positions for the external-transition label midpoint; reuse them.

For each **external** (non-Internal) transition where both pin positions resolve:
- Compute `dir = normalize(targetPin - sourcePin)` (screen space). If the two points coincide (degenerate), skip the arrowhead.
- Draw a small filled triangle at the **target-input pin** position, pointing along `dir` (into the target). Size ~ `7f * ctx.Zoom` long, `5f * ctx.Zoom` half-width; place the tip at (or just short of) `targetPin` so it reads as entering the node.
- Color: a visible wire color consistent with the mid-blue wire (e.g. reuse the data-wire color `new Vector4(0.4f, 0.55f, 0.9f, 1f)` or the theme's text-default) — pick one and keep it readable on the dark canvas; document the choice.
- Use the SAME fallback as the label: if pin positions don't resolve, fall back to node-rect centers (source center → target center) for the direction + place the arrowhead at the target rect's near edge; if neither resolves, skip.

Internal transitions: leave as-is (they already draw a self-loop with a label); no straight arrowhead.

Keep the existing label drawing unchanged — just add the arrowhead next to it.

## Constraints

- Only `Renderers/HsmTransitionLabelRenderer.cs`. Do NOT touch NodeEditor, other renderers, theming, the showcase JSON, or the command sinks.
- Preserve the renderer's counter seams (`LastLabelCount` / `LastInternalTransitionCount`) and the low-zoom early-out.
- Keep arrow drawing gated on the same `canDraw` (valid draw list) check the label uses.

## Tests

- Add/extend a unit test for any pure helper you introduce (e.g. an arrowhead-geometry function taking source+target screen points → triangle vertices), asserting the tip points toward the target and the triangle is non-degenerate. If the arrowhead math is inline, factor the geometry into an `internal static` helper so it's testable without ImGui (mirror how `ComputeMarkerGeometry` is tested).
- Existing `HsmTransitionLabelRenderer` tests must still pass.

## Verification (run + paste raw output)

1. `dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj -c Debug -v q -nologo` → 0 errors.
2. `dotnet test Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/Hrot.Hsm.Editor.Tests.csproj -c Debug --nologo -v q` → ≥481 passing, 0 failing.

## Report back

The arrowhead color/size chosen; diff summary; the testable geometry helper (if any) and its test; raw build + test output. Do NOT commit — lead reviews & commits.
