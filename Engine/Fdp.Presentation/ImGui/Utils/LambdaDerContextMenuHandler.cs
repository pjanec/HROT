using Fdp.Toolkit.DER;
using Fdp.Toolkit.ImGui.Abstractions;

namespace Fdp.Toolkit.ImGui.Utils;

/// <summary>
/// Adapter that wraps a delegate as an <see cref="IDerContextMenuHandler"/>.
/// Useful for registering one-liner lambda-based context menus directly in the
/// host application without defining a full class.
///
/// <example>
/// <code>
/// panel.RegisterContextMenuHandler(new LambdaDerContextMenuHandler((entity, builder) =>
/// {
///     if (entity.HasDescriptor&lt;MapVisualOverlay&gt;())
///         builder.AddItem("Edit Overlay", () => logic.StartEditingMode(entity.EntityId));
/// }));
/// </code>
/// </example>
/// </summary>
public sealed class LambdaDerContextMenuHandler : IDerContextMenuHandler
{
    private readonly Action<IDerEntity, IContextMenuBuilder> _populate;

    /// <param name="populate">
    /// Delegate that receives the right-clicked entity and the menu builder.
    /// </param>
    public LambdaDerContextMenuHandler(Action<IDerEntity, IContextMenuBuilder> populate)
    {
        ArgumentNullException.ThrowIfNull(populate);
        _populate = populate;
    }

    /// <inheritdoc/>
    public void PopulateMenu(IDerEntity entity, IContextMenuBuilder builder)
        => _populate(entity, builder);
}
