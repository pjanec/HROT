# D.0 — Command / Indicator API

The graph editor exposes its capabilities as commands. The host's UI shell
(toolbars, menus, status bars) binds to these commands. The editor does NOT
draw application chrome — only the canvas, panels, and picker.

This separation is non-negotiable. It means:
- Minimal host shell hooks editor up with a few buttons.
- Polished future shell binds same commands to a fancy toolbar.
- Demo app provides a reference shell as illustration only.

## D.0.1 IEditorCommands

```csharp
public interface IEditorCommands
{
    IReadOnlyList<EditorCommandDescriptor> All { get; }
    EditorCommandDescriptor? Get(string commandId);
    EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null);
    event Action<string>? AvailabilityChanged;
}

public sealed record EditorCommandDescriptor(
    string Id,
    string DisplayName,
    string? Category,
    string? Description,
    string? IconKey,
    KeyBinding? DefaultKey,
    Func<bool> IsEnabled,
    Func<bool>? IsChecked = null);

public readonly record struct EditorCommandContext(
    Vector2? ScreenPos,
    Vector2? CanvasPos,
    IReadOnlyDictionary<string, object?>? Args);

public readonly record struct EditorCommandResult(bool Success, string? Message);
```

## D.0.2 Full command catalog

Categories: File, Edit, View, Navigation, Add/Create, Refactor, Find, Debug, Alignment.

### File / Asset
- `editor.save` [MVP]
- `editor.save-all` [V2]
- `editor.reload` [V2]
- `editor.compile` [MVP]
- `editor.quick-reload` [V2]

### Edit
- `editor.undo` [MVP]
- `editor.redo` [MVP]
- `editor.cut` [MVP]
- `editor.copy` [MVP]
- `editor.paste` [MVP]
- `editor.duplicate` [MVP]
- `editor.select-all` [MVP]
- `editor.select-none` [MVP]
- `editor.invert-selection` [MVP]
- `editor.delete-selection` [MVP]

### View
- `editor.frame-all` [MVP]
- `editor.frame-selection` [MVP]
- `editor.zoom-in` [MVP]
- `editor.zoom-out` [MVP]
- `editor.zoom-reset` [MVP]
- `editor.toggle-grid` [V2]
- `editor.toggle-minimap` [V2]
- `editor.toggle-low-zoom-mode` [V2]

### Navigation
- `editor.next-tab` [MVP]
- `editor.prev-tab` [MVP]
- `editor.close-tab` [MVP]
- `editor.go-to-graph` [V2]
- `editor.next-error` [V2]
- `editor.prev-error` [V2]
- `editor.next-bookmark` [V2]
- `editor.prev-bookmark` [V2]

### Add/Create
- `editor.add-node` [MVP]
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
- `editor.find-in-graph` [V2]
- `editor.find-in-asset` [V2]
- `editor.find-in-project` [V2]
- `editor.go-to-definition` [V2]
- `editor.find-references` [V2]
- `editor.find-next` [V2]
- `editor.find-prev` [V2]

### Debug
- `editor.toggle-breakpoint` [V2]
- `editor.toggle-watch` [V2]
- `editor.continue` [V2]
- `editor.step-over` [V2]
- `editor.step-into` [V2]
- `editor.step-out` [V2]
- `editor.clear-all-breakpoints` [V2]

### Alignment (V2)
- `editor.align-left`
- `editor.align-right`
- `editor.align-top`
- `editor.align-bottom`
- `editor.align-center-h`
- `editor.align-center-v`
- `editor.distribute-h`
- `editor.distribute-v`
- `editor.straighten-connection`

## D.0.3 IEditorIndicators

```csharp
public interface IEditorIndicators
{
    EditorStatusSnapshot Snapshot { get; }
    event Action? Changed;
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
    EditorMode Mode,
    string? CurrentTool);

public enum EditorMode { Editing, Compiling, Debugging, DebugPaused }

public sealed record EditorNotification(
    string Id,
    NotificationSeverity Severity,
    string Title,
    string? Body,
    TimeSpan? AutoDismiss,
    IReadOnlyList<NotificationAction>? Actions);

public enum NotificationSeverity { Info, Success, Warning, Error }

public sealed record NotificationAction(string Label, string CommandId);
```

## D.0.4 Usage by minimal host

```csharp
// Menu binding (host's existing menu code):
if (ImGui.BeginMenu("Edit")) {
    foreach (var cmd in editor.Commands.All.Where(c => c.Category == "Edit")) {
        if (ImGui.MenuItem(cmd.DisplayName, ToShortcut(cmd.DefaultKey),
                           false, cmd.IsEnabled()))
            editor.Commands.Invoke(cmd.Id);
    }
    ImGui.EndMenu();
}

// Statusbar (host's existing statusbar):
var s = editor.Indicators.Snapshot;
ImGui.Text($"{s.CurrentGraphName}   {s.NodeCount} nodes   {s.Zoom * 100:F0}%");
if (s.ErrorCount > 0) ImGui.TextColored(Red, $"{s.ErrorCount} errors");
```

## D.0.5 Notifications (toasts)

Host implements the notification UI. Editor only publishes via
`indicators.Notify(...)`. Host renders toasts wherever it wants.

If host doesn't implement notifications, editor's notifications are lost
silently (acceptable; the underlying actions still succeed).

## D.0.6 Hotkey binding

Editor publishes `DefaultKey` per command. Host walks the command list at
startup and registers each `DefaultKey` with its input system.

Editor does NOT internally listen for hotkeys; the host translates key
events to command invocations. This keeps the action loop unidirectional:
input → host → command → editor → state change → render.

Exception: keys handled INSIDE the canvas (drag, box-select, etc.) are not
commands. Those are direct canvas interactions.

The line: if it works the same way regardless of canvas state, it's a
command. If it's a tool gesture within the canvas, it's a direct interaction.
