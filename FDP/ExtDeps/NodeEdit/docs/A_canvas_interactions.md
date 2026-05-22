# Part A — Canvas Interaction Specification

## Overview

This file specifies every interaction the user can perform on the canvas: pan, zoom, selection, dragging nodes, creating wires, all keyboard shortcuts, and all visual feedback timings. Read this file before implementing any canvas behavior.

Companion files: `B_mini_editors.md` (inline pin editors), `C_generic_picker.md` (popup for node search and other long lists).

## A.1 The interaction state machine — top level

The canvas at any moment is in exactly one interaction mode. Transitions are explicit.

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
   IDLE + Ctrl+drag from   →  DRAG_WIRE_FROM_MID  (move-existing-wire)
       connected input pin
   IDLE + Alt+click pin    →  immediate disconnect-all-on-pin
   IDLE + double-click wire →  insert reroute (immediate)
   IDLE + Tab / Space      →  OPEN_SEARCH_POPUP
   IDLE + RMB on empty     →  CONTEXT_MENU_CANVAS
   IDLE + RMB on node      →  CONTEXT_MENU_NODE
   IDLE + RMB on pin       →  CONTEXT_MENU_PIN
   IDLE + RMB on wire      →  CONTEXT_MENU_WIRE
```

Two rules apply globally:

- **Esc cancels** any active drag/popup, restoring IDLE.
- **Mouse-leaving-canvas during a drag does not cancel** — only mouse-up or Esc cancels.

## A.2 IDLE behaviors (hover)

Per-frame hover detection uses the spatial index. The procedure:

1. Compute mouse canvas position from screen position + viewport state.
2. Hit-test in this priority order (highest to lowest):
   1. Reroutes
   2. Pins
   3. Wires
   4. Comment title bars
   5. Node bodies
   6. Comment bodies
   7. Empty
3. Set the matching `HOVER_*` state.

Cursor update rules:

| Hover target | Cursor |
|---|---|
| Empty canvas | Default arrow |
| Node body | Grab (open hand) |
| Pin | Crosshair with plus glyph |
| Wire | Generic move/select cursor |
| Reroute | Four-way move |
| Comment title bar | Grab |
| Comment body | (treated as empty — passes through) |
| Resize handle (selected comment) | Directional resize arrow |

**Tooltip rule:** A tooltip appears after **600 ms** of hover without mouse movement. The tooltip:
- For a node header: full node description from catalog + any deprecation warning.
- For a pin: `Name : Type. Tooltip text. (Default: 5.0)`. Includes current value if debugging is paused.
- For a wire: `Float → Float, link id 0x…`. Mostly debug, kept cheap.
- For a status icon on a node (error/warning ⚠): the full diagnostic message.

The tooltip does **not** appear if any drag is active or popup is open.

Pins beat wires in hit-test priority because the wire hit area is generous and overlaps the pin region. Comment title bars beat node bodies so dragging a comment that overlaps nodes works. Comment bodies are last so box-selecting on top of a comment works.

## A.3 Selection — exact rules

**Click on node (no modifier):**

- If the node is **not** in the current selection:
  - Clear selection.
  - Select this node.
  - Set primary selection = this node.
  - Begin `DRAG_NODES` containing just this one node.
- If the node **is** in the current selection:
  - Keep selection as-is (do not clear).
  - Set primary selection = this node.
  - Begin `DRAG_NODES` containing the whole selection.
- Mouse-up without crossing the drag threshold (≥4 pixels): treat as click only — apply selection logic, no movement, no command emitted.

This rule (clicking unselected makes it the only selection, then drags) matches Unreal exactly. Confirmed.

**Shift+click on node:**

- Add to selection if not already there; no-op if already there.
- Set primary = the clicked node (so Details panel updates).
- Does **not** initiate drag on the click itself. Only if the mouse subsequently moves past the threshold.

**Ctrl+click on node:**

- Toggle the node's presence in the selection.
- If now selected, primary = this node; if now deselected, primary = any other selected node, or null.
- No drag.

**Click on empty space:**

- Clear selection. Begin `BOX_SELECT` in *replace* mode.

**Shift+click on empty space:**

- Begin `BOX_SELECT` in *additive* mode — boxed nodes will be added to existing selection.

**Ctrl+click on empty space:**

- Begin `BOX_SELECT` in *toggle* mode — boxed nodes are toggled in/out of selection.

**Click on selected primary while it's already primary:**

- No-op. Do not restart anything.

**Click on comment title bar:**

- Same rules as click on node. Comment becomes the primary selection target.

**Drag threshold:** 4 pixels of mouse movement before a click is reinterpreted as a drag.

## A.4 BOX_SELECT mode

State: rubber-band rectangle from drag start to current mouse position. Drawn as a thin dashed rectangle with semi-transparent fill.

**While dragging:**

- Highlight all nodes whose AABB is **fully enclosed** by the rect (default).
- **Alt held** during drag: highlight nodes whose AABB **intersects** the rect (more permissive, Unreal style). Show a small label near the cursor: `+ Touching`.
- Comments and reroutes use the same enclosure/intersection rule.
- Wires: included if both endpoints are within the rect (or both endpoint-nodes are selected).

**Mouse-up:**

- Apply selection per modifier mode (replace / add / toggle).
- Return to IDLE.

**Esc during BOX_SELECT:**

- Cancel; selection unchanged from before the drag started.
- Return to IDLE.

Do not draw the rubber-band if the user has not yet moved past the drag threshold — prevents flickering on click-release.

## A.5 DRAG_NODES mode

**At drag start (after threshold crossed):**

- Snapshot each selected node's starting position.
- Compute the drag offset = mouseCanvas − dragStartCanvas.

**Per-frame during drag:**

- Update each node's *visual* position = snapshotted + offset.
- The view-model holds a `dragOverridePositions: Dictionary<NodeId, Vector2>?` that the renderer consults if set. The host model is **not** mutated during the drag.
- If grid-snap is enabled (toggle in toolbar), round the offset to grid units before applying.

**Snap-to-other-nodes (alignment guides), V2 feature:**

- When moving a node, check if it's within ~6 pixels of another node's left/right/top/bottom edge or center.
- If so, draw a thin orange guide line and snap to that edge.
- Disabled while Shift is held.

**Mouse-up:**

- Build a single `MoveNodes` batch command with all moved nodes' final positions.
- Apply via `IGraphCommandSink`.
- Clear the drag override.
- Return to IDLE.

**Esc during DRAG_NODES:**

- Restore every node to its snapshot position by clearing the drag override.
- No command emitted.
- Return to IDLE.

**Critical:** during the drag, the canvas shows new positions but the host's model is **not** mutated until mouse-up. Reasons:
- One command per drag (not one per frame).
- Esc-cancel just clears the override.
- The host is never spammed with sub-pixel changes.

## A.6 DRAG_WIRE_FROM_PIN mode

**Entry:** LMB-down on a pin, mouse moves past drag threshold (≥3 px).

**State held during drag:**

- `sourcePin: PinId`
- `sourceIsOutput: bool` (determines which end follows the cursor)
- `currentHoverPin: PinId?`
- `currentValidation: LinkValidationResult?`
- `wirePreviewPath: Bezier`

**Per-frame:**

- The wire's anchored end is at the source pin's screen position.
- The free end follows the cursor.
- Bezier tangent strength = `abs(freeEnd.x − sourceEnd.x) * 0.5`, with a minimum (~50 px) so very-close drops still look curved.
- Hit-test for pins under the cursor. If a pin is under the cursor:
  - Call `ILinkValidator.Validate(sourcePin, hoverPin)`.
  - If `Valid` → green halo on the target pin + brighter wire.
  - If `ValidWithCast` → yellow halo + wire dashed yellow + small `↳ cast` badge near cursor.
  - If `Invalid` → red halo + red wire + tooltip near cursor showing `reason`.
- **Snap-to-pin:** when the cursor is within ~14 px of a compatible pin, the wire free end *snaps* to that pin's center. Critical tactile feel — pins are sticky.
- The popup is **not yet open** during the drag itself.

**Mouse-up on a pin:**

- If validation says `Valid`:
  - Emit `AddLink` command.
  - If the target input pin already has a connection (data input), also emit `RemoveLinks` for the prior link. Both batched together.
- If `ValidWithCast`:
  - Emit a `Batch` containing `AddNode(autoInsertCastKind, atMidpoint)` + two `AddLink`s.
- If `Invalid`:
  - Emit nothing. Silent return to IDLE. The user already saw the red halo.

**Mouse-up on empty canvas:**

- Open the **context-aware node search popup** at cursor position.
- Popup is pre-filtered by `PinContextQuery(sourcePin, …)`.
- On Confirm (Enter or click): create the node *and* connect it via a single batch command.
- On Cancel (Esc or click-outside): do nothing.
- Return to IDLE either way.

**Mouse-up on a node body (not on a specific pin):**

- Snap to the nearest compatible pin on the hovered node and connect.
- If no compatible pin exists: same as mouse-up on empty canvas (open popup).
- This matches Unreal's behavior — simpler and more predictable than pin-selection menus.

**Mouse-up on a wire:**

- Insert the new connection at the wire's pin endpoint that's a valid target.
- If both ends are valid targets, prefer the one closer to the cursor.

**Esc during DRAG_WIRE_FROM_PIN:**

- Cancel; no commands emitted. Return to IDLE.

### Wire merge / replace rules

Following Unreal exactly:

| Scenario | Behavior |
|---|---|
| One data output → many data inputs | Allowed. Reading from a single value doesn't mutate. |
| Many data outputs → one data input | Forbidden. Dropping a new wire on a data input pin that already has one **replaces** the prior wire silently. Tooltip during drag: `Drop to replace existing connection`. |
| One exec output → many exec inputs | Forbidden. Use a `Sequence` node. Validator returns Invalid with reason `Output exec pin can drive only one wire; insert a Sequence node to fan out`. |
| Many exec outputs → one exec input | Allowed. Multiple events can trigger the same path. |

`IPinModel.AcceptsMultipleConnections` is **computed**, not stored:
- `Direction == Output && Kind == Data` → true (one source feeds many readers).
- `Direction == Input && Kind == Exec` → true (many triggers feed one input).
- Otherwise → false.

### Input-pin LMB-drag — Unreal behavior

LMB-drag from an input pin creates a new wire originating from that input pin, treating it as the source for the drag. Cursor end is treated as needing an output. If the user drops on empty area, popup is filtered for nodes with compatible **output** pins.

**Ctrl+LMB-drag from a connected input pin** = steals the existing wire. The wire's output end becomes the source, the input pin is now free, cursor follows the wire end that used to be at the input pin.

**Output-pin LMB-drag** with an existing wire: silently steal that wire. Unreal forbids one exec-out to many exec-ins, so the only sane interpretation of dragging a new wire from a connected exec-out is "move it." Same logic for data outputs — but since data outputs already support fan-out, dragging from a connected data output creates a *new wire* rather than stealing. The distinction:
- Connected **exec** output + LMB-drag = steal (can't have two outgoing).
- Connected **data** output + LMB-drag = create new outgoing wire (data outputs fan out).

## A.7 DRAG_WIRE_FROM_MID mode

Ctrl+LMB-drag on an existing wire (anywhere along its length). Picks up the wire and lets the user re-attach one end.

**Which end gets picked up?** The end closer to the click point on the wire. The other end stays anchored.

After pickup, this collapses into a standard `DRAG_WIRE_FROM_PIN` starting from the unmoved end. All A.6 rules apply.

## A.8 Alt+click immediate disconnect

**On pin:** all links touching this pin are removed in a single batch command (undoable).

**On wire midpoint:** the single wire is removed.

Visual: brief snap-effect — disconnected wire ends recoil slightly inward over ~120 ms.

## A.9 Double-click behaviors

| Target | Action |
|---|---|
| Empty canvas | Open search popup at cursor (same as Tab) |
| Node header (renamable) | Begin inline rename in header text field |
| Node header (function call to user-defined function) | Navigate into that function's graph (open new tab or switch to existing) |
| Node body | If node has a `defaultDoubleClickAction` in its catalog entry (e.g., "open struct editor"), invoke it. Otherwise: collapse/expand the node |
| Pin | Open the "promote to variable" dialog |
| Wire | Insert a reroute node at the click point on the wire. Wire splits into two segments |
| Comment title bar | Begin inline rename |
| Comment body | If it overlaps a node, the node's behavior takes precedence (hit-test priority); otherwise no-op |
| Reroute node | Remove the reroute, rejoining the wire |

## A.10 Keyboard shortcuts (defaults)

All shortcuts should be remappable via a settings file. Defaults follow Unreal conventions.

| Key | Action |
|---|---|
| `Ctrl+Z` / `Ctrl+Shift+Z` (or `Ctrl+Y`) | Undo / Redo |
| `Ctrl+C` / `Ctrl+X` / `Ctrl+V` | Copy / Cut / Paste |
| `Ctrl+D` | Duplicate selection |
| `Ctrl+A` | Select all |
| `Delete` / `Backspace` | Delete selection |
| `Tab` (or `Space`) | Open search popup at cursor |
| `F` | Frame selection (or frame-all if no selection) |
| `Home` | Frame all |
| `End` | Frame primary selection only |
| `Ctrl+F` | Open Find Results panel |
| `Ctrl+S` | Save (host-defined) |
| `F7` | Compile (host-defined) |
| `F9` | Toggle breakpoint on primary selection |
| `F5` | Resume execution (when paused in debug) |
| `F10` / `F11` / `Shift+F11` | Step over / into / out |
| `C` | Add comment around selection |
| `Q` | Straighten connection — align two connected nodes so their pins share a Y |
| `Ctrl+E` | Collapse selection to function |
| `Alt+drag` | (during box-select) touch-mode selection |
| `Shift+drag node` | Disable alignment-guide snap |
| `Ctrl+drag wire-end` | Steal existing wire |
| `Alt+click pin/wire` | Disconnect |
| `Esc` | Cancel current operation / close popup |
| `←↑→↓` (when nodes selected) | Nudge selected by 1 px (Shift = 10 px) |
| `Ctrl+G` | Group selected nodes (collapse to comment) |
| `Ctrl+B` | Bookmark current viewport position |
| `Ctrl+1..9` | Jump to bookmark slot 1..9 |
| `Ctrl+Shift+1..9` | Set bookmark slot 1..9 to current viewport |
| `F2` | Rename selected entity |
| `F4` | Focus Details panel on selected entity |
| `F12` | Go to definition |

The `Q` shortcut (straighten connection): when two connected nodes are slightly misaligned vertically, select both, hit `Q`, and the *second* one (downstream of the first) snaps so their connected pins are at the same Y coordinate.

## A.11 Camera and viewport interactions

**Pan:**

- Middle-mouse drag, **or**
- Right-mouse drag (alternative; Unreal default), **or**
- Space+LMB drag (alternative).
- Cursor changes to four-arrow during pan.

**Zoom:**

- Mouse wheel, zoomed *toward the cursor position*. Compute world-point under cursor before zoom, apply zoom, recompute pan so the same world-point is still under the cursor.
- Zoom range: **0.25× to 3.0×**.
- Below ~0.5× zoom-out: switch nodes to a **simplified low-zoom rendering**. Solid color block matching header color, no text, no pins. Massively improves big-graph readability.
- `Ctrl+0` = reset zoom to 1.0. `Ctrl++` / `Ctrl+-` = zoom by step (with cursor-centered zoom).

**Frame all / Frame selection:**

- Compute the bounding box of the target set (all nodes or just selected).
- Add ~10% padding around the box.
- Compute the zoom that fits the box in the viewport.
- Compute the pan that centers the box.
- Animate over **180 ms** with ease-out-cubic from current viewport to target. Animate both pan and zoom simultaneously, not sequentially.

**Pan inertia (Polish, optional):**

- After releasing pan, continue with decaying velocity for ~250 ms. Tunable; some users dislike it. Off by default.

## A.12 Context menus

### Empty canvas (RMB on empty)

```
Add Node…              Tab
Add Comment            C       (only if there's a selection)
─────────
Paste                  Ctrl+V
─────────
Frame All              Home
Reset Zoom             Ctrl+0
```

### Node (RMB on node)

```
Cut                              Ctrl+X
Copy                             Ctrl+C
Duplicate                        Ctrl+D
Delete                              Del
─────────
Disable Node                     Alt+E
Break All Links
─────────
Find References                  Ctrl+F
Go to Definition                          (if function call)
Find in Catalog
─────────
Refactor →
    Rename                        F2     (renamable kinds)
    Collapse to Function        Ctrl+E
    Collapse to Macro
    Collapse to Comment
    Expand Node                            (collapsed call nodes)
─────────
Toggle Breakpoint                  F9
─────────
Properties…                       F4
Documentation…                            (if catalog entry has docs URL)
```

### Pin (RMB on pin)

```
Break Link(s)                  Alt+Click
─────────
Promote to Variable…
Promote to Local Variable…
Split Struct Pin                          (if pin type is a struct)
Recombine Struct Pin                      (if currently split)
─────────
Watch this Value                          (data pins only)
Reset to Default                          (data input pins with default)
─────────
Refresh Node                              (re-fetch node signature from catalog)
```

### Wire (RMB on wire)

```
Break Link
Select Connected Nodes
─────────
Insert Reroute Node Here
```

### Comment (RMB on comment title bar)

```
Rename                             F2
Color →                                   (8-color palette, custom picker, recents)
─────────
Bring to Front
Send to Back
─────────
Resize to Fit Contents
Move with Contents: ☑
─────────
Delete                              Del
```

## A.13 Copy / Paste mechanics

**Copy:**

- Serialize selected nodes + links *internal to the selection* + selected comments to a JSON blob.
- Strip external links (links from selection to non-selected nodes are dropped on paste, with a toast: `3 connections to external nodes were not copied`).
- Include node positions normalized to (0,0) at the selection's bounding-box top-left.
- Place blob in OS clipboard with a custom content-type marker so cross-app paste doesn't interpret it as text.

**Paste:**

- Deserialize from clipboard.
- Generate new `NodeId`s and `PinId`s for everything. Do not reuse — these are new instances.
- Position at: `cursor canvas position − saved selection top-left`. Cursor sits on the same relative point.
- If cursor is off-canvas: place at viewport center.
- Auto-select the pasted nodes (replace selection).
- Single batch command, single undo step.

**Duplicate (Ctrl+D):**

- Same as copy + paste, but with a fixed offset (~30 px down-right) instead of cursor placement. No clipboard touched.

**Cross-graph paste:**

- The clipboard JSON includes `TypeKey` and `NodeKindKey` references. If pasting into a graph where some referenced types or kinds are invalid, the host's command sink rejects them. Editor shows: `2 nodes could not be pasted (unsupported in this graph type)`.

## A.14 Inline default-value editing on pins

See `B_mini_editors.md` for the full spec of inline pin default editors. Summary:

When an input data pin has no incoming connection and its type has a default-value editor registered, the editor renders the input control *inline next to the pin label*. Edits commit on Enter or focus-loss, debounced during drag — one undo entry per drag-gesture, not 60.

When a wire is connected, the inline editor is hidden (replaced by italic grey text `← wired`). When disconnected, it reappears with the last-edited default.

## A.15 Multi-graph tab strip

A tab bar at the top of the editor window, like Unreal's "Event Graph / Construction Script / MyFunction1" tabs.

**Tab states:**

- Active (currently editing).
- Background (open but not visible).
- Modified (asterisk in label) — when its model is dirty.
- Error (red dot in label) — has compile errors.

**Tab interactions:**

- LMB-click: switch to.
- MMB-click: close. If modified, prompt `Discard changes to {Graph}?`.
- RMB-click: context menu (Close, Close Others, Close All to Right, Pin Tab).
- Ctrl+Tab / Ctrl+Shift+Tab: cycle through tabs.
- Drag a tab to reorder.
- Double-click empty space in tab bar: create new function/graph (host-defined).

**Per-tab state preserved:**

- Viewport pan/zoom (each graph has its own).
- Selection (ephemeral but preserved across tab switches in the same session).
- Active drag/popup: **switching tabs cancels any active drag**.

**Tab persistence across editor sessions:**

- Open tabs and their order saved to editor session state.
- On reopen, restore open tabs + restore the previously-active tab.

## A.16 Debug-mode visual layer

When `IDebugSession.IsAttached`, additional visuals overlay the canvas.

**Currently-executing node:**

- Bright outline pulsing at ~2 Hz (sine-wave alpha).
- Header glows with an additive overlay.
- If off-screen, show an arrow at the viewport edge pointing toward it.

**Recently-executed wires (last N seconds):**

- Brighter color, slightly thicker.
- An animated dash flows along the wire from output to input over ~400 ms — small triangles spaced every ~16 px, moving at ~150 px/sec.
- Fades out over ~800 ms after execution completes.
- Cap concurrently-animating wires at ~20 to avoid framerate hit. Oldest dropped first.

**Recently-executed nodes:**

- Brief afterglow that fades over ~500 ms.

**Breakpoint markers:**

- Red filled circle on node header, left side (16×16 px).
- If breakpoint is stale (per `IsStale` from `IBlueprintDebugSession`), the circle is yellow with a ⚠.
- Hover: tooltip shows hit count + stale reason if applicable.

**Watch markers:**

- Eye icon next to watched pins. Click to remove from watch list.
- When paused, current value shown next to the eye as inline text (truncated to ~20 chars; full value in tooltip).

**Pause overlay:**

- When `IsPaused`, canvas tint shifts subtly (desaturate by ~15%).
- A floating widget top-right: `▶ Resume (F5)  ↷ Step Over (F10)  ↳ Step Into (F11)  ↰ Step Out (Shift+F11)`.

## A.17 Visual feedback timings (canonical)

Reference table for all timed transitions. Tuning these is what makes the editor feel snappy vs sluggish.

| Event | Duration | Easing |
|---|---|---|
| Hover halo appear | 0 ms (immediate) | — |
| Hover halo disappear | 80 ms | linear |
| Selection outline appear | 0 ms | — |
| Frame-to-selection camera move | 180 ms | ease-out-cubic |
| Drag threshold | 4 px | — |
| Tooltip appearance delay | 600 ms | — |
| Tooltip fade-in | 80 ms | linear |
| Wire connect snap-effect | 120 ms | ease-out-cubic |
| Wire disconnect recoil | 120 ms | ease-out-cubic |
| Reroute node insertion | 100 ms scale-in | ease-out-back |
| Node creation appear | 100 ms fade+scale | ease-out-cubic |
| Node deletion fade | 80 ms | linear |
| Wire flow animation period | 400 ms | linear loop |
| Currently-executing pulse | 500 ms period | sine |
| Recently-executed afterglow | 800 ms fade | ease-out |
| Pan inertia (if enabled) | 250 ms | ease-out-cubic |
| Box-select rubber-band appear | 0 ms | — |
| Popup open (search) | 50 ms fade+slide | ease-out |
| Popup close | 80 ms fade | linear |
| Context menu open | 50 ms fade | linear |
| Toast (corner notification) lifetime | 3000 ms | — |

## A.18 Wrong-action responses

| Situation | Response |
|---|---|
| Connect incompatible pins | Red halo on target during drag, tooltip with reason. On drop: emit nothing, silent return to IDLE. No error toast. |
| Connect two outputs to one data input | Allow drop; silently replace existing connection. Drag tooltip: `Drop to replace existing connection`. |
| Make a cycle on exec graph | Red halo, reason `Would create a cycle in execution flow`. On drop: emit nothing. |
| Paste with no clipboard content | Silent — no toast. Don't pester for things the user is allowed to do that just don't have an effect. |
| Delete a node connected to many wires | Just do it — emit batch deleting node + all touching wires. No confirm dialog. Undo exists. |
| Delete with empty selection | Silent no-op. |
| Undo past the beginning | Silent (button greyed out in host UI). |
| Drag a node off-canvas to infinity | No clamping — node can be at any coordinate. `F` (frame selection) gets the user back. |
| Add a node whose kind no longer exists | Toast: `Unknown node kind: {kind}`. Red. |
| Two graphs in tabs with the same name | Tab labels disambiguate: `MyFunction (1)`, `MyFunction (2)`. |

**Toast notifications** are corner popups (bottom-right) for non-blocking info. Auto-dismiss after 3 seconds. Stack vertically. Max 3 visible at once; older toasts collapse into `+2 more` indicator.

Examples:
- `Undid: Move 5 nodes` (after Ctrl+Z, briefly).
- `Pasted 8 nodes, 3 external links not copied` (after cross-graph paste).
- `Compile failed: 2 errors` (with a "View" link that opens the Output panel).

## A.19 Performance budget per frame

Target framerate: **60 FPS vsync** (16.6 ms total per frame, ~4 ms reserved for raylib + other panels + ImGui rendering itself).

| Phase | Budget at 500 nodes | Budget at 2000 nodes |
|---|---|---|
| Hit-testing | ≤ 0.1 ms | ≤ 0.2 ms |
| Spatial-index update for moving nodes | ≤ 0.2 ms | ≤ 0.5 ms |
| Visible-node enumeration | ≤ 0.05 ms | ≤ 0.1 ms |
| Node rendering (visible only) | ≤ 3 ms | ≤ 5 ms (with low-zoom mode) |
| Wire rendering (visible only) | ≤ 2 ms | ≤ 4 ms |
| ImGui submission | ≤ 1 ms | ≤ 2 ms |
| **Total canvas budget** | **≤ 6 ms** | **≤ 12 ms** |

Optimizations required to meet these budgets:

- **Virtualize.** Only render what's in viewport (spatial index, see `K06_spatial_index.md`).
- **Low-zoom simplified rendering.** At zoom < 0.5, replace nodes with header-color blocks, skip pin rendering, render wires as straight lines.
- **Cache per-node measurements.** Don't recompute node size every frame; only when content changes.
- **Cache bezier sample points.** A wire's curve depends on its endpoints; cache the sampled points and only recompute when endpoints move.
- **Skip hover effects when many visible.** Above ~500 visible nodes, disable per-node hover animations.
