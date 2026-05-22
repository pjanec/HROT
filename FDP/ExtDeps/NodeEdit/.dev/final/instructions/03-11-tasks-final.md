# Tasks T-23, T-24, T-25 — Bookmarks, Hot-Reload, Polish

---

# T-23 — Bookmarks

## Goal
Implement bookmark slots (Ctrl+1..9 jump, Ctrl+Shift+1..9 set), with
viewport state persistence and edge markers.

## Project
`NodeEditor.UI` (with state in `NodeEditor.Core`).

## References
- `../specs/D9-bookmarks.md` — full normative behavior
- `../instructions/01-spec-brief-part2.md` §23 (Bookmarks)

## Deliverables

```
src/NodeEditor.Core/
    Bookmarks/
        Bookmark.cs              // record (BookmarkId, GraphId, Label, Pan, Zoom, SlotNumber, CreatedAt)
        BookmarkStore.cs         // per-asset bookmark collection + persistence interface

src/NodeEditor.UI/
    Bookmarks/
        BookmarkCommands.cs      // registers Ctrl+1..9 etc. with IEditorCommands
        BookmarkEdgeMarkerRenderer.cs   // canvas overlay: off-screen bookmark arrows
        BookmarksPanel.cs        // (V2 placeholder; renders the list as a side panel)
```

## Bookmark record

```csharp
namespace NodeEditor.Core.Bookmarks;

/// <summary>One bookmark targeting a specific viewport on a graph.</summary>
public sealed record Bookmark(
    string BookmarkId,        // GUID string, stable across saves
    GraphId TargetGraph,
    string Label,             // display name; default "{GraphName} @ ({x:F0}, {y:F0})"
    Vector2 ViewportPan,
    float ViewportZoom,
    int SlotNumber,           // 1..9 = hotkey slot; 0 = unbound
    DateTime CreatedAt);
```

## BookmarkStore

```csharp
namespace NodeEditor.Core.Bookmarks;

/// <summary>Per-asset bookmark collection. Persisted in editor session state.</summary>
public sealed class BookmarkStore
{
    private readonly Dictionary<string, Bookmark> _all = new();
    private readonly Dictionary<int, string> _slotToId = new();

    public IReadOnlyCollection<Bookmark> All => _all.Values;

    /// <summary>Returns the bookmark in the given slot (1-9), or null.</summary>
    public Bookmark? GetSlot(int slot) =>
        _slotToId.TryGetValue(slot, out var id) && _all.TryGetValue(id, out var b) ? b : null;

    /// <summary>
    /// Add or replace a bookmark in the given slot. Slot 0 = unbound.
    /// If <paramref name="slot"/> is in [1,9] and already occupied, the previous
    /// occupant's <see cref="Bookmark.SlotNumber"/> is set to 0 (becomes unbound).
    /// </summary>
    public void SetSlot(int slot, Bookmark bookmark);

    /// <summary>Remove a bookmark by id.</summary>
    public bool Remove(string bookmarkId);

    /// <summary>Remove bookmarks whose target graph no longer exists in <paramref name="model"/>.</summary>
    public int PurgeOrphans(IReadOnlyCollection<GraphId> validGraphIds);

    /// <summary>Serialize to JSON for session-state persistence.</summary>
    public string ToJson();

    /// <summary>Load from JSON; replaces existing contents.</summary>
    public static BookmarkStore FromJson(string json);
}
```

## Command registration

In `BookmarkCommands.RegisterAll(cmds, view, store, navigateToGraph)`:

```csharp
for (int slot = 1; slot <= 9; slot++)
{
    int captured = slot;

    cmds.Register(
        new EditorCommandDescriptor(
            $"editor.bookmark.jump.{captured}",
            $"Jump to Bookmark {captured}",
            "Navigation",
            $"Jump to bookmark in slot {captured}.",
            null,
            new KeyBinding(EditorKey.D0 + captured, KeyModifiers.Ctrl),
            IsEnabled: () => store.GetSlot(captured) is not null),
        _ => JumpToBookmark(store, captured, view, navigateToGraph));

    cmds.Register(
        new EditorCommandDescriptor(
            $"editor.bookmark.set.{captured}",
            $"Set Bookmark {captured}",
            "Navigation",
            $"Set bookmark in slot {captured} to the current viewport.",
            null,
            new KeyBinding(EditorKey.D0 + captured, KeyModifiers.Ctrl | KeyModifiers.Shift),
            IsEnabled: () => true),
        _ => SetBookmark(store, captured, view));
}
```

`JumpToBookmark` animates the camera over 180 ms to the bookmark's
viewport, opening the target graph if different.

`SetBookmark` prompts if the slot is occupied (set a flag; the demo
renders the prompt via a small modal).

## Edge markers

`BookmarkEdgeMarkerRenderer.Render(view, store, theme)` called by canvas
overlay phase:

- For each bookmark in slots 1-9 whose `TargetGraph == view.Model.Id` and
  whose `ViewportPan` falls outside the visible canvas rect:
  - Compute clipped projection on the canvas edge.
  - Draw small arrow pointing toward the off-screen position.
  - Hover tooltip shows label.
  - Click jumps via `editor.bookmark.jump.N`.

## Acceptance

- Ctrl+Shift+5 sets bookmark in slot 5.
- Ctrl+5 jumps back, animated.
- Edge marker arrow visible when current viewport doesn't include the
  bookmark.
- Bookmark survives switching graphs and switching back.

## Estimated Size
~200 LOC.

## Status
Pending.

---

# T-24 — Hot-Reload Badges

## Goal
Render visual feedback when the host raises `IGraphModel.Changed`:
add/remove/modify badges, toast notification, undo invalidation.

## Project
`NodeEditor.UI`

## References
- `../specs/D10-hot-reload.md` — full normative behavior
- `../instructions/01-spec-brief-part2.md` §24 (Hot-reload)

## Deliverables

```
src/NodeEditor.UI/
    HotReload/
        RecentChanges.cs          // ring buffer of recent changes for badge fade
        ChangeBadgeRenderer.cs    // canvas overlay: badges per affected node
        ChangeNotifier.cs         // subscribes to IGraphModel.Changed, produces toasts
```

## RecentChanges

```csharp
namespace NodeEditor.UI.HotReload;

/// <summary>
/// Ring buffer of recent <see cref="GraphChangeNotification"/> entries.
/// Used to render fading badges on affected nodes.
/// </summary>
public sealed class RecentChanges
{
    private readonly Queue<TimedChange> _entries = new();
    private const int MaxEntries = 200;
    private static readonly TimeSpan FadeWindow = TimeSpan.FromSeconds(2.0);

    public void Add(GraphChangeNotification n, TimeSpan now);

    /// <summary>Returns 0..1 opacity for a node's badge, or 0 if no badge.</summary>
    public float GetBadgeOpacity(NodeId node, TimeSpan now);

    /// <summary>Returns the kind of badge (Added/Removed/Modified) or null.</summary>
    public ChangeBadgeKind? GetBadgeKind(NodeId node, TimeSpan now);
}

public enum ChangeBadgeKind { Added, Removed, Modified }

internal readonly record struct TimedChange(GraphChangeNotification Notification, TimeSpan AddedAt);
```

## ChangeBadgeRenderer

```csharp
internal static class ChangeBadgeRenderer
{
    public static void Render(
        RecentChanges changes,
        IGraphModel model,
        ViewportState viewport,
        IEditorTheme theme,
        TimeSpan now)
    {
        foreach (var node in model.Nodes)
        {
            float alpha = changes.GetBadgeOpacity(node.Id, now);
            if (alpha <= 0) continue;
            var kind = changes.GetBadgeKind(node.Id, now);
            if (kind is null) continue;
            DrawBadge(node, kind.Value, alpha, viewport, theme);
        }
    }

    private static void DrawBadge(INodeModel node, ChangeBadgeKind kind, float alpha, ViewportState v, IEditorTheme theme)
    {
        // Top-right corner of node's screen rect.
        // Added:    green "+"
        // Removed:  red   "×"  (also for nodes mid-fade-out)
        // Modified: yellow "Δ"
        // Use small filled circle background, then icon text in white.
    }
}
```

## ChangeNotifier

```csharp
public sealed class ChangeNotifier : IDisposable
{
    private readonly IGraphModel _model;
    private readonly IEditorIndicators _indicators;
    private readonly RecentChanges _changes;
    private readonly UndoStack _undo;

    public ChangeNotifier(IGraphModel model, IEditorIndicators indicators, RecentChanges changes, UndoStack undo)
    {
        _model = model;
        _indicators = indicators;
        _changes = changes;
        _undo = undo;
        _model.Changed += OnGraphChanged;
    }

    private void OnGraphChanged(GraphChangeNotification n)
    {
        _changes.Add(n, TimeProvider.System.GetUtcNow().UtcDateTime.TimeOfDay);

        // Wholesale → clear undo.
        if (n.Kind == GraphChangeKind.Wholesale) _undo.Clear();

        // Build summary message.
        var added = n.AffectedNodes?.Count(id => /* added */ false) ?? 0;
        var removed = n.AffectedNodes?.Count(id => /* removed */ false) ?? 0;
        var modified = n.AffectedNodes?.Count(id => /* modified */ false) ?? 0;

        _indicators.Notify(new EditorNotification(
            Id: "hot-reload-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Severity: NotificationSeverity.Info,
            Title: $"↻ Asset reloaded.",
            Body: $"{added} added, {removed} removed, {modified} modified",
            AutoDismiss: TimeSpan.FromSeconds(5),
            Actions: null));
    }

    public void Dispose() => _model.Changed -= OnGraphChanged;
}
```

(The actual added/removed/modified classification depends on what
`GraphChangeNotification` carries; the host populates it.)

## Conflict handling

When the user has unsaved local changes (`view.Undo.CanUndo`) AND a
non-trivial external change arrives:
- Push notification with severity Warning, no auto-dismiss, with actions:
  - "Save" → `editor.save`
  - "Discard" → `editor.discard-changes`
  - "Ignore" → dismiss
- DO NOT auto-apply the change to the editor's view-model. Continue
  showing pre-reload state until user resolves.

This is partially out of MVP scope (the editor doesn't enforce a
"reload-blocked" state itself; the host is responsible for not re-emitting
notifications). Document this in `D10-hot-reload.md` §D.10.3 as a host
contract.

## Demo integration

Add to `S14_HotReload.cs`:
- Button "Simulate External Add Node" → injects a node directly into
  `FakeGraphModel` and raises `Changed` with `Kind = NodesAdded`.
- Button "Simulate External Modify" → mutates a node title.
- Button "Simulate Wholesale Reload" → rebuild whole model, raise
  `Wholesale`.
- Observe badges + toasts.

## Acceptance

- Simulating external add → green "+" badge fades over 2 s on new node.
- Wholesale reload → undo stack cleared, toast appears.
- Toast auto-dismisses after 5 s.

## Estimated Size
~200 LOC.

## Status
Pending.

---

# T-25 — Final Polish + Warnings Cleanup

## Goal
Last-mile cleanup: warnings, missing XML docs, naming inconsistencies,
README updates, and a final demo polish pass.

## Project
All projects.

## References
- All previous tasks.
- `../README.md`

## Acceptance Checklist

Run through the entire list. Each item is a binary done/not-done.

### Build
- [ ] `dotnet build` produces 0 warnings, 0 errors.
- [ ] `dotnet test` passes all xUnit tests.
- [ ] `TreatWarningsAsErrors = true` in all projects.
- [ ] `GenerateDocumentationFile = true` in all library projects (Primitives, Core, UI).

### Docs
- [ ] Every public type has an XML `<summary>`.
- [ ] Every public method/property has at least a one-line summary.
- [ ] Internal types may have brief comments but no XML.
- [ ] No `// TODO` left behind. Convert any remaining to issues in `QUESTIONS.md`.

### API consistency
- [ ] Identity-types are `record struct` (NodeId, PinId, …). Not `class`.
- [ ] Command records are `sealed`.
- [ ] All interfaces start with `I`.
- [ ] No public field declarations (use properties).
- [ ] No `string` typed IDs (always wrap in record struct or use the
      defined TypeKey/NodeKindKey).
- [ ] No raw `Guid` in public API except inside the wrappers.

### Threading
- [ ] All editor UI code runs on the ImGui thread (the demo's main loop).
- [ ] Host-data event handlers from `IGraphModel.Changed` are called on
      whatever thread the host uses; the editor marshals via a per-frame
      queue if needed. (Document this in `D10-hot-reload.md`.)

### Performance
- [ ] At 500 nodes, hit-test < 0.1 ms per frame.
- [ ] Spatial index rebuilt only when graph changes (cache by version).
- [ ] Low-zoom rendering kicks in below 0.5×.
- [ ] No allocations in render hot loop (no `new` in `Render`-tagged
      methods; pool any list buffers).

### Demo
- [ ] All 14 scenarios reachable.
- [ ] Default scenario on launch is S01_HelloCanvas.
- [ ] All commands in `CommandCatalog` bound to hotkeys via the demo's
      `HotkeyDispatcher`.
- [ ] Status bar shows snapshot fields.
- [ ] Toasts visible in lower-right corner.

### README
- [ ] Update `README.md` with screenshots (or placeholder text "see
      Demo").
- [ ] List all 25 tasks with their statuses.
- [ ] Document any open `QUESTIONS.md` items.

## Estimated Size
Mostly cleanup. ~100–200 LOC of edits across many files.

## Status
Pending.
