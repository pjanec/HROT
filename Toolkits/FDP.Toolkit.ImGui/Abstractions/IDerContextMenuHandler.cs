using FDP.Toolkit.DER;

namespace FDP.Toolkit.ImGui.Abstractions;

/// <summary>
/// Implemented by application code to add items to the DER entity inspector's
/// right-click context menu.
///
/// <para><b>Usage:</b> register one or more handlers via
/// <see cref="Panels.DerEntityInspectorPanel.RegisterContextMenuHandler"/>.
/// When the user right-clicks an entity row in the list, each registered handler
/// is called; it can add items (or do nothing, based on the entity state or any
/// other state it captures in its closure).</para>
///
/// <para>No handlers registered → no menu shown.</para>
///
/// <para>Multiple handlers → items are appended in registration order, separated
/// by a small visual divider.</para>
/// </summary>
public interface IDerContextMenuHandler
{
    /// <summary>
    /// Called once per right-click to give the handler a chance to populate
    /// the context menu.
    /// </summary>
    /// <param name="entity">The DER entity that was right-clicked.</param>
    /// <param name="builder">Builder used to add items / sub-menus.</param>
    void PopulateMenu(IDerEntity entity, IContextMenuBuilder builder);
}
