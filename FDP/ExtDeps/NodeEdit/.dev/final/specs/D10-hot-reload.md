# D.10 — Hot Reload Indicators

The host may reload graph data due to external changes (compile, refactor,
file edit). The editor must react gracefully.

## D.10.1 Change notification

```csharp
public sealed record GraphChangeNotification(
    GraphChangeKind Kind,
    IReadOnlySet<NodeId>? AffectedNodes,
    IReadOnlySet<LinkId>? AffectedLinks,
    string? Reason);

public enum GraphChangeKind
{
    NodesAdded,
    NodesRemoved,
    NodesModified,
    NodesMoved,
    LinksAdded,
    LinksRemoved,
    VariablesChanged,
    Wholesale            // everything may have changed
}
```

Host raises `IGraphModel.Changed`. Editor subscribes.

## D.10.2 Editor response

### Selection preservation

Editor preserves canvas selection as much as possible:
- Selection items that still exist remain selected.
- Selection items that no longer exist drop from selection silently.
- Primary selection drops to first remaining selected item, or None.

### Viewport preservation

Pan/zoom unchanged. No auto-jump to changed nodes.

### Badge overlays on affected nodes

Each affected node gets a temporary badge (rendered above node):

- **Added:** green `+` badge with fade-in. Persists for 2 s, then fades.
- **Removed:** red `×` badge during fade-out animation (~300 ms), node then
  disappears.
- **Modified:** yellow `Δ` badge with fade-in. Persists 2 s.
- **Moved:** no badge. Node animates to new position over 180 ms ease-out.

### Toast notification

Single notification summarizing change:
- `"↻ Asset reloaded. 3 added, 1 removed, 2 modified"` — info.
- Auto-dismiss after 5 s.
- Click → opens Find Results panel showing affected nodes.

### Details panel sync

If Details panel target was modified: re-renders with new state.
If target was removed: switch to `None` or `Asset`.

### Undo stack invalidation

Commands referring to removed entities are unsafe to redo.
- Lazy approach: don't invalidate proactively; mark commands "stale";
  during undo/redo, try to apply; if it fails, skip and try next.
- Aggressive: on Wholesale change, clear undo stack.

**MVP:** aggressive approach on Wholesale. Lazy approach otherwise.

## D.10.3 Conflict handling

When the user has UNSAVED LOCAL CHANGES (dirty) AND external reload is
triggered:

**Block the reload.**

Toast: `"⚠ External changes detected. Save or discard your changes to reload."`
- "Save" button — runs `editor.save`, then reload accepted.
- "Discard" button — discards local changes, then reload accepted.
- "Ignore" button — keeps local changes, reload deferred.

Until user picks, editor continues showing pre-reload state. The host's
notification stays pending.

## D.10.4 Stale breakpoints

(For hosts providing `IDebugSession`.)

If a breakpoint's anchor node was modified/removed/replaced:
- Mark breakpoint **stale**.
- Visual: yellow filled circle (instead of normal red), with `⚠`.
- Tooltip: "Breakpoint stale. Anchor node modified. Click to rebind or remove."
- RMB options:
  - Rebind: open picker to select new anchor.
  - Remove: delete the breakpoint.
  - Show what changed: open diff viewer (V2).

## D.10.5 In-flight drag

If reload arrives while user is mid-drag (DRAG_NODES, DRAG_WIRE):
- Cancel the drag (treat as Esc).
- Apply reload.
- Toast: "Reload during drag. Drag cancelled, no changes applied."

## D.10.6 Picker open

If reload arrives while picker is open:
- Picker stays open.
- Source's `Changed` event fires; picker re-queries.
- Selection in list may shift; keyboard-focused item stays the same key
  if it still exists.

## D.10.7 Find/find-results

If reload arrives while Find Results panel is open:
- Re-execute query.
- Refresh list; preserve scroll position best-effort.

## D.10.8 Performance

Reload notifications can be frequent (compile every keystroke in a code
editor, e.g.). Editor must:
- Batch multiple notifications within ~50 ms into one render pass.
- Lazy-evaluate badge animations; cap at ~50 simultaneous badges (excess
  collapse into "X more changes").

## D.10.9 Implementation notes

Editor maintains a `RecentChanges` ring buffer:
```csharp
internal sealed class RecentChanges
{
    private readonly Queue<TimedChange> _entries = new();
    private const int MaxEntries = 200;
    private static readonly TimeSpan FadeWindow = TimeSpan.FromSeconds(2.0);

    public void Add(GraphChangeNotification n) { /* enqueue with timestamp */ }
    public float GetBadgeOpacity(NodeId n) { /* based on age within fade window */ }
}
```

Per-frame, query GetBadgeOpacity for each visible node; draw badge if > 0.

## D.10.10 Disable per host

Host may disable badge/toast notifications via:
```csharp
editor.NotificationSettings.ShowHotReloadBadges = false;
editor.NotificationSettings.ShowHotReloadToasts = false;
```

Useful for hosts that handle their own change UI.
