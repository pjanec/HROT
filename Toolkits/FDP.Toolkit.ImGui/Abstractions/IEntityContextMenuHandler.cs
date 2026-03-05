using Fdp.Kernel;

namespace FDP.Toolkit.ImGui.Abstractions;

/// <summary>
/// Implemented by application code to add items to the entity inspector's
/// right-click context menu.
///
/// <para><b>Usage:</b> register one or more handlers via
/// <see cref="EntityInspectorPanel.RegisterContextMenuHandler"/>.
/// When the user right-clicks an entity row in the list each registered handler
/// is called; it can add items (or do nothing, based on the entity or any other
/// state it captures in its closure).</para>
///
/// <para>No handler registered → no menu shown.</para>
///
/// <para>Multiple handlers → items are appended in registration order, separated
/// by a small visual divider.</para>
/// </summary>
public interface IEntityContextMenuHandler
{
    /// <summary>
    /// Called once per right-click to give the handler a chance to populate
    /// the context menu.
    /// </summary>
    /// <param name="entity">The entity that was right-clicked.</param>
    /// <param name="builder">Builder used to add items / sub-menus.</param>
    void PopulateMenu(Entity entity, IContextMenuBuilder builder);
}

/// <summary>
/// Fluent builder for building an ImGui popup context menu.
/// Obtained from the <see cref="IEntityContextMenuHandler.PopulateMenu"/> call.
/// </summary>
public interface IContextMenuBuilder
{
    /// <summary>Adds a clickable menu item at the current level.</summary>
    /// <param name="label">Visible label text.</param>
    /// <param name="callback">Action invoked when the item is clicked.</param>
    /// <param name="enabled">When <c>false</c> the item is shown greyed-out.</param>
    void AddItem(string label, Action callback, bool enabled = true);

    /// <summary>
    /// Opens a submenu. All items added to the returned builder appear inside
    /// this submenu.  Call <see cref="EndSubmenu"/> on the returned builder to
    /// close the submenu scope.
    /// </summary>
    IContextMenuBuilder BeginSubmenu(string label);

    /// <summary>Closes a submenu scope opened by <see cref="BeginSubmenu"/>.</summary>
    void EndSubmenu();

    /// <summary>Appends a visual separator line between groups of items.</summary>
    void AddSeparator();
}
