# A — Canvas Interactions (Detailed)

This spec covers every canvas interaction with full state-machine detail and
edge cases. The brief (`../instructions/01-spec-brief.md` §6, §11, §12)
contains the normative summary. This file expands the rationale and edge cases.

## A.1 The top-level state machine

Exactly one state is active at a time. Transitions are explicit.

```
                    ┌──────────────────────────┐
                    │         IDLE             │ ◄─── default
                    └──────────────────────────┘
                       │  │  │  │  │  │  │  │
        ┌──────────────┘  │  │  │  │  │  │  └──────────────┐
        ▼                 ▼  ▼  ▼  ▼  ▼  ▼                 ▼
   HOVER_NODE      HOVER_PIN    HOVER_WIRE  HOVER_COMMENT  HOVER_REROUTE
        │                 │            │           │             │
        │ LMB-down        │ LMB-down   │ RMB       │ LMB-down    │ LMB-down
        ▼                 ▼            ▼           ▼             ▼
   DRAG_NODES       DRAG_WIRE_   CONTEXT_     DRAG_COMMENT   DRAG_REROUTE
   (or select)      FROM_PIN     MENU_WIRE

   IDLE + LMB on empty     →  BOX_SELECT
   IDLE + MMB / RMB drag   →  PAN_CANVAS
   IDLE + Ctrl+drag from   →  DRAG_WIRE_FROM_MID
       connected input pin
   IDLE + Alt+click pin    →  immediate disconnect-all-on-pin
   IDLE + double-click wire →  insert reroute (immediate)
   IDLE + Tab / Space      →  OPEN_SEARCH_POPUP
   IDLE + RMB on empty     →  CONTEXT_MENU_CANVAS
   IDLE + RMB on node      →  CONTEXT_MENU_NODE
   IDLE + RMB on pin       →  CONTEXT_MENU_PIN
   IDLE + RMB on wire      →  CONTEXT_MENU_WIRE
```

### Universal rules

- **Esc cancels** any active drag/popup, restoring IDLE.
- **Mouse leaving canvas during a drag does not cancel** — only mouse-up or
  Esc cancels.
- When ImGui has an active item (`ImGui.IsAnyItemActive()`), the canvas
  suppresses its own input handling for that frame.

## A.2 IDLE behaviors (hover)

Hover detection uses the spatial index. Per-frame:

1. Compute mouse canvas position.
2. Hit-test in priority: reroutes → pins → wires → comment title bars → node
   bodies → comment bodies → empty.
   - Pins beat wires because wire hit areas overlap pin regions.
   - Comment title bars beat node bodies (so dragging a comment that overlaps
     nodes works).
   - Comment bodies are LAST so box-selecting on top of a comment works.
3. Set the matching `HOVER_*` state.

### Cursor

- Empty: default arrow.
- Node body: grab cursor (open hand).
- Pin: crosshair-with-plus.
- Wire: I-beam-rotated (or generic move cursor).
- Reroute: 4-way move cursor.
- Resize handle (comment): directional resize cursor.

### Tooltip

Appears after 600 ms of hover-without-movement. Disabled while drags or
popups are active.

- Node header → full description + deprecation warning if any.
- Pin → "Name : Type. Tooltip text. (Default: 5.0)".
- Wire → "Float → Float, link id 0x…" (mostly debug info).
- Status icon → full diagnostic message.

## A.3 Selection rules

(Normative summary in brief §11. Edge cases here.)

**Click on node (no modifier):**
- Node NOT in selection → clear selection, select this node, set primary,
  begin DRAG_NODES.
- Node IS in selection → keep selection, primary = this node, begin DRAG_NODES
  with the whole set.
- Mouse-up below 4px movement: treat as click only. No drag commands emitted.

**Shift+click on node:**
- Add to selection if not present.
- Primary = clicked node (so Details panel updates).
- Does NOT initiate drag on click itself — only if mouse moves past threshold.

**Ctrl+click on node:**
- Toggle in/out of selection.
- Primary = clicked node if now selected; else some other selected node or
  null.
- No drag.

**Click on empty space:**
- Clear selection. Begin BOX_SELECT.

**Shift+click on empty space:**
- Begin BOX_SELECT in additive mode (boxed items add to selection).

**Ctrl+click on empty space:**
- Begin BOX_SELECT in toggle mode.

**Click on selected primary while already primary:**
- No-op.

**Click on comment title bar:**
- Same model as click on node, but the click targets a comment.

## A.4 BOX_SELECT mode

State: rubber-band rectangle from drag start to current mouse position.
Rendered as dashed rect, semi-transparent fill.

**While dragging:**
- Highlight nodes whose AABB is fully enclosed (default Unreal-style).
- Alt held during drag: highlight nodes whose AABB intersects (Touch mode).
  Show "+ Touching" label near cursor.
- Comments and reroutes included by same rule.
- Wires included if both endpoint-nodes are within rect.

**Mouse-up:**
- Apply selection per modifier mode (replace / add / toggle).
- Return to IDLE.

**Esc:**
- Cancel; selection unchanged from before drag.

Don't draw rubber-band before drag threshold is crossed.

## A.5 DRAG_NODES mode

**Setup at drag start:**
- Snapshot each selected node's starting position.
- Compute drag offset = mouseCanvas - dragStartCanvas.

**Per-frame:**
- Update visual position = snapshotPos + dragOffset.
- Grid snap: round offset to grid units if grid-snap enabled.
- Alignment guides (V2): show orange snap line when within ~8 px of another
  node's edge or center; Shift disables.

**Important:** during drag, mutate **view-model only**, not host model. The
view-model holds `dragOverridePositions: Dictionary<NodeId, Vector2>?` that
the renderer consults. Reasons:
- One command per drag, not per frame.
- Esc cancel clears the override.
- Host not spammed with sub-pixel changes.

**Mouse-up:**
- Build one `MoveNodes` batch command with all moved nodes' final positions.
- Apply via `IGraphCommandSink`.
- Return to IDLE.

**Esc:**
- Restore each node to snapshot position. No command emitted.
- Return to IDLE.

## A.6 DRAG_WIRE_FROM_PIN mode

Richest interaction. Full detail.

**Entry:** LMB-down on a pin, mouse moves past 3 px threshold.

**State held:**
```
sourcePin: PinId
sourceIsOutput: bool
currentHoverPin: PinId?
currentValidation: LinkValidationResult?
wirePreviewPath: Bezier
```

**Per-frame:**
- Anchored end at source pin's screen position.
- Free end at cursor.
- Bezier tangent = `max(50, abs(freeEnd.x - sourceEnd.x) * 0.5)`.
- Hit-test for pins under cursor. If found:
  - Call `ILinkValidator.Validate(sourcePin, hoverPin)`.
  - Valid → green halo + brighter wire.
  - ValidWithCast → yellow halo + dashed yellow wire + "↳ cast" badge.
  - Invalid → red halo + red wire + tooltip showing `reason`.
- Snap-to-pin: within 14 px of compatible pin → wire end snaps to pin center.

**Mouse-up on a pin:**
- Valid → emit `AddLink`. If target input data pin already has a connection,
  also emit `RemoveLinks` for the prior, batched together.
- ValidWithCast → Batch: AddNode(cast) + AddLink + AddLink.
- Invalid → no command. Silent.

**Mouse-up on empty canvas:**
- Open context-aware search popup at cursor.
- Filtered by `PinContextQuery(sourcePin, ...)`.
- Popup's onPick creates node + connects via single Batch.
- Popup cancel does nothing.

**Mouse-up on node body (not specific pin):**
- Snap to nearest compatible pin on hovered node.

**Mouse-up on wire:**
- Insert at the wire's pin endpoint that's a valid target.
- If both ends valid: prefer the one closer to cursor.

**Esc:**
- Cancel; no commands. Return to IDLE.

### Unreal-exact specific behaviors

1. **LMB-drag from output exec pin that already has a wire:** silently steal
   that wire (since exec-out → one wire only). The wire's existing target
   becomes the free end; user drags to a new target.

2. **LMB-drag from input data pin that already has a wire:** start a NEW wire
   originating from this input pin. The cursor becomes "looking for a
   compatible output." If dropped on empty canvas, popup is filtered for
   nodes with compatible OUTPUT pins.

3. **Ctrl+LMB-drag from connected input pin:** STEAL the existing wire.
   Wire's other end (the output side) is now the source; cursor follows
   the freed input-side endpoint.

## A.7 DRAG_WIRE_FROM_MID mode

Ctrl+LMB-drag on existing wire anywhere along its length. Picks up the wire
to re-attach one end.

**Which end?** The end closer to the click point.

After pickup: collapses to DRAG_WIRE_FROM_PIN starting from the un-moved end.

## A.8 Alt+click immediate disconnect

**On pin:** all wires touching the pin removed. Single Batch command.

**On wire midpoint:** the single wire removed.

Show brief snap-effect: disconnected wire ends recoil inward over ~120 ms.

## A.9 Double-click behaviors

| Target | Action |
|---|---|
| Empty canvas | Open search popup at cursor (= Tab). |
| Node header (renamable) | Begin inline rename. |
| Node header (function call to user-defined function) | Navigate into function graph. |
| Node body | If `defaultDoubleClickAction` in catalog, invoke it; else collapse/expand. |
| Pin | Open "promote to variable" dialog. |
| Wire | Insert reroute at click point. |
| Comment title bar | Begin inline rename. |
| Comment body | Hit-test passes through; node behavior takes precedence. |
| Reroute | Remove reroute, merge wire. |

## A.10 Keyboard shortcuts

See brief §13.

Additional notes:
- **Q (Straighten Connection):** select two connected nodes; pressing Q snaps
  the SECOND one vertically so the wire between them is horizontal. One of
  the most-loved Unreal shortcuts; almost no one discovers it organically.
- All shortcuts go through the command API (§D.0 / brief §26) so they're
  remappable by host.

## A.11 Camera/viewport

(Brief §6.)

### Inertia (Polish)

After releasing pan, continue with decaying velocity over ~250 ms. Tunable;
some users hate it. Provide an off toggle in settings.

## A.12 Visual feedback timings

See brief §28 table.

## A.13 What to do when the user does something wrong

| Situation | Response |
|---|---|
| Connect incompatible pins | Red halo + tooltip during drag. Silent on drop. No error toast. |
| Connect two outputs to one data input | Allow; silently replace existing. Tooltip during drag: "Drop to replace". |
| Create cycle on exec graph | Red halo, reason: "Would create a cycle in execution flow." Silent drop. |
| Paste empty clipboard | Silent. No toast. |
| Delete node with many wires | Just do it (no confirm). Undo exists. |
| Delete with empty selection | Silent no-op. |
| Undo past beginning | Silent (button greyed). |
| Drag node to infinity | No clamping. F (frame all) gets user back. |
| Add unknown node kind | Toast: "Unknown node kind: {kind}". Red. |
| Tabs with same name | Disambiguate: "MyFunction (1)", "MyFunction (2)". |

### Toast notifications

Bottom-right corner, non-blocking. Examples:
- "Undid: Move 5 nodes" (after Ctrl+Z).
- "Pasted 8 nodes, 3 external links not copied" (after cross-graph paste).
- "Compile failed: 2 errors" (with "View" link opening Output panel).

3-second auto-dismiss. Stack vertically. Max 3 visible; older collapse into
"+2 more".

## A.14 Performance budget per frame

See brief §27 table. Key optimizations:
- **Virtualize** via spatial index. Only render visible.
- **Low-zoom simplified rendering** below 0.5×.
- **Cache** per-node measurements; bezier samples.
- **Skip hover effects** above ~500 visible nodes.

## A.15 Resolved design decisions

For reference (already locked):
- LMB drag from input pin: matches Unreal (popup filters for compatible outputs).
- Ctrl+drag to steal: confirmed.
- Cycle detection: client-side quick check + server-side validator on commit.
- Many pins on node: grow vertically; "advanced pins" disclosure hides flagged ones.
- Inline rename: on node headers for renamable kinds.
- Connection snap animation: include.
- Many outputs → one data input: forbidden; new connection replaces old silently.
- One exec output → many exec inputs: forbidden (use Sequence node).
- Many exec outputs → one exec input: allowed.
