# D.8 — Comments and Reroutes

## D.8.1 Comments — data model

```csharp
public sealed record CommentBox(
    CommentId Id,
    string Text,
    Vector2 Position,
    Vector2 Size,
    Vector4 Color,
    int ZOrder,
    bool MoveWithContents);
```

Text is a single field. `\n` produces multi-line. Header strip grows
vertically to accommodate.

**Do not implement a separate body text region.** Only one text field.

## D.8.2 Comments — visual

```
┌─ Boss Phase 2 ───────────────────────┐   ← header strip ~24px, full alpha
│                                      │
│   (nodes inside)                     │   ← body, 20% alpha
│                                      │
└──────────────────────────────────────┘
```

- Header strip: ~24 px (scaled with zoom), full alpha of `Color`.
- Body: same color at 20% alpha.
- 1-px border at full alpha.
- 4-px corner radius.
- Resize handles when selected: 4 corners + 4 edge midpoints (8 total).
- Selection outline: 2-px theme accent overlay.

### Paint order

Comments render at paint step 2, BEHIND wires and nodes:

1. Background grid.
2. Comment bodies and headers.
3. Wires.
4. Nodes.
5. Comment selection outlines (rendered on top, even though body is behind,
   so selection is visible).
6. Reroute glyphs.
7. UI overlays (pin halos, drag-wire ghost, snap indicators).

## D.8.3 Comments — creation

Three paths:

1. Select nodes + `C` → comment created around selection (auto-sized with
   ~16 px padding).
2. RMB empty canvas → "Add Comment" → empty comment at cursor.
3. Paste from clipboard.

After creation, comment enters inline rename mode immediately. Cursor in
text field, focus on it; press Enter or click outside to commit.

## D.8.4 Comments — color palette

Default cycle by creation order:
1. Blue (#4A90E2)
2. Green (#7ED321)
3. Yellow (#F8E71C)
4. Orange (#F5A623)
5. Red (#D0021B)
6. Purple (#9013FE)
7. Cyan (#50E3C2)
8. Brown (#8B572A)

Cycle index = `existingCommentCount % 8`.

User can override per-comment via context menu → Color or Details panel
color swatch.

## D.8.5 Comments — interactions

| Action | Result |
|---|---|
| LMB click header | Select + initiate DRAG_COMMENT |
| LMB click body | Pass through to underlying entities (acts like empty canvas) |
| LMB double-click header | Inline rename |
| LMB drag handle | Resize |
| RMB header | Context menu |
| Hover header | Grab cursor |
| Hover body | Pass-through cursor |
| Drag from body onto comment header | Move (using comment's drag rules) |

Important: **clicking the body acts like clicking through to the canvas.**
This is what makes "click in comment to box-select nodes inside" work.

## D.8.6 Move with contents

When dragging a comment with `MoveWithContents = true`:

1. At drag start, snapshot all nodes fully enclosed by the comment box.
2. During drag, those nodes move with the comment (their relative positions
   to the comment preserved).
3. **Nodes that "fall in" during drag do NOT join.** Membership locked at
   drag start.

Modifier overrides:
- Shift held during drag: move comment alone (no contents).
- Alt held during drag: move only contents (comment stays).

Per-comment `MoveWithContents` toggle in Details panel.

## D.8.7 Comments — z-ordering

Comments overlap. Higher `ZOrder` draws later (on top).

- New comments get max ZOrder + 1.
- Bring to Front: ZOrder = current max + 1.
- Send to Back: ZOrder = current min - 1.
- Renormalize occasionally (when range exceeds 10000, compact to [0, N)).

Selection priority NOT affected by ZOrder for headers — top-most by
ZOrder wins when multiple headers overlap; for bodies, all click-through.

## D.8.8 Comments — context menu

```
Rename                 F2
─────
Color →     Blue
            Green
            Yellow
            Orange
            Red
            Purple
            Cyan
            Brown
            ─────
            Custom…
            Reset to Default
─────
Bring to Front         Ctrl+]
Send to Back           Ctrl+[
─────
Resize to Fit Contents
Move with Contents:    ☑   (toggle)
─────
Cut                    Ctrl+X
Copy                   Ctrl+C
Duplicate              Ctrl+D
Delete                 Del
```

"Resize to Fit Contents" computes AABB of currently-enclosed nodes + 16 px
padding.

## D.8.9 Comments — inline rename

Double-click header. Text field replaces title with full text editable.
Multi-line allowed (Shift+Enter inserts newline; Enter commits).

Esc reverts.

## D.8.10 Reroutes — model

```csharp
// Inside Link.Waypoints
public sealed record Link(
    LinkId Id,
    PinId FromPin,
    PinId ToPin,
    LinkStyle Style,
    IReadOnlyList<Vector2> Waypoints);  // canvas positions; index ordered along wire
```

**Reroutes are nested inside Link, not standalone entities.**

This means:
- A wire with N reroutes is a single Link with N waypoints.
- Removing a reroute leaves wire intact (just merges segments).
- LinkId stable regardless of reroute count.
- Selection of a "reroute" actually selects (Link, waypointIndex).

Selection model exposes a virtual `RerouteRef`:
```csharp
public readonly record struct RerouteRef(LinkId LinkId, int WaypointIndex);
```

## D.8.11 Reroutes — visual

- Small filled circle, ~12 px diameter at zoom 1.0.
- Color = wire color.
- Slightly darker outline (~1.5 px).
- Selected: 2-px theme accent outline.
- Hovered: scaled to ~1.2×.

## D.8.12 Reroutes — wire rendering

A wire with N waypoints renders as N+1 bezier segments. Each segment uses
the same tangent-strength rule on its own segment endpoints
(`max(50, |dx|*0.5)`).

Direction arrow on exec wires: rendered on segment closest to midpoint of
total wire length.

## D.8.13 Reroutes — creation

Three paths:

1. Double-click on a wire → insert reroute at click point.
2. RMB on wire → "Insert Reroute Node Here".
3. Wire drag → release on empty canvas → picker prefixes "+ Add Reroute" at
   top, accessible by typing or first arrow-down.

## D.8.14 Reroutes — interactions

| Action | Result |
|---|---|
| LMB click | Select |
| LMB drag | Move; both wire segments follow |
| LMB double-click | Remove reroute; wire segments merge |
| RMB | Context menu (Delete, Cut/Copy/Duplicate) |
| Alt+click | Remove reroute (same as double-click) |
| Shift+LMB | Add to selection |
| Ctrl+LMB | Toggle in selection |

## D.8.15 Reroutes — typing

Reroutes implicitly inherit the wire's type. No casting. Color matches wire.

If wire's source/target type changes (e.g., wildcard resolves later), reroute
color updates.

## D.8.16 Reroutes — context menu

```
Delete                 Del
Cut                    Ctrl+X
Copy                   Ctrl+C
Duplicate              Ctrl+D
─────
Disconnect Wire        Alt+Click
```

## D.8.17 Reroutes — selection participation

When the user box-selects a region containing reroutes, reroutes participate.
Selected reroutes:
- Highlighted with selection outline.
- Drag-with-selection: move with other selected entities.
- Delete: removes the reroute (does not break the wire).

Reroutes are NOT auto-selected when the wire is selected. Wire and reroute
selections are independent (a wire is just a link reference; reroutes are
(link, index) refs).

## D.8.18 Implementation notes

The command system has:
- `InsertReroute(LinkId, Vector2 Position)` — appends or inserts at correct
  position along wire (computed by canvas).
- `MoveReroute(LinkId, int WaypointIndex, Vector2 NewPosition)`.
- `RemoveReroute(LinkId, int WaypointIndex)`.
- All wrap into Batch for multi-edit operations.

Position-along-wire calculation: compute approximate t-value along the
existing wire path at the click point, then insert waypoint at correct index.

## D.8.19 Comments — copy/paste

Copy: serialize text, color, size, MoveWithContents, and (optionally) the
contained nodes. Two modes:
- Copy comment only: just the box (no contents).
- Copy with contents: contents come along (separate menu item).

Paste at cursor position. If pasting with contents, all node IDs regenerated
to new GUIDs.

## D.8.20 Reroutes — copy/paste

Reroute selection copy → produces a "fragment" with relative positions. On
paste, reroutes are NOT pasted on their own — they require a wire context.

If user copies a wire-with-reroutes (via "Select Connected Nodes" + Copy),
the reroutes are preserved as part of the link.
