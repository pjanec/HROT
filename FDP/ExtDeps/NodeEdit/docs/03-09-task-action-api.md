# T-19 — Action API (Commands + Indicators)

## Goal
Implement the `IEditorCommands` and `IEditorIndicators` surfaces. The editor
publishes commands and status; the host's chrome (toolbar, menu, status bar,
hotkey dispatcher) binds to them.

The editor itself does NOT render toolbars or menus. This is the
non-negotiable separation that keeps the editor reusable across hosts.

## Project
`NodeEditor.Core` (publication surface) + `NodeEditor.UI` (canvas hooks the
publication into actual operations).

## References

**Specs:**
- `../specs/D0-action-api.md` — full normative behavior + complete command
  list with categories
- `../instructions/01-spec-brief-part2.md` §26 (Command/indicator API)

**Kernel:**
- `../kernel/03-search-spatial-constants.md` — `CommandCatalog` (all
  command IDs as string constants)
- `../kernel/04-my-blueprint-and-rest.md` — `IEditorCommands`,
  `IEditorIndicators` interfaces

## Deliverables

```
src/NodeEditor.Core/
    Action/
        EditorCommandsImpl.cs        // default impl; host can subclass
        EditorIndicatorsImpl.cs      // default impl
        CommandRegistration.cs       // ergonomic builder for registering commands
        ToastQueue.cs                // notification queue
src/NodeEditor.UI/
    Action/
        BuiltinCommandHandlers.cs    // wires canvas operations to commands
        CanvasCommands.cs            // canvas-specific: pan, zoom, frame, …
        EditCommands.cs              // undo, redo, cut, copy, paste, …
        ViewCommands.cs              // zoom-in/out, frame-all, …
```

## EditorCommandsImpl

```csharp
namespace NodeEditor.Core.Action;

/// <summary>
/// Default implementation of <see cref="IEditorCommands"/>.
/// Holds a registry of <see cref="EditorCommandDescriptor"/> objects and
/// delegates invocations to their Invoke action.
/// </summary>
public sealed class EditorCommandsImpl : IEditorCommands
{
    private readonly Dictionary<string, RegisteredCommand> _commands = new();

    public IReadOnlyList<EditorCommandDescriptor> All { get; private set; } = Array.Empty<EditorCommandDescriptor>();

    public EditorCommandDescriptor? Get(string commandId) =>
        _commands.TryGetValue(commandId, out var c) ? c.Descriptor : null;

    public EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null)
    {
        if (!_commands.TryGetValue(commandId, out var cmd))
            return new EditorCommandResult(false, $"Unknown command: {commandId}");

        if (!cmd.Descriptor.IsEnabled())
            return new EditorCommandResult(false, "Command not enabled.");

        try
        {
            cmd.Action(ctx ?? default);
            return new EditorCommandResult(true, null);
        }
        catch (Exception ex)
        {
            return new EditorCommandResult(false, ex.Message);
        }
    }

    public event Action<string>? AvailabilityChanged;

    /// <summary>Register a command. Used at editor startup by the host or by the editor itself.</summary>
    public void Register(EditorCommandDescriptor descriptor, Action<EditorCommandContext> action)
    {
        _commands[descriptor.Id] = new RegisteredCommand(descriptor, action);
        RebuildList();
    }

    /// <summary>Trigger an AvailabilityChanged event for a command id.</summary>
    public void NotifyAvailabilityChanged(string commandId) => AvailabilityChanged?.Invoke(commandId);

    private void RebuildList()
    {
        All = _commands.Values.Select(c => c.Descriptor).ToList();
    }

    private readonly record struct RegisteredCommand(
        EditorCommandDescriptor Descriptor,
        Action<EditorCommandContext> Action);
}
```

## EditorIndicatorsImpl

```csharp
namespace NodeEditor.Core.Action;

public sealed class EditorIndicatorsImpl : IEditorIndicators
{
    private EditorStatusSnapshot _snapshot;
    private readonly ToastQueue _toasts;

    public EditorIndicatorsImpl(ToastQueue toasts)
    {
        _toasts = toasts;
    }

    public EditorStatusSnapshot Snapshot => _snapshot;

    public event Action? Changed;

    public void Notify(EditorNotification notification) => _toasts.Enqueue(notification);

    /// <summary>Update the snapshot; raises Changed if anything differs.</summary>
    public void UpdateSnapshot(EditorStatusSnapshot newSnapshot)
    {
        if (_snapshot == newSnapshot) return;
        _snapshot = newSnapshot;
        Changed?.Invoke();
    }
}

public sealed class ToastQueue
{
    private readonly Queue<EditorNotification> _pending = new();
    public IReadOnlyCollection<EditorNotification> Pending => _pending;
    public void Enqueue(EditorNotification n) => _pending.Enqueue(n);
    public bool TryDequeue(out EditorNotification n) =>
        _pending.TryDequeue(out n!);
    public int Count => _pending.Count;
}
```

## Built-in command handlers

In `NodeEditor.UI/Action/BuiltinCommandHandlers.cs`, register all the
commands the editor itself owns (versus host-owned commands like Save and
Compile). Use the constants from `CommandCatalog`:

```csharp
namespace NodeEditor.UI.Action;

/// <summary>
/// Registers handlers for all built-in editor commands on a given
/// EditorCommandsImpl instance. Called once during editor construction.
/// </summary>
public static class BuiltinCommandHandlers
{
    public static void RegisterAll(
        EditorCommandsImpl cmds,
        GraphView view,
        CanvasRenderer canvas,
        MyBlueprintPanel? myBlueprint,
        FindBar findBar)
    {
        // === Edit ===
        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.Undo, "Undo", "Edit", "Undo the last operation.",
                IconKey: "icon.undo",
                DefaultKey: new KeyBinding(EditorKey.Z, KeyModifiers.Ctrl),
                IsEnabled: () => view.Undo.CanUndo),
            _ => view.UndoLast());

        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.Redo, "Redo", "Edit", "Redo the next operation.",
                IconKey: "icon.redo",
                DefaultKey: new KeyBinding(EditorKey.Z, KeyModifiers.Ctrl | KeyModifiers.Shift),
                IsEnabled: () => view.Undo.CanRedo),
            _ => view.RedoLast());

        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.DeleteSelection, "Delete", "Edit",
                "Delete the current selection.", null,
                new KeyBinding(EditorKey.Delete, KeyModifiers.None),
                IsEnabled: () => !view.Selection.IsEmpty),
            _ => DeleteSelectedEntities(view));

        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.SelectAll, "Select All", "Edit",
                "Select every entity in the current graph.", null,
                new KeyBinding(EditorKey.A, KeyModifiers.Ctrl),
                IsEnabled: () => view.Model.Nodes.Count > 0),
            _ => SelectAll(view));

        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.SelectNone, "Select None", "Edit",
                "Clear the selection.", null,
                new KeyBinding(EditorKey.D, KeyModifiers.Ctrl | KeyModifiers.Shift),
                IsEnabled: () => !view.Selection.IsEmpty),
            _ => view.Selection.Clear());

        // === View ===
        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.FrameAll, "Frame All", "View",
                "Frame the camera to fit all nodes.", null,
                new KeyBinding(EditorKey.Home, KeyModifiers.None),
                IsEnabled: () => view.Model.Nodes.Count > 0),
            _ => FrameAll(view));

        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.FrameSelection, "Frame Selection", "View",
                "Frame the camera to fit the selection (or all if no selection).", null,
                new KeyBinding(EditorKey.F, KeyModifiers.None),
                IsEnabled: () => true),
            _ => FrameSelectionOrAll(view));

        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.ZoomReset, "Reset Zoom", "View",
                "Reset zoom to 100%.", null,
                new KeyBinding(EditorKey.D0, KeyModifiers.Ctrl),
                IsEnabled: () => true),
            _ => { view.Viewport.SetZoom(1.0f); });

        // === Find ===
        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.FindInGraph, "Find in Graph", "Find",
                "Open the find bar.", null,
                new KeyBinding(EditorKey.F, KeyModifiers.Ctrl),
                IsEnabled: () => true),
            _ => { findBar.IsVisible = true; });

        // === Add ===
        cmds.Register(
            new EditorCommandDescriptor(
                CommandCatalog.AddComment, "Add Comment", "Add",
                "Add a comment box around the selection.", null,
                new KeyBinding(EditorKey.C, KeyModifiers.None),
                IsEnabled: () => true),
            _ => AddCommentAroundSelection(view));

        // … etc for every command in CommandCatalog the editor implements.
    }

    // Helper methods (implementations omitted; straightforward).
    private static void DeleteSelectedEntities(GraphView view) { /* … */ }
    private static void SelectAll(GraphView view) { /* … */ }
    private static void FrameAll(GraphView view) { /* … */ }
    private static void FrameSelectionOrAll(GraphView view) { /* … */ }
    private static void AddCommentAroundSelection(GraphView view) { /* … */ }
}
```

## Command coverage

Implement handlers for every MVP-tagged command in `D0-action-api.md`
§D.0.2. V2 commands can be registered with `IsEnabled: () => false` and a
no-op action (the host can override later).

For host-owned commands (Save, Compile, …), DO NOT register handlers.
Document in `D0-action-api.md` that these are host-supplied.

## Indicator updates

The editor updates the indicator snapshot from one place: an
`IndicatorUpdater` helper called at end of each frame.

```csharp
public sealed class IndicatorUpdater
{
    public IndicatorUpdater(GraphView view, IDiagnosticsSink? diagnostics, EditorIndicatorsImpl indicators) { … }

    public void UpdateThisFrame()
    {
        var diag = _diagnostics?.CurrentDiagnostics ?? Array.Empty<DiagnosticEntry>();

        _indicators.UpdateSnapshot(new EditorStatusSnapshot(
            CurrentGraphName: _view.Model.DisplayName,
            NodeCount:        _view.Model.Nodes.Count,
            SelectedNodeCount: _view.Selection.Nodes.Count(),
            LinkCount:        _view.Model.Links.Count,
            IsDirty:          _view.Undo.CanUndo,
            ErrorCount:       diag.Count(d => d.Severity == DiagnosticSeverity.Error),
            WarningCount:     diag.Count(d => d.Severity == DiagnosticSeverity.Warning),
            Zoom:             _view.Viewport.Zoom,
            CanvasCursorPos:  _view.Viewport.LastCursorCanvas,
            Mode:             _view.Mode,
            CurrentTool:      _view.Interaction.Mode.ToString()));
    }
}
```

(Adjust property names to match what `GraphView` actually exposes — see
T-11.)

## Hotkey processing

Editor does NOT hook hotkeys. The host's input loop walks
`commands.All`, looks at each descriptor's `DefaultKey`, and binds.

For demo: in `Demo/Program.cs` (T-20), implement a `HotkeyDispatcher` that
each frame:
1. Walks `commands.All`.
2. For each with non-null `DefaultKey`, checks `input.IsKeyPressed(...)`.
3. On match, calls `commands.Invoke(id)`.

Exception: keys that are part of canvas state (e.g., LMB drag) are NOT
commands and are handled directly in `CanvasInput` (T-12).

## Acceptance

- Compiles.
- Demo (T-20) wires up hotkey dispatcher; all default keys work.
- `commands.All` contains every command listed in `D0-action-api.md` §D.0.2
  (MVP at minimum, V2 with `IsEnabled = false`).
- Status bar in demo reads from `Indicators.Snapshot` and updates as
  selection / zoom changes.
- Toast notifications appear when host calls `Indicators.Notify(...)`.

## Estimated Size
~350 LOC across all files.

## Status
Pending.
