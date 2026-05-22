# T-12 — UI: Canvas Renderer

## Goal
The main editor surface. Draws nodes, pins, wires, grid, marquee, pending wire, and dispatches mouse/keyboard input through the canvas state machine. This is the **largest single file** in the codebase (~700 LOC) and the only place that touches `ImGui.GetWindowDrawList()` for canvas drawing.

## Project
`NodeEditor.UI`

## References

**Specs (read in full before starting):**
- `../instructions/01-spec-brief.md` §6 (canvas), §7 (nodes), §8 (pins), §10 (wires), §12 (state machine)
- `../specs/A-canvas-interactions.md` — complete normative behavior
- `../instructions/01-spec-brief-part2.md` §28 (timing constants), §29 (colors)

**Kernel:**
- `../kernel/03-search-spatial-constants.md` — `TimingConstants`, `DefaultTheme`, `SpatialIndex`
- `../kernel/01-interfaces.md` — `IGraphModel`, `INodeModel`, `IPinModel`, `ILinkModel`, `ILinkValidator`, `ITypeSystem`

**Other view-model state:**
- T-09, T-10, T-11 outputs (`ViewportState`, `SelectionState`, `InteractionState`, `GraphView`)

## Deliverables

```
src/NodeEditor.UI/
    Canvas/
        CanvasRenderer.cs        // main entry point: Render(GraphView, region)
        CanvasInput.cs           // mouse/keyboard event handling, state-machine transitions
        NodeRenderer.cs          // draw one node body + pins
        PinRenderer.cs           // draw one pin shape + label + default editor stub
        WireRenderer.cs          // bezier + arrowhead, hit-testing
        GridRenderer.cs          // background grid
        HitTester.cs             // hit-test in priority order (reroutes > pins > wires > comment headers > node bodies > comments > empty)
    Util/
        ImDrawListExtensions.cs  // helpers: AddBezierThick, AddCircleFilledOutline, etc.
        ImGuiPushIdScope.cs      // RAII PushID/PopID disposable
```

## Implementation Notes

### Public entry point

```csharp
namespace NodeEditor.UI.Canvas;

public sealed class CanvasRenderer
{
    /// <summary>
    /// Render the entire canvas for one frame inside the current ImGui window.
    /// Caller is responsible for positioning the ImGui window/region.
    /// Reads from <paramref name="view"/> and dispatches commands through it.
    /// </summary>
    public void Render(GraphView view);
}
```

Split internally so `CanvasRenderer.Render` is a short orchestrator:

```csharp
public void Render(GraphView view)
{
    BeginCanvasChild(view);                  // BeginChild + InvisibleButton for hit area
    UpdateHover(view);                       // populate view.Interaction.Hover
    _input.Handle(view);                     // state machine: mouse/keyboard
    _grid.Draw(view);                        // background dots/grid
    DrawComments(view, foreground: false);   // comment bodies (behind nodes)
    DrawLinks(view);                         // wires + arrowheads + reroutes
    DrawNodes(view);                         // node bodies + pins + inline editors
    DrawComments(view, foreground: true);    // comment headers (in front of nodes)
    DrawPendingWire(view);                   // current drag-wire if any
    DrawMarquee(view);                       // marquee box if any
    DrawDebugOverlay(view);                  // breakpoint/exec-flow ring if debugging
    EndCanvasChild(view);
}
```

### Grid

```csharp
// Two-level dot grid:
//   minor dots every 16 graph units, color DefaultTheme.GridMinor
//   major dots every 128 graph units, color DefaultTheme.GridMajor (alpha boosted)
// Below 0.4 zoom skip minor dots to avoid moiré.
// Use only points (drawList.AddRectFilled) — no full lines — keeps overdraw cheap.
```

### Node rendering

One node is a rounded-rect body with:
- Header strip (colored by category, height = `TimingConstants.NodeHeaderHeight`).
- Title text + small icon (via `IIconProvider`) in header.
- Left column of input pins, right column of output pins.
- For each unconnected input data pin: an inline mini-editor widget (delegated to `IPinDefaultValueEditorRegistry` from T-13).
- Selection outline: 2px stroke in `DefaultTheme.SelectionOutline`, drawn outside the body.
- Error outline: red 2px stroke if `IDiagnosticsSink.GetNodeDiagnostic(nodeId)` returns Error.
- Debug outline: pulsing yellow if `IDebugSession.IsBreakpoint(nodeId)`.

Position lookup priority:
1. `view.Interaction.DragOverridePositions[nodeId]` if present.
2. `view.Model.GetNodePosition(nodeId)` otherwise.

Use `view.Host.IconProvider` for the title icon and `view.Host.Theme` for colors (with `DefaultTheme` as fallback).

Low-zoom mode (`view.Viewport.IsLowZoom`): render only the body rect + title strip, no pins, no labels, no inline editors. Improves overdraw at the 500-node scale.

### Pin rendering

For data pins: circle outline; filled if connected. Color from `view.TypeSystem.GetColorForType(pin.TypeKey)` with `DefaultTypeColors` fallback.

For exec pins: triangle (▶) pointing right for outputs, pointing right for inputs (same shape, position differs). White stroke; white fill if connected; otherwise hollow.

Pin label: drawn next to the pin shape. Input pins → label to the right of the shape. Output pins → label to the left of the shape. Hide labels at zoom < 0.4.

### Wire rendering

See `../specs/A-canvas-interactions.md` §7 for bezier math:

```csharp
// Cubic bezier from a (source-out, on right side of source node) to b (target-in, on left side of target node).
// dx = b.X - a.X
// tangent = max(50, |dx| * 0.5)
// c1 = a + (tangent, 0)
// c2 = b - (tangent, 0)
// ImGui drawList.AddBezierCubic(a, c1, c2, b, color, thickness, segments)
```

Exec wires: white/light-gray, thickness `TimingConstants.WireThicknessExecPx`, with a directional arrowhead at midpoint.
Data wires: pin-color, thickness `TimingConstants.WireThicknessDataPx`, no arrowhead.

If the link has waypoints (reroutes), break the wire into bezier segments between consecutive points (source pin → waypoint[0] → waypoint[1] → … → target pin). Each segment uses the same tangent formula.

Selected wires: thickness +1, color `DefaultTheme.SelectionOutline`.

Wire hit-testing: sample N=24 points along each bezier segment; cursor is on the wire if any sample is within `TimingConstants.WireHitRadiusPx` of the cursor.

### Hit-test priority (from spec)

Always in this order; first hit wins:

1. Reroute waypoints (small circles, radius `TimingConstants.RerouteRadiusPx`).
2. Pins (within pin shape + `TimingConstants.PinSnapRadiusPx` halo).
3. Wires (via bezier sampling).
4. Comment **headers** (drag target).
5. Node bodies.
6. Comment **bodies** (click-through to anything underneath, but if nothing else hit they become target).
7. Empty canvas.

### Input / state machine

Read `../specs/A-canvas-interactions.md` §1 carefully — it has the complete transition table. Sample entries:

- **Idle + LMB-down on empty**: record `DragStartScreen/Graph`, stay in Idle until drag threshold crossed; then → MarqueeSelecting.
- **Idle + LMB-down on node**: if node not selected, replace selection with it (respecting Shift/Ctrl modifiers); stay in Idle until threshold crossed; then → DraggingNodes.
- **Idle + LMB-down on pin**: → PendingWire with `SourcePin = clicked pin`.
- **Idle + LMB-down on reroute**: select it (with modifiers); on threshold → DraggingReroutes.
- **Idle + LMB-down on comment header**: select comment; on threshold → DraggingComment, snapshot enclosed nodes into `CommentDragContents`.
- **Idle + RMB-down on empty**: → Panning.
- **Idle + RMB-up without drag on empty**: open context menu (handled by host via `IEditorCommands` — emit a `CanvasContextRequested` event, not drawn here).
- **MarqueeSelecting + LMB-up**: compute hit set (touch vs enclosed by modifier), apply with selection modifiers, → Idle.
- **DraggingNodes + LMB-up**: flush `DragOverridePositions` as one `MoveNodes` command via `view.Execute(...)`, → Idle.
- **PendingWire + LMB-up on valid pin**: dispatch `AddLink` (or batched cast+AddLink+AddLink for ValidWithCast), → Idle.
- **PendingWire + LMB-up on empty canvas**: open contextual picker filtered by source pin type (delegated to T-14 picker; raise an event the host listens to).
- **PendingWire + LMB-up on invalid target**: → Idle (no link created).

Pan / Zoom:
- RMB-drag pans. Use `view.Viewport.PanScreen(-mouseDelta)` each frame in Panning mode.
- Mouse wheel zooms; call `view.Viewport.ZoomAt(mousePosScreen, factor)` where `factor = ImGui.GetIO().MouseWheel > 0 ? 1.15 : 1/1.15`.

Yield rule: at the top of `Handle()`, if `ImGui.IsAnyItemActive()` is true and the active item is not the canvas invisible button, **do nothing** — let widgets (inline editors, popups) own input that frame.

### Marquee

Drawn as a translucent filled rect plus 1px outline. Color: `DefaultTheme.MarqueeFill` / `DefaultTheme.MarqueeOutline`. Touch mode (Alt) uses a dashed outline to differentiate.

Touch mode hit-set: any element whose AABB intersects the marquee rect.
Enclosed mode hit-set: any element whose AABB is fully contained.

Apply with the modifier semantics:
- No modifier: replace selection with hit-set.
- Shift: add hit-set to selection.
- Ctrl: toggle each entry of hit-set.

### Pending wire visual

Draw a bezier from the source pin to the cursor graph position. Color: source pin's color. Thickness same as a normal wire of its kind. If `CandidateTarget` is set and `CandidateValid`, snap the endpoint to the candidate pin and tint green. If `CandidateValid` is false, tint red. If `CandidateNeedsCast`, tint yellow.

### Performance

Use a per-frame `SpatialIndex` build (rebuild only if `view.Model.Version` changed since last frame; cache the version int).

For `Render`-time culling: query `SpatialIndex.QueryRect(visibleGraphRect)` and only draw nodes whose AABB intersects the visible region. Wires: draw only if either endpoint node is in the culled visible set (or any of the link's reroute waypoints lies in the visible rect).

### Helpers (`Util/`)

- `ImDrawListExtensions.AddBezierWithArrow(drawList, a, c1, c2, b, color, thickness, arrowSize)` — for exec wires.
- `ImGuiPushIdScope` — IDisposable wrapping `ImGui.PushID(string|int|IntPtr) / PopID()`. Used for stable IDs on per-node widgets:

```csharp
using var _ = new ImGuiPushIdScope(nodeId.Value);
// ...node-scoped imgui widgets here...
```

## Acceptance

- Compiles, no warnings (Directory.Build.props sets TreatWarningsAsErrors).
- Demo app (T-20) shows a working canvas: pan with RMB, zoom with wheel, drag node with LMB, draw wire from pin, select with marquee, undo/redo with Ctrl+Z / Ctrl+Y.
- No tests in the UI layer for this task (UI is tested via the demo).

## Estimated Size
~700 LOC across all files in the task.

## Status
Pending.

## Open implementation choices delegated to coding agent

These are minor and should be decided when writing the code; document the choice with a brief XML doc comment:

- Exact pin shape pixel sizes for input vs output (use values close to Unreal: 10px circle diameter at zoom=1, 12px triangle width for exec).
- Whether to clip wires that exit the visible rect (recommended: no, draw them anyway; the bezier may re-enter).
- ID strategy for ImGui widgets inside nodes: nodeId.Value as string vs deterministic int. Both work; pick one and be consistent.

If any of these turn into design decisions instead of taste decisions, log them in `QUESTIONS.md` for the human.
