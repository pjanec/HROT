# D.10 — Hot Reload and Change Indicators

## What this is

When the underlying graph changes — externally (file watcher fires because someone edited the asset on disk), or because the catalog has changed (a node kind's signature was modified), or because of compile errors — the editor needs to surface this clearly without destroying the user's in-progress work.

This file specs the visual indicators, banners, and behaviors around external changes and catalog drift.

## D.10.1 External-change notification (file watcher fires)

The host watches asset files on disk. When a file changes externally while the editor has it open:

```csharp
public interface IExternalChangeNotifier
{
    event Action<ExternalChangeEvent>? Changed;
}

public sealed record ExternalChangeEvent(
    GraphId? AffectedGraph,
    string Description,
    ExternalChangeSeverity Severity);

public enum ExternalChangeSeverity { Info, Caution, Conflict }
```

The editor's response depends on whether the user has local edits:

### Case 1: Local model is clean (no unsaved edits)

- Editor silently reloads the model from the host.
- Toast at the bottom-right: `Asset reloaded from disk` (Info severity, 3 sec auto-dismiss).
- Selection cleared (since IDs may have changed). Viewport position preserved.
- Active drag / popup, if any: cancelled with brief notification.

### Case 2: Local model is dirty (user has unsaved edits)

- **Do NOT auto-reload.** This would destroy work.
- Show a persistent banner at the top of the canvas (across the full width):

```
┌─────────────────────────────────────────────────────────────────────┐
│ ⚠ External changes detected on disk while you have unsaved edits.   │
│   [ Discard my changes and reload ]    [ Keep my changes ]    [ × ] │
└─────────────────────────────────────────────────────────────────────┘
```

- **Discard my changes and reload**: clears the editor's undo stack, re-reads from disk. Toast confirms.
- **Keep my changes**: dismisses the banner; flags the asset as "diverged from disk" — next save will overwrite the disk changes. A small badge appears on the tab: ⚠ "diverged."
- **×**: same as Keep my changes.

Banner color: yellow (caution). Esc does NOT dismiss this banner — too consequential.

The banner stays until explicitly dismissed. If more external changes come in while the banner is up, the message updates count: `(3 changes since you started editing)`.

### Case 3: Conflict resolution (both local and external changed the same thing)

When the editor can detect a *true conflict* — same node both modified locally and externally — it offers a more detailed banner:

```
┌─────────────────────────────────────────────────────────────────────┐
│ ⚠ Conflict: 2 of your changes overlap with external edits.          │
│   [ Show details ]   [ Use mine ]   [ Use external ]   [ × ]        │
└─────────────────────────────────────────────────────────────────────┘
```

`Show details` opens a small diff dialog listing affected items. The merge logic is host-implemented (the editor just surfaces the affected IDs). For MVP, this case can collapse into Case 2 (full discard-or-keep choice); the per-conflict UI is V2.

## D.10.2 Catalog drift (node kind signature changed)

When `INodeCatalog` reports a change to a node kind already in use (added/removed/renamed pins), existing instances of that kind may no longer match the new signature.

### Detection

`INodeCatalog.Changed` event fires. Editor's catalog-drift detector compares:

- Each instance's pin set vs. the kind's current pin set.
- Match by pin name + type.

If mismatch found, the node is flagged with state `CatalogDrift`.

### Visual on the canvas

Drift-flagged nodes get a yellow ⚠ icon overlaid on the header, plus a thin yellow border:

```
┌──────────────────────────────┐
│ ⚠ Multiply (Vector × Vector) │   ← yellow border, yellow ⚠ on header
├──────────────────────────────┤
│ ▶ In           ▶ Out          │
│ ◯ A            ◯ Result      │
│ ◯ B                           │
│ ◯ NewPin   (red)               │   ← unrecognized pin, red dot
└──────────────────────────────┘
```

- Pins on the instance that don't exist in the current catalog: rendered in **red** with a dashed outline. Wires attached to them stay attached but are also dashed and red.
- Pins on the catalog that don't exist on the instance: appear as ghost outlines.
- The node still functions during edit but the compiler will reject it until refreshed.

### Hover tooltip

Hovering the ⚠ icon shows:

```
⚠ Node signature has changed:
   • Pin 'NewPin' was added (data input, Vector3)
   • Pin 'OldPin' was removed
Right-click → 'Refresh Node' to update.
```

### Right-click → Refresh Node

Right-click on a drift-flagged node → "Refresh Node":

- Adds missing pins (with default values from the catalog).
- Removes obsolete pins (breaking any wires attached). Notification toast lists broken connections.
- Updates display name and category if those changed in the catalog.
- Issued as a `RefreshNode` command for undoability.

### Refresh All Affected

In the editor's overflow menu (or as a command `editor.refresh-all-drift`): scans all open graphs, refreshes every drift-flagged node in one batch. Confirmation dialog if more than 5 are affected.

## D.10.3 Compile-error indicators

After the host compiles the asset, errors and warnings come back via `IDiagnosticsSink`:

```csharp
public interface IDiagnosticsSink
{
    IReadOnlyList<Diagnostic> Current { get; }
    event Action? Changed;
}

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Message,
    GraphId? Graph,
    NodeId? Node,
    PinId? Pin,
    string? Code);

public enum DiagnosticSeverity { Info, Warning, Error }
```

### Per-node visuals

For each node with one or more diagnostics:

- **Error**: red border (~2 px), red ⚠ icon on header.
- **Warning**: yellow border, yellow ⚠ icon.
- **Info**: blue underline only.

When both error and warning exist, error wins.

### Per-pin visuals

When a diagnostic is attached to a specific pin, the pin gets a small badge ⚠ next to it. Hover the badge → tooltip with full message.

### Per-graph visuals

Tab labels show error count:

```
EventGraph •          ← clean
ComputeDamage 2⚠      ← 2 errors
Init •                ← clean but dirty (asterisk-like dot)
```

`•` = dirty (unsaved edits). `n⚠` = n errors. Both can show at once (`• 3⚠`).

### Output panel (host-rendered, editor publishes data)

The editor publishes diagnostics through `IEditorIndicators` (see D.0). The host's output / errors panel reads from `IDiagnosticsSink` and renders the list. Each entry is clickable; clicking jumps to the affected node (opens its graph if needed, frames on it).

This is the host's responsibility — the editor doesn't draw an output panel. It publishes data.

## D.10.4 Live edit indicator

When the editor model is dirty (unsaved local changes), the active tab label gets a `•` prefix:

```
• EventGraph
```

The host's status bar (or whatever shows save state) reads `EditorIndicators.Snapshot.IsDirty`.

Saving clears the dirty state across all tabs.

## D.10.5 Debug-attached indicator

When `IDebugSession.IsAttached`:

- Tab labels for debug-relevant graphs (those currently executing) get a red dot prefix: `● EventGraph`.
- A persistent banner at the top of the canvas: `🐞 Debugging` with the debug controls (Resume / Step Over / Step Into / Step Out).
- See `A_canvas_interactions.md §A.16` for the rest of the debug visual layer.

## D.10.6 Stale-breakpoint indicator

Per `IBlueprintDebugSession.IsStale`, breakpoints set on nodes that no longer exist or no longer execute are flagged stale:

- Red breakpoint marker becomes **yellow with ⚠**.
- Hover tooltip: `Breakpoint is stale: {reason}`.
- Right-click → "Clear stale breakpoint".

## D.10.7 Read-only indicator

When the asset is read-only (locked, externally checked out, etc.), the host signals this:

```csharp
public interface IEditorReadOnlyState
{
    bool IsReadOnly { get; }
    string? Reason { get; }
    event Action? Changed;
}
```

The editor responds:

- Persistent banner: `🔒 Read-only: {reason}`.
- All mutation commands return `EditorCommandResult(false, "Read-only")`.
- Editing UI is visible but greyed.
- Cursor on hover over an editable region: a small "no-edit" overlay icon.

## D.10.8 Behaviors that the editor refuses to do under certain states

| State | What's disabled |
|---|---|
| Dirty + external change pending decision | Save is disabled until banner is resolved |
| Read-only | All mutation commands |
| Catalog drift (any node) | Compile is disabled (host enforces); banner shows: `Refresh outdated nodes before compiling` |
| Debug-paused | New compile is disabled |

Each of these is enforced by the host through command rejection + by the editor through indicator state.

## D.10.9 Notification stacking and lifecycle

Toasts (transient, bottom-right) and banners (persistent, top of canvas) coexist:

- Banners: at most 2 visible simultaneously. Third one stacks vertically. Persistent until dismissed.
- Toasts: at most 3 visible simultaneously. Older toasts collapse into `+N more` indicator. Auto-dismiss after 3 seconds.

Banner priority (when 3+ would show, only top 2 visible):

1. Read-only (lock)
2. External changes pending
3. Catalog drift summary
4. Debug-attached
5. Compile errors summary

## D.10.10 Implementation note: how indicators are computed

These indicators are not directly maintained by the editor view code. They live in the model layer:

- `IGraphModel` reports dirty state.
- `INodeCatalog.Changed` fires when catalog drift might exist.
- `IDiagnosticsSink` reports diagnostics.
- `IExternalChangeNotifier`, `IDebugSession`, `IEditorReadOnlyState` report their respective states.

The editor's rendering layer subscribes to all of these and computes the visual overlay on each frame from the union of states. No imperative "set state" calls — every visual derives from observable model state.
