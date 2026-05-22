using System.Numerics;

namespace NodeEditor.Core.Action;

/// <summary>
/// Read-only status surface for a host shell to read editor state and
/// receive notifications.
/// </summary>
public interface IEditorIndicators
{
    /// <summary>Current status snapshot.</summary>
    EditorStatusSnapshot Snapshot { get; }

    /// <summary>Raised when the snapshot changes.</summary>
    event System.Action? Changed;

    /// <summary>Emit a notification (toast).</summary>
    void Notify(EditorNotification notification);
}

/// <summary>Snapshot of editor state for status bar / chrome rendering.</summary>
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

/// <summary>Coarse editor mode classification.</summary>
public enum EditorMode
{
    Editing,
    Compiling,
    Debugging,
    DebugPaused,
}

/// <summary>One notification posted by the editor for the host to render.</summary>
public sealed record EditorNotification(
    string Id,
    NotificationSeverity Severity,
    string Title,
    string? Body,
    TimeSpan? AutoDismiss,
    IReadOnlyList<NotificationAction>? Actions);

public enum NotificationSeverity { Info, Success, Warning, Error }

/// <summary>An action shown in a notification (a button that invokes a command).</summary>
public sealed record NotificationAction(string Label, string CommandId);
