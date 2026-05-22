# Part D.0 — Command and Indicator Action API

## Purpose

The graph editor exposes its capabilities as **commands** that any UI shell can bind to. It does **not** draw its own toolbars, menus, or status bars — those belong to the host shell. The editor only renders the canvas, panels, and the picker.

This separation lets:
- A minimalist host (your current shell) hook the editor up now with just a few buttons.
- A future polished shell (toolbar, professional Unreal-like menu) bind the same commands without the editor changing.
- The demo app demonstrate what a "professional Unreal-like" shell could look like, but as a host — not as part of the editor library.

## The command surface

```csharp
public interface IEditorCommands
{
    /// All commands the editor exposes, queryable for binding to UI.
    IReadOnlyList<EditorCommandDescriptor> All { get; }

    /// Look up by stable ID.
    EditorCommandDescriptor? Get(string commandId);

    /// Invoke a command. Some commands take context (canvas pos, etc).
    EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null);

    /// Subscribe to availability changes (e.g. Undo becomes available after first edit).
    event Action<string>? AvailabilityChanged;
}

public sealed record EditorCommandDescriptor(
    string Id,                       // "editor.undo", "editor.frame-all"
    string DisplayName,              // "Undo", "Frame All"
    string? Category,                // "Edit", "View", "Debug", "Refactor"
    string? Description,             // for tooltips
    string? IconKey,                 // hook for host's icon provider
    KeyBinding? DefaultKey,          // suggested keyboard shortcut
    Func<bool> IsEnabled,            // computed live
    Func<bool>? IsChecked = null);   // for toggleable commands (View toggles)

public readonly record struct EditorCommandContext(
    Vector2? ScreenPos,              // for "place node here"-type commands
    Vector2? CanvasPos,
    IReadOnlyDictionary<string, object?>? Args);

public readonly record struct EditorCommandResult(bool Success, string? Message);

public readonly record struct KeyBinding(
    EditorKey Key,
    KeyModifiers Modifiers);
```

The editor publishes its own catalog at startup. The host walks `IEditorCommands.All` and builds whatever UI it wants — menu items, toolbar buttons, statusbar icons, command palette entries. Everything is data-driven.

## The full command catalog

These are the commands the editor publishes, by category. Tagged `[MVP]` / `[V2]`.

### File / Asset

Host typically owns these, but editor publishes them so the host's binding is consistent.

- `editor.save` — request host to save current asset [MVP]
- `editor.save-all` [V2]
- `editor.reload` — host re-reads asset, editor reflects [V2]
- `editor.compile` — host triggers compile [MVP]
- `editor.quick-reload` — host triggers hot reload [V2]

### Edit

- `editor.undo` / `editor.redo` [MVP]
- `editor.cut` / `editor.copy` / `editor.paste` / `editor.duplicate` [MVP]
- `editor.select-all` / `editor.select-none` / `editor.invert-selection` [MVP]
- `editor.delete-selection` [MVP]

### View

- `editor.frame-all` / `editor.frame-selection` [MVP]
- `editor.zoom-in` / `editor.zoom-out` / `editor.zoom-reset` [MVP]
- `editor.toggle-grid` / `editor.toggle-minimap` [V2]
- `editor.toggle-low-zoom-mode` [V2]

### Navigation

- `editor.next-tab` / `editor.prev-tab` [MVP]
- `editor.close-tab` [MVP]
- `editor.go-to-graph` (opens picker for graph) [V2]
- `editor.next-error` / `editor.prev-error` [V2]
- `editor.next-bookmark` / `editor.prev-bookmark` [V2]

### Add / Create

- `editor.add-node` (opens search popup) [MVP]
- `editor.add-comment` [MVP]
- `editor.add-reroute` [V2]
- `editor.create-function` [V2]
- `editor.create-custom-event` [V2]
- `editor.create-variable` [MVP]
- `editor.create-macro` [V2]

### Refactor

- `editor.collapse-to-function` [V2]
- `editor.collapse-to-macro` [V2]
- `editor.collapse-to-comment` [MVP]
- `editor.expand-node` [V2]
- `editor.promote-to-variable` [V2]
- `editor.rename` [V2]

### Find

- `editor.find-in-graph` / `editor.find-in-asset` / `editor.find-in-project` [V2]
- `editor.go-to-definition` [V2]
- `editor.find-references` [V2]
- `editor.find-next` / `editor.find-prev` [V2]

### Debug

- `editor.toggle-breakpoint` [V2]
- `editor.toggle-watch` [V2]
- `editor.continue` / `editor.step-over` / `editor.step-into` / `editor.step-out` [V2]
- `editor.clear-all-breakpoints` [V2]

### Alignment (matches Unreal hotkeys)

- `editor.align-left` / `editor.align-right` / `editor.align-top` / `editor.align-bottom` [V2]
- `editor.align-center-h` / `editor.align-center-v` [V2]
- `editor.distribute-h` / `editor.distribute-v` [V2]
- `editor.straighten-connection` [V2]

## Indicators — the other direction

The editor needs to *publish* state for the host's statusbar / notifications.

```csharp
public interface IEditorIndicators
{
    /// Read-only snapshot of editor state for status display.
    EditorStatusSnapshot Snapshot { get; }

    /// Fires when any field of the snapshot changes (debounced to ~60Hz).
    event Action? Changed;

    /// Push a notification (the host's toast / banner system).
    void Notify(EditorNotification notification);
}

public readonly record struct EditorStatusSnapshot(
    string? CurrentGraphName,
    int NodeCount,
    int SelectedNodeCount,
    int LinkCount,
    bool IsDirty,
    int ErrorCount,
    int WarningCount,
    float Zoom,
    Vector2 CanvasCursorPos,
    EditorMode Mode,           // Editing / Debugging / DebugPaused / Compiling
    string? CurrentTool);      // null = idle; "BoxSelect", "DragWire", "Panning"

public enum EditorMode { Editing, Compiling, Debugging, DebugPaused }

public sealed record EditorNotification(
    string Id,                 // for deduplication
    NotificationSeverity Severity,
    string Title,
    string? Body,
    TimeSpan? AutoDismiss,     // null = persistent until acknowledged
    IReadOnlyList<NotificationAction>? Actions);

public enum NotificationSeverity { Info, Success, Warning, Error }

public sealed record NotificationAction(string Label, string CommandId);
```

The host's statusbar reads `Snapshot` and renders however it wants. Notifications can include actions that invoke editor commands when clicked.

## What this gives you concretely

For a minimalist shell, hook the editor up like this:

```csharp
// In existing main menu:
if (ImGui.BeginMenu("Edit")) {
    foreach (var cmd in editor.Commands.All.Where(c => c.Category == "Edit"))
        if (ImGui.MenuItem(cmd.DisplayName, ToShortcut(cmd.DefaultKey), false, cmd.IsEnabled()))
            editor.Commands.Invoke(cmd.Id);
    ImGui.EndMenu();
}

// In existing statusbar:
var s = editor.Indicators.Snapshot;
ImGui.Text($"{s.CurrentGraphName}   {s.NodeCount} nodes   {s.Zoom * 100:F0}%");
if (s.ErrorCount > 0) ImGui.TextColored(Red, $"{s.ErrorCount} errors");
```

Later, when a polished shell is built, the same command IDs power a real toolbar. No editor changes.

## Default keybindings

The default keybindings table is in `04_kernel_code/K08_constants_and_theme.md`. The host can override any binding by registering different `KeyBinding` values for the same command IDs. Some commands have no default binding (e.g., `editor.add-reroute`).
