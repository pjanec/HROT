using System.Numerics;
using System.Text;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using Fdp.Presentation.Icons;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// Adapter that bridges <see cref="IEditorCommands"/> → <see cref="MainToolbarManager"/> (§6.2).
/// Given a command id + <see cref="IIconProvider"/>, registers a toolbar entry whose render
/// delegate resolves the icon, draws via <see cref="IconWidgets"/>, and invokes the command
/// on click. State (<see cref="EditorCommandDescriptor.IsEnabled"/>,
/// <see cref="EditorCommandDescriptor.IsChecked"/>) is re-read every frame (immediate mode).
/// </summary>
public static class ToolbarCommandAdapter
{
    private static Vector2 DefaultSize => new(Gui.GetFrameHeight(), Gui.GetFrameHeight());

    /// <summary>
    /// Registers a toolbar entry for <paramref name="commandId"/> in <paramref name="toolbar"/>.
    /// </summary>
    /// <param name="toolbar">The target toolbar manager.</param>
    /// <param name="commands">The command set containing <paramref name="commandId"/>.</param>
    /// <param name="commandId">The id of the command to bind.</param>
    /// <param name="iconProvider">Resolves <see cref="EditorCommandDescriptor.IconKey"/> to an <see cref="IconHandle"/>.</param>
    /// <param name="sortOrder">Ascending sort order in the toolbar.</param>
    /// <param name="perspective">Optional perspective filter.</param>
    /// <exception cref="ArgumentNullException">Thrown when any required argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="commandId"/> is not found.</exception>
    public static void Register(
        MainToolbarManager toolbar,
        IEditorCommands commands,
        string commandId,
        IIconProvider iconProvider,
        int sortOrder,
        string? perspective = null)
    {
        ArgumentNullException.ThrowIfNull(toolbar);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(commandId);
        ArgumentNullException.ThrowIfNull(iconProvider);

        var descriptor = commands.Get(commandId)
            ?? throw new InvalidOperationException($"Command '{commandId}' not found in the provided command set.");

        toolbar.RegisterEntry(commandId, sortOrder, MainToolbarManager.DefaultEntryHeight, () =>
        {
            RenderEntry(commands, commandId, descriptor, iconProvider);
        }, perspective);
    }

    /// <summary>
    /// Computes the current visual state and click handler for a command.
    /// This is the headless-testable seam — pure computation, no ImGui calls.
    /// State is evaluated immediately against the descriptor's current delegates.
    /// </summary>
    public static ToolbarCommandState GetState(IEditorCommands commands, string commandId)
    {
        var descriptor = commands.Get(commandId);
        if (descriptor == null)
            return new ToolbarCommandState(false, false, null);

        bool enabled = descriptor.IsEnabled();
        bool toggled = descriptor.IsChecked?.Invoke() ?? false;
        Action? onClick = enabled
            ? () => commands.Invoke(commandId)
            : null;

        return new ToolbarCommandState(enabled, toggled, onClick);
    }

    /// <summary>
    /// Per-frame render delegate for a single toolbar command entry.
    /// Resolves the icon, draws via <see cref="IconWidgets"/>, and handles tooltip + click.
    /// </summary>
    private static void RenderEntry(
        IEditorCommands commands,
        string commandId,
        EditorCommandDescriptor descriptor,
        IIconProvider iconProvider)
    {
        // Re-read live state every frame (immediate mode).
        bool enabled = descriptor.IsEnabled();
        bool isToggled = descriptor.IsChecked?.Invoke() ?? false;

        // Resolve icon.
        IconHandle iconHandle = default;
        bool hasIcon = descriptor.IconKey != null
                       && iconProvider.TryGet(descriptor.IconKey, out iconHandle);

        bool clicked;
        if (hasIcon)
        {
            if (descriptor.IsChecked != null)
            {
                // Checkable → ToggleIcon.
                clicked = IconWidgets.ToggleIcon(in iconHandle, commandId, DefaultSize,
                    ref isToggled, enabled);
            }
            else
            {
                // Plain → IconButton.
                clicked = IconWidgets.IconButton(in iconHandle, commandId, DefaultSize, enabled);
            }
        }
        else
        {
            // Fallback: text button when icon is missing.
            if (descriptor.IsChecked != null)
            {
                // Checkable text fallback — simple selectable.
                bool toggledCopy = isToggled;
                clicked = Gui.MenuItem(descriptor.DisplayName, "", ref toggledCopy, enabled);
            }
            else
            {
                clicked = Gui.Button($"{descriptor.DisplayName}##{commandId}", DefaultSize);
                if (!enabled)
                {
                    // Dimmed dummy overlay when disabled and no icon.
                    // The button still renders but we suppress the click.
                    clicked = false;
                }
            }
        }

        // Tooltip: DisplayName + optional Description and shortcut.
        if (Gui.IsItemHovered())
        {
            var tooltip = new StringBuilder(descriptor.DisplayName);
            if (!string.IsNullOrEmpty(descriptor.Description))
                tooltip.Append('\n').Append(descriptor.Description);
            if (descriptor.DefaultKey != null)
                tooltip.Append(" (").Append(descriptor.DefaultKey.Value.ToString()).Append(')');
            Gui.SetTooltip(tooltip.ToString());
        }

        // Invoke on click (only when enabled).
        if (clicked && enabled)
            commands.Invoke(commandId);
    }

    /// <summary>
    /// Headless-testable visual state and click action for a toolbar command.
    /// </summary>
    /// <param name="IsEnabled">Whether the command is currently enabled.</param>
    /// <param name="IsToggled">Whether the command is currently checked/toggled.</param>
    /// <param name="OnClick">
    /// The action to invoke on click, or <c>null</c> when the command is disabled.
    /// When non-null, calling this action invokes <c>commands.Invoke(id)</c>.
    /// </param>
    public readonly record struct ToolbarCommandState(
        bool IsEnabled,
        bool IsToggled,
        Action? OnClick
    );
}
