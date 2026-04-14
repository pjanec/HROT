using Fdp.Kernel;
using Fdp.Toolkit.ImGui.Abstractions;

namespace Fdp.Toolkit.ImGui.Utils;

/// <summary>
/// Adapter that wraps a delegate as an <see cref="IEntityContextMenuHandler"/>.
/// Useful for registering one-liner lambda-based context menus directly in the
/// host application without defining a full class.
///
/// <example>
/// <code>
/// inspector.RegisterContextMenuHandler(new LambdaEntityContextMenuHandler((entity, builder) =>
/// {
///     builder.AddItem("Center on entity", () => CenterCameraOn(entity));
///     builder.AddItem("Select entity",    () => SelectEntityOnMap(entity));
/// }));
/// </code>
/// </example>
/// </summary>
public sealed class LambdaEntityContextMenuHandler : IEntityContextMenuHandler
{
    private readonly Action<Entity, IContextMenuBuilder> _populate;

    /// <param name="populate">
    /// Delegate that receives the right-clicked entity and the menu builder.
    /// </param>
    public LambdaEntityContextMenuHandler(Action<Entity, IContextMenuBuilder> populate)
    {
        ArgumentNullException.ThrowIfNull(populate);
        _populate = populate;
    }

    /// <inheritdoc/>
    public void PopulateMenu(Entity entity, IContextMenuBuilder builder)
        => _populate(entity, builder);
}
