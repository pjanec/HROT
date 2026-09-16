# BATCH-10 — Report

**Task:** TASK-BT-10 — BTree vertical pin orientation (tree layout) **[VISUAL GATE]**
**Date:** 2026-06-12
**Decision:** D-06 (reversed-pin convention stays)

## Summary

Added an opt-in **vertical pin orientation** to NodeEditor. BTree graphs now declare `PinOrientation.Vertical`, placing output pins on the TOP edge and input pins on the BOTTOM edge (matching BTree's reversed-pin convention where child.OutputPin → parent.InputPin). Blueprint and HSM are unchanged — their `GraphKindDescriptor` defaults to `Orientation = Horizontal`, preserving input-left/output-right layout.

## Changes

### 1. `NodeEditor.Core.Interfaces` — new enum + orientation property

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IGraphModel.cs`
- Added `PinOrientation` enum (`Horizontal = 0`, `Vertical = 1`)
- Added `Orientation` property to `GraphKindDescriptor` record, defaulting to `PinOrientation.Horizontal`

### 2. `NodeEditor.UI.Canvas` — pin-position branching + testable helper

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasLayout.cs`
- `CanvasLayoutBuilder.Build()`: Node height is compact for vertical graphs (no pin rows needed); pin positioning delegates to `ComputePinGraphPosition()`
- Extracted `PinCenterX()` (now `internal static`) — centers single pin or evenly spreads multiple across top/bottom edge
- Extracted `ComputePinGraphPosition()` (`internal static`) — returns graph-space pin position given orientation, direction, node geometry, index and count. Pure math, no ImGui dependency → headless-testable.
  - **Vertical:** output → `(centerX, graphPos.Y + PinTopPadGu)` [top edge]; input → `(centerX, graphPos.Y + nodeHGu - PinBottomPadGu)` [bottom edge]
  - **Horizontal:** input → `(graphPos.X + NodeHorizPadGu, offsetY)` [left edge]; output → `(graphPos.X + nodeW - NodeHorizPadGu, offsetY)` [right edge]

### 3. `BTreeGraphModel` — opts into Vertical

**File:** `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs`
- `Kind` property now sets `{ Orientation = PinOrientation.Vertical }` on the `GraphKindDescriptor`

### 4. Tests

**New file:** `FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/Canvas/CanvasLayoutTests.cs` — 8 tests:
- `Layout_VerticalOrientation_OutputPinOnTopEdge_InputOnBottom` — asserts output.Y < input.Y, both X within node bounds, single pin centered
- `Layout_VerticalOrientation_MultiplePins_SpreadAcrossWidth` — 3 pins spread left→right across top edge
- `Layout_VerticalOrientation_InputPinOnBottom_HasLargerY` — parameterized 1..4 pins, all verify output-above-input
- `Layout_HorizontalOrientation_InputOnLeftEdge_OutputOnRightEdge` — regression: input on left, output on right
- `Layout_HorizontalOrientation_Unchanged_DefaultIsHorizontal` — default `GraphKindDescriptor` stays Horizontal
- `PinCenterX_SinglePin_ReturnsCenter` — math test
- `PinCenterX_TwoPins_SpreadsEvenly` — math test
- `PinCenterX_Xpositions_WithinNodeBounds` — parameterized 1..5 pins, all within bounds

**Modified file:** `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeGraphModelTests.cs` — 1 test:
- `Kind_Orientation_IsVertical` — asserts `BTreeGraphModel.Kind.Orientation == PinOrientation.Vertical`

## Verification

| Criterion | Result |
|-----------|--------|
| `dotnet build IOS-IG-SimHost.sln` — 0 errors | ✅ 0 errors, 21 warnings (all pre-existing) |
| `Failed: 0` in NodeEditor.UI.Tests | ✅ 59 passed, 0 failed |
| `Failed: 0` in Hrot.BTree.Editor.Tests | ✅ 506 passed, 0 failed |
| Blueprint/HSM unchanged (Horizontal default) | ✅ `PinOrientation.Horizontal` is the default; no Blueprint/HSM graph kind modified |
| BTree declares Vertical | ✅ `Kind.Orientation == PinOrientation.Vertical` |
| Output pin top / Input pin bottom | ✅ Tests assert `output.Y < input.Y` for vertical |
| Wires follow (position-agnostic) | ✅ `WireRenderer` uses pin positions directly |
| Reversed convention stays (D-06) | ✅ No change to BTree's Output=child→parent, Input=parent→child convention |
| Pixel/wire look | 🔲 REVIEW-BT-2 (lead confirms in running editor) |

## Design decisions

- Orientation lives on `GraphKindDescriptor` (not `IGraphModel` or a separate service) because it is graph-kind-specific metadata, consistent with `AllowsLatent`/`RequiresEntryNode`
- Default is `Horizontal` — all existing `new GraphKindDescriptor(...)` callsite continue to work without change
- Pin-position math is extracted into a pure `internal static` method so tests can assert real computed values without initializing ImGui's native context
- Node height is compact for vertical graphs (`headerHt + pads`, no pin rows) since pins sit on edges

## Files changed

| File | Status |
|------|--------|
| `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/Interfaces/IGraphModel.cs` | Modified (+19) |
| `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasLayout.cs` | Modified (+59) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor/Model/BTreeGraphModel.cs` | Modified (+1) |
| `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeGraphModelTests.cs` | Modified (+12) |
| `FDP/ExtDeps/NodeEdit/tests/NodeEditor.UI.Tests/Canvas/CanvasLayoutTests.cs` | **New** (+185) |
