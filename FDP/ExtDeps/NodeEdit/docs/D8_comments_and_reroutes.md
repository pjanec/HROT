# D.8 — Comments and Reroute Nodes

Both are visual decorations on the canvas — they don't represent execution, they help humans read the graph. Different interaction models, so specced separately.

## Comment boxes

### D.8.1 What they are

A colored rectangle behind nodes with editable text. Visually like Unreal's comments: a translucent fill, a header strip with the title, can be resized, can be colored.

```
┌─ Combat resolution ────────────────────────────┐   ← title bar, colored
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │   ← body, semi-transparent same color
│  ┌─────────┐    ┌─────────┐                    │
│  │ Node A  │───→│ Node B  │                    │
│  └─────────┘    └─────────┘                    │
│                                                │
│  ┌─────────┐                                   │
│  │ Node C  │                                   │
│  └─────────┘                                   │
└────────────────────────────────────────────────┘
```

### Data model

```csharp
public sealed record CommentBox(
    CommentId Id,
    string Text,
    Vector2 Position,           // top-left in canvas coords
    Vector2 Size,
    Vector4 Color,              // RGBA, alpha ~0.2 for body
    int ZOrder,                 // for stacking when comments overlap
    bool MoveWithContents);
```

`Color` is stored as the *header* color; the body uses the same RGB at ~15-25% alpha. One color value, two visual treatments.

### D.8.2 Creation

Three paths:

**Path 1: From selection (most common)**

- User selects 1+ nodes, presses `C` (or right-click → Add Comment, or `editor.add-comment` command).
- Editor computes bounding box of selection, adds ~24 px padding around it.
- Creates a comment box with default color (a session-default that remembers user's last picked color).
- Comment text defaults to empty; immediately enters inline rename mode so user types a title.
- Enter / focus-out commits the title.
- Esc during initial title entry **deletes the comment** (treats whole creation as cancelled).

**Path 2: From empty canvas**

- Right-click empty area → Add Comment.
- Creates a default-sized comment (~300 × 200 canvas units) at click position.
- Immediately enters inline rename.

**Path 3: From clipboard paste**

- Pasting a previously-copied comment recreates it at cursor offset.

### Default color sequence

When the user creates multiple comments without picking colors, cycle through a palette:

```
Blue → Green → Yellow → Orange → Red → Purple → Cyan → Brown → back to Blue
```

Subtle but good default — a typical graph with 5 comments has 5 colors instead of identical.

### D.8.3 Visual rendering rules

The comment renders **behind** all nodes (lowest Z order in canvas paint).

Order of canvas drawing:

```
1. Background (grid, origin)
2. Comment bodies (sorted by ZOrder ascending)
3. Wires
4. Nodes
5. Reroute nodes
6. In-progress drag visuals (rubber band, dragging-wire preview)
7. Selection outlines (drawn over everything they apply to)
```

**Comment header strip:**

- Sits at the top of the comment, ~24 px tall.
- Full opacity of the color.
- Contains the title text (left-aligned, padded ~8 px).
- The header is the **only hit target for click-drag-to-move and click-to-select**. The body is click-through to nodes underneath.

**Body:**

- Same color as header, ~20% alpha.
- A thin border at full opacity (~1 px) around the whole rectangle for definition.
- No hit target — clicks pass through to anything underneath.

**Resize handles:**

- Visible only when the comment is selected.
- Small grab dots at the 4 corners and 4 edge midpoints.
- Each gives a directional resize cursor on hover.

**Selection outline:**

- 2 px outline at full opacity around the whole rectangle.
- Drawn at paint step 7, on top of everything.

**Low-zoom rendering:**

- When zoom < 0.5, comments fade to ~50% of their normal alpha and the title text is rendered larger relative to zoom (so it stays readable when zoomed way out).
- Matches Unreal — comments become navigation landmarks at low zoom.

### D.8.4 Interactions

**LMB-click on header:**

- Selects the comment (clearing other selection unless Shift/Ctrl).
- Begin drag-comment if mouse moves past threshold.

**LMB-click on body:**

- Passes through. If a node is under the click → node interaction. Otherwise → empty-canvas behavior (clear selection, begin box-select).

This is the key rule: users *constantly* want to box-select nodes that visually sit on top of a comment. Click-body = click-through makes this trivial. **Confirmed in design.**

**LMB-double-click on header:**

- Inline rename. Header text becomes editable.

**LMB-drag on header:**

- Move the comment. If `MoveWithContents` is true, all enclosed nodes move too.

**LMB-drag on resize handle:**

- Resize. Body and header resize together. The header height stays constant (~24 px).

**RMB on header:**

- Context menu (see D.8.7).

**RMB on body:**

- Treated as RMB on canvas (since body is click-through).

**Hover on header:**

- Cursor: grab hand.
- Tooltip: full comment text if it's truncated in the header.

**Hover on resize handle:**

- Cursor: directional resize arrow.

### Drag threshold

Same 4-pixel threshold as nodes — small jitter on click doesn't start a drag.

### Snap to grid

When grid-snap toggle is on, comment position and size both snap. Many users keep snap on for comments and off for nodes.

### D.8.5 "Move with contents"

Unreal's behavior: when you drag a comment, all nodes *fully inside* the comment's rectangle move with it.

**Containment is checked at the *start* of the drag, not continuously.**

- Editor takes a snapshot at mouse-down: which nodes are fully enclosed?
- Those nodes get pinned to the comment's relative position for the duration of the drag.
- Nodes that happen to "fall inside" during drag do NOT join.

**Node intersection counts as "fully inside."**

- A node whose AABB is entirely within the comment's AABB (allowing the header strip area) is "contained."
- Partial overlap doesn't count — that node stays put while the comment slides over it.

**Hold Shift while dragging the comment** = move comment alone, ignore contents. (Unreal binding; matches.)

**Hold Alt while dragging** = reverse — move only the contents, comment stays. Lets you "extract" nodes from a comment. (V2.)

### Per-comment override

`CommentBox.MoveWithContents: bool` — defaults to true. The Details panel exposes it. A user can pin a comment as "never move contents."

### D.8.6 Resize behavior

While resizing:

- Show current size in canvas units as small label near cursor: `420 × 280`.
- Aspect-ratio unconstrained (no auto-square unless Shift held; even then, optional).
- Minimum size: roughly enough to hold the title text (~120 × 48). Clamp below that.

If resize would shrink the comment to **exclude** previously-enclosed nodes, those nodes are silently un-pinned for the next move-with-contents cycle. No notification.

**"Resize to fit contents"** — right-click option that auto-sizes the comment to enclose all nodes currently inside its AABB with ~24 px padding.

### D.8.7 Comment context menu

Right-click on header:

```
Rename                          F2
─────────
Color →                         (swatch palette)
    [█][█][█][█][█][█][█][█]    8 named colors
    ─────────
    Custom Color…               (opens ImGui color picker popup)
    ─────────
    Reset to Default
─────────
Bring to Front                  Ctrl+]
Send to Back                    Ctrl+[
─────────
Resize to Fit Contents
Move with Contents: ☑           (toggle)
─────────
Cut                             Ctrl+X
Copy                            Ctrl+C
Duplicate                       Ctrl+D
Delete                          Del
```

### Color palette

8 named colors in a fixed palette. Host can override via:

```csharp
public interface IEditorTheme
{
    IReadOnlyList<(string Name, Vector4 Color)> CommentColorPalette { get; }
    Vector4 CommentDefaultColor { get; }
}
```

Default palette: Blue, Green, Yellow, Orange, Red, Purple, Cyan, Brown.

A "Recent custom colors" strip appears below the palette (last 8 custom-picked colors, persistent globally).

### D.8.8 Z-ordering

When comments overlap, the visible header at any point belongs to the topmost comment by ZOrder.

- Bring to Front: ZOrder = max + 1.
- Send to Back: ZOrder = min − 1.
- Renormalize Z order occasionally to prevent unbounded growth.

Matters when users wrap an outer comment ("Combat") around several smaller comments. Smaller inner comments need higher Z so their headers are clickable; the outer wrapping comment goes to back.

### D.8.9 Inline rename

Double-click header → text field replaces the header label in place. ImGui input text.

**Comment text is single-line in the header, with `\n` allowed in the same field for multi-line if needed.** Confirmed in design. For long descriptions, users widen the comment or use `\n` line breaks. The header auto-grows taller for multi-line text.

### D.8.10 Performance

Comments are cheap to render — one filled rect + one outline + one text. Even hundreds shouldn't be a problem.

- Hit-test for header uses the spatial index (same as nodes).
- Move-with-contents pin-snapshot at drag start is O(nodes-in-comment), not per-frame.

### D.8.11 Copy / paste

Comments are part of the clipboard payload. Copying a selection that includes a comment includes the comment. Pasting recreates with new IDs.

If a copied comment had `MoveWithContents = true` and nodes inside, the copied selection includes those nodes too (already part of selection by default since they were inside).

### D.8.12 Nested comments

Comments can visually contain other comments. No logical parent-child relationship — they're just stacked rectangles. Z-order determines visibility.

"Move with contents" works recursively naturally: moving the outer comment moves the inner comments (because their AABBs are fully inside) which moves their contents.

## Reroute nodes

### D.8.13 What they are

A tiny pin-only "junction" that lets wires bend deliberately. One input pin and one output pin, both of the same type. Does **nothing** semantically — the compiler treats it as a passthrough.

```
        ┌─────────┐
        │ NodeA   │─────●
        └─────────┘     │
                        │      ●─────┐
                        ●──────┤    │
                                │   ▼
                                ┌──────┐
                                │NodeB │
                                └──────┘
```

Each `●` is a reroute node.

### D.8.14 Visual

```
   ●        (about 12 px diameter at zoom 1.0)
   │
```

- A solid circle in the wire's color.
- Slightly darker outline for definition.
- No label.
- When selected: 2 px outline in the editor's selection color.
- When hovered: slight glow.

Visually identical to a pin. Mentally users think "wires bend at little dots."

### D.8.15 Creation

Multiple paths:

**Path 1: Double-click a wire**

- Click directly on the wire's bezier curve.
- A reroute spawns at the click point (snapped to the wire's actual curve, not strictly at cursor).
- The wire splits: source-output → reroute-input, reroute-output → original-target-input.

**Path 2: RMB on wire → Insert Reroute Node Here**

- Same result as double-click.

**Path 3: From context menu when dragging wire → release on empty canvas**

- Picker popup that opens has a "+ Add Reroute Here" option at the top of the results (always present, doesn't need to fuzzy-match). Selecting it places a reroute at cursor position and connects.

**Path 4: From the search popup**

- Typing "reroute" in the Tab-popup shows it as a node kind. Place anywhere; user wires manually. Less common but possible.

### D.8.16 Interactions

**LMB-click on reroute:**

- Selects it (cleared selection model same as nodes).
- Begin drag if past threshold.

**LMB-drag:**

- Moves the reroute.
- Both wires connected to it follow.
- Snap to grid same as nodes when grid-snap is on.

**LMB-double-click on reroute:**

- **Removes** the reroute. The two wires merge back into one connecting original source to original target.

**RMB on reroute:**

```
Delete Reroute
─────────
Cut / Copy / Duplicate
```

**Alt+click on reroute:**

- Same as RMB → Delete Reroute (consistent with Alt+click on pins/wires).

**Box-select includes reroutes** when their position is inside the rectangle.

**Multi-select with reroutes:**

- Dragging a multi-selection that includes reroutes moves them with the rest.

### D.8.17 Wire routing through reroutes

A wire's path now has multiple segments:

```
Source pin ──► Reroute1 ──► Reroute2 ──► Target pin
```

Each segment is a bezier between adjacent endpoints. Tangent strength still proportional to horizontal distance per segment (same rule as plain wires).

Hit-test for the wire: any segment can be hovered/clicked. The wire's "logical" identity stays one `LinkId` regardless of how many reroutes it passes through.

### D.8.18 Wire model — reroutes nested in links

Confirmed in design: reroutes belong to the **link**, not as standalone graph entities.

```csharp
public sealed record Link(
    LinkId Id,
    PinId FromPin,
    PinId ToPin,
    IReadOnlyList<RerouteNode> Reroutes  // ordered; 0..N
);

public sealed record RerouteNode(
    RerouteId Id,
    Vector2 Position);
```

When the host serializes graphs, reroutes are nested under links. When the compiler walks the graph, reroutes are *invisible* — the compiler sees `FromPin → ToPin` connections and ignores the reroute list. Zero semantic effect.

### Reroutes in the spatial index

Reroutes are indexed for hit-testing as small AABBs (12 × 12 px). Selection logic treats them as their own selectable type (separate from nodes/links/comments).

### D.8.19 Reroute typing

A reroute is implicitly typed by the wire passing through it. Visually colored to match.

If a wire's source or target type changes (e.g., wildcard resolved differently), all reroutes along the wire automatically update their color. They're never typed independently.

### D.8.20 Performance

- One extra bezier segment per reroute. Cheap.
- Reroutes add nothing to compile time (compiler ignores them).
- Spatial index handles them with the same machinery as pins.

### D.8.21 Reroutes vs comments — the mental distinction

|   | Comments | Reroutes |
|---|---|---|
| Purpose | Group / annotate | Bend wires |
| Position | Anywhere | On a specific wire |
| Selection effect | Drag moves contents (optional) | Drag bends the wire |
| Semantic effect | None | None |
| Visual size | Large | Tiny |
| Created from | Selection / RMB | Wire double-click |

They never interact. Comments don't contain reroutes; reroutes don't care about comments.
