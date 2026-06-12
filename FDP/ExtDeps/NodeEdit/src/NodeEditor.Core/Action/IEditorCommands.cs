using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Action;

/// <summary>
/// Surface exposing the editor's commands to a host shell. The host binds
/// these to its toolbars, menus, and hotkeys.
/// </summary>
public interface IEditorCommands
{
    /// <summary>All published commands.</summary>
    IReadOnlyList<EditorCommandDescriptor> All { get; }

    /// <summary>Look up a single command by id, or null.</summary>
    EditorCommandDescriptor? Get(string commandId);

    /// <summary>Invoke a command. Returns success/failure with optional message.</summary>
    EditorCommandResult Invoke(string commandId, EditorCommandContext? ctx = null);

    /// <summary>Raised when a command's enabled/checked state changes (host should refresh UI).</summary>
    event System.Action<string>? AvailabilityChanged;
}

/// <summary>Static metadata for a single command.</summary>
public sealed record EditorCommandDescriptor(
    string Id,
    string DisplayName,
    string? Category,
    string? Description,
    string? IconKey,
    KeyBinding? DefaultKey,
    Func<bool> IsEnabled,
    Func<bool>? IsChecked = null,
    Func<string>? DynamicDisplayName = null);

/// <summary>Optional invocation context (e.g., screen position for context-menu commands).</summary>
public readonly record struct EditorCommandContext(
    Vector2? ScreenPos,
    Vector2? CanvasPos,
    IReadOnlyDictionary<string, object?>? Args);

public readonly record struct EditorCommandResult(bool Success, string? Message);

/// <summary>Default key binding for a command.</summary>
public readonly record struct KeyBinding(EditorKey Key, KeyModifiers Modifiers)
{
    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & KeyModifiers.Ctrl) != 0)  parts.Add("Ctrl");
        if ((Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & KeyModifiers.Alt) != 0)   parts.Add("Alt");
        if ((Modifiers & KeyModifiers.Super) != 0) parts.Add("Super");
        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }
}
