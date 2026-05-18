using System;
using StructEdit.Core;
using StructEdit.Core.Plugins;

namespace Fdp.Presentation.Editing;

public sealed class TypeFieldEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(Type);

    public EditNode? CreateNode(
        EditNodeId id,
        string name,
        string jsonPath,
        IValueBinding binding,
        EditNodeMetadata metadata)
    {
        return new EditNode(
            id,
            name,
            jsonPath,
            EditNodeKind.Custom,
            typeof(Type),
            binding,
            null,
            metadata);
    }
}
