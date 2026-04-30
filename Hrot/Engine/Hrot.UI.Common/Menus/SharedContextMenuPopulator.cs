using FDP.Toolkit.ImGui.Abstractions;
using Hrot.UI.Common.Facades;

namespace Hrot.UI.Common.Menus;

/// <summary>
/// Pure static helper that populates ImGui context menus for entity actions.
///
/// <para>This class has <b>no state</b> and makes <b>no ImGui calls</b>.
/// It only calls <see cref="IContextMenuBuilder"/> methods and is therefore
/// fully unit-testable without an active ImGui render frame.</para>
///
/// <para>The host application calls
/// <see cref="PopulateEntityMenu"/> or <see cref="PopulateEmptyMapMenu"/>
/// inside an <c>ImGui.BeginPopup</c>/<c>EndPopup</c> block and supplies
/// concrete adapter implementations of both interfaces.</para>
/// </summary>
public static class SharedContextMenuPopulator
{
    /// <summary>
    /// Populates a context menu for a right-clicked entity.
    /// </summary>
    /// <param name="entityId">Network entity ID of the right-clicked entity.</param>
    /// <param name="tkbType">TKB entity type (reserved for future sub-menu filtering).</param>
    /// <param name="hasEditablePolyline"><c>true</c> when the entity has a polyline overlay that can be edited.</param>
    /// <param name="hasRoutePlan"><c>true</c> when the entity has a route plan that can be edited.</param>
    /// <param name="builder">Context-menu builder obtained from the host's ImGui popup.</param>
    /// <param name="actions">Action controller that executes the menu commands.</param>
    public static void PopulateEntityMenu(
        long entityId,
        long tkbType,
        bool hasEditablePolyline,
        bool hasRoutePlan,
        IContextMenuBuilder builder,
        IEntityActionController actions)
    {
        builder.AddItem("Center on Entity", () => actions.CenterOnEntity(entityId));

        if (entityId != 0)
            builder.AddItem("Rename...", () => actions.Rename(entityId));

        if (hasEditablePolyline)
            builder.AddItem("Edit Shape", () => actions.EditOverlay(entityId));

        if (hasRoutePlan)
            builder.AddItem("Edit Route", () => actions.EditRoute(entityId));

        builder.AddItem("Rotate", () => actions.ActivateRotateTool(entityId));

        builder.AddSeparator();
        builder.AddItem("Delete", () => actions.DeleteEntity(entityId));
    }

    /// <summary>
    /// Populates a context menu for a right-click on empty map space (no entity selected).
    /// </summary>
    /// <param name="builder">Context-menu builder obtained from the host's ImGui popup.</param>
    /// <param name="actions">Action controller that executes the menu commands.</param>
    public static void PopulateEmptyMapMenu(
        IContextMenuBuilder builder,
        IEntityActionController actions)
    {
        builder.AddItem("Measurement Tool", () => actions.ActivateMeasureTool());
    }
}
