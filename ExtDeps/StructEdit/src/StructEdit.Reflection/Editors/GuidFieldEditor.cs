using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace StructEdit.Reflection.Editors;

/// <summary>
/// Built-in <see cref="ICustomFieldEditor"/> for <see cref="Guid"/> fields.
/// Produces an <see cref="EditNode"/> with <see cref="EditNodeKind.Guid"/>.
/// </summary>
public sealed class GuidFieldEditor : ICustomFieldEditor
{
    /// <inheritdoc/>
    public Type TargetType => typeof(Guid);

    /// <inheritdoc/>
    public EditNode? CreateNode(
        EditNodeId id,
        string name,
        string jsonPath,
        IValueBinding binding,
        EditNodeMetadata metadata)
    {
        return new EditNode(id, name, jsonPath, EditNodeKind.Guid, typeof(Guid), binding, null, metadata);
    }
}
