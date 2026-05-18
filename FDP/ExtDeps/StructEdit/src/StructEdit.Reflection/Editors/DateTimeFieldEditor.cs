using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace StructEdit.Reflection.Editors;

/// <summary>
/// Built-in <see cref="ICustomFieldEditor"/> for <see cref="DateTime"/> fields.
/// Produces an <see cref="EditNode"/> with <see cref="EditNodeKind.DateTime"/>.
/// </summary>
public sealed class DateTimeFieldEditor : ICustomFieldEditor
{
    /// <inheritdoc/>
    public Type TargetType => typeof(DateTime);

    /// <inheritdoc/>
    public EditNode? CreateNode(
        EditNodeId id,
        string name,
        string jsonPath,
        IValueBinding binding,
        EditNodeMetadata metadata)
    {
        return new EditNode(id, name, jsonPath, EditNodeKind.DateTime, typeof(DateTime), binding, null, metadata);
    }
}
