# BATCH-10 Review — TASK-BT-10 BTree vertical pin orientation

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED

## Verification (independent)
- `PinOrientation { Horizontal=0, Vertical }` + `GraphKindDescriptor.Orientation { get; init; } = Horizontal` (default) → Blueprint/HSM (which don't set it) unchanged; additive init-property, full build 0 errors (no implementer broke).
- `CanvasLayout.ComputePinGraphPosition`: Vertical → **Output pin = top edge** (`graphPos.Y + PinTopPadGu`), **Input pin = bottom edge** (`graphPos.Y + nodeHGu - PinBottomPadGu`), X spread via `PinCenterX`; node height compact for vertical. Horizontal branch preserved (input left / output right). Matches D-06 + reversed-pin convention (output=up-to-parent=top).
- `BTreeGraphModel.Kind` opts into `Vertical`. Wire routing is position-agnostic → wires follow.
- Re-run: NodeEditor.UI.Tests **59/0** (incl. new `CanvasLayoutTests` asserting vertical vs horizontal), Hrot.BTree.Editor.Tests **506/0**. Full `dotnet build` 0 errors.

## Issues
None. (Shared NodeEditor change kept additive + default-Horizontal so Blueprint/HSM are untouched.)

## Verdict
APPROVED. `[VISUAL GATE]`: the actual top-down tree look + wire routing confirmed by the lead at REVIEW-BT-2.

## Commit message
```
feat(nodeeditor): vertical pin orientation (opt-in) + BTree tree layout (BATCH-10 / TASK-BT-10)

Add PinOrientation to GraphKindDescriptor (default Horizontal — Blueprint/HSM
unchanged); CanvasLayout places pins on top/bottom edges when Vertical (output
top, input bottom — matches BTree's reversed-pin convention, root-top→leaves-
bottom). BTreeGraphModel.Kind opts into Vertical; wire routing is position-
agnostic so wires follow. +CanvasLayout + host tests.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
