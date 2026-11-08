using NodeEditor.Core.Action;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// Adapter that bridges <see cref="IEditorCommands"/> → <see cref="GlobalMenuRegistry"/> (§6.2).
/// Given a command id and a menu path, registers the appropriate menu item type
/// (plain action or checkable) in the global menu registry.
/// </summary>
public static class MenuCommandAdapter
{
    /// <summary>
    /// Registers a menu item for <paramref name="commandId"/> at <paramref name="menuPath"/>
    /// in <paramref name="menu"/>.
    /// </summary>
    /// <param name="menu">The target menu registry.</param>
    /// <param name="commands">The command set containing <paramref name="commandId"/>.</param>
    /// <param name="commandId">The id of the command to bind. Must be registered in <paramref name="commands"/>.</param>
    /// <param name="menuPath">Slash-separated menu path (e.g. "File/Open").</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="commandId"/> is not found in <paramref name="commands"/>.</exception>
    /// <param name="allowedInIdle">⭐ <c>CE-4096</c> — see <see cref="GlobalMenuRegistry.RegisterItem"/>.</param>
    /// <param name="perspective">
    /// ⭐⭐ <b><c>UXI-05</c></b> — the perspective this item serves, or <see langword="null"/> *(default)*
    /// for a GLOBAL item. ⛔ A pure passthrough to the registry: the adapter does not decide scope, the
    /// caller does. ⚠ The default keeps every existing call byte-identical.
    /// </param>
    public static void Register(GlobalMenuRegistry menu, IEditorCommands commands, string commandId,
                                string menuPath, string? perspective = null, bool allowedInIdle = false)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(commandId);
        ArgumentNullException.ThrowIfNull(menuPath);

        var descriptor = commands.Get(commandId)
            ?? throw new InvalidOperationException($"Command '{commandId}' not found in the provided command set.");

        if (descriptor.IsChecked != null)
        {
            // Checkable menu item.
            menu.RegisterCheckableItem(
                menuPath,
                getChecked: () => descriptor.IsChecked(),
                onChanged: _ =>
                {
                    if (descriptor.IsEnabled())
                        commands.Invoke(commandId);
                },
                perspective: perspective);
        }
        else
        {
            // Plain action item.
            menu.RegisterItem(
                menuPath,
                onClick: () =>
                {
                    if (descriptor.IsEnabled())
                        commands.Invoke(commandId);
                },
                perspective: perspective,
                allowedInIdle: allowedInIdle);   // ⭐ CE-4096 — a plain action may be offered in Idle; a checkable one never is
        }

        // Apply shortcut and enabled state to the leaf node.
        ApplyLeafNode(menu, menuPath, descriptor);
    }

    /// <summary>
    /// ⭐⭐ B-10 K5 (docs/UX/DESIGN_Input_And_Commands.md §6e) — a menu item that runs code NAMES AN ACTION: this declares the command <paramref name="commandId"/> in the ONE shell catalogue
    /// (<see cref="ShellEditorCommands"/>, the same one the keymap binds keys to, the toolbar and the HTTP surface invoke) and binds the item to it. ⛔ SUPERSEDES
    /// <c>GlobalMenu.RegisterItem(path, closure)</c> in production code (rail <c>NoProductionMenuItemIsAClosureOnly</c>): the closure is now the ACTION's handler, so a key, a button or a
    /// remote call reaches the very same code as the menu click.
    /// </summary>
    public static void RegisterAction(GlobalMenuRegistry menu, ShellEditorCommands commands, string commandId, string menuPath, Action run,
        string? perspective = null, bool allowedInIdle = false, KeyBinding? key = null, Func<bool>? enabled = null, Func<string>? dynamicLabel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (commands.Get(commandId) == null)
            commands.Register(new EditorCommandDescriptor(commandId, LabelOf(menuPath), "Menu", null, null, key, enabled ?? (() => true), DynamicDisplayName: dynamicLabel), _ => run());
        Register(menu, commands, commandId, menuPath, perspective, allowedInIdle);
    }

    /// <summary>⭐ B-10 K5 — the checkable form of <see cref="RegisterAction"/>: the command reads <paramref name="isChecked"/> and its handler flips it through <paramref name="set"/>.</summary>
    public static void RegisterToggle(GlobalMenuRegistry menu, ShellEditorCommands commands, string commandId, string menuPath, Func<bool> isChecked, Action<bool> set,
        string? perspective = null, KeyBinding? key = null)
    {
        ArgumentNullException.ThrowIfNull(isChecked);
        ArgumentNullException.ThrowIfNull(set);
        if (commands.Get(commandId) == null)
            commands.Register(new EditorCommandDescriptor(commandId, LabelOf(menuPath), "Menu", null, null, key, () => true, isChecked), _ => set(!isChecked()));
        Register(menu, commands, commandId, menuPath, perspective);
    }

    private static string LabelOf(string menuPath) => menuPath.Substring(menuPath.LastIndexOf('/') + 1);

    /// <summary>
    /// Applies the shortcut text, enabled delegate, and dynamic label
    /// to the menu leaf node after it has been registered.
    /// </summary>
    private static void ApplyLeafNode(GlobalMenuRegistry menu, string menuPath, EditorCommandDescriptor descriptor)
    {
        var node = FindNode(menu.Root, menuPath);
        if (node == null) return;

        // Shortcut text from DefaultKey.
        if (descriptor.DefaultKey != null)
            node.Shortcut = descriptor.DefaultKey.Value.ToString();

        // Enabled state delegates to IsEnabled.
        node.GetEnabled = () => descriptor.IsEnabled();

        // Dynamic label from descriptor (may be null — ResolveLabel falls back to Name).
        node.DynamicLabel = descriptor.DynamicDisplayName;
    }

    /// <summary>
    /// Walks the trie from <paramref name="root"/> along <paramref name="path"/>
    /// and returns the leaf <see cref="MenuItemNode"/>, or null if not found.
    /// </summary>
    internal static MenuItemNode? FindNode(MenuItemNode root, string path)
    {
        var segments = path.Split('/');
        var current = root;

        foreach (var segment in segments)
        {
            if (segment.Length == 0) continue;

            if (!current.Children.TryGetValue(segment, out var child))
                return null;

            current = child;
        }

        return current;
    }
}
