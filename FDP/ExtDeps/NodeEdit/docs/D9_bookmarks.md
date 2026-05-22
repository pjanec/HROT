# D.9 — Bookmarks

## What this is

A user-configurable viewport-jump system. Slots 1–9 per asset; each slot remembers a viewport position (pan + zoom + active graph). Lets the user move quickly between regions of a large graph.

Largely a power-user feature, but cheap to implement and high-value for big graphs.

## D.9.1 Data model

```csharp
public sealed record Bookmark(
    int Slot,                       // 1..9
    GraphId Graph,
    Vector2 CanvasCenter,           // canvas coordinate at viewport center
    float Zoom,
    string? Label);                 // optional, user-set; auto-generated if null

public interface IBookmarkStore
{
    IReadOnlyList<Bookmark> All { get; }      // ordered by slot
    Bookmark? Get(int slot);
    void Set(int slot, Bookmark bookmark);
    void Clear(int slot);
    event Action? Changed;
}
```

The `IBookmarkStore` is per-asset. Stored in editor session state, keyed by asset identity.

## D.9.2 Keyboard bindings

| Key | Action |
|---|---|
| `Ctrl+1` … `Ctrl+9` | Jump to bookmark slot 1..9. No-op if slot is empty. |
| `Ctrl+Shift+1` … `Ctrl+Shift+9` | Set slot 1..9 to current viewport. Overwrites silently if slot already set. |
| `Ctrl+B` | Add bookmark to first empty slot. If all 9 are used, replaces slot 1 (oldest). |
| `Ctrl+Shift+B` | Opens bookmark management dropdown. |

When jumping:

1. If bookmark's graph is in an open tab → switch to that tab.
2. If not open → open it in a new tab.
3. Animate viewport to the saved center + zoom over **180 ms** (same animation as Frame All).
4. Selection is **not** restored — just the viewport.

## D.9.3 Visual indicator

When the current viewport matches a bookmark (cursor center ≈ bookmark center, within ~50 px tolerance, and zoom ≈ bookmark zoom within ~5%), a small badge appears in the bottom-right of the canvas area:

```
                              ⛬ 3
```

Shows the slot number. Subtle, fades out after ~2 seconds of no movement. Reappears when matching again.

## D.9.4 Management dropdown

`Ctrl+Shift+B` opens a small dropdown from the canvas's top-right corner:

```
┌───────────────────────────────────┐
│ Bookmarks                         │
├───────────────────────────────────┤
│ 1  EventGraph @ Combat            │
│ 2  ComputeDamage @ Entry          │
│ 3  EventGraph @ Spawn area        │
│ 4  (empty)                  [set] │
│ 5  (empty)                  [set] │
│ ...                               │
│ ─────────                         │
│ Clear All                         │
└───────────────────────────────────┘
```

Each row:
- LMB-click on a bookmarked row → jump.
- `[set]` button → set current viewport to that slot.
- Right-click → context menu: Rename, Clear, Set to current.

Inline rename via F2 or double-click. Label is user-editable; if cleared, falls back to auto-generated label.

## D.9.5 Auto-generated labels

When a bookmark is set without an explicit label, the editor generates one:

- Find the most prominent node near the center: largest visible node, or one with a comment encompassing it.
- Use that node's title or the comment's title.
- Format: `{GraphName} @ {NearbyLabel}` or just `{GraphName}` if nothing notable nearby.

Examples: `EventGraph @ Combat`, `ComputeDamage @ Entry`, `ApplyKnockback`.

## D.9.6 Tab bar integration (V2)

In the tab bar context menu, add a "Jump to Bookmark…" submenu listing only bookmarks for the right-clicked tab's graph.

## D.9.7 Persistence

Bookmarks stored per-asset in editor session state. JSON shape:

```json
{
  "assets": {
    "{assetId-or-path}": {
      "bookmarks": [
        {
          "slot": 1,
          "graphId": "{guid}",
          "centerX": 1240.0,
          "centerY": -380.0,
          "zoom": 1.25,
          "label": "Combat resolution"
        }
      ]
    }
  }
}
```

When an asset is closed, its bookmarks remain in the file but stop loading. When reopened, they restore.

## D.9.8 Edge cases

| Situation | Behavior |
|---|---|
| Jump to a bookmark whose graph was deleted | Bookmark auto-invalidates. Show toast `Bookmark 3 invalid: graph no longer exists`. Clear the slot. |
| Jump to a bookmark on an asset that has been edited so the labeled node no longer exists | Jump still works — viewport position is preserved. The label might be stale but that's cosmetic. |
| Asset moved/renamed | Editor tries to track by asset GUID, not path. If host doesn't expose stable asset identity, bookmarks may be lost. |
| Set a slot while picker / popup open | The key combo is intercepted by the popup first. Bookmark setting is canvas-only. |
| Set a slot while a tab has no active graph | No-op with notification `Cannot bookmark: no graph active`. |
| Conflict with host keybinding | Host's `IInputSource` handles binding resolution. Defaults assume no conflict; host can override. |

## D.9.9 Performance

Bookmarks add zero per-frame cost when no bookmark is being matched. The "current viewport matches a bookmark" check is one comparison per frame across at most 9 entries — negligible.
