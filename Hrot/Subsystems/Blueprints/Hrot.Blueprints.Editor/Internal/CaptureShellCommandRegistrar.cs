using System;
using System.Collections.Generic;

namespace Hrot.Blueprints.Editor.Internal;

/// <summary>
/// Minimal <see cref="IShellCommandRegistrar"/> implementation used at the composition root
/// to capture registered menu entries, toolbar entries, and shortcuts without creating
/// any ImGui dependency. The composition root calls
/// <see cref="IShellCommandRegistrar.RegisterToolbarEntry"/> here, then retrieves the
/// captured callbacks via <see cref="GetToolbarCallback"/> to wire them into the
/// ImGui draw loop.
/// </summary>
public sealed class CaptureShellCommandRegistrar : IShellCommandRegistrar
{
    private readonly Dictionary<string, Action> _toolbarEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _menuEntries    = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action> _shortcuts      = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public void RegisterMenuEntry(string path, Action onSelected)
    {
        if (path is null)       throw new ArgumentNullException(nameof(path));
        if (onSelected is null) throw new ArgumentNullException(nameof(onSelected));
        _menuEntries[path] = onSelected;
    }

    /// <inheritdoc/>
    public void RegisterToolbarEntry(string label, Action onClicked)
    {
        if (label is null)     throw new ArgumentNullException(nameof(label));
        if (onClicked is null) throw new ArgumentNullException(nameof(onClicked));
        _toolbarEntries[label] = onClicked;
    }

    /// <inheritdoc/>
    public void RegisterShortcut(string keybind, Action onTriggered)
    {
        if (keybind is null)     throw new ArgumentNullException(nameof(keybind));
        if (onTriggered is null) throw new ArgumentNullException(nameof(onTriggered));
        _shortcuts[keybind] = onTriggered;
    }

    /// <summary>
    /// Returns the callback registered for a toolbar entry, or <c>null</c> if not registered.
    /// </summary>
    public Action? GetToolbarCallback(string label)
        => _toolbarEntries.TryGetValue(label, out var cb) ? cb : null;

    /// <summary>All registered toolbar entries (label → callback).</summary>
    public IReadOnlyDictionary<string, Action> ToolbarEntries => _toolbarEntries;
}
