# D.9 — Bookmarks

## D.9.1 Data model

```csharp
public sealed record Bookmark(
    string BookmarkId,        // stable; usually GUID string
    GraphId TargetGraph,
    string Label,             // user-defined display name
    Vector2 ViewportPan,
    float ViewportZoom,
    int SlotNumber,           // 1-9 = hotkey-bound; 0 = unbound
    DateTime CreatedAt);
```

Bookmarks live in **editor session state** (per-user, per-asset). Not
committed to asset data.

Scope: **global per asset**. Cross-graph jumps expected (a bookmark in
graph A can be jumped to from graph B).

## D.9.2 Interactions

| Key | Action |
|---|---|
| Ctrl+Shift+1..9 | Set bookmark in that slot to current viewport |
| Ctrl+1..9 | Jump to bookmark in that slot |

Setting an occupied slot: prompts to confirm "Overwrite bookmark in slot 3?"
with Confirm/Cancel.

Cross-graph jump: if bookmark's `TargetGraph` differs from current graph,
opens that graph (in current tab; user can Ctrl+click to open in new tab —
V2).

Camera animates ~180 ms ease-out-cubic.

## D.9.3 Visual indicators

Off-screen bookmarks in current graph: render edge marker arrows on the
canvas edge, pointing toward the bookmark's direction.

```
       ↑(1)
   ↑(3)         ← top edge: two bookmarks off-screen up
─────────────────────
│                   │
│                   │
│       canvas      │← (5)  ← right edge: one bookmark off-screen right
│                   │
─────────────────────
        ↓(7)        ← bottom edge: one off-screen down
```

Edge markers:
- Only for bookmarks in the current graph.
- Only for slots 1-9 (unbound bookmarks don't render markers).
- Tooltip on hover shows label.
- Click jumps to bookmark.

Maximum 9 edge markers max (1 per slot).

## D.9.4 Bookmarks panel (V2)

Listed via host command `editor.show-bookmarks-panel`:

```
┌──────────────────────────────────┐
│ Bookmarks                      ✕ │
├──────────────────────────────────┤
│ 1  [EventGraph]  Start of combat │
│ 2  [EventGraph]  After death     │
│ 3  [Damage]      Knockback       │
│ 4  -- empty --                   │
│ 5  -- empty --                   │
│ ─────                            │
│   [Custom Label]   "Boss intro"  │   ← unbound bookmark
└──────────────────────────────────┘
```

Click row: jump.
RMB row: Rename, Delete, Move to Slot N, Set Slot to None.

## D.9.5 Persistence

Saved in editor session state. Path is host-defined; editor publishes the
session-state blob; host saves it next to the asset (e.g., `MyAsset.bookmarks`)
or in user-config (e.g., `%APPDATA%/host/editor-sessions/`).

## D.9.6 Orphan handling

When asset reloads (hot reload), each bookmark's `TargetGraph` may no
longer exist (graph deleted). Orphaned bookmarks silently removed on
next asset open.

If bookmark's target is still valid but the saved viewport coordinates
point at empty space (because nodes moved/removed), still jump there.
User sees an empty canvas region; they can then Frame All.

## D.9.7 Setting a bookmark (UX detail)

When user presses Ctrl+Shift+N:
- If slot N empty: silent set; small toast "Bookmark 1 set: EventGraph @ (340, 280)".
- If slot N occupied:
  - Modal: "Overwrite bookmark in slot N? [Cancel] [Overwrite]".
  - Confirm overwrites.
  - Cancel does nothing.

## D.9.8 Labels

Default label format on set: `{GraphName} @ ({pan.X:F0}, {pan.Y:F0})`.

User can rename via Bookmarks panel context menu (V2).

## D.9.9 Cross-tab behavior (V2)

Multiple asset tabs open. Each asset has its own bookmarks. Switching tabs
preserves each tab's bookmarks independently. Edge markers per-graph.

## D.9.10 Performance

Negligible. <100 bookmarks per asset realistically; lookups are dictionary-
keyed by slot. Edge markers computed once per frame.
